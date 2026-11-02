using System.Net;
using System.Text;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibManagarr.Api.Tests.Services;

public sealed class CalendarServiceTests
{
    private const string SonarrEpisodeJson = """
        [
          {
            "id": 101,
            "seriesId": 7,
            "title": "Erste Folge",
            "seasonNumber": 2,
            "episodeNumber": 5,
            "hasFile": false,
            "airDate": "2026-11-02T21:00:00Z",
            "airDateUtc": "2026-11-02T20:00:00Z",
            "monitored": true,
            "series": { "id": 7, "title": "Beispielserie", "titleSlug": "beispielserie" }
          },
          {
            "id": 102,
            "seriesId": 7,
            "title": "Folge ohne Uhrzeit",
            "seasonNumber": 2,
            "episodeNumber": 6,
            "hasFile": true,
            "airDate": "2026-11-04T00:00:00",
            "monitored": true,
            "series": { "id": 7, "title": "Beispielserie", "titleSlug": "beispielserie" }
          },
          {
            "id": 103,
            "seriesId": 7,
            "title": "Nach dem Zeitraum",
            "seasonNumber": 2,
            "episodeNumber": 7,
            "hasFile": false,
            "airDateUtc": "2026-11-09T00:00:00Z",
            "monitored": true,
            "series": { "id": 7, "title": "Beispielserie", "titleSlug": "beispielserie" }
          }
        ]
        """;

    private const string RadarrMovieJson = """
        [
          {
            "id": 201,
            "title": "Beispielfilm",
            "titleSlug": "beispielfilm",
            "hasFile": false,
            "inCinemas": "2026-11-02T00:00:00Z",
            "digitalRelease": "2026-11-08T00:00:00+02:00",
            "physicalRelease": "2026-11-10T00:00:00Z",
            "monitored": true
          }
        ]
        """;

    [Fact]
    public async Task GetCalendar_MapsSonarrEpisodes_AndAppliesRangeBoundaries()
    {
        StubJsonHandler sonarrHandler = new(SonarrEpisodeJson);
        CalendarService service = CreateService(
            sonarrInstances: [CreateInstance("sonarr-1", "Sonarr Primär")],
            radarrInstances: [],
            sonarrHandler: sonarrHandler,
            radarrHandler: new StubJsonHandler("[]"));

        CalendarResponse response = await service.GetCalendarAsync(
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 9),
            TimeZoneInfo.Utc,
            refresh: false,
            CancellationToken.None);

        // The 2026-11-09 episode sits exactly on the exclusive end boundary and must be dropped.
        Assert.Equal(2, response.Entries.Count);

