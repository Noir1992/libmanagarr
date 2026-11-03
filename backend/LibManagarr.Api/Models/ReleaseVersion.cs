namespace LibManagarr.Api.Models;

public sealed class ReleaseVersion
{
    public required string Version { get; init; }

    public required string Name { get; init; }

    public required string Tag { get; init; }

    public required DateTimeOffset PublishedAt { get; init; }

    public string? Notes { get; init; }

    public required string Url { get; init; }

    public required bool IsPrerelease { get; init; }

    public required bool IsCurrent { get; init; }

    public required bool IsNewerThanCurrent { get; init; }

    public required string PullReference { get; init; }
}
