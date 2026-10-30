using System.Text.Json.Serialization;

namespace LibManagarr.Api.Models;

// Wire body for the Seerr request edit call. Seerr validates mediaType as movie|tv and
// requires it; the remaining fields are optional and echo the request's values unless edited.
public sealed class SeerrRequestUpdateResource
{
    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = "movie";

    [JsonPropertyName("serverId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ServerId { get; set; }

    [JsonPropertyName("profileId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ProfileId { get; set; }

    [JsonPropertyName("rootFolder")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RootFolder { get; set; }

    [JsonPropertyName("languageProfileId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LanguageProfileId { get; set; }

    [JsonPropertyName("is4k")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Is4k { get; set; }

    // Live-verified: seasons must be a plain int array ([1,2]); season objects are rejected.
    [JsonPropertyName("seasons")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<int>? Seasons { get; set; }
}

// Body for "POST /api/v1/request/{id}/decline"; Seerr accepts an optional reason.
public sealed class SeerrDeclineResource
{
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

// Options for the request edit dialog, resolved per request. Profiles and root folders come
// from the request's arr server via the service route; Seasons is the table shown for tv media,
// one entry per season of the show with its episode count and state. ProfileId and RootFolder
// are the preselection and always a member of their list: the request's own values when the arr
// still offers them, else the server defaults, because pending requests do not carry values yet
// and older ones can outlive the profile or folder they were created with.
public sealed class SeerrRequestEditOptions
{
    public IReadOnlyList<SeerrEditOption> QualityProfiles { get; set; } = [];

    public IReadOnlyList<string> RootFolders { get; set; } = [];

    public IReadOnlyList<SeerrSeasonOption> Seasons { get; set; } = [];

    public int? ProfileId { get; set; }

    public string? RootFolder { get; set; }
}

public sealed record SeerrEditOption(int Id, string Name);

// One row of the season table. State is "requested" when the request already covers the season,
// "available" when it is on disk, "processing" while it is being downloaded and "none" when
// nothing covers it yet. Only the seasons without a state can be (de)selected in the dialog.
public sealed record SeerrSeasonOption(int Number, int? EpisodeCount, string State);
