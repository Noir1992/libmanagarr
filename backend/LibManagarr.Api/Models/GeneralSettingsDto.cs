namespace LibManagarr.Api.Models;

public sealed class GeneralSettingsDto
{
    public AppLanguage Language { get; set; }

    public SizeUnitSystem SizeUnitSystem { get; set; } = SizeUnitSystem.Decimal;

    public ThemeMode Theme { get; set; }

    public TitleSortingSettings TitleSorting { get; set; } = new();

    public AuthProtectionSettings AuthProtection { get; set; } = new();
}
