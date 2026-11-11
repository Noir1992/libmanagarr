using System.Text.Json;

namespace LibManagarr.Api.Models;

/// <summary>
/// One command Sonarr runs asynchronously. Only the states a caller has to wait on are modelled;
/// a command that is still queued and one that failed are different answers and must not be
/// collapsed into "done".
/// </summary>
public sealed class SonarrCommandResource
{
    public int? Id { get; set; }

    public string? Name { get; set; }

    /// <summary>"queued", "started", "completed" or "failed", exactly as the instance reports it.</summary>
    public string? Status { get; set; }

    public string? Message { get; set; }

    public DateTimeOffset? Queued { get; set; }

    public DateTimeOffset? Started { get; set; }

    public DateTimeOffset? End { get; set; }

    /// <summary>What the instance was told to act on, kept so a recorded command can be recognised later.</summary>
    public Dictionary<string, JsonElement>? Body { get; set; }

    public bool IsFinished =>
        Status is not null && !Status.Equals("queued", StringComparison.OrdinalIgnoreCase)
        && !Status.Equals("started", StringComparison.OrdinalIgnoreCase)
        && !Status.Equals("cancelling", StringComparison.OrdinalIgnoreCase);

    public bool Completed => Status?.Equals("completed", StringComparison.OrdinalIgnoreCase) ?? false;
}
