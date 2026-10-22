namespace LibManagarr.Api.Models;

public sealed class GeneralSettingsUpdateRequest
{
    public AppLanguage Language { get; set; }

    public ThemeMode Theme { get; set; }

    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalProperties { get; set; }
}
