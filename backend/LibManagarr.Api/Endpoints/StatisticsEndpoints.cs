using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Endpoints;

public static class StatisticsEndpoints
{
    public static IEndpointRouteBuilder MapStatisticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/statistics");
        group.RequireAuthorization();
        group.MapGet("/codecs", GetCodecStatisticsAsync);
        group.MapPost("/codecs", GetFilteredCodecStatisticsAsync);
        group.MapGet("/languages", GetLanguageStatisticsAsync);
        group.MapGet("/library-trend", GetLibraryTrendStatisticsAsync);
        return endpoints;
    }

    private static async Task<Ok<CodecStatisticsResponse>> GetCodecStatisticsAsync(
        string? sonarrInstanceIds,
        string? radarrInstanceIds,
        HttpRequest request,
        ISettingsService settingsService,
        CodecStatisticsService codecStatisticsService,
        CancellationToken cancellationToken)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        IReadOnlySet<string>? sonarrFilter = ParseInstanceIds(sonarrInstanceIds, request.Query.ContainsKey("sonarrInstanceIds"));
        IReadOnlySet<string>? radarrFilter = ParseInstanceIds(radarrInstanceIds, request.Query.ContainsKey("radarrInstanceIds"));
        CodecStatisticsResponse response = codecStatisticsService.Build(settings, sonarrFilter, radarrFilter);
        return TypedResults.Ok(response);
    }

    private static async Task<Ok<CodecStatisticsResponse>> GetFilteredCodecStatisticsAsync(
        LibraryRequestFilter filter,
        ISettingsService settingsService,
        CodecStatisticsService codecStatisticsService,
        CancellationToken cancellationToken)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        return TypedResults.Ok(CodecStatisticsTransport.Compact(codecStatisticsService.Build(settings, null, null, filter)));
    }

    private static async Task<Ok<LanguageStatisticsResponse>> GetLanguageStatisticsAsync(
        string? sonarrInstanceIds,
        string? radarrInstanceIds,
        HttpRequest request,
        ISettingsService settingsService,
        LanguageStatisticsService languageStatisticsService,
        CancellationToken cancellationToken)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        IReadOnlySet<string>? sonarrFilter = ParseInstanceIds(sonarrInstanceIds, request.Query.ContainsKey("sonarrInstanceIds"));
        IReadOnlySet<string>? radarrFilter = ParseInstanceIds(radarrInstanceIds, request.Query.ContainsKey("radarrInstanceIds"));
        LanguageStatisticsResponse response = languageStatisticsService.Build(settings, sonarrFilter, radarrFilter);
        return TypedResults.Ok(response);
    }

    private static async Task<Ok<LibraryHistoryStatisticsResponse>> GetLibraryTrendStatisticsAsync(
        string? sonarrInstanceIds,
        string? radarrInstanceIds,
        string? start,
        string? end,
        HttpRequest request,
        ISettingsService settingsService,
        IHistoryStore historyStore,
        LibraryTrendStatisticsService libraryTrendStatisticsService,
        CancellationToken cancellationToken)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        IReadOnlyList<LibraryChangeHistoryEntry> historyEntries = await historyStore.GetLibraryHistoryAsync(null, cancellationToken);
        IReadOnlySet<string>? sonarrFilter = ParseInstanceIds(sonarrInstanceIds, request.Query.ContainsKey("sonarrInstanceIds"));
        IReadOnlySet<string>? radarrFilter = ParseInstanceIds(radarrInstanceIds, request.Query.ContainsKey("radarrInstanceIds"));
        DateTimeOffset? startDateTime = ParseDateTimeOffset(start, "start");
        DateTimeOffset? endDateTime = ParseDateTimeOffset(end, "end");
        LibraryHistoryStatisticsResponse response = libraryTrendStatisticsService.Build(
            settings,
            historyEntries,
            sonarrFilter,
            radarrFilter,
            startDateTime,
            endDateTime);
        return TypedResults.Ok(response);
    }

    private static IReadOnlySet<string>? ParseInstanceIds(string? rawValue, bool wasProvided)
    {
        if (!wasProvided)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        HashSet<string> ids = rawValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where((id) => id.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return ids;
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? rawValue, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(
                rawValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset parsed))
        {
            return parsed;
        }

        throw new ApiProblemException(
            StatusCodes.Status400BadRequest,
            "Invalid date range",
            $"The query parameter '{fieldName}' must be a valid date/time value.");
    }
}
