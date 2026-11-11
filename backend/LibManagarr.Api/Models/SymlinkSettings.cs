namespace LibManagarr.Api.Models;

/// <summary>
/// One common media mount for all readers. The revision invalidates capability checks when the
/// configured paths change. Manual areas, mappings and views remain readable for legacy installations;
/// saving a media root replaces them with one internal boundary and identity paths discovered from Arr.
/// </summary>
public sealed class SymlinkSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Shared mount prefix. Null is retained only for pre-existing manual configurations.</summary>
    public string? MediaRoot { get; set; }

    public const string SharedStorageAreaId = "01900000-0000-7000-8000-000000000001";

    public MediaStorageArea? FindStorageArea(string id) => MediaRoot is not null
        ? StorageAreas.FirstOrDefault()
        : StorageAreas.FirstOrDefault(area => string.Equals(area.Id, id, StringComparison.OrdinalIgnoreCase));

    public List<MediaStorageArea> StorageAreas { get; set; } = [];

    public List<ArrPathMapping> PathMappings { get; set; } = [];

    public List<PathReaderView> ReaderViews { get; set; } = [];

    public int Revision { get; set; }
}

/// <summary>
/// One media shelf as LibManagarr itself sees it. Both roots are paths of this process, not of
/// Sonarr, Radarr or the host, and the probe writes its throwaway files into <see cref="SeriesRoot"/>.
/// </summary>
public sealed class MediaStorageArea
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string MoviesRoot { get; set; } = string.Empty;

    public string SeriesRoot { get; set; } = string.Empty;
}

/// <summary>
/// Maps the path prefix one Arr instance reports for its part of a storage area onto the prefix
/// LibManagarr uses for the same files. Source and target are kept apart on purpose: two equal
/// looking strings are not proof that the same shelf is mounted.
/// </summary>
public sealed class ArrPathMapping
{
    public string Id { get; set; } = string.Empty;

    public string InstanceId { get; set; } = string.Empty;

    public ArrApplicationKind Kind { get; set; }

    /// <summary>Prefix exactly as that instance's API reports it, in that instance's own path syntax.</summary>
    public string InstanceRoot { get; set; } = string.Empty;

    /// <summary>Prefix of the same files in this process' view, so in the runtime path syntax.</summary>
    public string LibManagarrRoot { get; set; } = string.Empty;
}

/// <summary>
/// How a process that must read the link afterwards expects to see it. Declaring a view is not
/// proof of one, which is why <see cref="Verified"/> only ever comes from a real check.
/// </summary>
public sealed class PathReaderView
{
    public string Id { get; set; } = string.Empty;

    public PathReaderKind Kind { get; set; }

    public string Root { get; set; } = string.Empty;

    public PathReaderVerification Verification { get; set; }
}

public enum PathReaderKind
{
    Sonarr,
    MediaPlayer
}

/// <summary>
/// Declaring a view only documents an expectation, so the three states stay apart: nobody looked,
/// somebody looked and it worked, somebody looked and it did not.
/// </summary>
public enum PathReaderVerification
{
    Unverified,
    Verified,
    Failed
}
