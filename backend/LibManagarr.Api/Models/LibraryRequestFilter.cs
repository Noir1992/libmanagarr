namespace LibManagarr.Api.Models;

public sealed class LibraryRequestFilter
{
    public IReadOnlyList<string> ExcludedFilters { get; set; } = [];
    public string? AiredFilter { get; set; }
    public string? MonitoredFilter { get; set; }
    public string? UpgradeTargetFilter { get; set; }
    public string? FileStatusFilter { get; set; }
    public string? EpisodeMonitoredFilter { get; set; }
    public IReadOnlyList<string>? Types { get; set; }
    public IReadOnlyList<string>? Statuses { get; set; }
    public IReadOnlyList<string>? Genres { get; set; }
    public IReadOnlyList<string>? Tags { get; set; }
    public IReadOnlyList<string>? Profiles { get; set; }
    public IReadOnlyList<string>? AudioLanguages { get; set; }
    public IReadOnlyList<string>? SubtitleLanguages { get; set; }
    public IReadOnlyList<string>? Resolutions { get; set; }
    public IReadOnlyList<string>? VideoCodecs { get; set; }
    public IReadOnlyList<string>? AudioCodecs { get; set; }
    public IReadOnlyList<string>? DynamicRanges { get; set; }
    public IReadOnlyList<string>? ContainerFormats { get; set; }
    public decimal? VideoBitrateMin { get; set; }
    public decimal? VideoBitrateMax { get; set; }
    public int? ScoreMin { get; set; }
    public int? ScoreMax { get; set; }
    public string? AddedFrom { get; set; }
    public string? AddedTo { get; set; }
    public string? AiredFrom { get; set; }
    public string? AiredTo { get; set; }
    public IReadOnlyList<string> RadarrInstanceFilter { get; set; } = [];
    public IReadOnlyList<string> SonarrInstanceFilter { get; set; } = [];
    public LibrarySortField? SortField { get; set; }
    public LibrarySortDirection? SortDirection { get; set; }
}
