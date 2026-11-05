using Microsoft.Extensions.Logging;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public static class AppSettingsNormalizer
{
    public static AppSettings Normalize(AppSettings settings)
    {
        return new AppSettings
        {
            Language = settings.Language,
            TitleSorting = TitleSortingSettingsRules.Normalize(settings.TitleSorting),
            Theme = settings.Theme,
            SonarrInstances = NormalizeInstances(settings.SonarrInstances),
            RadarrInstances = NormalizeInstances(settings.RadarrInstances),
            Seerr = NormalizeSeerrInstance(settings.Seerr),
            SonarrSnapshots = settings.SonarrSnapshots ?? [],
            RadarrSnapshots = settings.RadarrSnapshots ?? [],
            LibraryFilterValues = settings.LibraryFilterValues ?? [],
            Logging = NormalizeLogging(settings.Logging),
            AuthProtection = NormalizeAuthProtection(settings.AuthProtection)
        };
    }

    /// <summary>
    /// Only a missing section gains the defaults here. A value that is present but out of range is
    /// left untouched so the validator can refuse it instead of the write silently repairing it.
    /// </summary>
    private static AuthProtectionSettings NormalizeAuthProtection(AuthProtectionSettings? settings)
    {
        return settings ?? new AuthProtectionSettings();
    }

    private static SeerrInstanceSettings? NormalizeSeerrInstance(SeerrInstanceSettings? instance)
    {
        if (instance is null)
        {
            return null;
        }

        return new SeerrInstanceSettings
        {
            Id = UuidV7.IsUuidV7(instance.Id) ? instance.Id.Trim() : UuidV7.Create(),
            BaseUrl = instance.BaseUrl.Trim().TrimEnd('/'),
            ApiKey = instance.ApiKey.Trim()
        };
    }

    private static List<ArrInstanceSettings> NormalizeInstances(IEnumerable<ArrInstanceSettings>? instances)
    {
        return (instances ?? [])
            .Select((instance) => new ArrInstanceSettings
            {
                Id = UuidV7.IsUuidV7(instance.Id) ? instance.Id.Trim() : UuidV7.Create(),
                Name = instance.Name.Trim(),
                BaseUrl = instance.BaseUrl.Trim().TrimEnd('/'),
                ApiKey = instance.ApiKey.Trim()
            })
            .ToList();
    }

    private static LoggingSettings NormalizeLogging(LoggingSettings? settings)
    {
        LoggingSettings logging = settings ?? new LoggingSettings();

        return new LoggingSettings
        {
            Path = "./logs",
            RetentionDays = Math.Max(1, logging.RetentionDays),
            MinimumLevel = logging.MinimumLevel is LogLevel.Debug or LogLevel.Information or LogLevel.Warning or LogLevel.Error
                ? logging.MinimumLevel
                : LogLevel.Information
        };
    }
}
