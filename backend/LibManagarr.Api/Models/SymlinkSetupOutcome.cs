namespace LibManagarr.Api.Models;

/// <summary>
/// How one setup pass ended. Waiting and blocked are deliberately different: a pass that is waiting on
/// Sonarr should simply run again, while a blocked one needs a decision and must not be retried in a
/// loop. The link itself carries which step is open and what was found.
/// </summary>
public enum SymlinkSetupOutcome
{
    Completed,
    Waiting,
    Blocked
}
