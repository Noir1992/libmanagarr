using LibManagarr.Api.Models;
using Microsoft.Extensions.Logging;

namespace LibManagarr.Api.Services;

public static class AppSettingsValidator
{
    private const int MaxInstanceFieldLength = 255;

    // Shared with the backup import check, which has to refuse an archive carrying unusable limits
    // before they are put in place.
    public const int MinRequestsPerMinute = 1;
    public const int MaxRequestsPerMinute = 1000;
    public const int MinMaxFailedAttempts = 1;
    public const int MaxMaxFailedAttempts = 100;

    public static Dictionary<string, string[]> Validate(AppSettings settings)
    {
        Dictionary<string, string[]> errors = [];

        AppendGeneralErrors(errors, settings);
        foreach (var error in TitleSortingSettingsRules.Validate(settings.TitleSorting)) errors[error.Key] = error.Value;
        AppendLoggingErrors(errors, settings.Logging);

        AppendInstanceErrors(errors, "sonarrInstances", settings.SonarrInstances);
        AppendInstanceErrors(errors, "radarrInstances", settings.RadarrInstances);
        AppendSeerrErrors(errors, settings.Seerr);

        foreach (var error in SymlinkSettingsRules.Validate(
                     settings.Symlinks, settings.SonarrInstances, settings.RadarrInstances))
        {
            errors[error.Key] = error.Value;
        }

        return errors;
    }

    public static Dictionary<string, string[]> ValidateGeneral(AppSettings settings)
    {
        Dictionary<string, string[]> errors = [];
        AppendGeneralErrors(errors, settings);
        return errors;
    }

    public static Dictionary<string, string[]> ValidateLogging(LoggingSettings settings)
    {
        Dictionary<string, string[]> errors = [];
        AppendLoggingErrors(errors, settings);
        return errors;
    }

    public static Dictionary<string, string[]> ValidateInstances(
        IReadOnlyList<ArrInstanceSettings>? instances,
        string propertyName)
    {
        Dictionary<string, string[]> errors = [];
        AppendInstanceErrors(errors, propertyName, instances);
        return errors;
    }

