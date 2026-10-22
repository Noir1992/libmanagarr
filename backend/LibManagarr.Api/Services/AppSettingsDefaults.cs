using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public static class AppSettingsDefaults
{
    public static AppSettings Create()
    {
        return new AppSettings
        {
            Language = AppLanguage.En,
            Theme = ThemeMode.System,
            SonarrInstances = [],
            RadarrInstances = [],
            SonarrSnapshots = [],
            RadarrSnapshots = [],
            Logging = new LoggingSettings
            {
                Path = "./logs",
                RetentionDays = 30,
                MinimumLevel = LogLevel.Information
            }
        };
    }
}
