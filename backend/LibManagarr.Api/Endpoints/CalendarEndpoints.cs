using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Endpoints;

// Aggregated release calendar across every configured Sonarr and Radarr instance.
public static class CalendarEndpoints
{
    public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api");
        group.RequireAuthorization();

        group.MapGet("/calendar", GetCalendarAsync);

        return endpoints;
    }

    // The timezone turns the requested calendar days into the UTC boundaries used for timed
    // releases; pure-date releases are matched by their calendar day alone.
    private static async Task<Results<Ok<CalendarResponse>, ValidationProblem>> GetCalendarAsync(
        string? startDate,
        string? endDate,
        string? timeZone,
        bool refresh = false,
        CalendarService calendarService = null!,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, string[]> validationErrors = ValidateQuery(startDate, endDate, timeZone);
        if (validationErrors.Count > 0)
        {
            return TypedResults.ValidationProblem(validationErrors);
        }

        DateOnly start = DateOnly.Parse(startDate!, CultureInfo.InvariantCulture);
        DateOnly end = DateOnly.Parse(endDate!, CultureInfo.InvariantCulture);
        TimeZoneInfo zone = ResolveTimeZone(timeZone!);

        CalendarResponse response = await calendarService.GetCalendarAsync(
            start,
            end,
            zone,
            refresh,
            cancellationToken);

        return TypedResults.Ok(response);
    }

    private static Dictionary<string, string[]> ValidateQuery(string? startDate, string? endDate, string? timeZone)
    {
        Dictionary<string, string[]> errors = [];

        if (!TryParseDate(startDate, out DateOnly start))
        {
            errors["startDate"] = ["startDate must be a date in the format yyyy-MM-dd."];
        }

        if (!TryParseDate(endDate, out DateOnly end))
        {
            errors["endDate"] = ["endDate must be a date in the format yyyy-MM-dd."];
        }

        if (errors.Count == 0)
        {
            if (end <= start)
            {
                errors["endDate"] = ["endDate must be after startDate."];
            }
            else if (end.DayNumber - start.DayNumber > CalendarService.MaxRangeDays)
            {
                errors["endDate"] = [$"The requested range must not exceed {CalendarService.MaxRangeDays} days."];
            }
        }

        if (TryResolveTimeZone(timeZone, out TimeZoneInfo? _))
        {
            // Valid identifiers are accepted; the resolution failure below reports the rest.
        }
        else
        {
            errors["timeZone"] = ["timeZone must be a valid IANA time zone identifier."];
        }

        return errors;
    }

    private static bool TryParseDate(string? value, out DateOnly date)
    {
        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static bool TryResolveTimeZone(string? timeZoneId, out TimeZoneInfo timeZone)
    {
        timeZone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return false;
        }

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        if (!TryResolveTimeZone(timeZoneId, out TimeZoneInfo timeZone))
        {
            throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                "Invalid time zone",
                "The requested time zone is not supported by the server.");
        }

        return timeZone;
    }
}
