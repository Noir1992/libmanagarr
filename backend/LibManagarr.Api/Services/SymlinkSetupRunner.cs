using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>
/// Carries every register row that owes work forward, whichever half of an operation an interrupted run
/// left standing. It is the only place that decides which owed row is worked on and which one waits for a
/// person instead, which is why neither the register nor the Sonarr side loops over entries itself.
/// </summary>
public sealed class SymlinkSetupRunner(
    ISymlinkStore store,
    SymlinkService links,
    SonarrSpecialIntegrationService setup,
    ILogger<SymlinkSetupRunner> logger)
{
    /// <summary>
    /// Advances the owed rows and answers how many of them wait on an instance rather than on a decision,
    /// which is all the caller needs to know whether coming back on its own is worth it. A library that
    /// moved on is noticed at the start of the pass, because an upgrade reaches the register as a
    /// synchronized snapshot and not as a request of its own.
    /// </summary>
    public async Task<int> RunDueSetupsAsync(CancellationToken cancellationToken)
    {
        using IDisposable lease = await SymlinkOperationGate.EnterAsync(cancellationToken);
        if (!await links.CanProcessAsync(cancellationToken)) return 0;
        await links.MarkReplacedSourcesAsync(cancellationToken);

        List<SymlinkLink> owed = (await store.GetLinksAsync(cancellationToken)).Where(HasOwedWork).ToList();
        if (owed.Count == 0) return 0;
        logger.LogDebug("{Count} managed links owe work.", owed.Count);

        int waiting = 0;
        foreach (SymlinkLink link in owed)
        {
            try
            {
                if (await CarryOutAsync(link, cancellationToken) == SymlinkSetupOutcome.Waiting) waiting++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // What this pass already carried forward is written down, and what it never reached stays
                // owed, so a host that stops here loses nothing.
                break;
            }
            catch (Exception exception)
            {
                waiting++;
                // One pair that cannot be carried forward is never allowed to stop the others.
                logger.LogError(exception, "Carrying managed link {LinkId} forward failed; it stays as it is.", link.Id);
            }
        }

        return waiting;
    }

    /// <summary>
    /// The one operation an entry is owed right now. An entry that came back from a backup is compared with
    /// the file system before anything of its own is attempted, and that comparison is its whole work for
    /// this pass: what it decided is carried out by the next one, which is the same path an interrupted
    /// operation already resumes from. Counting it as waiting is what makes the pass come back for it.
    /// </summary>
    private async Task<SymlinkSetupOutcome> CarryOutAsync(SymlinkLink link, CancellationToken cancellationToken)
    {
        if (SymlinkService.WaitsForRestoreCheck(link))
        {
            SymlinkSetupOutcome afterCheck = await links.CheckRestoredEntryAsync(link, cancellationToken);
            return afterCheck == SymlinkSetupOutcome.Blocked ? afterCheck : SymlinkSetupOutcome.Waiting;
        }

        if (link.OperationStatus is SymlinkOperationStatus.DeletePending or SymlinkOperationStatus.CleanupPending)
        {
            return await links.FinishDeletionAsync(link, cancellationToken);
        }

        if (link.OperationStatus == SymlinkOperationStatus.Updating)
        {
            return await links.FinishUpdateAsync(link, cancellationToken);
        }

        return await CarryForwardAsync(link, cancellationToken);
    }

    /// <summary>
    /// Until the row's own link is proven, nothing else may be attempted: Sonarr must not be told about a
    /// file that is not there yet. A conflict that got past the link itself names a decision a person has
    /// to make, and re-writing the same finding on a timer would only bury it. An entry that never was
    /// compared after a restore counts as owed for the same reason — acting on it would be a guess.
    /// </summary>
    private static bool HasOwedWork(SymlinkLink link) =>
        SymlinkService.WaitsForRestoreCheck(link)
        || link.OperationStatus is SymlinkOperationStatus.DeletePending or SymlinkOperationStatus.CleanupPending
        || link.OperationStatus == SymlinkOperationStatus.Updating
        || (link.PendingUpdate is null && (link.SetupStep is null or SymlinkSetupStep.LinkCreated))
        || (link.OperationStatus == SymlinkOperationStatus.Active && link.SetupStep != SymlinkSetupStep.Completed);

    private async Task<SymlinkSetupOutcome> CarryForwardAsync(SymlinkLink link, CancellationToken cancellationToken) =>
        link.SetupStep is null or SymlinkSetupStep.LinkCreated
            ? await links.FinishCreationAsync(link, cancellationToken)
            : await setup.RunSetupAsync(link, cancellationToken);
}