    public static Dictionary<string, string[]> ValidateInstanceAtIndex(
        IReadOnlyList<ArrInstanceSettings>? instances,
        int index,
        string propertyName)
    {
        Dictionary<string, string[]> errors = [];
        IReadOnlyList<ArrInstanceSettings> currentInstances = instances ?? [];
        if (index < 0 || index >= currentInstances.Count)
        {
            errors["instanceIndex"] = ["The instance index is out of range."];
            return errors;
        }

        Dictionary<string, List<string>> fieldErrors = [];
        ArrInstanceSettings instance = currentInstances[index];
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

        for (int otherIndex = 0; otherIndex < currentInstances.Count; otherIndex += 1)
        {
            if (otherIndex == index)
            {
                continue;
            }

            ArrInstanceSettings otherInstance = currentInstances[otherIndex];

            if (!string.IsNullOrWhiteSpace(otherInstance.Id))
            {
                ids.Add(otherInstance.Id.Trim());
            }

            if (!string.IsNullOrWhiteSpace(otherInstance.Name))
            {
                names.Add(otherInstance.Name.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(instance.Id) && ids.Contains(instance.Id.Trim()))
        {
            AddFieldError(fieldErrors, $"{propertyName}[{index}].id", "The id must be unique.");
        }

        if (!string.IsNullOrWhiteSpace(instance.Name) && names.Contains(instance.Name.Trim()))
        {
            AddFieldError(fieldErrors, $"{propertyName}[{index}].name", "The name must be unique.");
        }

        AppendInstanceCoreErrors(fieldErrors, $"{propertyName}[{index}]", instance);

        foreach (KeyValuePair<string, List<string>> entry in fieldErrors)
        {
            errors[entry.Key] = entry.Value.ToArray();
        }

        return errors;
    }

    public static Dictionary<string, string[]> ValidateInstance(ArrInstanceSettings instance)
    {
        Dictionary<string, string[]> errors = [];
        Dictionary<string, List<string>> fieldErrors = [];
        AppendInstanceCoreErrors(fieldErrors, "instance", instance);

        foreach (KeyValuePair<string, List<string>> entry in fieldErrors)
        {
            errors[entry.Key] = entry.Value.ToArray();
        }

        return errors;
    }

    public static Dictionary<string, string[]> ValidateSeerr(SeerrInstanceSettings instance)
    {
        Dictionary<string, string[]> errors = [];
        AppendSeerrErrors(errors, instance);
        return errors;
    }

    public static Dictionary<string, string[]> ValidateConnectionRequest(TestConnectionRequest request)
    {
        // Seerr has no instance name, so only the connection-sensitive fields are validated for it.
        Dictionary<string, string[]> errors = request.Kind == ArrApplicationKind.Seerr
            ? ValidateSeerrConnectionInstance(request.Instance)
            : ValidateInstance(request.Instance);

        if (!Enum.IsDefined(request.Kind))
        {
            errors["kind"] = ["The kind must be 'Sonarr', 'Radarr', or 'Seerr'."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateSeerrConnectionInstance(ArrInstanceSettings instance)
    {
        Dictionary<string, string[]> errors = [];
        Dictionary<string, List<string>> fieldErrors = [];
        AppendBaseUrlAndApiKeyErrors(fieldErrors, "instance", instance.BaseUrl, instance.ApiKey);

        foreach (KeyValuePair<string, List<string>> entry in fieldErrors)
        {
            errors[entry.Key] = entry.Value.ToArray();
        }

        return errors;
    }

    private static void AppendSeerrErrors(Dictionary<string, string[]> errors, SeerrInstanceSettings? instance)
    {
        if (instance is null)
        {
            return;
        }

        Dictionary<string, List<string>> fieldErrors = [];
        AppendSeerrFieldErrors(fieldErrors, "seerr", instance);

        foreach (KeyValuePair<string, List<string>> entry in fieldErrors)
        {
            errors[entry.Key] = entry.Value.ToArray();
        }
    }

    private static void AppendSeerrFieldErrors(
        Dictionary<string, List<string>> fieldErrors,
        string instancePath,
        SeerrInstanceSettings instance)
    {
        AppendBaseUrlAndApiKeyErrors(fieldErrors, instancePath, instance.BaseUrl, instance.ApiKey);
    }

    private static void AppendInstanceErrors(
        Dictionary<string, string[]> errors,
        string propertyName,
        IReadOnlyList<ArrInstanceSettings>? instances)
    {
        IReadOnlyList<ArrInstanceSettings> currentInstances = instances ?? [];
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<string>> fieldErrors = [];

        for (int index = 0; index < currentInstances.Count; index += 1)
        {
            ArrInstanceSettings instance = currentInstances[index];

            if (!string.IsNullOrWhiteSpace(instance.Id) && !ids.Add(instance.Id.Trim()))
            {
                AddFieldError(fieldErrors, $"{propertyName}[{index}].id", "The id must be unique.");
            }

            if (!string.IsNullOrWhiteSpace(instance.Name) && !names.Add(instance.Name.Trim()))
            {
                AddFieldError(fieldErrors, $"{propertyName}[{index}].name", "The name must be unique.");
            }

            AppendInstanceCoreErrors(fieldErrors, $"{propertyName}[{index}]", instance);
        }

        foreach (KeyValuePair<string, List<string>> entry in fieldErrors)
        {
            errors[entry.Key] = entry.Value.ToArray();
        }
    }

    private static void AppendGeneralErrors(Dictionary<string, string[]> errors, AppSettings settings)
    {
        if (!Enum.IsDefined(settings.Language))
        {
            errors["language"] = ["The language must be 'en' or 'de'."];
        }

        if (!Enum.IsDefined(settings.Theme))
        {
            errors["theme"] = ["The theme must be 'light', 'dark', or 'system'."];
        }

        AppendAuthProtectionErrors(errors, settings.AuthProtection);
    }

    private static void AppendAuthProtectionErrors(Dictionary<string, string[]> errors, AuthProtectionSettings? authProtection)
    {
        if (authProtection is null)
        {
            return;
        }

        if (authProtection.RequestsPerMinute < MinRequestsPerMinute || authProtection.RequestsPerMinute > MaxRequestsPerMinute)
        {
            errors["authProtection.requestsPerMinute"] =
                [$"The requests per minute must be between {MinRequestsPerMinute} and {MaxRequestsPerMinute}."];
        }

        if (authProtection.MaxFailedAttempts < MinMaxFailedAttempts || authProtection.MaxFailedAttempts > MaxMaxFailedAttempts)
        {
            errors["authProtection.maxFailedAttempts"] =
                [$"The failed attempts must be between {MinMaxFailedAttempts} and {MaxMaxFailedAttempts}."];
        }
    }

    private static void AppendLoggingErrors(Dictionary<string, string[]> errors, LoggingSettings settings)
    {
        if (settings.RetentionDays < 1 || settings.RetentionDays > 3650)
        {
            errors["logging.retentionDays"] = ["The log retention must be between 1 and 3650 days."];
        }

        if (settings.MinimumLevel is not (LogLevel.Debug or LogLevel.Information or LogLevel.Warning or LogLevel.Error))
        {
            errors["logging.minimumLevel"] = ["The minimum log level must be Debug, Information, Warning, or Error."];
        }
    }

    private static void AppendInstanceCoreErrors(
        Dictionary<string, List<string>> fieldErrors,
        string instancePath,
        ArrInstanceSettings instance)
    {
        AppendInstanceCoreErrors(fieldErrors, instancePath, instance.Name, instance.BaseUrl, instance.ApiKey);
    }

    private static void AppendInstanceCoreErrors(
        Dictionary<string, List<string>> fieldErrors,
        string instancePath,
        string name,
        string baseUrl,
        string apiKey)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            AddFieldError(fieldErrors, $"{instancePath}.name", "The name is required.");
        }
        else if (name.Trim().Length > MaxInstanceFieldLength)
        {
            AddFieldError(
                fieldErrors,
                $"{instancePath}.name",
                $"The name must not exceed {MaxInstanceFieldLength} characters.");
        }

        AppendBaseUrlAndApiKeyErrors(fieldErrors, instancePath, baseUrl, apiKey);
    }

    private static void AppendBaseUrlAndApiKeyErrors(
        Dictionary<string, List<string>> fieldErrors,
        string instancePath,
        string baseUrl,
        string apiKey)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            !Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? _))
        {
            AddFieldError(fieldErrors, $"{instancePath}.baseUrl", "The baseUrl must be a valid absolute URL.");
        }
        else if (baseUrl.Trim().Length > MaxInstanceFieldLength)
        {
            AddFieldError(
                fieldErrors,
                $"{instancePath}.baseUrl",
                $"The baseUrl must not exceed {MaxInstanceFieldLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            AddFieldError(fieldErrors, $"{instancePath}.apiKey", "The apiKey is required.");
        }
        else if (apiKey.Trim().Length > MaxInstanceFieldLength)
        {
            AddFieldError(
                fieldErrors,
                $"{instancePath}.apiKey",
                $"The apiKey must not exceed {MaxInstanceFieldLength} characters.");
        }
    }

    private static void AddFieldError(Dictionary<string, List<string>> fieldErrors, string fieldPath, string message)
    {
        if (!fieldErrors.TryGetValue(fieldPath, out List<string>? messages))
        {
            messages = [];
            fieldErrors[fieldPath] = messages;
        }

        messages.Add(message);
    }
}
