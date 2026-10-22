using System.Reflection;
using Microsoft.AspNetCore.Http.HttpResults;
using LibManagarr.Api.Endpoints;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Endpoints;

public sealed class TitleSortingLibraryEndpointTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LibraryGetAndPost_UsePersistedRulesForEveryInstance(bool sonarr, bool post)
    {
        string[] titles = ["The Office", "A Quiet Place", "Batman", "Office"];
        AppSettings settings = new() { TitleSorting = new() { Enabled = true, IgnoredLeadingWords = ["The", "A"] } };
        foreach (string id in new[] { "one", "two" })
        {
            settings.RadarrInstances.Add(new() { Id = id });
            settings.SonarrInstances.Add(new() { Id = id });
            settings.RadarrSnapshots.Add(new() { InstanceId = id, Movies = titles.Select(title => new RadarrMovieSnapshot { Title = title }).ToList() });
            settings.SonarrSnapshots.Add(new() { InstanceId = id, Series = titles.Select(title => new SonarrSeriesSnapshot
            {
                Title = title, Seasons = [new() { SeasonNumber = 1, Episodes = [new() { EpisodeNumber = 1 }] }]
            }).ToList() });
        }
        MethodInfo handler = (sonarr ? typeof(SonarrEndpoints) : typeof(RadarrEndpoints))
            .GetMethod(post ? "PostLibraryAsync" : "GetLibraryAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] arguments = handler.GetParameters().Select(parameter => parameter.Name switch
        {
            "filter" => (object)new LibraryRequestFilter { SonarrInstanceFilter = ["__all__"], RadarrInstanceFilter = ["__all__"] },
            "instanceId" => "one",
            "settingsService" => new StubSettingsService(settings),
            "cancellationToken" => CancellationToken.None,
            _ => parameter.DefaultValue
        }).ToArray();
        Task task = (Task)handler.Invoke(null, arguments)!;
        await task;
        object union = task.GetType().GetProperty("Result")!.GetValue(task)!;
        object result = union.GetType().GetProperty("Result")!.GetValue(union)!;
        List<string[]> instances = result switch
        {
            Ok<RadarrLibraryResponse> response => [response.Value!.Rows.Select(row => row.MovieTitle).ToArray()],
            Ok<SonarrLibraryResponse> response => [response.Value!.Rows.Select(row => row.SeriesTitle).ToArray()],
            Ok<RadarrLibraryBatchResponse> response => response.Value!.Instances.Select(instance => instance.Rows.Select(row => row.MovieTitle).ToArray()).ToList(),
            Ok<SonarrLibraryBatchResponse> response => response.Value!.Instances.Select(instance => instance.Rows.Select(row => row.SeriesTitle).ToArray()).ToList(),
            _ => throw new InvalidOperationException("Unexpected endpoint result.")
        };
        Assert.Equal(post ? 2 : 1, instances.Count);
        Assert.All(instances, rows => Assert.Equal(["Batman", "Office", "The Office", "A Quiet Place"], rows));
    }

    private sealed class StubSettingsService(AppSettings settings) : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);
        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
