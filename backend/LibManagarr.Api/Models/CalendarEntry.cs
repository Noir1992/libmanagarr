namespace LibManagarr.Api.Models;

public sealed class CalendarEntry
{
    public required string Id { get; init; }

    public required ArrApplicationKind Application { get; init; }

    public required string InstanceId { get; init; }

    public required string InstanceName { get; init; }

    public required int MediaId { get; init; }

    public int? SeriesId { get; init; }

    public required string Title { get; init; }

    public string? EpisodeTitle { get; init; }

    public int? SeasonNumber { get; init; }

    public int? EpisodeNumber { get; init; }

    public required CalendarReleaseType ReleaseType { get; init; }

    // Exactly one of ReleaseDate and ReleaseDateTimeUtc is set: pure calendar days stay
    // date-only so they never gain an invented clock time or shift a day across timezones.
    public DateOnly? ReleaseDate { get; init; }

    public DateTimeOffset? ReleaseDateTimeUtc { get; init; }

    public required bool HasFile { get; init; }
}
