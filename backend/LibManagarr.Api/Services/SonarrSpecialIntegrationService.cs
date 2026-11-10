using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>
/// Carries a confirmed link the rest of the way inside Sonarr: monitoring off, a rescan of its own
/// series, the file that scan produced, the file information Sonarr may take from the chosen Radarr
/// file, and finally the name Sonarr's own rules ask for. Each step is a durable point of no return —
/// what a pass achieved is written down before the next thing is attempted, because the register, Sonarr
/// and the file system never share a transaction and a run that dies halfways has to resume rather than
/// start over.
/// </summary>
public sealed class SonarrSpecialIntegrationService(
    ISymlinkStore store,
    ISettingsService settings,
    SonarrClient sonarr,
    MediaPathMappingService paths,
    ISymlinkFileSystem files,
    TimeProvider clock,
    ILogger<SonarrSpecialIntegrationService> logger,
    SymlinkCapabilityService? capability = null,
    RadarrClient? radarr = null,
    ISnapshotSyncService? snapshotSync = null)
{
    /// <summary>
    /// How long one queued command may hold a link before the pass says so out loud instead of waiting
    /// again. The clock starts at the instance's own queue time, not at the last write here, because a
    /// pass that only polled would otherwise keep pushing the deadline away.
    /// </summary>
    private static readonly TimeSpan CommandWaitBudget = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Advances one link as far as it gets in this pass and stops at the first step that cannot be
    /// finished now. Waiting means the same pass should run again; blocked means somebody has to decide.
    /// </summary>
    public async Task<SymlinkSetupOutcome> RunSetupAsync(SymlinkLink link, CancellationToken cancellationToken)
    {
        AppSettings snapshot = await settings.GetSnapshotAsync(cancellationToken);
        if (!snapshot.Symlinks.Enabled) return SymlinkSetupOutcome.Waiting;
        ArrInstanceSettings? instance = snapshot.SonarrInstances
            .FirstOrDefault(candidate => string.Equals(candidate.Id, link.SonarrInstanceId, StringComparison.OrdinalIgnoreCase));
        if (instance is null)
        {
            return await BlockAsync(link, "sonarr-instance-missing",
                $"The Sonarr instance '{link.SonarrInstanceId}' this link belongs to is no longer configured.",
                conflict: true,
                cancellationToken);
        }

        SymlinkSetupStep open = link.SetupStep ?? SymlinkSetupStep.LinkCreated;
        if (open == SymlinkSetupStep.Renamed && capability is not null)
        {
            SymlinkCapabilityCheck check = await capability.GetStatusAsync(link.StorageAreaId, cancellationToken);
            if (!check.IsCurrent(snapshot.Symlinks.Revision) || check.Status != SymlinkCapabilityStatus.Available)
            {
                Pause(link, "capability-untested", "Run the capability test in this process before naming the link.");
                await PersistAsync(link, cancellationToken);
                return SymlinkSetupOutcome.Waiting;
            }
        }
        while (open != SymlinkSetupStep.Completed)
        {
            SymlinkSetupOutcome outcome;
            try
            {
                outcome = open switch
                {
                    SymlinkSetupStep.LinkCreated => Pause(link, "link-not-created",
                        "The file link itself does not exist yet; Sonarr is only touched once it does."),
                    SymlinkSetupStep.MonitoringDisabled => await DisableMonitoringAsync(link, instance, cancellationToken),
                    SymlinkSetupStep.RescanRequested => await RunScanAsync(link, instance, cancellationToken),
                    SymlinkSetupStep.EpisodeAssigned => await AssignEpisodeFileAsync(link, instance, cancellationToken),
                    SymlinkSetupStep.MetadataMatched => await MatchMetadataAsync(link, instance, cancellationToken),
                    SymlinkSetupStep.Renamed => await ApplySonarrNameAsync(link, instance, cancellationToken),
                    SymlinkSetupStep.Completed => SymlinkSetupOutcome.Completed,
                    _ => Pause(link, "setup-step-unknown",
                        $"The entry names setup step {(int)open}, which this version cannot act on.")
                };
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                // An instance that is unreachable is a reason to try again later, not a broken link. The
                // step stays exactly where it is, so the next pass resumes the same operation.
                Pause(link, "arr-unreachable", $"{instance.Name} could not be reached: {exception.Message}");
                outcome = SymlinkSetupOutcome.Waiting;
            }

            if (!await PersistAsync(link, cancellationToken)) return SymlinkSetupOutcome.Blocked;
            if (outcome != SymlinkSetupOutcome.Completed) return outcome;
            open = link.SetupStep ?? SymlinkSetupStep.LinkCreated;
        }

        return SymlinkSetupOutcome.Completed;
    }

    private async Task<SymlinkSetupOutcome> DisableMonitoringAsync(
        SymlinkLink link,
        ArrInstanceSettings instance,
        CancellationToken cancellationToken)
    {
        SonarrEpisodeResource? episode = await sonarr.FetchEpisodeAsync(instance, link.SonarrEpisodeId, cancellationToken);
        if (episode?.Id is null)
        {
            return Conflict(link, "sonarr-episode-missing",
                $"{instance.Name} has no episode {link.SonarrEpisodeId}, so nothing can be set up on it.");
        }

        // Only the chosen episode loses its monitoring. The series, its season and every other special
        // keep what they had, and the original value is kept for transparency instead of being restored
        // by this feature on its own.
        link.MonitoringOriginal ??= episode.Monitored ?? false;
        if (episode.Monitored == false)
        {
            link.MonitoringDisabled = true;
            link.SetupStep = SymlinkSetupStep.RescanRequested;
            return SymlinkSetupOutcome.Completed;
        }

        await sonarr.SetEpisodeMonitoredAsync(instance, link.SonarrEpisodeId, false, cancellationToken);
        SonarrEpisodeResource? readBack = await sonarr.FetchEpisodeAsync(instance, link.SonarrEpisodeId, cancellationToken);
        if (readBack?.Monitored != false)
        {
            return Pause(link, "monitoring-still-on",
                $"{instance.Name} still reports episode {link.SonarrEpisodeId} as monitored after the write, so the setup is not complete.");
        }

        link.MonitoringDisabled = true;
        link.SetupStep = SymlinkSetupStep.RescanRequested;
        return SymlinkSetupOutcome.Completed;
    }

    private async Task<SymlinkSetupOutcome> RunScanAsync(
        SymlinkLink link,
        ArrInstanceSettings instance,
        CancellationToken cancellationToken)
    {
        if (link.SonarrScanCommandId is null)
        {
            SonarrCommandResource? queued = await sonarr.StartRescanSeriesAsync(instance, link.SonarrSeriesId, cancellationToken);
            if (queued?.Id is null)
            {
                return Pause(link, "scan-not-queued",
                    $"{instance.Name} answered the rescan request without a command, so nothing was scanned.");
            }

            link.SonarrScanCommandId = queued.Id.Value.ToString(CultureInfo.InvariantCulture);
            return SymlinkSetupOutcome.Waiting;
        }

        SonarrCommandResource? status =
            await sonarr.FetchCommandAsync(instance, int.Parse(link.SonarrScanCommandId, CultureInfo.InvariantCulture), cancellationToken);
        SymlinkSetupOutcome waiting = AwaitCommand(link, status, "scan");
        if (waiting != SymlinkSetupOutcome.Completed) return waiting;

        // The id is the command this link still owes, so a finished one is dropped: a repeated setup
        // step has to trigger a real scan instead of trusting that an old command once succeeded.
        link.SonarrScanCommandId = null;
        link.SetupStep = SymlinkSetupStep.EpisodeAssigned;
        return SymlinkSetupOutcome.Completed;
    }

    private async Task<SymlinkSetupOutcome> AssignEpisodeFileAsync(
        SymlinkLink link,
        ArrInstanceSettings instance,
        CancellationToken cancellationToken)
    {
        SonarrEpisodeResource? episode = await sonarr.FetchEpisodeAsync(instance, link.SonarrEpisodeId, cancellationToken);
        if (episode?.EpisodeFileId is not int sonarrFileId)
        {
            return Pause(link, "episode-file-missing",
                $"The scan of series {link.SonarrSeriesId} left no file on episode {link.SonarrEpisodeId}. The link is kept and this step can be repeated.");
        }

        PathMappingResult instanceSide = await paths.TranslateToInstancePathAsync(
            link.SonarrInstanceId, ArrApplicationKind.Sonarr, link.LinkPath, cancellationToken);
        if (!instanceSide.Success)
        {
            return Conflict(link, "link-path-not-translatable", instanceSide.Failure!);
        }

        SonarrEpisodeFileResource? file = await sonarr.FetchEpisodeFileAsync(instance, sonarrFileId, cancellationToken);
        if (file?.Path is null)
        {
            return Pause(link, "sonarr-file-unreadable",
                $"{instance.Name} knows episode file {sonarrFileId} but reported no path for it.");
        }

        // The one check that keeps this feature honest: the file Sonarr put on the episode has to be the
        // link this entry owns. Anything else belongs to somebody else and stays untouched.
        if (!IsSamePath(file.Path, instanceSide.Path!))
        {
            return Conflict(link, "sonarr-file-not-the-link",
                $"{instance.Name} put episode {link.SonarrEpisodeId} at '{file.Path}', which is not the managed link '{instanceSide.Path}'.");
        }

        link.SonarrFileId = sonarrFileId;
        link.SetupStep = SymlinkSetupStep.MetadataMatched;
        return SymlinkSetupOutcome.Completed;
    }

    private async Task<SymlinkSetupOutcome> MatchMetadataAsync(
        SymlinkLink link,
        ArrInstanceSettings instance,
        CancellationToken cancellationToken)
    {
        if (link.SonarrFileId is not int sonarrFileId)
        {
            return Pause(link, "sonarr-file-missing",
                "This step needs the episode file the previous step found, and there is none.");
        }

        if (link.ExpectedMetadata is null) return Pause(link, "metadata-unknown", "No source metadata was recorded.");
        link.Findings.RemoveAll(value => value.Code.StartsWith("metadata-", StringComparison.Ordinal));
        if (!await OwnsEpisodeFileAsync(link, instance, cancellationToken))
            return Conflict(link, "sonarr-file-not-the-link", "The episode no longer owns the registered file. Nothing was written.");
        try
        {
            await sonarr.SetMappedEpisodeFileMetadataAsync(instance, sonarrFileId, link.ExpectedMetadata, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return Pause(link, "metadata-mapping-unavailable", exception.Message);
        }

        SonarrEpisodeFileResource? readBack = await sonarr.FetchEpisodeFileAsync(instance, sonarrFileId, cancellationToken);
        if (readBack?.Path is null)
        {
            return Pause(link, "sonarr-file-unreadable",
                $"{instance.Name} reported no path for episode file {sonarrFileId} after the write, so nothing could be confirmed.");
        }

        if (!string.IsNullOrWhiteSpace(link.ExpectedMetadata?.ReleaseGroup)
            && !string.Equals(readBack.ReleaseGroup, link.ExpectedMetadata?.ReleaseGroup, StringComparison.OrdinalIgnoreCase))
        {
            return Pause(link, "metadata-not-confirmed",
                $"{instance.Name} reports release group '{readBack.ReleaseGroup ?? "nothing"}' instead of the chosen Radarr value '{link.ExpectedMetadata?.ReleaseGroup}'. That version might not allow writing the field.");
        }

        link.ConfirmedMetadata = MetadataFrom(readBack);
        // What Sonarr derived for itself and did not confirm stays visible as a finding instead of
        // silently counting as done; the health check compares the same two sides later on.
        ReportDeviations(link);
        if (string.IsNullOrWhiteSpace(readBack.MediaInfo?.VideoCodec))
            return Pause(link, "metadata-media-info-unknown", "Sonarr has not confirmed video MediaInfo for this file. Check that media analysis is enabled.");
        if (link.Findings.Any(value => value.Code.StartsWith("metadata-", StringComparison.Ordinal)))
            return Pause(link, "metadata-not-confirmed", "The file information is incomplete or differs from the selected source.");
        link.SetupStep = SymlinkSetupStep.Renamed;
        return SymlinkSetupOutcome.Completed;
    }

    public static SymlinkFileMetadata MetadataFrom(SonarrEpisodeFileResource readBack) => new()
    {
        Origin = "sonarr",
        ReleaseGroup = readBack.ReleaseGroup,
        Languages = [.. readBack.Languages?
            .Select(language => language.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!) ?? []],
        Quality = readBack.Quality?.Quality?.Name,
        QualityRevision = readBack.Quality?.Revision,
        Size = readBack.Size,
        VideoCodec = readBack.MediaInfo?.VideoCodec,
        AudioCodec = readBack.MediaInfo?.AudioCodec,
        Resolution = readBack.MediaInfo?.Resolution
    };


    private async Task<SymlinkSetupOutcome> ApplySonarrNameAsync(
        SymlinkLink link,
        ArrInstanceSettings instance,
        CancellationToken cancellationToken)
    {
        if (link.NamingMove is not null) return await FinishNameMoveAsync(link, instance, cancellationToken);
        if (link.SonarrFileId is not int sonarrFileId)
        {
            return Pause(link, "sonarr-file-missing",
                "The name of a file Sonarr does not know yet cannot be settled.");
        }

        if (link.SonarrRenameCommandId is null)
        {
            SonarrNamingConfigResource? naming = await sonarr.FetchNamingConfigAsync(instance, cancellationToken);
            SonarrRenameResource? proposal = (await sonarr.FetchRenameProposalsAsync(instance, link.SonarrSeriesId, cancellationToken))
                .FirstOrDefault(candidate => (candidate.EpisodeFileId ?? candidate.Id) == sonarrFileId);

            if (proposal?.NewPath is not null)
            {
                SonarrSeriesResource? series = await sonarr.FetchSeriesAsync(instance, link.SonarrSeriesId, cancellationToken);
                if (series?.Path is null) return Pause(link, "series-path-unknown", "Sonarr returned no series path for the naming preview.");
                char separator = ArrPathRules.Detect(series.Path) == ArrPathSyntax.Windows ? '\\' : '/';
                string proposedPath = series.Path.TrimEnd('/', '\\') + separator + proposal.NewPath.Replace('\\', separator).Replace('/', separator);
                PathMappingResult translated = await paths.TranslateToLibManagarrPathAsync(instance.Id, ArrApplicationKind.Sonarr, proposedPath, cancellationToken);
                if (!translated.Success) return Conflict(link, "rename-path-invalid", translated.Failure!);
                PathMappingResult seriesPath = await paths.TranslateToLibManagarrPathAsync(instance.Id, ArrApplicationKind.Sonarr, series.Path, cancellationToken);
                if (!seriesPath.Success || !ArrPathRules.IsWithinRoot(seriesPath.Path!, translated.Path!))
                    return Conflict(link, "rename-outside-series", "The rename preview leaves the selected series directory.");
                link.DesiredFilename = FileNameOf(translated.Path!);
                link.NamingRevision = NamingRevisionOf(naming);
                if (!IsSamePath(link.LinkPath, translated.Path!) && files.EntryExists(translated.Path!))
                    return Conflict(link, "rename-destination-taken", "The proposed path already contains an unregistered entry.");
                link.NamingMove = new(link.LinkPath, translated.Path!, Path.GetRelativePath(Path.GetDirectoryName(translated.Path!)!, link.SourcePath));
                if (!await PersistAsync(link, cancellationToken)) return SymlinkSetupOutcome.Blocked;
                return await FinishNameMoveAsync(link, instance, cancellationToken);
            }

            if (proposal?.NewFilename is null)
            {
                // Two different reasons for no proposal, and only one of them is finished: the name can
                // already be what the rules ask for, or renaming can be off in that instance. Only the
                // second one is reported, because a name nobody proposed is not a proven name.
                if (naming?.RenameEpisodes == false)
                {
                    link.AppliedFilename = FileNameOf(link.LinkPath);
                    link.NamingRevision = NamingRevisionOf(naming);
                    Note(link, "renaming-disabled",
                        $"{instance.Name} has episode renaming switched off, so the link keeps the name it has. Its settings were not changed for this.");
                    return await FinishVerifiedAsync(link, cancellationToken);
                }

                if (naming?.RenameEpisodes != true || !await OwnsEpisodeFileAsync(link, instance, cancellationToken))
                    return Pause(link, "rename-proposal-missing", "No naming evidence or matching episode file was returned.");
                link.AppliedFilename = FileNameOf(link.LinkPath);
                link.DesiredFilename = link.AppliedFilename;
                link.NamingRevision = NamingRevisionOf(naming);
                return await FinishVerifiedAsync(link, cancellationToken);
            }

            if (proposal.NewFilename.IndexOfAny(['/', '\\']) >= 0)
                return Conflict(link, "rename-preview-unsupported", "The legacy filename preview must contain a filename only.");
            if (string.Equals(FileNameOf(link.LinkPath), proposal.NewFilename, StringComparison.Ordinal))
            {
                if (!await OwnsEpisodeFileAsync(link, instance, cancellationToken))
                    return Conflict(link, "sonarr-file-not-the-link", "The episode file changed before its name could be verified.");
                link.DesiredFilename = proposal.NewFilename;
                link.AppliedFilename = FileNameOf(link.LinkPath);
                link.NamingRevision = NamingRevisionOf(naming);
                return await FinishVerifiedAsync(link, cancellationToken);
            }

            if (!await OwnsEpisodeFileAsync(link, instance, cancellationToken))
                return Conflict(link, "sonarr-file-not-the-link", "The episode file changed before renaming.");
            link.DesiredFilename = proposal.NewFilename;
            link.NamingRevision = NamingRevisionOf(naming);
            string destination = Path.Combine(Path.GetDirectoryName(link.LinkPath)!, proposal.NewFilename);
            if (files.EntryExists(destination))
                return Conflict(link, "rename-destination-taken", "The proposed path already contains an unregistered entry.");
            link.NamingMove = new(link.LinkPath, destination, Path.GetRelativePath(Path.GetDirectoryName(destination)!, link.SourcePath));
            if (!await PersistAsync(link, cancellationToken)) return SymlinkSetupOutcome.Blocked;
            return await FinishNameMoveAsync(link, instance, cancellationToken);
        }

        SonarrCommandResource? status =
            await sonarr.FetchCommandAsync(instance, int.Parse(link.SonarrRenameCommandId, CultureInfo.InvariantCulture), cancellationToken);
        SymlinkSetupOutcome waiting = AwaitCommand(link, status, "rename");
        if (waiting != SymlinkSetupOutcome.Completed) return waiting;

        SonarrEpisodeFileResource? file = await sonarr.FetchEpisodeFileAsync(instance, sonarrFileId, cancellationToken);
        if (file?.Path is null)
        {
            return Pause(link, "sonarr-file-unreadable",
                $"{instance.Name} reported no path for episode file {sonarrFileId} after the rename, so the name cannot be verified.");
        }

        PathMappingResult renamed = await paths.TranslateToLibManagarrPathAsync(
            link.SonarrInstanceId, ArrApplicationKind.Sonarr, file.Path, cancellationToken);
        if (!renamed.Success)
        {
            return Conflict(link, "renamed-path-not-translatable", renamed.Failure!);
        }

        if (!IsSamePath(renamed.Path!, link.LinkPath))
        {
            SymlinkSetupOutcome? trouble = AdoptRenamedPath(link, renamed.Path!);
            if (trouble is not null) return trouble.Value;
        }

        link.AppliedFilename = FileNameOf(file.Path);
        link.SonarrRenameCommandId = null;
        return await FinishVerifiedAsync(link, cancellationToken);
    }

    /// <summary>
    /// Sonarr moved the entry, so the register has to follow it — but only if what stands at the new path
    /// really is this link and still points at the chosen Radarr file. A copy in place of the link, or a
    /// link that leads somewhere else, is reported instead of repaired: deleting the one entry that
    /// connects the episode to a file is not a compensation this service should attempt on its own.
    /// </summary>
    private SymlinkSetupOutcome? AdoptRenamedPath(SymlinkLink link, string newPath)
    {
        if (!files.EntryExists(newPath))
        {
            return Pause(link, "renamed-link-missing",
                $"Sonarr reports the file at '{newPath}', but nothing is there for this process.");
        }

        string? linkText = files.ReadLinkTarget(newPath);
        if (linkText is null)
        {
            return Conflict(link, "renamed-entry-not-a-link",
                $"'{newPath}' is a regular file rather than the managed link, so the link was replaced by a copy. The registered entry stays as it is and nothing is deleted.");
        }

        // A relative link text only means something from its own folder, which is exactly what a rename
        // into another folder can change. Getting the full path is what settles ".." segments before the
        // comparison instead of comparing a path that still walks back and forth.
        string resolved = Path.GetFullPath(Path.IsPathRooted(linkText)
            ? linkText
            : Path.Combine(Path.GetDirectoryName(newPath) ?? string.Empty, linkText));
        if (!ArrPathRules.SegmentEquals(resolved, link.SourcePath, ArrPathRules.RuntimeSyntax))
        {
            return Conflict(link, "renamed-link-stale",
                $"'{newPath}' points at '{linkText}', which is not the chosen Radarr file '{Path.GetFileName(link.SourcePath)}' anymore.");
        }

        link.LinkPath = newPath;
        link.LinkText = linkText;
        return null;
    }

    /// <summary>
    /// One queued command, read once. Waiting, failed and overdue are three different answers here
    /// because each of them leaves the link in a different place.
    /// </summary>
    private SymlinkSetupOutcome AwaitCommand(SymlinkLink link, SonarrCommandResource? status, string what)
    {
        if (status is null)
        {
            return Pause(link, $"{what}-command-unknown",
                $"The instance no longer knows the {what} command this link was waiting for.");
        }

        if (!status.IsFinished)
        {
            if (status.Queued is { } queuedAt && clock.GetUtcNow() - queuedAt > CommandWaitBudget)
            {
                return Pause(link, $"{what}-wait-expired",
                    $"The {what} command is still running after {CommandWaitBudget.TotalMinutes:0} minutes; the step stays open and can be repeated.");
            }

            return SymlinkSetupOutcome.Waiting;
        }

        if (!status.Completed)
        {
            return Pause(link, $"{what}-failed", status.Message ?? $"The {what} command ended without completing.");
        }

        return SymlinkSetupOutcome.Completed;
    }

    private void ReportDeviations(SymlinkLink link)
    {
        if (link.ExpectedMetadata is not { } expected || link.ConfirmedMetadata is not { } confirmed)
        {
            return;
        }

        if (expected.Size is not null && confirmed.Size != expected.Size)
        {
            Note(link, "metadata-size-deviation",
                $"{confirmed.Origin} measured {confirmed.Size} bytes while the chosen Radarr file has {expected.Size}.");
        }

        foreach ((string field, string? wanted, string? actual) in new[]
        {
            ("video-codec", expected.VideoCodec, confirmed.VideoCodec),
            ("audio-codec", expected.AudioCodec, confirmed.AudioCodec),
            ("resolution", expected.Resolution, confirmed.Resolution)
        })
            if (!string.IsNullOrWhiteSpace(wanted) && !string.Equals(wanted, actual, StringComparison.OrdinalIgnoreCase))
                Note(link, "metadata-" + field + "-deviation", $"Sonarr reports '{actual ?? "unknown"}' for {field}, expected '{wanted}'.");

        if (expected.Quality is not null && !string.Equals(expected.Quality, confirmed.Quality, StringComparison.OrdinalIgnoreCase))
        {
            Note(link, "metadata-quality-deviation",
                $"{confirmed.Origin} reads the quality as '{confirmed.Quality ?? "nothing"}'; the Radarr file was labelled '{expected.Quality}'.");
        }
        if (expected.QualityRevision is not null && expected.QualityRevision != confirmed.QualityRevision)
            Note(link, "metadata-quality-revision-deviation", "Sonarr has not confirmed the source quality revision/repack information.");

        if (expected.Languages.Count > 0
            && !expected.Languages.All(language => confirmed.Languages.Contains(language, StringComparer.OrdinalIgnoreCase)))
        {
            Note(link, "metadata-languages-deviation",
                $"{confirmed.Origin} found no audio language of the chosen Radarr file ({string.Join(", ", expected.Languages)}).");
        }
    }

    /// <summary>
    /// Writes what this pass achieved before it does anything else. The revision the store advanced on a
    /// successful write is followed here, because one pass writes the same row again for the next step.
    /// </summary>
    private async Task<bool> PersistAsync(SymlinkLink link, CancellationToken cancellationToken)
    {
        long expectedRevision = link.Revision;
        link.UpdatedAt = clock.GetUtcNow();
        if (!await store.UpdateLinkAsync(link, expectedRevision, cancellationToken))
        {
            logger.LogWarning(
                "Symlink link {LinkId} was changed by another operation while the setup run worked on it; the run stopped instead of overwriting it.",
                link.Id);
            return false;
        }

        link.Revision = expectedRevision + 1;
        return true;
    }

    private async Task<SymlinkSetupOutcome> BlockAsync(
        SymlinkLink link,
        string code,
        string detail,
        bool conflict,
        CancellationToken cancellationToken)
    {
        if (conflict) link.OperationStatus = SymlinkOperationStatus.Conflict;
        Note(link, code, detail);
        await PersistAsync(link, cancellationToken);
        return SymlinkSetupOutcome.Blocked;
    }

    private SymlinkSetupOutcome Pause(SymlinkLink link, string code, string detail)
    {
        Note(link, code, detail);
        return SymlinkSetupOutcome.Blocked;
    }

    private SymlinkSetupOutcome Conflict(SymlinkLink link, string code, string detail)
    {
        link.OperationStatus = SymlinkOperationStatus.Conflict;
        return Pause(link, code, detail);
    }

    private static SymlinkSetupOutcome Finished(SymlinkLink link)
    {
        link.SetupStep = SymlinkSetupStep.Completed;
        link.OperationStatus = SymlinkOperationStatus.Active;
        return SymlinkSetupOutcome.Completed;
    }

    private async Task<SymlinkSetupOutcome> FinishVerifiedAsync(SymlinkLink link, CancellationToken token)
    {
        if (radarr is not null && link.AutoFollowUp)
        {
            ArrInstanceSettings? instance = (await settings.GetSnapshotAsync(token)).RadarrInstances.FirstOrDefault(value => value.Id == link.RadarrInstanceId);
            if (instance is null) return Conflict(link, "radarr-instance-missing", "The selected Radarr instance is no longer configured.");
            IReadOnlyList<RadarrMovieFileResource> current = await radarr.FetchMovieFilesAsync(instance, link.RadarrMovieId, token);
            if (current.Count != 1 || SymlinkService.SourceVersionOf(current[0]) != link.SourceVersion)
            {
                Pause(link, "source-moved-during-setup", "The Radarr source changed during setup. The next pass follows its current version.");
                return SymlinkSetupOutcome.Waiting;
            }
        }
        link.Findings.RemoveAll(value => value.Code == "source-moved-during-setup");
        if (snapshotSync is not null)
        {
            try
            {
                await snapshotSync.SyncSonarrSeriesAsync(link.SonarrInstanceId, link.SonarrSeriesId, token);
            }
            catch (HttpRequestException)
            {
                Note(link, "library-sync-pending", "The special is ready in Sonarr, but updating the LibManagarr library is still pending. It will be retried automatically.");
                return SymlinkSetupOutcome.Waiting;
            }
            link.Findings.RemoveAll(value => value.Code == "library-sync-pending");
        }
        return Finished(link);
    }

    /// <summary>
    /// The newest finding for a code wins: a step that reports the same problem on every pass must not
    /// bury the register in copies of it.
    /// </summary>
    private void Note(SymlinkLink link, string code, string detail)
    {
        link.Findings.RemoveAll(existing => existing.Code == code);
        link.Findings.Add(new SymlinkFinding(code, detail, clock.GetUtcNow()));
        logger.LogWarning("Symlink link {LinkId} stopped with {Code}: {Detail}", link.Id, code, detail);
    }

    /// <summary>
    /// Compares two paths in the grammar they are actually written in. Both sides of a comparison with
    /// an instance speak that instance's syntax, which is not necessarily this process' one.
    /// </summary>
    private static bool IsSamePath(string left, string right)
    {
        if (!ArrPathRules.TryParse(left, out ParsedPath? leftPath, out _)
            || !ArrPathRules.TryParse(right, out ParsedPath? rightPath, out _)
            || leftPath!.Syntax != rightPath!.Syntax)
        {
            return false;
        }

        return ArrPathRules.SegmentEquals(left, right, leftPath.Syntax);
    }

    private static string FileNameOf(string path)
    {
        int cut = path.TrimEnd('/', '\\').LastIndexOfAny('/', '\\');
        return cut < 0 ? path : path[(cut + 1)..];
    }

    public static string NamingRevisionOf(SonarrNamingConfigResource? naming)
    {
        // Names the rule set the desired name came from, so a later change of Sonarr's own patterns
        // shows up as a different revision instead of an unexplained filename.
        string rules = System.Text.Json.JsonSerializer.Serialize(naming);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rules)))[..12];
    }

    private async Task<bool> OwnsEpisodeFileAsync(SymlinkLink link, ArrInstanceSettings instance, CancellationToken cancellationToken)
    {
        SonarrEpisodeResource? episode = await sonarr.FetchEpisodeAsync(instance, link.SonarrEpisodeId, cancellationToken);
        if (episode?.EpisodeFileId != link.SonarrFileId) return false;
        SonarrEpisodeFileResource? file = await sonarr.FetchEpisodeFileAsync(instance, link.SonarrFileId!.Value, cancellationToken);
        if (file?.Path is null) return false;
        PathMappingResult translated = await paths.TranslateToLibManagarrPathAsync(instance.Id, ArrApplicationKind.Sonarr, file.Path, cancellationToken);
        return translated.Success && IsSamePath(translated.Path!, link.LinkPath)
            && files.ReadLinkTarget(link.LinkPath) == link.LinkText && OwnLink(link.LinkPath, link.SourcePath);
    }

    private async Task<SymlinkSetupOutcome> FinishNameMoveAsync(SymlinkLink link, ArrInstanceSettings instance, CancellationToken token)
    {
        SymlinkNameMove move = link.NamingMove!;
        SymlinkPathGuard.CheckParents(files, move.PreviousPath);
        SymlinkPathGuard.CheckParents(files, move.NewPath);
        if (move.ScanCommandId is null)
        {
            if (files.EntryExists(move.PreviousPath) && !OwnLink(move.PreviousPath, link.SourcePath))
                return Conflict(link, "rename-entry-foreign", "The old link was changed externally; nothing was removed.");
            if (files.EntryExists(move.NewPath) && !OwnLink(move.NewPath, link.SourcePath))
                return Conflict(link, "rename-destination-taken", "Another entry occupies Sonarr's proposed path.");
            if (!files.EntryExists(move.NewPath))
            {
                files.OpenForRead(link.SourcePath);
                files.CreateDirectory(Path.GetDirectoryName(move.NewPath)!);
                files.CreateFileSymlink(move.NewPath, move.LinkText);
            }
            if (!OwnLink(move.NewPath, link.SourcePath)) return Conflict(link, "rename-link-invalid", "The new entry is not the expected link.");
            files.OpenForRead(move.NewPath);
            if (!IsSamePath(move.PreviousPath, move.NewPath) && files.EntryExists(move.PreviousPath)) files.DeleteEntry(move.PreviousPath);
            SonarrCommandResource? queued = await sonarr.StartRescanSeriesAsync(instance, link.SonarrSeriesId, token);
            if (queued?.Id is null) return Pause(link, "rename-scan-not-queued", "Sonarr returned no rescan command for the renamed link.");
            link.NamingMove = move with { ScanCommandId = queued.Id.Value.ToString(CultureInfo.InvariantCulture) };
            return SymlinkSetupOutcome.Waiting;
        }
        SonarrCommandResource? command = await sonarr.FetchCommandAsync(instance, int.Parse(move.ScanCommandId, CultureInfo.InvariantCulture), token);
        SymlinkSetupOutcome state = AwaitCommand(link, command, "rename-scan");
        if (state != SymlinkSetupOutcome.Completed) return state;
        SonarrEpisodeResource? episode = await sonarr.FetchEpisodeAsync(instance, link.SonarrEpisodeId, token);
        SonarrEpisodeFileResource? file = episode?.EpisodeFileId is int fileId ? await sonarr.FetchEpisodeFileAsync(instance, fileId, token) : null;
        PathMappingResult? mapped = file?.Path is null ? null : await paths.TranslateToLibManagarrPathAsync(instance.Id, ArrApplicationKind.Sonarr, file.Path, token);
        if (mapped?.Success != true || !IsSamePath(mapped.Path!, move.NewPath) || !OwnLink(move.NewPath, link.SourcePath))
            return Pause(link, "rename-association-unconfirmed", "Sonarr has not assigned the renamed link to the selected episode.");
        link.LinkPath = move.NewPath;
        link.LinkText = files.ReadLinkTarget(move.NewPath)!;
        link.SonarrFileId = file!.Id;
        link.AppliedFilename = FileNameOf(move.NewPath);
        link.NamingMove = null;
        link.SetupStep = SymlinkSetupStep.MetadataMatched;
        return SymlinkSetupOutcome.Completed;
    }

    private bool OwnLink(string path, string source) => files.IsSymlink(path) && files.ReadLinkTarget(path) is not null
        && SymlinkPathGuard.ResolvesTo(files, path, source);
}
