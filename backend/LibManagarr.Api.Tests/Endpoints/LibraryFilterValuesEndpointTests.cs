using System.Reflection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using LibManagarr.Api.Endpoints;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Endpoints;

public sealed class LibraryFilterValuesEndpointTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingCache_ShouldReturnOptionsFromExistingSnapshots(bool sonarr)
    {
        AppSettings settings = CreateSettings();
        LibraryFilterValues actual = await Query(sonarr, settings, ["__all__"]);

        Assert.Equal(new[] { "Drama" }, actual.Genres);
        Assert.Equal(new[] { "Favorite" }, actual.Tags);
        Assert.Equal(new[] { "released" }, actual.Statuses);
        Assert.Equal(new[] { "HD" }, actual.QualityProfiles);
        Assert.Equal(new[] { "German", "Japanese" }, actual.AudioLanguages.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(new[] { "English", "German" }, actual.SubtitleLanguages.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Empty(settings.LibraryFilterValues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedInstances_ShouldExcludeOptionsFromOtherInstances(bool sonarr)
    {
        AppSettings settings = CreateSettings();
        (sonarr ? settings.SonarrInstances : settings.RadarrInstances).Add(new() { Id = "second" });
        settings.LibraryFilterValues.Add(new() { InstanceId = "second", Genres = ["Horror"] });

        LibraryFilterValues selected = await Query(sonarr, settings, ["first"]);
        Assert.Equal(new[] { "Drama" }, selected.Genres);
        LibraryFilterValues all = await Query(sonarr, settings, ["__all__"]);
        Assert.Equal(new[] { "Drama", "Horror" }, all.Genres.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingCache_ShouldRemainAvailableWithoutSnapshots(bool sonarr)
    {
        AppSettings settings = CreateSettings();
        settings.RadarrSnapshots.Clear();
        settings.SonarrSnapshots.Clear();
        settings.LibraryFilterValues.Add(new()
        {
            InstanceId = "first", Genres = ["Comedy"], AudioLanguages = ["French"], SubtitleLanguages = ["Italian"]
        });

        LibraryFilterValues actual = await Query(sonarr, settings, ["first"]);
        Assert.Equal(new[] { "Comedy" }, actual.Genres);
        Assert.Equal(new[] { "French" }, actual.AudioLanguages);
        Assert.Equal(new[] { "Italian" }, actual.SubtitleLanguages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingCacheAndSnapshot_ShouldReturnEmptyOptions(bool sonarr)
    {
        AppSettings settings = CreateSettings();
        settings.RadarrSnapshots.Clear();
        settings.SonarrSnapshots.Clear();
        LibraryFilterValues actual = await Query(sonarr, settings, ["first"]);
        Assert.Empty(actual.Genres);
        Assert.Empty(actual.Statuses);
        Assert.Empty(actual.AudioLanguages);
        Assert.Empty(actual.SubtitleLanguages);
    }

    private static AppSettings CreateSettings()
    {
        MediaFileSnapshot file = new()
        {
            AudioLanguages = ["Japanese", "German", "GERMAN", ""], SubtitleLanguages = ["German", "English", " "]
        };
        return new AppSettings
        {
            RadarrInstances = [new() { Id = "first" }], SonarrInstances = [new() { Id = "first" }],
            RadarrSnapshots = [new()
            {
                InstanceId = "first", Movies = [new()
                {
                    Genres = ["Drama", "DRAMA"], Tags = ["Favorite"], Status = "released", Profile = new() { Name = "HD" }, File = file
                }]
            }],
            SonarrSnapshots = [new()
            {
                InstanceId = "first", Series = [new()
                {
                    Genres = ["Drama", "DRAMA"], Tags = ["Favorite"], Status = "released", Profile = new() { Name = "HD" },
                    Seasons = [new() { Episodes = [new() { File = file }, new() { File = new() { AudioLanguages = ["Japanese"] } }] }]
                }]
            }]
        };
    }

    private static async Task<LibraryFilterValues> Query(bool sonarr, AppSettings settings, string[] instanceIds)
    {
        Type endpoints = sonarr ? typeof(SonarrEndpoints) : typeof(RadarrEndpoints);
        MethodInfo method = endpoints.GetMethod("PostFilterValuesAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        Type requestType = method.GetParameters()[0].ParameterType;
        object request = Activator.CreateInstance(requestType, true)!;
        requestType.GetProperty("InstanceIds")!.SetValue(request, instanceIds);
        object task = method.Invoke(null, [request, new SettingsStub(settings)])!;
        if (sonarr)
        {
            var response = await (Task<Results<Ok<SonarrLibraryFilterValuesResponse>, NotFound<ProblemDetails>>>)task;
            SonarrLibraryFilterValuesResponse values = Assert.IsType<Ok<SonarrLibraryFilterValuesResponse>>(response.Result).Value!;
            return new() { Genres = [.. values.Genres], Tags = [.. values.Tags], Statuses = [.. values.Statuses],
                QualityProfiles = [.. values.QualityProfiles], AudioLanguages = [.. values.AudioLanguages], SubtitleLanguages = [.. values.SubtitleLanguages] };
        }
        var radarrResponse = await (Task<Results<Ok<RadarrLibraryFilterValuesResponse>, NotFound<ProblemDetails>>>)task;
        RadarrLibraryFilterValuesResponse radarrValues = Assert.IsType<Ok<RadarrLibraryFilterValuesResponse>>(radarrResponse.Result).Value!;
        return new() { Genres = [.. radarrValues.Genres], Tags = [.. radarrValues.Tags], Statuses = [.. radarrValues.Statuses],
            QualityProfiles = [.. radarrValues.QualityProfiles], AudioLanguages = [.. radarrValues.AudioLanguages], SubtitleLanguages = [.. radarrValues.SubtitleLanguages] };
    }

    private sealed class SettingsStub(AppSettings settings) : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);
        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Filter queries must not write settings.");
    }
}
