using System.Reflection;
using System.Text.Json;
using LibManagarr.Api.Endpoints;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Endpoints;

public sealed class LibraryFilterCountEndpointTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Counts_ShouldRespectSelectedInstancesAndRebuildOldCaches(bool sonarr)
    {
        AppSettings settings = new()
        {
            RadarrInstances = [new() { Id = "first" }, new() { Id = "second" }],
            SonarrInstances = [new() { Id = "first" }, new() { Id = "second" }],
            LibraryFilterValues = [new() { InstanceId = "first", Version = 1, VideoCodecs = ["Old"] }],
            RadarrSnapshots = [
                new() { InstanceId = "first", Movies = [new() { File = new() { VideoCodec = "HEVC" } }] },
                new() { InstanceId = "second", Movies = [new() { File = new() { VideoCodec = "HEVC" } }, new() { File = new() { VideoCodec = "AV1" } }] }
            ],
            SonarrSnapshots = [
                new() { InstanceId = "first", Series = [Series("HEVC")] },
                new() { InstanceId = "second", Series = [Series("HEVC"), Series("AV1")] }
            ]
        };
        JsonElement selected = await Query(sonarr, settings, ["first"]);
        JsonElement selectedCounts = selected.GetProperty("Counts");
        Assert.Equal(1, selectedCounts.GetProperty("video-codec-filter").GetProperty("HEVC").GetInt32());
        Assert.Equal(1, selectedCounts.GetProperty("video-codec-filter").GetProperty("__all__").GetInt32());
        Assert.False(selectedCounts.GetProperty("video-codec-filter").TryGetProperty("AV1", out _));
        string instanceFilter = sonarr ? "sonarr-instance-filter" : "radarr-instance-filter";
        Assert.Equal(1, selectedCounts.GetProperty(instanceFilter).GetProperty("first").GetInt32());
        Assert.Equal(2, selectedCounts.GetProperty(instanceFilter).GetProperty("second").GetInt32());
        Assert.Equal(3, selectedCounts.GetProperty(instanceFilter).GetProperty("__all__").GetInt32());
        JsonElement all = (await Query(sonarr, settings, ["__all__"])).GetProperty("Counts");
        Assert.Equal(2, all.GetProperty("video-codec-filter").GetProperty("HEVC").GetInt32());
        Assert.Equal(1, all.GetProperty("video-codec-filter").GetProperty("AV1").GetInt32());
        Assert.Equal(3, all.GetProperty("video-codec-filter").GetProperty("__all__").GetInt32());
        Assert.Equal(1, Assert.Single(settings.LibraryFilterValues).Version);
    }

    private static SonarrSeriesSnapshot Series(string codec) => new()
    {
        Seasons = [new() { Episodes = [new() { File = new() { VideoCodec = codec } }] }]
    };

    private static async Task<JsonElement> Query(bool sonarr, AppSettings settings, string[] instanceIds)
    {
        Type endpoints = sonarr ? typeof(SonarrEndpoints) : typeof(RadarrEndpoints);
        MethodInfo method = endpoints.GetMethod("PostFilterValuesAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        Type requestType = method.GetParameters()[0].ParameterType;
        object request = Activator.CreateInstance(requestType, true)!;
        requestType.GetProperty("InstanceIds")!.SetValue(request, instanceIds);
        Task task = (Task)method.Invoke(null, [request, new SettingsStub(settings)])!;
        await task;
        object result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        object ok = result.GetType().GetProperty("Result")!.GetValue(result)!;
        return JsonSerializer.SerializeToElement(ok.GetType().GetProperty("Value")!.GetValue(ok)!);
    }

    private sealed class SettingsStub(AppSettings settings) : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);
        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Filter queries must not write settings.");
    }
}
