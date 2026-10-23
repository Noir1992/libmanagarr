namespace LibManagarr.Api.Models;

public sealed class GeneralSettingsDto
{
    public AppLanguage Language { get; set; }

    public ThemeMode Theme { get; set; }

    public TitleSortingSettings TitleSorting { get; set; } = new();
}
