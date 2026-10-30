using System.Text.Json.Serialization;

namespace LibManagarr.Api.Models;

// Wire model for the server entries of "GET /api/v1/settings/{radarr|sonarr}" and for the
// "server" object of "GET /api/v1/service/{radarr|sonarr}/{serverId}". Seerr embeds each
// server's arr apiKey in plaintext on the settings route, so this model deliberately maps
// only the display fields and the edit-dialog defaults and never forwards secrets.
public sealed class SeerrServerResource
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("activeDirectory")]
    public string? ActiveDirectory { get; set; }

    // Default preselection for edits; pending requests carry no profileId of their own yet.
    [JsonPropertyName("activeProfileId")]
    public int? ActiveProfileId { get; set; }

    // Anime media (TMDB keyword anime) preselect these in Seerr's dialogs; the radarr
    // service route does not expose them at all.
    [JsonPropertyName("activeAnimeProfileId")]
    public int? ActiveAnimeProfileId { get; set; }

    [JsonPropertyName("activeAnimeDirectory")]
    public string? ActiveAnimeDirectory { get; set; }

    [JsonPropertyName("isDefault")]
    public bool? IsDefault { get; set; }
}

// Wire model for "GET /api/v1/service/{radarr|sonarr}/{serverId}", the route Seerr's own
// dialogs use: it supplies the quality profiles of both applications and the arr's real
// root folder list in one response.
public sealed class SeerrServiceResource
{
    [JsonPropertyName("server")]
    public SeerrServerResource? Server { get; set; }

    [JsonPropertyName("profiles")]
    public List<SeerrProfileResource?>? Profiles { get; set; }

    [JsonPropertyName("rootFolders")]
    public List<SeerrRootFolderResource?>? RootFolders { get; set; }
}

public sealed class SeerrProfileResource
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class SeerrRootFolderResource
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }
}
