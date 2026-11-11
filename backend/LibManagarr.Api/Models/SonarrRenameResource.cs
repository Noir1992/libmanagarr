namespace LibManagarr.Api.Models;

/// <summary>
/// One rename Sonarr proposes for an existing episode file. It is a proposal, not a fact: whether the
/// instance really renames a managed symlink is proven by reading the file path back afterwards.
/// </summary>
public sealed class SonarrRenameResource
{
    /// <summary>The episode file this proposal is about.</summary>
    public int? Id { get; set; }
    public int? EpisodeFileId { get; set; }
    public string? ExistingPath { get; set; }
    public string? NewPath { get; set; }

    public int? SeriesId { get; set; }

    public int? EpisodeId { get; set; }

    /// <summary>Filename as it is on disk now.</summary>
    public string? Filename { get; set; }

    /// <summary>Filename Sonarr's own naming rules would produce.</summary>
    public string? NewFilename { get; set; }
}

/// <summary>
/// Sonarr's naming configuration, only as far as a managed link has to know it. Whether the instance
/// renames at all decides if a missing proposal means "already correct" or "naming is off".
/// </summary>
public sealed class SonarrNamingConfigResource
{
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; set; }
    public bool? RenameEpisodes { get; set; }

    /// <summary>Applies to specials; the pattern is Sonarr's own, never rebuilt here.</summary>
    public string? SpecialEpisodeFormat { get; set; }

    public string? EpisodeFormat { get; set; }

    public string? AnimeEpisodeFormat { get; set; }

    public string? DailyEpisodeFormat { get; set; }
}
