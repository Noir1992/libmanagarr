namespace LibManagarr.Api.Models;

/// <summary>
/// The shared media directory and its capability result. The legacy collections remain in the
/// response for existing clients; new clients configure only MediaRoot.
/// </summary>
public sealed class SymlinkSettingsDto
{
    public bool Enabled { get; set; } = true;

    public string MediaRoot { get; set; } = "/media";

    public List<MediaStorageArea> StorageAreas { get; set; } = [];

    public List<ArrPathMapping> PathMappings { get; set; } = [];

    public List<PathReaderView> ReaderViews { get; set; } = [];

    public int Revision { get; set; }

    public List<SymlinkCapabilityStateDto> CapabilityChecks { get; set; } = [];
}

public sealed class SymlinkCapabilityStateDto
{
    public required string StorageAreaId { get; set; }

    public SymlinkCapabilityStatus Status { get; set; }

    public string? Detail { get; set; }

    public DateTimeOffset? CheckedAt { get; set; }

    public int ConfigRevision { get; set; }

    /// <summary>The probe ran on an older configuration, so its answer says nothing about the current paths.</summary>
    public bool Stale { get; set; }
}
