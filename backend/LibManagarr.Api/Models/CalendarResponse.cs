namespace LibManagarr.Api.Models;

public sealed class CalendarResponse
{
    public required DateTimeOffset FetchedAt { get; init; }

    public required IReadOnlyList<CalendarEntry> Entries { get; init; }

    public required IReadOnlyList<CalendarInstanceStatus> Instances { get; init; }
}

public sealed class CalendarInstanceStatus
{
    public required string InstanceId { get; init; }

    public required string Name { get; init; }

    public required ArrApplicationKind Application { get; init; }

    public required bool Loaded { get; init; }

    public string? Error { get; init; }
}