        CalendarEntry timed = response.Entries.Single(entry => entry.Id == "sonarr:sonarr-1:101:episode");
        Assert.Equal(ArrApplicationKind.Sonarr, timed.Application);
        Assert.Equal("Beispielserie", timed.Title);
        Assert.Equal("Erste Folge", timed.EpisodeTitle);
        Assert.Equal(2, timed.SeasonNumber);
        Assert.Equal(5, timed.EpisodeNumber);
        Assert.Equal(CalendarReleaseType.Episode, timed.ReleaseType);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 20, 0, 0, TimeSpan.Zero), timed.ReleaseDateTimeUtc);
        Assert.Null(timed.ReleaseDate);
        Assert.False(timed.HasFile);
        Assert.Equal(7, timed.SeriesId);
        Assert.Equal("/api/v3/calendar", sonarrHandler.CapturedRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("unmonitored=true", sonarrHandler.CapturedRequest.RequestUri.Query);

        // The episode without airDateUtc keeps its pure calendar day without an invented time.
        CalendarEntry dateOnly = response.Entries.Single(entry => entry.Id == "sonarr:sonarr-1:102:episode");
        Assert.Equal(new DateOnly(2026, 11, 4), dateOnly.ReleaseDate);
        Assert.Null(dateOnly.ReleaseDateTimeUtc);
        Assert.True(dateOnly.HasFile);
    }

    [Fact]
    public async Task GetCalendar_MapsRadarrMovies_AsDateOnlyEntriesPerReleaseType()
    {
        CalendarService service = CreateService(
            sonarrInstances: [],
            radarrInstances: [CreateInstance("radarr-1", "Radarr Primär")],
            sonarrHandler: new StubJsonHandler("[]"),
            radarrHandler: new StubJsonHandler(RadarrMovieJson));

        CalendarResponse response = await service.GetCalendarAsync(
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 9),
            TimeZoneInfo.Utc,
            refresh: false,
            CancellationToken.None);

        // Cinema and digital fall inside the range, physical on 2026-11-10 does not. The digital
        // value carries a +02:00 offset and must stay on 2026-11-08 instead of shifting a day back.
        Assert.Equal(2, response.Entries.Count);

        CalendarEntry cinema = response.Entries.Single(entry => entry.Id == "radarr:radarr-1:201:cinema");
        Assert.Equal(new DateOnly(2026, 11, 2), cinema.ReleaseDate);
        Assert.Null(cinema.ReleaseDateTimeUtc);
        Assert.Equal(CalendarReleaseType.Cinema, cinema.ReleaseType);

        CalendarEntry digital = response.Entries.Single(entry => entry.Id == "radarr:radarr-1:201:digital");
        Assert.Equal(new DateOnly(2026, 11, 8), digital.ReleaseDate);
        Assert.False(digital.HasFile);

        Assert.DoesNotContain(response.Entries, entry => entry.ReleaseType == CalendarReleaseType.Physical);
    }

    [Fact]
    public async Task GetCalendar_ReportsPerInstanceFailure_WhileKeepingHealthyEntries()
    {
        CalendarService service = CreateService(
            sonarrInstances: [CreateInstance("sonarr-ok", "Sonarr Ok"), CreateInstance("sonarr-broken", "Sonarr Broken")],
            radarrInstances: [],
            sonarrHandler: new StubRoutingHandler(sonarrOkJson: SonarrEpisodeJson, brokenInstanceId: "sonarr-broken"),
            radarrHandler: new StubJsonHandler("[]"));

        CalendarResponse response = await service.GetCalendarAsync(
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 9),
            TimeZoneInfo.Utc,
            refresh: false,
            CancellationToken.None);

        Assert.NotEmpty(response.Entries);
        CalendarInstanceStatus broken = response.Instances.Single(status => status.InstanceId == "sonarr-broken");
        Assert.False(broken.Loaded);
        Assert.False(string.IsNullOrWhiteSpace(broken.Error));
        CalendarInstanceStatus ok = response.Instances.Single(status => status.InstanceId == "sonarr-ok");
        Assert.True(ok.Loaded);
        Assert.Equal(2, response.Instances.Count);
    }

    [Fact]
    public async Task GetCalendar_ServesSecondCallFromCache_AndBypassesItOnRefresh()
    {
        StubJsonHandler sonarrHandler = new(SonarrEpisodeJson);
        CalendarService service = CreateService(
            sonarrInstances: [CreateInstance("sonarr-1", "Sonarr Primär")],
            radarrInstances: [],
            sonarrHandler: sonarrHandler,
            radarrHandler: new StubJsonHandler("[]"));

        DateOnly start = new(2026, 11, 2);
        DateOnly end = new(2026, 11, 9);

        await service.GetCalendarAsync(start, end, TimeZoneInfo.Utc, refresh: false, CancellationToken.None);
        await service.GetCalendarAsync(start, end, TimeZoneInfo.Utc, refresh: false, CancellationToken.None);
        Assert.Equal(1, sonarrHandler.CallCount);

        await service.GetCalendarAsync(start, end, TimeZoneInfo.Utc, refresh: true, CancellationToken.None);
        Assert.Equal(2, sonarrHandler.CallCount);
    }

    [Fact]
    public async Task GetCalendar_AppliesTimeZoneBoundariesToTimedEntries()
    {
        // The episode airs at 2026-11-01T23:30Z which is 2026-11-02 in Europe/Berlin and therefore
        // inside the requested local week, while the same episode is outside a UTC week window.
        const string json = """
            [
              {
                "id": 301,
                "seriesId": 9,
                "title": "Spaetae Folge",
                "seasonNumber": 1,
                "episodeNumber": 1,
                "hasFile": false,
                "airDateUtc": "2026-11-01T23:30:00Z",
                "monitored": true,
                "series": { "id": 9, "title": "Grenzwertige Serie", "titleSlug": "grenze" }
              }
            ]
            """;

        CalendarService berlinService = CreateService(
            sonarrInstances: [CreateInstance("sonarr-1", "Sonarr")],
            radarrInstances: [],
            sonarrHandler: new StubJsonHandler(json),
            radarrHandler: new StubJsonHandler("[]"));

        CalendarResponse berlinResponse = await berlinService.GetCalendarAsync(
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 3),
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"),
            refresh: false,
            CancellationToken.None);

        CalendarService utcService = CreateService(
            sonarrInstances: [CreateInstance("sonarr-1", "Sonarr")],
            radarrInstances: [],
            sonarrHandler: new StubJsonHandler(json),
            radarrHandler: new StubJsonHandler("[]"));

        CalendarResponse utcResponse = await utcService.GetCalendarAsync(
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 3),
            TimeZoneInfo.Utc,
            refresh: false,
            CancellationToken.None);

        Assert.Single(berlinResponse.Entries);
        Assert.Empty(utcResponse.Entries);
    }

    private static CalendarService CreateService(
        List<ArrInstanceSettings> sonarrInstances,
        List<ArrInstanceSettings> radarrInstances,
        HttpMessageHandler sonarrHandler,
        HttpMessageHandler radarrHandler)
    {
        AppSettings settings = new()
        {
            SonarrInstances = sonarrInstances,
            RadarrInstances = radarrInstances
        };

        TimeProvider timeProvider = TimeProvider.System;
        return new CalendarService(
            new StubSettingsService(settings),
            new SonarrClient(new HttpClient(sonarrHandler)),
            new RadarrClient(new HttpClient(radarrHandler)),
            new CalendarCache(timeProvider),
            timeProvider,
            NullLogger<CalendarService>.Instance);
    }

    private static ArrInstanceSettings CreateInstance(string id, string name)
    {
        return new ArrInstanceSettings
        {
            Id = id,
            Name = name,
            BaseUrl = $"http://{id}.local",
            ApiKey = "secret"
        };
    }

    private sealed class StubSettingsService(AppSettings settings) : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);

        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken) => Task.FromResult(value);
    }

    private sealed class StubJsonHandler(string json) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public HttpRequestMessage? CapturedRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            CapturedRequest = request;

            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
                RequestMessage = request
            };

            return Task.FromResult(response);
        }
    }

    // Answers the healthy Sonarr instance with the fixture and every other host with a server error.
    private sealed class StubRoutingHandler(string sonarrOkJson, string brokenInstanceId) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            bool broken = request.RequestUri?.Host.Contains(brokenInstanceId, StringComparison.OrdinalIgnoreCase) == true;
            HttpStatusCode status = broken ? HttpStatusCode.InternalServerError : HttpStatusCode.OK;
            string body = broken ? string.Empty : sonarrOkJson;

            HttpResponseMessage response = new(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request
            };

            return Task.FromResult(response);
        }
    }
}
