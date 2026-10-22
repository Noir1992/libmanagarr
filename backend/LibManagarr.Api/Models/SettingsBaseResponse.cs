namespace LibManagarr.Api.Models;

public sealed class SettingsBaseResponse
{
    public AppLanguage Language { get; set; } = AppLanguage.En;

    public ThemeMode Theme { get; set; } = ThemeMode.System;
}
