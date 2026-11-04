using Microsoft.Extensions.Logging;

namespace LibManagarr.Api.Services.Backup;

/// <summary>
/// Outcome of the early restore pass, used for logging and for the post restart status shown to
/// the UI. It never carries archive contents.
/// </summary>
public enum RestoreBootstrapOutcome
{
    NothingToDo,

    Applied,

    RolledBack,

    Failed
}

/// <summary>
/// Processes a prepared restore before the application starts using any state. Running here,
/// ahead of migrations, settings loading, the productive data protection provider and the task
/// scheduler, is what keeps the old process from writing to files that are about to be replaced
/// and keeps the new process from mixing two states. The sequence per component is always: move
/// the current file aside into the rollback area, then move the imported file in. Any failure
/// before the final verification puts every component back and reports the restore as abandoned;
/// a failed rollback is fatal on purpose, because starting on a half imported state would show a
/// broken installation as a working one.
/// </summary>
public sealed class RestoreBootstrapper(
    StoragePaths paths, RestoreJournalStore journals, ILogger<RestoreBootstrapper> logger)
{
    public RestoreBootstrapOutcome Run(CancellationToken cancellationToken)
    {
        RestoreJournal? journal = journals.Read();
        if (journal is null)
        {
            return RestoreBootstrapOutcome.NothingToDo;
        }

        // Orders prepared by the old account-inclusive format must never replace local auth state.
        if (journal.Components.Any(component => component.IsDirectory ||
            (!string.Equals(component.TargetPath, paths.SettingsFilePath, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(component.TargetPath, paths.DatabasePath, StringComparison.OrdinalIgnoreCase))))
        {
            logger.LogCritical("Restore {OperationId} contains unsupported components. Resolve the old restore journal before starting.",
                journal.OperationId);
            return RestoreBootstrapOutcome.Failed;
        }

        logger.LogInformation("Found a prepared restore {OperationId} in state {State}.", journal.OperationId, journal.State);
        try
        {
            return journal.State switch
            {
                RestoreJournalState.Prepared => TakeOver(journal),
                RestoreJournalState.MovedAside => RollBack(journal, "the previous process stopped before importing any file"),
                RestoreJournalState.TakenOver => RollBack(journal, "the imported state did not survive the previous start"),
                RestoreJournalState.RolledBack => Finished(journal),
                RestoreJournalState.Completed => Finished(journal),
                _ => Fail(journal, $"unknown restore state {journal.State}")
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogCritical(exception,
                "Restore {OperationId} could not be completed or undone automatically. LibManagarr will not start on a partial state. " +
                "Restore the files from {RollbackDirectory} into {ConfigDirectory} by hand, then start again.",
                journal.OperationId, journal.RollbackDirectory, paths.ConfigDirectory);
            return RestoreBootstrapOutcome.Failed;
        }
    }

    private RestoreBootstrapOutcome TakeOver(RestoreJournal journal)
    {
        if (!VerifyStagedFiles(journal))
        {
            return Fail(journal, "the staged files no longer match the verified archive");
        }

        Directory.CreateDirectory(journal.RollbackDirectory);
        List<RestoreComponent> movedAside = [];
        foreach (RestoreComponent component in journal.Components)
        {
            string rollbackPath = Path.Combine(journal.RollbackDirectory, Path.GetFileName(component.TargetPath));
            if (Exists(component.TargetPath, component.IsDirectory))
            {
                Move(component, component.TargetPath, rollbackPath);
            }
            else
            {
                rollbackPath = string.Empty;
            }

            movedAside.Add(component with { RollbackPath = rollbackPath });
        }

        RestoreJournal aside = journals.WithState(journal, RestoreJournalState.MovedAside, movedAside);
        return MoveIntoPlace(aside);
    }

    private RestoreBootstrapOutcome MoveIntoPlace(RestoreJournal journal)
    {
        List<RestoreComponent> placed = [];
        foreach (RestoreComponent component in journal.Components)
        {
            if (Exists(component.SourcePath, component.IsDirectory))
            {
                Move(component, component.SourcePath, component.TargetPath);
                if (!component.IsDirectory)
                {
                    BackupStaging.ProtectFile(component.TargetPath);
                }
            }

            placed.Add(component with { MovedIntoPlace = true });
        }

        RestoreJournal takenOver = journals.WithState(journal, RestoreJournalState.TakenOver, placed);
        return VerifyAndFinish(takenOver);
    }

    private RestoreBootstrapOutcome VerifyAndFinish(RestoreJournal journal)
    {
        string? failure = BackupStateVerifier.VerifyAfterTakeover(paths);
        if (failure is null)
        {
            journals.WithState(journal, RestoreJournalState.Completed);
            journals.Delete();
            BackupStaging.TryDelete(journal.StagingDirectory);
            logger.LogInformation("Restore {OperationId} completed; the rollback copy stays in {RollbackDirectory}.",
                journal.OperationId, journal.RollbackDirectory);
            return RestoreBootstrapOutcome.Applied;
        }

        logger.LogWarning("Restore {OperationId} failed verification: {Failure}. Rolling back.", journal.OperationId, failure);
        return RollBack(journal, failure);
    }

    /// <summary>
    /// Puts every component back where it came from. Imported files that made it into the config
    /// directory are removed first, so the previous version is restored as a complete set and no
    /// SQLite sidecar from one state is ever paired with a database from the other.
    /// </summary>
    private RestoreBootstrapOutcome RollBack(RestoreJournal journal, string reason)
    {
        foreach (RestoreComponent component in journal.Components.Where((component) =>
            component.MovedIntoPlace && Exists(component.TargetPath, component.IsDirectory)))
        {
            Remove(component.TargetPath, component.IsDirectory);
        }

        foreach (RestoreComponent component in journal.Components.Where((component) =>
            !string.IsNullOrEmpty(component.RollbackPath) && Exists(component.RollbackPath!, component.IsDirectory)))
        {
            if (component.IsDirectory)
            {
                Directory.Move(component.RollbackPath!, component.TargetPath);
            }
            else
            {
                File.Move(component.RollbackPath!, component.TargetPath, overwrite: true);
            }
        }

        journals.WithState(journal, RestoreJournalState.RolledBack);
        journals.Delete();
        BackupStaging.TryDelete(journal.StagingDirectory);
        logger.LogWarning("Restore {OperationId} was rolled back: {Reason}. The previous state is active again.",
            journal.OperationId, reason);
        return RestoreBootstrapOutcome.RolledBack;
    }

    private RestoreBootstrapOutcome Finished(RestoreJournal journal)
    {
        BackupStaging.TryDelete(journal.StagingDirectory);
        journals.Delete();
        return RestoreBootstrapOutcome.NothingToDo;
    }

    private RestoreBootstrapOutcome Fail(RestoreJournal journal, string reason)
    {
        logger.LogCritical(
            "Restore {OperationId} was not started: {Reason}. Nothing was replaced; fix the cause and prepare the import again.",
            journal.OperationId, reason);
        journals.Delete();
        return RestoreBootstrapOutcome.Failed;
    }

    private static bool VerifyStagedFiles(RestoreJournal journal) =>
        Directory.Exists(journal.StagingDirectory) &&
        journal.Components.All((component) =>
            !Exists(component.SourcePath, component.IsDirectory) || HasContent(component));

    /// <summary>
    /// A staged component counts as present only with content behind it, so an interrupted upload
    /// that left an empty file is reported instead of being moved in as a working state.
    /// </summary>
    private static bool HasContent(RestoreComponent component) =>
        component.IsDirectory
            ? Directory.EnumerateFiles(component.SourcePath, "*", SearchOption.AllDirectories).Any()
            : new FileInfo(component.SourcePath).Length > 0;

    private static bool Exists(string path, bool isDirectory) =>
        isDirectory ? Directory.Exists(path) : File.Exists(path);

    /// <summary>
    /// One move per component kind. A directory uses the directory move, which stays on a single
    /// filesystem call and therefore cannot leave half a key ring behind.
    /// </summary>
    private static void Move(RestoreComponent component, string source, string destination)
    {
        if (component.IsDirectory)
        {
            Directory.Move(source, destination);
            return;
        }

        File.Move(source, destination, overwrite: true);
    }

    private static void Remove(string path, bool isDirectory)
    {
        if (isDirectory)
        {
            Directory.Delete(path, recursive: true);
            return;
        }

        File.Delete(path);
    }
}
