using System.Globalization;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;
using Microsoft.AspNetCore.Http;

namespace LibManagarr.Api.Services;

/// <summary>
/// What creating one link would do and what stands in the way. The draft is the register row a confirm
/// would write, filled from the live reads of this very preview, so a preview cannot promise a path the
/// create would then have to invent; it has no id, because nothing exists yet. Blockers stop the create,
/// warnings travel with the entry.
/// </summary>
public sealed record SymlinkLinkPlan(
    SymlinkLink Draft,
    IReadOnlyList<SymlinkFinding> Blockers,
    IReadOnlyList<SymlinkFinding> Warnings)
{
    public bool Runnable => Blockers.Count == 0;
}

/// <summary>
/// The register's only writer: it turns one confirmed pair into a file link and a row, and it is the
/// only place that decides what may be overwritten. Database and file system share no transaction, so
/// every operation is first written down as owed, then carried out, then confirmed — which is what lets
/// an interrupted run be finished instead of guessed at.
/// </summary>
/// <remarks>
/// Nothing here deletes a source file, and nothing here compensates by deleting after a failure: a link
/// that could not be verified stays on disk as a conflict for a person to look at.
/// </remarks>
public sealed class SymlinkService(
    ISymlinkStore store,
    ISettingsService settings,
    MediaPathMappingService paths,
    SymlinkCapabilityService capability,
    SymlinkSetupRequests setupRequests,
    RadarrClient radarr,
    SonarrClient sonarr,
    ISymlinkFileSystem files,
    TimeProvider clock,
    ILogger<SymlinkService> logger)
{
    /// <summary>
    /// Where a special's file goes. Sonarr's own folder for specials is configurable, but its tokens are
    /// deliberately not rebuilt here: the file only has to sit inside the series folder for the scan to
    /// find it, and the rename step later moves it exactly where that instance's rules want it.
    /// </summary>
    private const string SpecialsFolderName = "Specials";

    /// <summary>
    /// The codes an operation writes when it stops and its resumption has to agree with. They are named
    /// here because the operation and its resumption are two calls that describe the same obstacle.
    /// </summary>
    private const string LinkNotCreatedCode = "link-not-created";
    private const string EntryNotALinkCode = "delete-entry-not-a-link";
    private const string DeletionRefusedCode = "delete-not-removed";
    private const string RemovedOnPurposeDetail = "The managed link of this pair was removed on purpose.";

    /// <summary>
    /// What the register says about an entry that owes something nobody has carried out yet: the source
    /// moved, the way onto it stopped, or a restored backup has not been compared with the file system.
    /// They are findings because the entry is still readable while it waits, and a code because the run
    /// that finishes it has to know which note to take away again.
    /// </summary>
    private const string SourceReplacedCode = "source-replaced";
    private const string UpdateNotAppliedCode = "update-not-applied";
    private const string ReplacedLinkLeftBehindCode = "replaced-link-left-behind";
    private const string RestoredUncheckedCode = "restored-unchecked";

    /// <summary>
    /// What the comparison after a restore answers when the file system and the register do not agree. It
    /// is one code because the person reading it does the same thing either way — look at that path — while
    /// the detail says which of the two is the one that moved.
    /// </summary>
    private const string RestoredEntryDiffersCode = "restored-entry-differs";

    /// <summary>
    /// Shows one pair as it stands right now: both live instances, both path translations, the storage
    /// area the two halves share and the entry that would be written. Reads only — no row, no file.
    /// </summary>
    public async Task<SymlinkLinkPlan> PreviewAsync(
        SpecialMoviePair pair,
        int? radarrFileId,
        CancellationToken cancellationToken)
    {
        (SymlinkLink draft, List<Obstacle> blockers, List<SymlinkFinding> warnings) =
            await PlannedAsync(pair, radarrFileId, replaces: null, cancellationToken);
        return new SymlinkLinkPlan(draft, [.. blockers.Select(blocker => blocker.Finding)], warnings);
    }

    /// <summary>
    /// Confirms a pair: the preview is computed again from live data, the row is reserved as owed, the
    /// link is created and only then is the entry confirmed. The Sonarr side is not part of this call —
    /// it starts from the row's setup step, so an instance that answers slowly never hangs the request.
    /// </summary>
    public async Task<SymlinkLink> CreateAsync(
        SpecialMoviePair pair,
        int? radarrFileId,
        int configRevision,
        string? suggestionId,
        CancellationToken cancellationToken)
    {
        SymlinkSettings configuration = (await settings.GetSnapshotAsync(cancellationToken)).Symlinks;
        if (configuration.Revision != configRevision)
        {
            throw Conflict("The path configuration changed after this preview, so its paths are not the current ones.",
                "Ask for a preview again and confirm from that one.");
        }

        // Checked before anything is written, because the register keeps exactly one live entry per
        // special and the database index is the last resort rather than the usual way in.
        SymlinkLink? managed = (await store.GetLinksAsync(cancellationToken))
            .FirstOrDefault(link => SharesPair(link, pair) && link.OperationStatus != SymlinkOperationStatus.DeletePending);
        if (managed is not null)
        {
            throw Conflict($"Sonarr episode {pair.SonarrEpisodeId} is already managed by entry '{managed.Id}'.",
                "Edit or remove that entry instead of adding a second one.");
        }

        (SymlinkLink draft, List<Obstacle> blockers, _) = await PlannedAsync(pair, radarrFileId, replaces: null, cancellationToken);
        if (blockers.Count > 0)
        {
            Obstacle blocker = blockers[0];
            throw new ApiProblemException(blocker.StatusCode, "The link cannot be created", blocker.Finding.Detail!);
        }

        draft.Id = UuidV7.Create();
        draft.SuggestionId = suggestionId;
        draft.UpdatedAt = clock.GetUtcNow();
        await store.InsertLinkAsync(draft, cancellationToken);
        await MarkSuggestionAsync(suggestionId, SymlinkSuggestionStatus.Creating, draft.Id, detail: null, cancellationToken);

        try
        {
            PlaceLink(draft);
        }
        catch (ApiProblemException exception)
        {
            // The row stays as the record of what was attempted and why it stopped, which is what the
            // next run resumes from. Removing it would lose the only trace of the owed operation.
            await MarkConflictAsync(draft, "link-not-created", exception.Detail, cancellationToken);
            throw;
        }

        draft.OperationStatus = SymlinkOperationStatus.Active;
        draft.SetupStep = SymlinkSetupStep.MonitoringDisabled;
        draft.UpdatedAt = clock.GetUtcNow();
        if (!await store.UpdateLinkAsync(draft, draft.Revision, cancellationToken))
        {
            throw Conflict("The new entry changed while it was being created.", "Reload the register and try again.");
        }

        await MarkSuggestionAsync(suggestionId, SymlinkSuggestionStatus.Completed, draft.Id, detail: null, cancellationToken);
        logger.LogInformation(
            "Created the managed link {LinkId} at {LinkPath} for Sonarr episode {EpisodeId}.",
            draft.Id, draft.LinkPath, draft.SonarrEpisodeId);
        // The Sonarr half of the operation is owed from here on and is deliberately not part of this
        // call, so an instance that answers slowly cannot hold a request open.
        setupRequests.Request();
        return (await store.GetLinkAsync(draft.Id, cancellationToken))!;
    }

    /// <summary>
    /// The one decision a confirmed entry leaves to the user afterwards: whether later Radarr upgrades of
    /// this source keep being followed. Every other field belongs to the pairing or to the file system.
    /// Turning it on says so about the file that is current right now, too — an entry that is already
    /// behind is marked for its new source in the same write, so switching following on is enough to start
    /// it and does not wait for the library to be synchronized again.
    /// </summary>
    public async Task<SymlinkLink?> SetAutoFollowUpAsync(
        string id,
        bool enabled,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        SymlinkLink? link = await store.GetLinkAsync(id, cancellationToken);
        if (link is null) return null;
        if (link.Revision != expectedRevision) throw StaleRevision();

        link.AutoFollowUp = enabled;
        link.UpdatedAt = clock.GetUtcNow();
        if (enabled && link.OperationStatus == SymlinkOperationStatus.Active && link.SetupStep == SymlinkSetupStep.Completed)
        {
            AppSettings snapshot = await settings.GetSnapshotAsync(cancellationToken);
            MediaFileSnapshot? current = SnapshotFileOf(snapshot, link);
            if (current?.Id is int fileId && SourceVersionOf(fileId, current.Size) != link.SourceVersion)
            {
                string instanceName = snapshot.RadarrInstances
                    .FirstOrDefault(instance => string.Equals(instance.Id, link.RadarrInstanceId, StringComparison.OrdinalIgnoreCase))
                    ?.Name ?? link.RadarrInstanceId;
                if (!await MarkUpdateOwedAsync(link, current, instanceName, cancellationToken)) throw StaleRevision();
                return await store.GetLinkAsync(id, cancellationToken);
            }
        }

        if (!await store.UpdateLinkAsync(link, expectedRevision, cancellationToken)) throw StaleRevision();
        return await store.GetLinkAsync(id, cancellationToken);
    }

    /// <summary>
    /// What the library sync asks for: every finished entry whose movie has another file in the snapshots
    /// than the one the entry points at is marked as owed a way onto the new one. Marking is what makes the
    /// intent survive a restart; the snapshots are never trusted as the source itself, which is why the run
    /// that carries the mark out reads Radarr again. Entries that are not finished, that stopped for a
    /// person or that are marked already are left exactly as they are — several upgrades in a row follow the
    /// newest stand once, not each other.
    /// </summary>
    public async Task<int> MarkReplacedSourcesAsync(CancellationToken cancellationToken)
    {
        List<SymlinkLink> followable = (await store.GetLinksAsync(cancellationToken)).Where(FollowsUpgrades).ToList();
        if (followable.Count == 0) return 0;

        AppSettings snapshot = await settings.GetSnapshotAsync(cancellationToken);
        int marked = 0;
        foreach (SymlinkLink link in followable)
        {
            if (await MarkReplacedSourceAsync(link, snapshot, cancellationToken))
            {
                marked += 1;
            }
        }

        if (marked > 0)
        {
            logger.LogInformation("{Count} managed links were marked for follow-up because their Radarr movie has another file now.", marked);
        }

        return marked;
    }

    /// <summary>
    /// The same decision about one entry that <see cref="MarkReplacedSourcesAsync"/> makes about all of
    /// them, for the caller that is looking at a single row — which is the health check, and the reason
    /// this is a method rather than a second comparison next to the first. Two ways of deciding whether a
    /// movie moved would be two opinions about the same file, and only one of them queues the follow-up.
    /// The answer is false for every row whose movie did not move, so it says nothing about why; the
    /// caller that has to report the difference looks at <see cref="SnapshotFileOf"/> itself.
    /// </summary>
    public async Task<bool> MarkReplacedSourceAsync(SymlinkLink link, AppSettings snapshot, CancellationToken cancellationToken)
    {
        if (!FollowsUpgrades(link)) return false;

        MediaFileSnapshot? current = SnapshotFileOf(snapshot, link);
        if (current is null || current.Id is null || SourceVersionOf(current.Id, current.Size) == link.SourceVersion)
        {
            return false;
        }

        string instanceName = snapshot.RadarrInstances
            .FirstOrDefault(instance => string.Equals(instance.Id, link.RadarrInstanceId, StringComparison.OrdinalIgnoreCase))
            ?.Name ?? link.RadarrInstanceId;
        if (await MarkUpdateOwedAsync(link, current, instanceName, cancellationToken))
        {
            // The store advanced the row, so the copy in hand follows it. Without this, a caller that looks
            // at the same entry again in the same pass — the health check does — would lose against a write
            // it made itself.
            link.Revision += 1;
            return true;
        }

        logger.LogInformation("Entry {LinkId} changed while its pending update was being written; the next pass looks again.", link.Id);
        return false;
    }

    /// <summary>
    /// Whether a row is owed the way onto its movie's newest file: a person has to have asked for it, and
    /// the entry has to be standing on its own feet. A row that is still being set up, one that stopped for
    /// a person and one whose following was switched off are left alone — the first two because their own
    /// pass reports about them, the last because that is the switch doing what it promises.
    /// </summary>
    private static bool FollowsUpgrades(SymlinkLink link) =>
        link.AutoFollowUp
        && link.OperationStatus == SymlinkOperationStatus.Active
        && link.SetupStep == SymlinkSetupStep.Completed;

    /// <summary>
    /// Writes the answer that this entry is owed the way onto the file its movie has now. It is one method
    /// because the library sync and the person who switches following on are the same finding about the
    /// same entry, noticed at two different moments.
    /// </summary>
    private async Task<bool> MarkUpdateOwedAsync(
        SymlinkLink link,
        MediaFileSnapshot current,
        string instanceName,
        CancellationToken cancellationToken)
    {
        link.OperationStatus = SymlinkOperationStatus.Updating;
        // Back to the file half: the entry has to be pointed at the new source before Sonarr is told about
        // it again, which is the same point a creation starts from.
        link.SetupStep = SymlinkSetupStep.LinkCreated;
        link.UpdateRevision += 1;
        link.UpdatedAt = clock.GetUtcNow();
        Note(link, SourceReplacedCode,
            $"{instanceName} has file {current.Id} of movie {link.RadarrMovieId} now, while this entry still points at file {link.RadarrFileId}.");
        return await store.UpdateLinkAsync(link, link.Revision, cancellationToken);
    }

    /// <summary>
    /// What a completed restore asks for: nothing in the register may be acted on before it has been seen
    /// again, because a backup says what was registered, not what the media tree looks like today. This
    /// writes no link and removes none — it only says on every entry that its check is owed, which is what
    /// keeps a resumption of an old operation from touching files nobody has compared since.
    /// </summary>
    public async Task<int> MarkRestoredEntriesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SymlinkLink> rows = await store.GetLinksAsync(cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();
        int marked = 0;
        foreach (SymlinkLink link in rows)
        {
            if (link.Findings.Any(finding => finding.Code == RestoredUncheckedCode)) continue;

            link.UpdatedAt = now;
            Note(link, RestoredUncheckedCode,
                "This entry came back from a backup, so its link and its Sonarr entry have not been compared with what is really there.");
            if (await store.UpdateLinkAsync(link, link.Revision, cancellationToken))
            {
                marked += 1;
            }
            else
            {
                logger.LogInformation("Entry {LinkId} changed while its restore marker was being written.", link.Id);
            }
        }

        logger.LogInformation("{Count} managed links came back from a backup and are waiting to be checked.", marked);
        return marked;
    }

    /// <summary>
    /// Whether an entry waits for the check a restored backup owes it. While it does, no operation of its
    /// own may be carried out — not the link it still owes and not the one it was to lose.
    /// </summary>
    public static bool WaitsForRestoreCheck(SymlinkLink link) =>
        link.Findings.Any(finding => finding.Code == RestoredUncheckedCode);

    /// <summary>
    /// Compares an entry that came back from a backup with the file system, which is the only answer the
    /// register does not carry itself. What it finds is written on the row; nothing here writes a link,
    /// removes one or touches what stands at the path. An entry whose own link is still there as this
    /// entry made it loses the marker and is workable again; one that disagrees is stopped for a person,
    /// because the two possible explanations — the media tree moved, or this backup is from another
    /// installation — must not be told apart by a timer. An entry that was on its way out keeps its
    /// deletion: what stands at its path is answered by the deletion itself, which never removes anything
    /// that is not the link it made.
    /// </summary>
    public async Task<SymlinkSetupOutcome> CheckRestoredEntryAsync(SymlinkLink link, CancellationToken cancellationToken)
    {
        bool present = files.EntryExists(link.LinkPath);
        bool ownLink = present && IsOwnLinkAt(link, link.LinkPath);
        // Only an entry whose link was once proven has an expectation the file system can contradict: for
        // one that still owes its first link, finding nothing there is the expected answer, and whatever
        // stands in its way is refused by the creation after it. An entry on its way out keeps its
        // deletion, which never removes anything it did not make.
        bool linkClaimed = link.SetupStep is not null && link.SetupStep != SymlinkSetupStep.LinkCreated;
        bool workable = ownLink
            || link.OperationStatus == SymlinkOperationStatus.DeletePending
            || !linkClaimed;
        DateTimeOffset now = clock.GetUtcNow();
        link.LastCheckedAt = now;
        link.UpdatedAt = now;
        if (workable)
        {
            link.LastSuccessfulCheckAt = now;
        }

        link.Findings = [.. link.Findings.Where(finding => finding.Code != RestoredUncheckedCode)];
        if (!workable)
        {
            link.OperationStatus = SymlinkOperationStatus.Conflict;
            link.Findings =
            [
                .. link.Findings,
                new SymlinkFinding(RestoredEntryDiffersCode, present
                    ? $"'{link.LinkPath}' is not the link this entry made, so it was left exactly where it is. Check whether this backup belongs to this media tree."
                    : $"'{link.LinkPath}' is not there anymore, so this entry describes a link the file system does not have. Check the media tree before this entry is used again.",
                    now)
            ];
        }

        if (!await store.UpdateLinkAsync(link, link.Revision, cancellationToken))
        {
            return SymlinkSetupOutcome.Blocked;
        }

        string answer = "still its own link";
        if (!ownLink)
        {
            answer = linkClaimed ? "no link of its own to be found" : "nothing was expected at its path yet";
        }

        logger.LogInformation(
            "Checked the restored managed link {LinkId} against {LinkPath}: {Answer}.", link.Id, link.LinkPath, answer);
        return workable ? SymlinkSetupOutcome.Waiting : SymlinkSetupOutcome.Blocked;
    }

    /// <summary>
    /// Removes an entry: the intent is written down first, the link is deleted as a link — never through
    /// it — and only then does the row go. What is not a link anymore is left where it is, because a
    /// file this feature did not make is not this call's to remove. Sonarr keeps its episode as it is;
    /// neither monitoring is restored nor is any download started.
    /// </summary>
    public async Task<bool> DeleteAsync(string id, long expectedRevision, CancellationToken cancellationToken)
    {
        SymlinkLink? link = await store.GetLinkAsync(id, cancellationToken);
        if (link is null) return false;
        if (link.Revision != expectedRevision) throw StaleRevision();

        if (files.EntryExists(link.LinkPath) && !files.IsSymlink(link.LinkPath))
        {
            // Said before anything is written down, because this row is not owed a deletion at all: what
            // stands at its path is not something this feature made.
            await MarkConflictAsync(link, EntryNotALinkCode, EntryNotALinkDetail(link.LinkPath), cancellationToken);
            throw Conflict($"'{link.LinkPath}' is not a link anymore, so nothing was deleted.",
                "The file at that path is not one LibManagarr created. Remove it yourself if it is truly unwanted, then delete this entry again.");
        }

        link.OperationStatus = SymlinkOperationStatus.DeletePending;
        link.UpdatedAt = clock.GetUtcNow();
        if (!await store.UpdateLinkAsync(link, expectedRevision, cancellationToken)) throw StaleRevision();

        if (files.EntryExists(link.LinkPath))
        {
            try
            {
                files.DeleteEntry(link.LinkPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The owed deletion stays written down, so the next start finishes it instead of
                // forgetting an entry that is still sitting in the media tree.
                throw new ApiProblemException(StatusCodes.Status503ServiceUnavailable,
                    "The link could not be removed",
                    DeletionRefusedDetail(link.LinkPath, exception.Message));
            }
        }

        if (!await store.DeleteLinkAsync(id, expectedRevision + 1, cancellationToken)) throw StaleRevision();

        // A pair whose link was removed on purpose must not come back as a fresh suggestion by itself.
        // The rejection archive is where that decision lives, and the user can withdraw it there.
        await store.RejectAsync(link.Pair, RemovedOnPurposeDetail, clock.GetUtcNow(), cancellationToken);
        logger.LogInformation("Removed the managed link {LinkId} at {LinkPath}.", link.Id, link.LinkPath);
        return true;
    }

    /// <summary>
    /// Resumes a create that an interrupted run owed, which is the state a start finds rows in when the
    /// process died between the row and the file. A link that is missing is written; an entry that is
    /// already there is adopted only when it provably points at this row's source, because that entry is
    /// the link the dead run made and never got to confirm here.
    /// </summary>
    public async Task<SymlinkSetupOutcome> FinishCreationAsync(SymlinkLink link, CancellationToken cancellationToken)
    {
        if (!files.EntryExists(link.LinkPath))
        {
            try
            {
                PlaceLink(link);
            }
            catch (ApiProblemException exception)
            {
                await MarkConflictAsync(link, LinkNotCreatedCode, exception.Detail, cancellationToken);
                return SymlinkSetupOutcome.Blocked;
            }
        }
        else if (!ResolvesToSource(link))
        {
            await MarkConflictAsync(link, "link-entry-not-ours",
                $"'{link.LinkPath}' points somewhere else than '{link.SourcePath}', so it is not the link this entry owes and nothing was changed.",
                cancellationToken);
            return SymlinkSetupOutcome.Blocked;
        }

        link.OperationStatus = SymlinkOperationStatus.Active;
        link.SetupStep = SymlinkSetupStep.MonitoringDisabled;
        link.UpdatedAt = clock.GetUtcNow();
        // The obstacle this row was marked with is gone as soon as its own link is proven, so the finding
        // that named it does not stay behind as a warning about something that no longer stands.
        link.Findings = [.. link.Findings.Where(finding => finding.Code != LinkNotCreatedCode)];
        if (!await store.UpdateLinkAsync(link, link.Revision, cancellationToken))
        {
            return SymlinkSetupOutcome.Blocked;
        }

        logger.LogInformation("Resumed the managed link {LinkId} at {LinkPath}.", link.Id, link.LinkPath);
        setupRequests.Request();
        return SymlinkSetupOutcome.Completed;
    }

    /// <summary>
    /// Resumes a delete that an interrupted run owed: the entry goes as a link — never through it — and
    /// only then does the row go. What is not a link anymore stays where it is and the row keeps the
    /// finding, because a file this feature did not make is not this operation's to remove.
    /// </summary>
    public async Task<SymlinkSetupOutcome> FinishDeletionAsync(SymlinkLink link, CancellationToken cancellationToken)
    {
        if (files.EntryExists(link.LinkPath) && !files.IsSymlink(link.LinkPath))
        {
            await MarkConflictAsync(link, EntryNotALinkCode, EntryNotALinkDetail(link.LinkPath), cancellationToken);
            return SymlinkSetupOutcome.Blocked;
        }

        if (files.EntryExists(link.LinkPath))
        {
            try
            {
                files.DeleteEntry(link.LinkPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await MarkConflictAsync(link, DeletionRefusedCode,
                    DeletionRefusedDetail(link.LinkPath, exception.Message), cancellationToken);
                return SymlinkSetupOutcome.Blocked;
            }
        }

        if (!await store.DeleteLinkAsync(link.Id, link.Revision, cancellationToken))
        {
            return SymlinkSetupOutcome.Blocked;
        }

        await store.RejectAsync(link.Pair, RemovedOnPurposeDetail, clock.GetUtcNow(), cancellationToken);
        logger.LogInformation("Removed the managed link {LinkId} that an earlier run owed.", link.Id);
        return SymlinkSetupOutcome.Completed;
    }

    /// <summary>
    /// Follows one entry onto the file its movie has now. The pair is read live the same way a preview
    /// reads it — with the entry's own link and Sonarr file counted as ours rather than as an obstacle —
    /// the managed link is moved onto the new file, and only then is the row handed back to the Sonarr
    /// steps, which run again for what is now there. An instance that has not finished its import is
    /// waited for; a source that cannot be told apart, a foreign file at the path or an episode that got
    /// a file nobody made here are conflicts for a person. Nothing here deletes a source file, and the
    /// entry this feature made is removed only once its replacement is proven.
    /// </summary>
    public async Task<SymlinkSetupOutcome> FinishUpdateAsync(SymlinkLink link, CancellationToken cancellationToken)
    {
        SymlinkLink draft;
        if (link.PendingUpdate is SymlinkSourceUpdate pending)
        {
            // Finish the reserved swap first, even if Radarr moved again in the meantime. The old row
            // and this destination are the two sources recovery can safely recognise on disk.
            draft = NewDraft(link.Pair);
            ApplySource(pending, draft);
        }
        else
        {
            List<Obstacle> blockers;
            (draft, blockers, _) = await PlannedAsync(link.Pair, radarrFileId: null, replaces: link, cancellationToken);
            if (blockers.Count > 0)
            {
                Obstacle blocker = blockers[0];
                if (blocker.StatusCode == StatusCodes.Status503ServiceUnavailable)
                {
                    logger.LogInformation(
                        "The managed link {LinkId} waits for its new source: {Detail}", link.Id, blocker.Finding.Detail);
                    return SymlinkSetupOutcome.Waiting;
                }

                await MarkConflictAsync(link, blocker.Finding.Code, blocker.Finding.Detail, cancellationToken);
                return SymlinkSetupOutcome.Blocked;
            }

            if (draft.SourceVersion == link.SourceVersion)
            {
                // Once a swap was recorded, equality confirms the new source, but Sonarr still owes
                // its scan and metadata work. Only an untouched source can settle immediately.
                return link.SetupStep == SymlinkSetupStep.MonitoringDisabled
                    ? await CompleteUpdateAsync(link, cancellationToken)
                    : await SettleUnchangedAsync(link, cancellationToken);
            }

            link.PendingUpdate = SourceUpdateOf(draft);
            link.UpdatedAt = clock.GetUtcNow();
            if (!await store.UpdateLinkAsync(link, link.Revision, cancellationToken))
            {
                return SymlinkSetupOutcome.Blocked;
            }

            link.Revision += 1;
        }

        try
        {
            if (!IsOwnLinkAt(draft, draft.LinkPath))
            {
                if (files.EntryExists(draft.LinkPath))
                {
                    if (!IsSameLocalPath(link.LinkPath, draft.LinkPath) || !IsOwnLinkAt(link, draft.LinkPath))
                    {
                        throw Conflict($"'{draft.LinkPath}' is not the old link or its reserved replacement.",
                            "The entry was left where it is; check the path before resuming the update.");
                    }

                    files.DeleteEntry(draft.LinkPath);
                }

                PlaceLink(draft);
            }

            if (!IsSameLocalPath(link.LinkPath, draft.LinkPath))
            {
                RemoveReplacedLink(link.LinkPath, link.SourcePath, link);
            }
        }
        catch (ApiProblemException exception)
        {
            await MarkConflictAsync(link, UpdateNotAppliedCode, exception.Detail, cancellationToken);
            return SymlinkSetupOutcome.Blocked;
        }

        ApplySource(link.PendingUpdate!, link);
        link.PendingUpdate = null;
        // This checkpoint survives a delayed Radarr read-back: the link changed, so Sonarr must still
        // process it even when the next live plan reports exactly the version now stored on the row.
        link.SetupStep = SymlinkSetupStep.MonitoringDisabled;

        string? stillThere = await LiveSourceVersionAsync(link, cancellationToken);
        if (stillThere is null)
        {
            logger.LogInformation(
                "The managed link {LinkId} points at its new source but the instance stopped answering, so the row stays owed.",
                link.Id);
            return await RecordSwapAsync(link, cancellationToken);
        }

        if (stillThere != link.SourceVersion)
        {
            // The movie moved again while this run worked. The row says what its link points at now and
            // stays owed, so the next pass follows the newest stand and an older answer never settles on
            // a state that is already behind.
            logger.LogInformation("The source of managed link {LinkId} moved again while it was being followed.", link.Id);
            return await RecordSwapAsync(link, cancellationToken);
        }

        return await CompleteUpdateAsync(link, cancellationToken);
    }

    private async Task<SymlinkSetupOutcome> CompleteUpdateAsync(SymlinkLink link, CancellationToken cancellationToken)
    {
        link.OperationStatus = SymlinkOperationStatus.Active;
        // The Sonarr half runs again from the scan on: the new file has to be seen, attributed, read and
        // named. Monitoring stays as it is — it was already turned off for this episode, and the entry
        // keeps the value it was made from.
        link.SetupStep = SymlinkSetupStep.MonitoringDisabled;
        link.SonarrScanCommandId = null;
        link.SonarrRenameCommandId = null;
        link.ConfirmedMetadata = null;
        link.DesiredFilename = null;
        link.AppliedFilename = null;
        link.Findings =
        [
            .. link.Findings.Where(finding => finding.Code != SourceReplacedCode && finding.Code != UpdateNotAppliedCode)
        ];
        link.UpdatedAt = clock.GetUtcNow();
        if (!await store.UpdateLinkAsync(link, link.Revision, cancellationToken))
        {
            return SymlinkSetupOutcome.Blocked;
        }

        logger.LogInformation(
            "Moved the managed link {LinkId} onto file {RadarrFileId}, which its movie has now; its Sonarr steps run again.",
            link.Id, link.RadarrFileId);
        setupRequests.Request();
        return SymlinkSetupOutcome.Completed;
    }

    private static SymlinkSourceUpdate SourceUpdateOf(SymlinkLink draft) => new(
        draft.RadarrFileId, draft.StorageAreaId, draft.SourcePath, draft.LinkPath, draft.LinkText,
        draft.SeasonNumber, draft.EpisodeNumber, draft.ExpectedMetadata, draft.SourceVersion);

    /// <summary>The file facts of the source a followed entry now reflects.</summary>
    private static void ApplySource(SymlinkSourceUpdate draft, SymlinkLink link)
    {
        link.RadarrFileId = draft.RadarrFileId;
        link.StorageAreaId = draft.StorageAreaId;
        link.SourcePath = draft.SourcePath;
        link.LinkPath = draft.LinkPath;
        link.LinkText = draft.LinkText;
        link.SeasonNumber = draft.SeasonNumber;
        link.EpisodeNumber = draft.EpisodeNumber;
        link.ExpectedMetadata = draft.ExpectedMetadata;
        link.SourceVersion = draft.SourceVersion;
    }

    /// <summary>
    /// Lets go of the entry the replacement replaced — but only while it is provably still this entry's own
    /// link. Anything else there is somebody else's file and stays exactly where it is; what is left of it
    /// is said out loud instead of being cleaned up on suspicion.
    /// </summary>
    private void RemoveReplacedLink(string previousPath, string previousSource, SymlinkLink link)
    {
        if (!files.EntryExists(previousPath)) return;

        bool ours = files.IsSymlink(previousPath)
            && files.ReadLinkTarget(previousPath) is string target
            && IsSameLocalPath(Path.Combine(Path.GetDirectoryName(previousPath) ?? string.Empty, target), previousSource);
        if (!ours)
        {
            Note(link, ReplacedLinkLeftBehindCode,
                $"'{previousPath}' is not the link this entry made anymore, so it was left where it is. Remove it by hand if it is truly unwanted.");
            return;
        }

        try
        {
            files.DeleteEntry(previousPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The replacement is in place and verified, so the entry is followed. What is left of the old
            // link is reported, because a file this process cannot remove is not a reason to undo the update.
            Note(link, ReplacedLinkLeftBehindCode,
                $"'{previousPath}' could not be removed: {exception.Message}. The entry points at the new file and the old link stays where it is.");
        }
    }

    /// <summary>
    /// The version of the file this entry now claims to reflect, read again from the instance. Null is the
    /// answer for an instance or a file that cannot be seen, which is a reason to try later rather than a
    /// state to write down.
    /// </summary>
    private async Task<string?> LiveSourceVersionAsync(SymlinkLink link, CancellationToken cancellationToken)
    {
        AppSettings snapshot = await settings.GetSnapshotAsync(cancellationToken);
        ArrInstanceSettings? instance = snapshot.RadarrInstances
            .FirstOrDefault(candidate => string.Equals(candidate.Id, link.RadarrInstanceId, StringComparison.OrdinalIgnoreCase));
        if (instance is null) return null;

        RadarrMovieFileResource? file = await ChooseSourceAsync(instance, link.Pair, radarrFileId: null, cancellationToken);
        return file is null ? null : SourceVersionOf(file.Id, file.Size);
    }

    /// <summary>
    /// Writes down a swap the link already proves without settling for it: the row says which file its link
    /// points at now and stays owed, so the register never claims to point at the file that was let go of
    /// while the next pass reads the instance again.
    /// </summary>
    private async Task<SymlinkSetupOutcome> RecordSwapAsync(SymlinkLink link, CancellationToken cancellationToken)
    {
        link.UpdatedAt = clock.GetUtcNow();
        if (!await store.UpdateLinkAsync(link, link.Revision, cancellationToken))
        {
            return SymlinkSetupOutcome.Blocked;
        }

        return SymlinkSetupOutcome.Waiting;
    }

    private async Task<SymlinkSetupOutcome> SettleUnchangedAsync(SymlinkLink link, CancellationToken cancellationToken)
    {
        link.OperationStatus = SymlinkOperationStatus.Active;
        link.SetupStep = SymlinkSetupStep.Completed;
        link.Findings = [.. link.Findings.Where(finding => finding.Code != SourceReplacedCode)];
        link.UpdatedAt = clock.GetUtcNow();
        if (!await store.UpdateLinkAsync(link, link.Revision, cancellationToken))
        {
            return SymlinkSetupOutcome.Blocked;
        }

        logger.LogInformation(
            "The source of managed link {LinkId} turned out to be the file it already points at, so nothing was changed.",
            link.Id);
        return SymlinkSetupOutcome.Completed;
    }

    /// <summary>
    /// The plan for one pair, read live. <paramref name="replaces"/> is the row a follow-up is updating:
    /// its own link and its own episode file are then the reason the run exists and not obstacles in front
    /// of it, while anything else standing at either place still stops the plan.
    /// </summary>
    private async Task<(SymlinkLink Draft, List<Obstacle> Blockers, List<SymlinkFinding> Warnings)> PlannedAsync(
        SpecialMoviePair pair,
        int? radarrFileId,
        SymlinkLink? replaces,
        CancellationToken cancellationToken)
    {
        try
        {
            return await BuildPlanAsync(pair, radarrFileId, replaces, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // An instance that does not answer is a reason to try again later, not a pair that has
            // become wrong. Nothing was written, so the whole attempt simply repeats.
            SymlinkLink draft = NewDraft(pair);
            return (draft,
                [Unavailable("arr-unreachable", $"An Arr instance of this pair did not answer: {exception.Message}")],
                []);
        }
    }

    private async Task<(SymlinkLink Draft, List<Obstacle> Blockers, List<SymlinkFinding> Warnings)> BuildPlanAsync(
        SpecialMoviePair pair,
        int? radarrFileId,
        SymlinkLink? replaces,
        CancellationToken cancellationToken)
    {
        AppSettings snapshot = await settings.GetSnapshotAsync(cancellationToken);
        SymlinkLink draft = NewDraft(pair);
        List<Obstacle> blockers = [];
        List<SymlinkFinding> warnings = [];

        ArrInstanceSettings? radarrInstance = snapshot.RadarrInstances
            .FirstOrDefault(instance => string.Equals(instance.Id, pair.RadarrInstanceId, StringComparison.OrdinalIgnoreCase));
        if (radarrInstance is null)
        {
            return (draft, [ConflictFinding("radarr-instance-missing",
                $"The Radarr instance '{pair.RadarrInstanceId}' of this pair is no longer configured.")], warnings);
        }

        ArrInstanceSettings? sonarrInstance = snapshot.SonarrInstances
            .FirstOrDefault(instance => string.Equals(instance.Id, pair.SonarrInstanceId, StringComparison.OrdinalIgnoreCase));
        if (sonarrInstance is null)
        {
            return (draft, [ConflictFinding("sonarr-instance-missing",
                $"The Sonarr instance '{pair.SonarrInstanceId}' of this pair is no longer configured.")], warnings);
        }

        // Both instances are read live rather than from the snapshots, because the snapshots are what
        // produced the suggestion and a link must not be laid onto a file that is already gone.
        RadarrMovieResource? movie = await radarr.FetchMovieAsync(radarrInstance, pair.RadarrMovieId, cancellationToken);
        if (movie?.Id is null)
        {
            return (draft, [ConflictFinding("radarr-movie-missing",
                $"{radarrInstance.Name} has no movie {pair.RadarrMovieId}, so there is nothing to link to.")], warnings);
        }

        RadarrMovieFileResource? source = await ChooseSourceAsync(radarrInstance, pair, radarrFileId, cancellationToken);
        if (source is null)
        {
            Obstacle blocker = radarrFileId is null
                ? ConflictFinding("radarr-file-undetermined",
                    $"{radarrInstance.Name} does not report exactly one file for movie {pair.RadarrMovieId}, so the source is not chosen yet.")
                : ConflictFinding("radarr-file-changed",
                    $"{radarrInstance.Name} no longer has file {radarrFileId} of movie {pair.RadarrMovieId}. The source moved since the preview.");
            return (draft, [blocker], warnings);
        }

        if (string.IsNullOrWhiteSpace(source.Path))
        {
            return (draft, [ConflictFinding("radarr-file-pathless",
                $"{radarrInstance.Name} reports file {source.Id} without a path, so it cannot be placed anywhere.")], warnings);
        }

        PathMappingResult sourcePath =
            await paths.TranslateToLibManagarrPathAsync(pair.RadarrInstanceId, ArrApplicationKind.Radarr, source.Path, cancellationToken);
        if (!sourcePath.Success)
        {
            return (draft, [ConflictFinding("source-not-mapped", sourcePath.Failure!)], warnings);
        }

        PathMappingResult sourceInArea =
            await paths.EnsureWithinStorageAreaAsync(ArrApplicationKind.Radarr, sourcePath.Path!, cancellationToken);
        if (!sourceInArea.Success)
        {
            return (draft, [ConflictFinding("source-outside-storage-area", sourceInArea.Failure!)], warnings);
        }

        SonarrEpisodeResource? episode = await sonarr.FetchEpisodeAsync(sonarrInstance, pair.SonarrEpisodeId, cancellationToken);
        if (episode?.Id is null)
        {
            return (draft, [ConflictFinding("sonarr-episode-missing",
                $"{sonarrInstance.Name} has no episode {pair.SonarrEpisodeId}, so there is nothing to attach a file to.")], warnings);
        }

        if (episode.SeriesId != pair.SonarrSeriesId)
        {
            return (draft, [ConflictFinding("sonarr-episode-moved",
                $"Episode {pair.SonarrEpisodeId} belongs to series {episode.SeriesId} now, not to {pair.SonarrSeriesId}, so the pairing is no longer the one that was matched.")], warnings);
        }

        if (episode.SeasonNumber is not int seasonNumber || episode.EpisodeNumber is not int episodeNumber)
        {
            return (draft, [ConflictFinding("sonarr-episode-incomplete",
                $"{sonarrInstance.Name} reports no season or episode number for episode {pair.SonarrEpisodeId}, so no file name could name it.")], warnings);
        }

        if (seasonNumber != 0)
        {
            return (draft, [ConflictFinding("sonarr-episode-not-special",
                $"Episode {pair.SonarrEpisodeId} is in season {seasonNumber}, and only season 0 specials are managed.")], warnings);
        }

        // A follow-up runs on the episode that already carries the link this very entry made, so that file
        // is the reason the run exists rather than an obstacle in front of it. Anything the entry does not
        // know about still stops the plan.
        if ((episode.HasFile == true || episode.EpisodeFileId is not null) && !IsOwnEpisodeFile(replaces, episode))
        {
            return (draft, [ConflictFinding("sonarr-episode-has-file",
                $"{sonarrInstance.Name} already has a file for episode {pair.SonarrEpisodeId}, so adding a link would put a second source in front of it.")], warnings);
        }

        SonarrSeriesResource? series = await sonarr.FetchSeriesAsync(sonarrInstance, pair.SonarrSeriesId, cancellationToken);
        if (series?.Id is null)
        {
            return (draft, [ConflictFinding("sonarr-series-missing",
                $"{sonarrInstance.Name} has no series {pair.SonarrSeriesId}.")], warnings);
        }

        if (string.IsNullOrWhiteSpace(series.Path))
        {
            return (draft, [ConflictFinding("sonarr-series-pathless",
                $"{sonarrInstance.Name} reports series {pair.SonarrSeriesId} without a folder, so a link has nowhere to go.")], warnings);
        }

        PathMappingResult seriesFolder =
            await paths.TranslateToLibManagarrPathAsync(pair.SonarrInstanceId, ArrApplicationKind.Sonarr, series.Path, cancellationToken);
        if (!seriesFolder.Success)
        {
            return (draft, [ConflictFinding("series-not-mapped", seriesFolder.Failure!)], warnings);
        }

        PathMappingResult seriesInArea =
            await paths.EnsureWithinStorageAreaAsync(ArrApplicationKind.Sonarr, seriesFolder.Path!, cancellationToken);
        if (!seriesInArea.Success)
        {
            return (draft, [ConflictFinding("series-outside-storage-area", seriesInArea.Failure!)], warnings);
        }

        MediaStorageArea? area = FindArea(snapshot.Symlinks, sourceInArea.Path!, seriesInArea.Path!);
        if (area is null)
        {
            return (draft, [ConflictFinding(
                "no-common-storage-area",
                $"The source and the series folder do not sit in one storage area, so nothing guarantees that a link made here can be read there: '{sourceInArea.Path}' and '{seriesInArea.Path}'.")], warnings);
        }

        draft.StorageAreaId = area.Id;
        draft.SourcePath = sourceInArea.Path!;
        draft.RadarrFileId = source.Id!.Value;
        draft.SeasonNumber = seasonNumber;
        draft.EpisodeNumber = episodeNumber;
        draft.ExpectedMetadata = ExpectedFromRadarr(source);
        draft.SourceVersion = SourceVersionOf(source.Id, source.Size);

        string extension = Path.GetExtension(draft.SourcePath);
        if (string.IsNullOrEmpty(extension))
        {
            return (draft, [ConflictFinding("source-extension-missing",
                $"'{draft.SourcePath}' has no file extension, so Sonarr would not read it as a video file.")], warnings);
        }

        string linkFolder = Path.Combine(seriesInArea.Path!, SpecialsFolderName);
        draft.LinkPath = Path.Combine(linkFolder, LinkNameOf(series, seasonNumber, episodeNumber, extension));
        // Relative, because that is the form the media tree keeps working in when the same shelf is
        // mounted somewhere else, and it is the shape the capability probe proved end to end.
        draft.LinkText = Path.GetRelativePath(linkFolder, draft.SourcePath);

        // The same entry a follow-up is moving counts as ours only while it provably still points at the
        // source the row was built for. Anything else at that path has to be overwritten by a person.
        if (files.EntryExists(draft.LinkPath) && !IsOwnLinkAt(replaces, draft.LinkPath))
        {
            return (draft, [ConflictFinding("link-path-taken",
                $"'{draft.LinkPath}' is already used by an entry that is not managed here, and it would have to be overwritten.")], warnings);
        }

        SymlinkCapabilityCheck check = await capability.GetStatusAsync(area.Id, cancellationToken);
        if (!check.IsCurrent(snapshot.Symlinks.Revision))
        {
            return (draft, [Unavailable("capability-untested",
                $"The symlink capability test for storage area '{area.Name}' has not run on the current path configuration: {check.Detail}")], warnings);
        }

        if (check.Status != SymlinkCapabilityStatus.Available)
        {
            return (draft, [Unavailable("capability-unavailable",
                $"Storage area '{area.Name}' cannot hold file links right now: {check.Detail}")], warnings);
        }

        if (!snapshot.Symlinks.ReaderViews.Any(view =>
                view.Kind == PathReaderKind.Sonarr && view.Verification == PathReaderVerification.Verified))
        {
            // Not a blocker: the link is written in this process' view, and a Sonarr on another host may
            // well read the same files. It is said out loud because nobody has ever proven that here.
            warnings.Add(new SymlinkFinding("reader-view-unverified",
                $"No verified Sonarr path view is configured, so it is unproven that {sonarrInstance.Name} reads '{draft.LinkPath}' as the same file it scans.",
                clock.GetUtcNow()));
        }

        return (draft, blockers, warnings);
    }

    /// <summary>
    /// The file the pair was confirmed for, and nothing else. Choosing "the newest file" would silently
    /// follow an upgrade the user never saw, and several files on disk is the ambiguous case the plan
    /// says has to be decided by a person.
    /// </summary>
    private async Task<RadarrMovieFileResource?> ChooseSourceAsync(
        ArrInstanceSettings instance,
        SpecialMoviePair pair,
        int? radarrFileId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RadarrMovieFileResource> filesOfMovie =
            await radarr.FetchMovieFilesAsync(instance, pair.RadarrMovieId, cancellationToken);
        if (radarrFileId is int wanted)
        {
            return filesOfMovie.FirstOrDefault(file => file.Id == wanted);
        }

        return filesOfMovie.Count == 1 ? filesOfMovie[0] : null;
    }

    /// <summary>
    /// Writes the entry, then reads it back as an entry: a link that does not resolve to the chosen
    /// source is reported instead of being deleted on suspicion. The entry check is repeated here on
    /// purpose — the preview already looked, and a lot can happen to a media tree in between.
    /// </summary>
    private void PlaceLink(SymlinkLink link)
    {
        string folder = Path.GetDirectoryName(link.LinkPath)!;
        if (!files.EntryExists(folder))
        {
            try
            {
                files.CreateDirectory(folder);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw UnavailableException($"The folder '{folder}' could not be created: {exception.Message}");
            }
        }

        if (files.EntryExists(link.LinkPath))
        {
            throw Conflict($"'{link.LinkPath}' is taken by an entry this feature does not own.",
                "Nothing was overwritten. Free that path or leave the special unmanaged.");
        }

        try
        {
            files.CreateFileSymlink(link.LinkPath, link.LinkText);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An entry that appeared in the meantime is a collision; anything else is the mount or the
            // rights refusing. Both keep the row as an owed operation.
            throw files.EntryExists(link.LinkPath)
                ? Conflict($"'{link.LinkPath}' appeared while the link was being created: {exception.Message}",
                    "Reload the register; nothing was overwritten.")
                : UnavailableException($"The file system refused to create '{link.LinkPath}': {exception.Message}");
        }

        if (!ResolvesToSource(link))
        {
            throw Conflict($"The entry created at '{link.LinkPath}' does not resolve to '{link.SourcePath}'.",
                "The link stays as it is so nothing is lost; check what the mount in between points at.");
        }
    }

    /// <summary>
    /// Whether the entry standing at the row's path is a link whose target is the file this row chose. A
    /// link text only means something from its own folder, which is why this is the comparison and not a
    /// look at the stored text — and why the health check asks the same question through this method
    /// instead of answering it a second time somewhere else.
    /// </summary>
    public bool ResolvesToSource(SymlinkLink link)
    {
        if (!files.IsSymlink(link.LinkPath)) return false;
        string? target = files.ReadLinkTarget(link.LinkPath);
        if (target is null) return false;
        string? folder = Path.GetDirectoryName(link.LinkPath);
        return folder is not null && IsSameLocalPath(Path.Combine(folder, target), link.SourcePath);
    }

    private async Task MarkSuggestionAsync(
        string? suggestionId,
        SymlinkSuggestionStatus status,
        string linkId,
        string? detail,
        CancellationToken cancellationToken)
    {
        if (suggestionId is null) return;
        SymlinkSuggestion? suggestion = await store.GetSuggestionAsync(suggestionId, cancellationToken);
        if (suggestion is null)
        {
            logger.LogWarning("Link {LinkId} was made for suggestion {SuggestionId}, which is no longer stored.", linkId, suggestionId);
            return;
        }

        if (suggestion.Status == status && suggestion.LinkId == linkId) return;

        suggestion.Status = status;
        suggestion.LinkId = linkId;
        suggestion.Detail = detail ?? suggestion.Detail;
        suggestion.UpdatedAt = clock.GetUtcNow();
        if (status == SymlinkSuggestionStatus.Completed) suggestion.DecidedAt = suggestion.UpdatedAt;
        if (!await store.UpdateSuggestionAsync(suggestion, suggestion.Revision, cancellationToken))
        {
            logger.LogWarning("Suggestion {SuggestionId} changed while link {LinkId} was being created for it.", suggestionId, linkId);
        }
    }

    /// <summary>
    /// What the register says about an entry that is still readable while it waits. The newest answer for a
    /// code wins, so a run that reports the same state again does not bury the entry in copies of it.
    /// </summary>
    private void Note(SymlinkLink link, string code, string detail)
    {
        link.Findings =
        [
            .. link.Findings.Where(existing => existing.Code != code),
            new SymlinkFinding(code, detail, clock.GetUtcNow())
        ];
    }

    private async Task MarkConflictAsync(SymlinkLink link, string code, string? detail, CancellationToken cancellationToken)
    {
        link.OperationStatus = SymlinkOperationStatus.Conflict;
        link.Findings =
        [
            .. link.Findings.Where(existing => existing.Code != code),
            new SymlinkFinding(code, detail, clock.GetUtcNow())
        ];
        link.UpdatedAt = clock.GetUtcNow();
        if (!await store.UpdateLinkAsync(link, link.Revision, cancellationToken))
        {
            logger.LogWarning("Entry {LinkId} changed while its conflict was being written.", link.Id);
        }
    }

    /// <summary>
    /// The row a confirm would write. It has no id yet, because an id is only given to something that
    /// really exists; a preview shows the entry it describes without pretending it is registered.
    /// </summary>
    private SymlinkLink NewDraft(SpecialMoviePair pair) => new()
    {
        RadarrInstanceId = pair.RadarrInstanceId,
        RadarrMovieId = pair.RadarrMovieId,
        SonarrInstanceId = pair.SonarrInstanceId,
        SonarrSeriesId = pair.SonarrSeriesId,
        SonarrEpisodeId = pair.SonarrEpisodeId,
        // The first confirmation is what authorizes following later upgrades of this source, so a new
        // entry starts with it and the user can withdraw it per entry afterwards.
        AutoFollowUp = true,
        OperationStatus = SymlinkOperationStatus.Pending,
        SetupStep = SymlinkSetupStep.LinkCreated,
        CreatedAt = clock.GetUtcNow(),
        UpdatedAt = clock.GetUtcNow()
    };

    /// <summary>
    /// A scene-style name naming exactly this episode, so the scan attributes the file to the special it
    /// belongs to. The series folder's own name is used rather than the title the API reports, because
    /// the folder name is already legal on the file system the link is written to.
    /// </summary>
    private static string LinkNameOf(SonarrSeriesResource series, int seasonNumber, int episodeNumber, string extension)
    {
        string folderName = ArrPathRules.TryParse(series.Path, out ParsedPath? parsed, out _) && parsed!.Segments.Count > 0
            ? parsed.Segments[^1]
            : series.Title;
        return string.Create(CultureInfo.InvariantCulture,
            $"{folderName} - S{seasonNumber:D2}E{episodeNumber:D2}{extension}");
    }

    /// <summary>
    /// What the chosen Radarr file says about itself, as the expectation the later comparison is against.
    /// Audio languages are gathered the same way the library views gather them, so the two never disagree
    /// about what "the languages of this file" means.
    /// </summary>
    private static SymlinkFileMetadata ExpectedFromRadarr(RadarrMovieFileResource file) => new()
    {
        Origin = "radarr",
        ReleaseGroup = file.ReleaseGroup,
        Languages =
        [
            .. MediaFileLanguageHelper.CollectLanguageNames(file.Languages)
                .Concat(MediaFileLanguageHelper.CollectLanguageNames(file.MediaInfo?.AudioLanguages))
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ],
        Quality = file.Quality?.Quality?.Name,
        Size = file.Size
    };

    /// <summary>
    /// Identity of the source file, not of the movie. A Radarr upgrade issues a new file id, and the size
    /// is there for the case where the id survived but the bytes did not. Both halves are the instance's
    /// own spelling, so the same number from two instances is still two files.
    /// </summary>
    public static string SourceVersionOf(int? fileId, long? fileSize) =>
        string.Create(CultureInfo.InvariantCulture, $"radarr:{fileId}:{fileSize}");

    /// <summary>
    /// The file the library currently says the entry's movie has. The snapshots are the only place the
    /// register is told about an upgrade without asking Radarr again, so this is the comparison that
    /// decides whether an entry is owed a new source; an entry whose instance or movie was never synced
    /// gets no answer, which is a reason to wait and not a reason to act.
    /// </summary>
    public static MediaFileSnapshot? SnapshotFileOf(AppSettings settings, SymlinkLink link) =>
        settings.RadarrSnapshots
            .FirstOrDefault(instance => string.Equals(instance.InstanceId, link.RadarrInstanceId, StringComparison.OrdinalIgnoreCase))
            ?.Movies
            .FirstOrDefault(movie => movie.MovieId == link.RadarrMovieId)
            ?.File;

    /// <summary>
    /// The episode file that is this entry's own work: Sonarr renamed the link into the episode, so that
    /// file is the consequence of the entry, not something in front of it.
    /// </summary>
    private static bool IsOwnEpisodeFile(SymlinkLink? replaces, SonarrEpisodeResource episode) =>
        replaces?.SonarrFileId is int fileId && episode.EpisodeFileId == fileId;

    /// <summary>
    /// Whether what stands at the planned link path is still the link this very entry made. While it is,
    /// the run is replacing its own work; anything else there is somebody else's file.
    /// </summary>
    private bool IsOwnLinkAt(SymlinkLink? replaces, string linkPath) =>
        replaces is not null
        && files.IsSymlink(linkPath)
        && files.ReadLinkTarget(linkPath) is string target
        && IsSameLocalPath(Path.Combine(Path.GetDirectoryName(linkPath) ?? string.Empty, target), replaces.SourcePath);

    private static MediaStorageArea? FindArea(SymlinkSettings configuration, string sourcePath, string seriesPath) =>
        configuration.StorageAreas.FirstOrDefault(area =>
            ArrPathRules.IsWithinRoot(area.MoviesRoot, sourcePath) && ArrPathRules.IsWithinRoot(area.SeriesRoot, seriesPath));

    private static bool SharesPair(SymlinkLink link, SpecialMoviePair pair) =>
        string.Equals(link.RadarrInstanceId, pair.RadarrInstanceId, StringComparison.OrdinalIgnoreCase)
        && link.RadarrMovieId == pair.RadarrMovieId
        && string.Equals(link.SonarrInstanceId, pair.SonarrInstanceId, StringComparison.OrdinalIgnoreCase)
        && link.SonarrSeriesId == pair.SonarrSeriesId
        && link.SonarrEpisodeId == pair.SonarrEpisodeId;

    private static string EntryNotALinkDetail(string linkPath) =>
        $"'{linkPath}' is not a symbolic link anymore, so nothing was removed. Clear that entry by hand and delete this row again.";

    private static string DeletionRefusedDetail(string linkPath, string reason) =>
        $"'{linkPath}' is still there and marked for deletion: {reason}";

    private static bool IsSameLocalPath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private Obstacle ConflictFinding(string code, string detail) =>
        new(new SymlinkFinding(code, detail, clock.GetUtcNow()), StatusCodes.Status409Conflict);

    private Obstacle Unavailable(string code, string detail) =>
        new(new SymlinkFinding(code, detail, clock.GetUtcNow()), StatusCodes.Status503ServiceUnavailable);

    private static ApiProblemException Conflict(string title, string detail) =>
        new(StatusCodes.Status409Conflict, title, detail);

    private static ApiProblemException UnavailableException(string detail) =>
        new(StatusCodes.Status503ServiceUnavailable, "The file system is not ready for this link", detail);

    private static ApiProblemException StaleRevision() =>
        Conflict("The entry changed since it was loaded.", "Reload the register and apply the change again.");

    /// <summary>
    /// A blocker plus the status the interface gets for it. "Something else is in the way" and "the
    /// environment is not ready yet" are different answers, and only the second one is worth retrying.
    /// </summary>
    private readonly record struct Obstacle(SymlinkFinding Finding, int StatusCode);
}
