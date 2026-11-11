using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Endpoints;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/settings");
        group.RequireAuthorization();

        group.MapGet("/", GetSettingsAsync);
        group.MapGet("/general", GetGeneralSettingsAsync);
        group.MapGet("/instances", GetInstancesSettingsAsync);
        group.MapGet("/logging", GetLoggingSettingsAsync);
        group.MapScheduledTaskEndpoints();
        group.MapSymlinkEndpoints();
        group.MapPut("/", SaveSettingsAsync);
        group.MapPut("/general", SaveGeneralSettingsAsync);
        group.MapPut("/title-sorting", SaveTitleSortingAsync);
        group.MapPut("/logging", SaveLoggingSettingsAsync);
        group.MapPut("/sonarr/{index:int}", SaveSonarrInstanceAsync);
        group.MapDelete("/sonarr/{index:int}", DeleteSonarrInstanceAsync);
        group.MapPut("/radarr/{index:int}", SaveRadarrInstanceAsync);
        group.MapDelete("/radarr/{index:int}", DeleteRadarrInstanceAsync);
        group.MapGet("/seerr", GetSeerrInstanceAsync);
        group.MapPut("/seerr", SaveSeerrInstanceAsync);
        group.MapDelete("/seerr", DeleteSeerrInstanceAsync);
        group.MapPost("/test-connection", TestConnectionAsync);
        group.MapPost("/sync", SyncInstanceAsync);

        return endpoints;
    }

    private static async Task<Ok<GeneralSettingsDto>> GetGeneralSettingsAsync(
        ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);

        return TypedResults.Ok(new GeneralSettingsDto
        {
            Language = settings.Language,
            Theme = settings.Theme,
            TitleSorting = TitleSortingSettingsRules.Normalize(settings.TitleSorting),
            AuthProtection = settings.AuthProtection
        });
    }

    private static async Task<Ok<InstancesSettingsDto>> GetInstancesSettingsAsync(
        ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);

        return TypedResults.Ok(new InstancesSettingsDto
        {
            SonarrInstances = settings.SonarrInstances,
            RadarrInstances = settings.RadarrInstances
        });
    }

    private static async Task<Ok<LoggingSettings>> GetLoggingSettingsAsync(
        ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);

        return TypedResults.Ok(settings.Logging);
    }

    private static async Task<Ok<AppSettings>> GetSettingsAsync(
        ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        AppSettings settings = await settingsService.GetAsync(cancellationToken);

        return TypedResults.Ok(settings);
    }

    private static async Task<Results<NoContent, ValidationProblem>> SaveSettingsAsync(
        JsonElement request,
        ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        if (request.ValueKind == JsonValueKind.Object && request.EnumerateObject().Any(property =>
            property.Name.Equals("scheduledTasks", StringComparison.OrdinalIgnoreCase)))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["scheduledTasks"] = ["Use /api/settings/tasks/{taskKey} to change task configuration."] });
        using IDisposable updateLease = await settingsService.BeginUpdateAsync(cancellationToken);
        AppSettings settings;
        try
        {
            settings = request.Deserialize<AppSettings>(new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            }) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["settings"] = ["Invalid settings."] });
        }
        AppSettings currentSettings = await settingsService.GetAsync(cancellationToken);
        bool hasTitleSorting = request.ValueKind == JsonValueKind.Object && request.EnumerateObject()
            .Any(property => property.Name.Equals("titleSorting", StringComparison.OrdinalIgnoreCase));
        if (hasTitleSorting)
        {
            JsonElement titleSorting = request.EnumerateObject().Last(property =>
                property.Name.Equals("titleSorting", StringComparison.OrdinalIgnoreCase)).Value;
            if (!HasTitleSortingFields(titleSorting))
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["titleSorting"] = ["Provide enabled and ignoredLeadingWords."] });
        }
        if (!hasTitleSorting) settings.TitleSorting = currentSettings.TitleSorting;
        if (!HasProperty(request, "authProtection")) settings.AuthProtection = currentSettings.AuthProtection;
        settings.LibraryFilterValues = currentSettings.LibraryFilterValues;

        settings.SonarrSnapshots = currentSettings.SonarrSnapshots
            .Where((snapshot) => settings.SonarrInstances.Any(
                (instance) => string.Equals(instance.Id?.Trim(), snapshot.InstanceId, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        settings.RadarrSnapshots = currentSettings.RadarrSnapshots
            .Where((snapshot) => settings.RadarrInstances.Any(
                (instance) => string.Equals(instance.Id?.Trim(), snapshot.InstanceId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Dictionary<string, string[]> validationErrors = AppSettingsValidator.Validate(settings);

        if (validationErrors.Count > 0)
        {
            return TypedResults.ValidationProblem(validationErrors);
        }

        await settingsService.SaveAsync(settings, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<TitleSortingSettings>, ValidationProblem>> SaveTitleSortingAsync(
        JsonElement request,
        ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        TitleSortingSettings? settings;
        try
        {
            settings = request.Deserialize<TitleSortingSettings>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (!HasTitleSortingFields(request))
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["titleSorting"] = ["Provide enabled and ignoredLeadingWords."] });
        }
        catch (JsonException)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["titleSorting"] = ["Invalid title sorting settings."] });
        }
        Dictionary<string, string[]> errors = TitleSortingSettingsRules.Validate(settings);
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
        TitleSortingSettings normalized = TitleSortingSettingsRules.Normalize(settings);
        AppSettings saved = await settingsService.UpdateAsync(current => current.TitleSorting = normalized, cancellationToken);
        return TypedResults.Ok(TitleSortingSettingsRules.Normalize(saved.TitleSorting));
    }

    private static bool HasTitleSortingFields(JsonElement request) => request.ValueKind == JsonValueKind.Object &&
        request.TryGetProperty("enabled", out JsonElement enabled) &&
        enabled.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        request.TryGetProperty("ignoredLeadingWords", out _);

    private static bool HasProperty(JsonElement request, string propertyName) => request.ValueKind == JsonValueKind.Object &&
        request.EnumerateObject().Any(property => property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));

    private static async Task<Results<NoContent, ValidationProblem>> SaveGeneralSettingsAsync(
        GeneralSettingsUpdateRequest settings,
        ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        using IDisposable updateLease = await settingsService.BeginUpdateAsync(cancellationToken);
        if (settings.AdditionalProperties?.Keys.Any(key => key.Equals("scheduledTasks", StringComparison.OrdinalIgnoreCase)) == true)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["scheduledTasks"] = ["Use /api/settings/tasks/{taskKey} to change task configuration."] });
        AppSettings currentSettings = await settingsService.GetAsync(cancellationToken);
        AppSettings updatedSettings = currentSettings;
        updatedSettings.Language = settings.Language;
        updatedSettings.Theme = settings.Theme;
        // The language and theme autosave sends only its two fields, so an absent section has to keep
        // the stored limits; otherwise every language change would reset them to the defaults.
        if (settings.AuthProtection is not null)
        {
            updatedSettings.AuthProtection = settings.AuthProtection;
        }

        Dictionary<string, string[]> validationErrors = AppSettingsValidator.ValidateGeneral(updatedSettings);
        if (validationErrors.Count > 0)
        {
            return TypedResults.ValidationProblem(validationErrors);
        }

        await settingsService.SaveAsync(updatedSettings, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ValidationProblem>> SaveLoggingSettingsAsync(
        LoggingSettingsUpdateRequest request,
        ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        using IDisposable updateLease = await settingsService.BeginUpdateAsync(cancellationToken);
        AppSettings currentSettings = await settingsService.GetAsync(cancellationToken);
        Dictionary<string, string[]> validationErrors = AppSettingsValidator.ValidateLogging(request.Logging);
        if (validationErrors.Count > 0)
        {
            return TypedResults.ValidationProblem(validationErrors);
        }

        currentSettings.Logging = new LoggingSettings
        {
            RetentionDays = request.Logging.RetentionDays,
            MinimumLevel = request.Logging.MinimumLevel
        };

        await settingsService.SaveAsync(currentSettings, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ValidationProblem>> SaveSonarrInstanceAsync(
        int index,
        ArrInstanceSettings instance,
        ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        using IDisposable updateLease = await settingsService.BeginUpdateAsync(cancellationToken);
        AppSettings currentSettings = await settingsService.GetAsync(cancellationToken);
        if (index < 0 || index > currentSettings.SonarrInstances.Count)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["instanceIndex"] = ["The instance index is out of range."]
            });
        }

        if (index == currentSettings.SonarrInstances.Count)
        {
            currentSettings.SonarrInstances.Add(instance);
        }
        else
        {
            currentSettings.SonarrInstances[index] = instance;
        }

        Dictionary<string, string[]> validationErrors =
            AppSettingsValidator.ValidateInstanceAtIndex(currentSettings.SonarrInstances, index, "sonarrInstances");
        if (validationErrors.Count > 0)
        {
            return TypedResults.ValidationProblem(validationErrors);
        }

        currentSettings.SonarrSnapshots = currentSettings.SonarrSnapshots
            .Where((snapshot) => currentSettings.SonarrInstances.Any(
                (savedInstance) => string.Equals(savedInstance.Id?.Trim(), snapshot.InstanceId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        await settingsService.SaveAsync(currentSettings, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ValidationProblem>> DeleteSonarrInstanceAsync(
        int index,
        ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        using IDisposable updateLease = await settingsService.BeginUpdateAsync(cancellationToken);
        AppSettings currentSettings = await settingsService.GetAsync(cancellationToken);
        if (index < 0 || index >= currentSettings.SonarrInstances.Count)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["instanceIndex"] = ["The instance index is out of range."]
            });
        }

        currentSettings.SonarrInstances.RemoveAt(index);
        currentSettings.SonarrSnapshots = currentSettings.SonarrSnapshots
            .Where((snapshot) => currentSettings.SonarrInstances.Any(
                (savedInstance) => string.Equals(savedInstance.Id?.Trim(), snapshot.InstanceId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        await settingsService.SaveAsync(currentSettings, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ValidationProblem>> SaveRadarrInstanceAsync(
        int index,
        ArrInstanceSettings instance,
        ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        using IDisposable updateLease = await settingsService.BeginUpdateAsync(cancellationToken);
        AppSettings currentSettings = await settingsService.GetAsync(cancellationToken);
        if (index < 0 || index > currentSettings.RadarrInstances.Count)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["instanceIndex"] = ["The instance index is out of range."]
            });
        }

        if (index == currentSettings.RadarrInstances.Count)
        {
            currentSettings.RadarrInstances.Add(instance);
        }
        else
        {
            currentSettings.RadarrInstances[index] = instance;
        }

        Dictionary<string, string[]> validationErrors =
            AppSettingsValidator.ValidateInstanceAtIndex(currentSettings.RadarrInstances, index, "radarrInstances");
        if (validationErrors.Count > 0)
        {
            return TypedResults.ValidationProblem(validationErrors);
        }

        currentSettings.RadarrSnapshots = currentSettings.RadarrSnapshots
            .Where((snapshot) => currentSettings.RadarrInstances.Any(
                (savedInstance) => string.Equals(savedInstance.Id?.Trim(), snapshot.InstanceId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        await settingsService.SaveAsync(currentSettings, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ValidationProblem>> DeleteRadarrInstanceAsync(
        int index,
        ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        using IDisposable updateLease = await settingsService.BeginUpdateAsync(cancellationToken);
        AppSettings currentSettings = await settingsService.GetAsync(cancellationToken);
        if (index < 0 || index >= currentSettings.RadarrInstances.Count)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["instanceIndex"] = ["The instance index is out of range."]
            });
        }

        currentSettings.RadarrInstances.RemoveAt(index);
        currentSettings.RadarrSnapshots = currentSettings.RadarrSnapshots
            .Where((snapshot) => currentSettings.RadarrInstances.Any(
                (savedInstance) => string.Equals(savedInstance.Id?.Trim(), snapshot.InstanceId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        await settingsService.SaveAsync(currentSettings, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<SeerrInstanceSettings>, NoContent>> GetSeerrInstanceAsync(
        ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        if (settings.Seerr is not SeerrInstanceSettings seerrInstance)
        {
            return TypedResults.NoContent();
        }

        return TypedResults.Ok(seerrInstance);
    }

    private static async Task<Results<NoContent, ValidationProblem>> SaveSeerrInstanceAsync(
        SeerrInstanceSettings instance,
        ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        using IDisposable updateLease = await settingsService.BeginUpdateAsync(cancellationToken);
        AppSettings currentSettings = await settingsService.GetAsync(cancellationToken);
        currentSettings.Seerr = instance;

        Dictionary<string, string[]> validationErrors = AppSettingsValidator.ValidateSeerr(instance);
        if (validationErrors.Count > 0)
        {
            return TypedResults.ValidationProblem(validationErrors);
        }

        await settingsService.SaveAsync(currentSettings, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> DeleteSeerrInstanceAsync(
        ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        using IDisposable updateLease = await settingsService.BeginUpdateAsync(cancellationToken);
        AppSettings currentSettings = await settingsService.GetAsync(cancellationToken);
        currentSettings.Seerr = null;

        await settingsService.SaveAsync(currentSettings, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<TestConnectionResult>, ValidationProblem, ProblemHttpResult>> TestConnectionAsync(
        TestConnectionRequest request,
        ArrConnectionTestService connectionTestService,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> validationErrors = AppSettingsValidator.ValidateConnectionRequest(request);

        if (validationErrors.Count > 0)
        {
            return TypedResults.ValidationProblem(validationErrors);
        }

        try
        {
            TestConnectionResult result = await connectionTestService.TestAsync(request, cancellationToken);

            return TypedResults.Ok(result);
        }
        catch (ApiProblemException exception)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = exception.Title,
                Detail = exception.Detail,
                Status = exception.StatusCode
            });
        }
    }

    private static async Task<Results<Ok<SyncInstanceResult>, ValidationProblem, ProblemHttpResult>> SyncInstanceAsync(
        SyncInstanceRequest request,
        ISnapshotSyncService snapshotSyncService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.InstanceId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["instanceId"] = ["The instance id is required."]
            });
        }

        try
        {
            SyncInstanceResult result = await snapshotSyncService.SyncAsync(request, cancellationToken);

            return TypedResults.Ok(result);
        }
        catch (ApiProblemException exception)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = exception.Title,
                Detail = exception.Detail,
                Status = exception.StatusCode
            });
        }
    }

}
