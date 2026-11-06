namespace LibManagarr.Api.Models;

/// <summary>
/// The configuration together with the answer of the capability test per storage area. The two are one
/// response on purpose: a mapping list without the probe result would let the interface show a setup
/// that cannot produce a single link.
/// </summary>
public sealed class SymlinkSettingsDto
{
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
