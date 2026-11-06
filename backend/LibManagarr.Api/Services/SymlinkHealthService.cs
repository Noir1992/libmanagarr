using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>
/// What one health pass ended with. The counts are what the task log and the answer of a manual run need;
/// the findings themselves stay on the rows, because a complaint the register cannot show is not one an
/// operator can act on. <see cref="Undetermined"/> is counted apart on purpose: a pass that could not look
/// at something is not a pass that found nothing.
/// </summary>
public sealed record SymlinkHealthRun(
    int LinksChecked,
    int Reported,
    int Undetermined,
    int FollowUpsQueued,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt);

/// <summary>
/// Looks at the managed links as they stand right now: whether the entry is still the link its row made,
/// whether it still leads at the chosen file, and whether the Sonarr side still matches what the row
/// proved when it was set up. It writes no link, removes none and touches no media file. The one thing it
/// starts is the follow-up the register already knows how to carry out, and it starts it by asking the
/// existing code to queue it rather than by inventing a second path of its own.
///
/// The Arr half of every answer comes from the instance snapshots the library sync already keeps, and the
/// local half from the file system, so a pass over the whole register asks no Arr API per row and waits on
/// no sync of its own. Where that data cannot answer — an instance nobody ever synced, a share this
/// process cannot read — the row says exactly that with the date its answer rests on, instead of calling a
/// mount that is merely offline a deleted file.
/// </summary>
public sealed class SymlinkHealthService(
    ISymlinkStore store,
    ISettingsService settings,
    SymlinkService links,
    ISymlinkFileSystem files,
    SymlinkSetupRequests setupRequests,
    TimeProvider clock,
    ILogger<SymlinkHealthService> logger)
{
    private const string LinkLostCode = "link-lost";
    private const string EntryNotALinkCode = "entry-not-a-link";
    private const string LinkMovedCode = "link-target-moved";
    private const string SourceMissingCode = "source-missing";
    private const string AccessDeniedCode = "access-denied";
    private const string StorageUnreachableCode = "storage-unreachable";
    private const string RadarrUnsyncedCode = "radarr-unsynced";
    private const string SourceReplacedCode = "source-replaced";
    private const string SonarrUnsyncedCode = "sonarr-unsynced";
    private const string EpisodeMissingCode = "sonarr-episode-missing";
    private const string EpisodeFileMissingCode = "episode-file-missing";
    private const string MonitoringReopenedCode = "monitoring-reactivated";
    private const string FilenameDiffersCode = "filename-differs";
    private const string MetadataUnconfirmedCode = "metadata-unconfirmed";
    private const string SizeDeviationCode = "metadata-size-deviation";
    private const string LanguagesDeviationCode = "metadata-languages-deviation";
    private const string ReleaseGroupDeviationCode = "metadata-release-group-deviation";

    /// <summary>
    /// The codes this pass owns: it writes them fresh on every look and retires the ones that did not occur
    /// again, so a link that came back does not carry an old complaint forever. Anything another service
    /// wrote — a restore marker, a rename the instance refused — is left exactly as it is.
    /// </summary>
    private static readonly string[] OwnedCodes =
    [
        LinkLostCode, EntryNotALinkCode, LinkMovedCode, SourceMissingCode, AccessDeniedCode,
        StorageUnreachableCode, RadarrUnsyncedCode, SourceReplacedCode, SonarrUnsyncedCode,
        EpisodeMissingCode, EpisodeFileMissingCode, MonitoringReopenedCode, FilenameDiffersCode,
        MetadataUnconfirmedCode, SizeDeviationCode, LanguagesDeviationCode, ReleaseGroupDeviationCode
    ];

    /// <summary>
    /// The codes that say the pass could not answer instead of answering "nothing to report": an instance
    /// that was never synchronized, a share that would not be read, a file information that was never
    /// recorded. A row carrying one of these is not a row that was found sound, so the count of them is read
    /// from the findings themselves rather than from a second opinion next to them.
    /// </summary>
    private static readonly string[] UnanswerableCodes =
    [
        RadarrUnsyncedCode, SonarrUnsyncedCode, AccessDeniedCode, StorageUnreachableCode, MetadataUnconfirmedCode
    ];

    /// <summary>
    /// A media tree on a network share answers slowly per entry, so one thread over the whole register is
    /// the slow way to say "nothing to report" every night. It stays a small number on purpose: the pass
    /// competes with the Arr instances and the media server for the same shares, and it has no deadline.
    /// </summary>
    private const int CheckConcurrency = 4;

    /// <summary>
    /// One entry, examined now. The manual check and the scheduled pass run the same comparison against the
    /// same data — being asked about one row or about all of them is the only difference.
    /// </summary>
    public async Task<SymlinkLink?> CheckLinkAsync(string linkId, CancellationToken cancellationToken)
    {
        SymlinkLink? row = await store.GetLinkAsync(linkId, cancellationToken);
        if (row is null) return null;

        AppSettings snapshot = await settings.GetSnapshotAsync(cancellationToken);
        await CheckRowAsync(row, snapshot, cancellationToken);
        // The row was written, so the answer handed back is read again rather than handed out as the copy in
        // hand: whoever received it is about to write against it and has to present the revision the store
        // holds, not the one this pass started from.
        return await store.GetLinkAsync(linkId, cancellationToken);
    }

    /// <summary>
    /// A pass over the whole register. Rows are independent, and a row that cannot be looked at is never
    /// allowed to end the pass, so its trouble is reported and the next row is examined anyway. A follow-up
    /// this pass queued is carried out by the setup pass asked for at the end — the health check notices a
    /// missed upgrade, it does not perform one.
    /// </summary>
    public async Task<SymlinkHealthRun> CheckAllAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset startedAt = clock.GetUtcNow();
        AppSettings snapshot = await settings.GetSnapshotAsync(cancellationToken);
        List<SymlinkLink> rows = [.. await store.GetLinksAsync(cancellationToken)];
        if (rows.Count == 0)
        {
            logger.LogDebug("Symlink health check found a register holding nothing at all.");
            return new SymlinkHealthRun(0, 0, 0, 0, startedAt, clock.GetUtcNow());
        }

        int reported = 0;
        int undetermined = 0;
        int queued = 0;
        await Parallel.ForEachAsync(
            rows,
            new ParallelOptions { MaxDegreeOfParallelism = CheckConcurrency, CancellationToken = cancellationToken },
            async (SymlinkLink row, CancellationToken token) =>
            {
                try
                {
                    RowAnswer answer = await CheckRowAsync(row, snapshot, token);
                    if (answer.Reported) Interlocked.Increment(ref reported);
                    if (answer.Undetermined) Interlocked.Increment(ref undetermined);
                    if (answer.Queued) Interlocked.Increment(ref queued);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Checking managed link {LinkId} failed; the entry keeps what it had and the next pass looks again.",
                        row.Id);
                }
            });

        if (queued > 0) setupRequests.Request();

        SymlinkHealthRun run = new(rows.Count, reported, undetermined, queued, startedAt, clock.GetUtcNow());
        if (undetermined > 0)
        {
            logger.LogWarning(
                "Symlink health pass over {LinksChecked} entries reported {Reported} and could not tell the state of {Undetermined}; {FollowUpsQueued} follow-ups were queued.",
                run.LinksChecked, run.Reported, run.Undetermined, run.FollowUpsQueued);
        }
        else
        {
            logger.LogInformation(
                "Symlink health pass over {LinksChecked} entries reported {Reported}; {FollowUpsQueued} follow-ups were queued.",
                run.LinksChecked, run.Reported, run.FollowUpsQueued);
        }

        return run;
    }

    /// <summary>
    /// The whole examination of one row, written as one pass over the row it belongs to: the moment it
    /// looked and what it found go down together, so a row can never carry today's timestamp next to last
    /// week's answer.
    /// </summary>
    private async Task<RowAnswer> CheckRowAsync(SymlinkLink row, AppSettings snapshot, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        List<SymlinkFinding> findings = [.. row.Findings.Where(existing => !OwnedCodes.Contains(existing.Code))];

        bool queued = await FollowMovedSourceAsync(row, snapshot, findings, cancellationToken);
        if (queued)
        {
            // The code that owns a follow-up put its own sentence on the row rather than into this list. It
            // stays, because it came out of a decision and not out of a look — and it is the answer a later
            // pass reads to know why the entry is owed something.
            SymlinkFinding? decided = row.Findings.Find(existing => existing.Code == SourceReplacedCode);
            if (decided is not null) findings.Add(decided);
        }

        CheckLocalState(row, snapshot, findings);
        CheckSonarrSide(row, snapshot, findings);
        bool undetermined = findings.Any(finding => UnanswerableCodes.Contains(finding.Code));

        row.Findings = [.. findings];
        row.LastCheckedAt = now;
        // The stamp means "this pass had nothing to say", and the queueing above may have said something of
        // its own directly on the row, so the answer is read from the row rather than from this pass's list.
        if (!row.Findings.Any(finding => OwnedCodes.Contains(finding.Code))) row.LastSuccessfulCheckAt = now;

        if (!await store.UpdateLinkAsync(row, row.Revision, cancellationToken))
        {
            logger.LogWarning(
                "Entry {LinkId} changed while the health check was looking at it; the next pass starts from the new state.",
                row.Id);
        }

        return new RowAnswer(findings.Count > 0 || queued, undetermined, queued);
    }

    /// <summary>
    /// The Radarr half: does the movie this row follows have another file now. Queueing is left to the code
    /// that owns it, because that is the one place that knows what a follow-up costs the entry — and its
    /// sentence stays on the row, because it came out of a decision rather than out of a look. What it
    /// declined to queue is reported anyway: a source that moved while following is switched off is worth
    /// seeing, and it is not the same thing as a row that is fine.
    /// </summary>
    private async Task<bool> FollowMovedSourceAsync(
        SymlinkLink row,
        AppSettings snapshot,
        List<SymlinkFinding> findings,
        CancellationToken cancellationToken)
    {
        if (await links.MarkReplacedSourceAsync(row, snapshot, cancellationToken)) return true;

        string instanceName = snapshot.RadarrInstances
            .FirstOrDefault(instance => string.Equals(instance.Id, row.RadarrInstanceId, StringComparison.OrdinalIgnoreCase))
            ?.Name ?? row.RadarrInstanceId;
        MediaFileSnapshot? current = SymlinkService.SnapshotFileOf(snapshot, row);
        if (current is null)
        {
            findings.Add(Finding(RadarrUnsyncedCode,
                $"{instanceName} has no library snapshot carrying movie {row.RadarrMovieId}, so whether the file this entry points at is still the current one cannot be told."));
            return false;
        }

        if (current.Id is int fileId
            && !string.Equals(SymlinkService.SourceVersionOf(fileId, current.Size), row.SourceVersion, StringComparison.Ordinal))
        {
            findings.Add(Finding(SourceReplacedCode,
                $"{instanceName} has file {fileId} of movie {row.RadarrMovieId} now, while this entry still points at file {row.RadarrFileId}. Later upgrades are not followed for this entry, so this one is left to you."));
        }

        return false;
    }

    /// <summary>
    /// The file-system half, and the only part of a check that looks at the media tree itself. A row that
    /// never had a link of its own, or one that is on its way out, claims nothing here: for the first the
    /// entry is still owed, and for the second the deletion itself answers what stands at the path. A refusal
    /// to be asked is written down as such rather than as a state of the file, which is what the last two
    /// answers are for.
    /// </summary>
    private void CheckLocalState(SymlinkLink row, AppSettings snapshot, List<SymlinkFinding> findings)
    {
        bool claimsALink = row.SetupStep is not null
                           && row.SetupStep != SymlinkSetupStep.LinkCreated
                           && row.OperationStatus != SymlinkOperationStatus.DeletePending
                           && !SymlinkService.WaitsForRestoreCheck(row);
        if (!claimsALink) return;

        if (!files.EntryExists(row.LinkPath))
        {
            findings.Add(Finding(LinkLostCode,
                $"'{row.LinkPath}' is not there anymore, although this entry was proven standing there."));
            return;
        }

        if (!files.IsSymlink(row.LinkPath))
        {
            findings.Add(Finding(EntryNotALinkCode,
                $"'{row.LinkPath}' is a regular file rather than the managed link, so this entry no longer speaks for what stands there."));
            return;
        }

        if (!links.ResolvesToSource(row))
        {
            findings.Add(Finding(LinkMovedCode,
                $"'{row.LinkPath}' points at '{files.ReadLinkTarget(row.LinkPath) ?? "nothing"}' instead of the chosen file '{row.SourcePath}'."));
            return;
        }

        try
        {
            files.OpenForRead(row.LinkPath);
        }
        catch (UnauthorizedAccessException exception)
        {
            findings.Add(Finding(AccessDeniedCode,
                $"'{row.LinkPath}' is the right link but could not be opened for reading: {exception.Message}"));
        }
        catch (IOException exception)
        {
            // Two reasons look the same from here: the file at the end of the link is gone, or the share it
            // hangs on is not mounted. Only the second is a reason not to write a film off as deleted, so
            // the roots are asked before the source is.
            if (RootUnreadable(row, snapshot))
            {
                findings.Add(Finding(StorageUnreachableCode,
                    $"'{row.LinkPath}' could not be read and the roots of its storage area are unreadable too, so this is reported as a share that is offline rather than as a file that is gone."));
                return;
            }

            findings.Add(Finding(SourceMissingCode,
                $"'{row.LinkPath}' is the right link, but the file it leads to could not be opened: {exception.Message}"));
        }
    }

    /// <summary>
    /// The Sonarr half, read from that instance's library snapshot. What the entry rests on is asked apart
    /// piece by piece — that the episode is the one the entry names, that it really carries a file, that
    /// the file information Sonarr read agrees with the chosen Radarr file, that the episode is still not
    /// being watched — because each of them fails differently and each has a different thing the operator
    /// can do about it.
    /// </summary>
    private void CheckSonarrSide(SymlinkLink row, AppSettings snapshot, List<SymlinkFinding> findings)
    {
        SonarrInstanceSnapshot? instance = snapshot.SonarrSnapshots
            .FirstOrDefault(candidate => string.Equals(candidate.InstanceId, row.SonarrInstanceId, StringComparison.OrdinalIgnoreCase));
        if (instance is null)
        {
            string configuredName = snapshot.SonarrInstances
                .FirstOrDefault(candidate => string.Equals(candidate.Id, row.SonarrInstanceId, StringComparison.OrdinalIgnoreCase))
                ?.Name ?? row.SonarrInstanceId;
            findings.Add(Finding(SonarrUnsyncedCode,
                $"{configuredName} has no library snapshot, so the episode side of this entry could not be looked at at all."));
            return;
        }

        SonarrSeriesSnapshot? series = instance.Series
            .FirstOrDefault(candidate => candidate.SeriesId == row.SonarrSeriesId);
        SonarrEpisodeSnapshot? episode = series?.Seasons
            .SelectMany(season => season.Episodes)
            .FirstOrDefault(candidate => candidate.EpisodeId == row.SonarrEpisodeId);
        if (episode is null)
        {
            findings.Add(Finding(EpisodeMissingCode, series is null
                ? $"{instance.InstanceName} has no series {row.SonarrSeriesId} in its snapshot of {instance.SyncedAt:yyyy-MM-dd HH:mm}, so the episode this entry was made for is not there either."
                : $"{instance.InstanceName} has no episode {row.SonarrEpisodeId} in series {row.SonarrSeriesId} in its snapshot of {instance.SyncedAt:yyyy-MM-dd HH:mm}. The pairing this entry was made for is gone."));
            return;
        }

        if (row.MonitoringDisabled && episode.Monitored)
        {
            findings.Add(Finding(MonitoringReopenedCode,
                $"{instance.InstanceName} watches episode {row.SonarrEpisodeId} again, so the special this entry hands over is competing with the movie it points at."));
        }

        if (!episode.HasFile)
        {
            findings.Add(Finding(EpisodeFileMissingCode,
                $"The episode this entry made available carries no file at {instance.InstanceName} anymore, so Sonarr is not reading through the link."));
            return;
        }

        if (row.AppliedFilename is string applied
            && episode.File.Path is string sonarrPath
            && !string.Equals(FileNameOf(sonarrPath), applied, StringComparison.Ordinal))
        {
            findings.Add(Finding(FilenameDiffersCode,
                $"The file on this episode is named '{FileNameOf(sonarrPath)}' at {instance.InstanceName}, while this entry left it as '{applied}'."));
        }

        if (row.ExpectedMetadata is not SymlinkFileMetadata expected)
        {
            findings.Add(Finding(MetadataUnconfirmedCode,
                "This entry never recorded what the chosen Radarr file carried, so nothing here can say whether the episode reads the same film."));
            return;
        }

        if (expected.Size is long expectedSize && episode.File.Size != expectedSize)
        {
            findings.Add(Finding(SizeDeviationCode,
                $"The snapshot of {instance.InstanceName} measures {episode.File.Size} bytes for this file while the chosen Radarr file has {expectedSize}. The link leads at the right name, so what is behind it changed."));
        }

        if (!string.IsNullOrWhiteSpace(expected.ReleaseGroup)
            && !string.Equals(episode.File.ReleaseGroup, expected.ReleaseGroup, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(Finding(ReleaseGroupDeviationCode,
                $"The snapshot of {instance.InstanceName} reads the release group as '{episode.File.ReleaseGroup ?? "nothing"}'; the Radarr file this entry chose was labelled '{expected.ReleaseGroup}'."));
        }

        if (expected.Languages.Count > 0
            && !episode.File.AudioLanguages.Any(language => expected.Languages.Contains(language, StringComparer.OrdinalIgnoreCase)))
        {
            findings.Add(Finding(LanguagesDeviationCode,
                $"The snapshot of {instance.InstanceName} found none of the audio languages of the chosen Radarr file ({string.Join(", ", expected.Languages)}); it knows {episode.File.AudioLanguages.Count} of its own."));
        }
    }

    /// <summary>
    /// Whether the storage area behind a row cannot be listed at all. Asked only after a read through a
    /// correct link failed, because that is the one case where the difference decides what gets reported:
    /// an unmounted share has no right to be written up as a deleted film. An area the row names but nobody
    /// configured counts as unreadable, since nothing about it can be proven either way.
    /// </summary>
    private bool RootUnreadable(SymlinkLink row, AppSettings snapshot)
    {
        MediaStorageArea? area = snapshot.Symlinks.StorageAreas
            .FirstOrDefault(candidate => string.Equals(candidate.Id, row.StorageAreaId, StringComparison.OrdinalIgnoreCase));
        if (area is null) return true;

        try
        {
            files.ListEntries(area.SeriesRoot);
            files.ListEntries(area.MoviesRoot);
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(exception, "Storage area {AreaId} of link {LinkId} could not be listed.", area.Id, row.Id);
            return true;
        }
    }

    /// <summary>
    /// The name of a file as both views of the same path mean it. The register keeps paths of this process
    /// and the snapshot keeps paths of the instance that reported them, so neither separator may be assumed
    /// away — which is why this does not go through one operating system's path rules.
    /// </summary>
    private static string FileNameOf(string path)
    {
        string[] parts = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? path : parts[^1];
    }

    private SymlinkFinding Finding(string code, string detail) => new(code, detail, clock.GetUtcNow());

    /// <summary>
    /// What one row told the pass: that it had something to say, that part of it could not be determined,
    /// and that a follow-up was queued for it.
    /// </summary>
    private readonly record struct RowAnswer(bool Reported, bool Undetermined, bool Queued);
}
