namespace LibManagarr.Api.Models;

public sealed class LanguageStatisticsResponse
{
    public IReadOnlyList<LanguageStatisticItem> AudioLanguages { get; set; } = [];

    public IReadOnlyList<LanguageStatisticItem> SubtitleLanguages { get; set; } = [];
}
