using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Endpoints;

public static class LogsEndpoints
{
    public static IEndpointRouteBuilder MapLogsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/logs");
        group.RequireAuthorization();
        group.MapGet("/", GetLogsAsync);
        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<LogEntry>>> GetLogsAsync(
        ISettingsService settingsService,
        LogFileService logFileService,
        TimeProvider timeProvider,
        [FromQuery] DateTimeOffset? startDateTime,
        [FromQuery] DateTimeOffset? endDateTime,
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        [FromQuery] string[]? levels,
        CancellationToken cancellationToken)
    {
        await settingsService.GetSnapshotAsync(cancellationToken);
        DateTimeOffset now = timeProvider.GetLocalNow();
        DateTimeOffset normalizedStartDateTime = startDateTime
            ?? (startDate.HasValue
                ? new DateTimeOffset(startDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local))
                : new DateTimeOffset(now.LocalDateTime.Date));
        DateTimeOffset normalizedEndDateTime = endDateTime
            ?? (endDate.HasValue
                ? new DateTimeOffset(endDate.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Local))
                : new DateTimeOffset(now.LocalDateTime.Date.AddDays(1).AddTicks(-1)));
        IReadOnlyCollection<LogLevel>? requestedLevels = ParseRequestedLevels(levels);

        IReadOnlyList<LogEntry> entries = logFileService.Read(
            normalizedStartDateTime,
            normalizedEndDateTime,
            requestedLevels);

        return TypedResults.Ok(entries);
    }

    private static IReadOnlyCollection<LogLevel>? ParseRequestedLevels(string[]? levels)
    {
        if (levels is null)
        {
            return null;
        }

        if (levels.Length == 0 || levels.All(string.IsNullOrWhiteSpace))
        {
            return [];
        }

        List<LogLevel> parsedLevels = [];
        foreach (string rawLevel in levels)
        {
            if (!Enum.TryParse(rawLevel, ignoreCase: true, out LogLevel parsedLevel))
            {
                continue;
            }

            parsedLevels.Add(parsedLevel);
        }

        return parsedLevels;
    }
}
