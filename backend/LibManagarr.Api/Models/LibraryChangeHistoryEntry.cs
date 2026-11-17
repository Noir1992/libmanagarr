namespace LibManagarr.Api.Models;

public sealed class LibraryChangeHistoryEntry
{
    public string SourceKey { get; set; } = string.Empty;

    public ArrApplicationKind Kind { get; set; }

    public string InstanceId { get; set; } = string.Empty;

    public string InstanceName { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public string MediaKey { get; set; } = string.Empty;

    public LibraryChangeType ChangeType { get; set; }

    public string EntityTitle { get; set; } = string.Empty;

    public string? SeriesTitle { get; set; }

    public string? FilePath { get; set; }

    public long? SizeBytes { get; set; }

    public long DeltaSizeBytes { get; set; }

    public bool IsSizeDeltaKnown { get; set; } = true;

    public bool IsInferred { get; set; }

    public DateTimeOffset? ObservedSince { get; set; }

    public string? SourceTitle { get; set; }

    public string? EventType { get; set; }

    public string? DeletionReason { get; set; }

    public long? HistoryId { get; set; }

    public string? FileId { get; set; }

    public Dictionary<string, string>? EventData { get; set; }

    // Migration bookkeeping. Legacy merged entries cannot always be decomposed again.
    public bool IsLegacy { get; set; }

    public bool NeedsHistoryBackfill { get; set; }
}
