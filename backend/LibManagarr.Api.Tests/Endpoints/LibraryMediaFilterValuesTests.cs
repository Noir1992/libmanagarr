using System.Reflection;
using System.Text.Json;
using LibManagarr.Api.Endpoints;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Endpoints;

public sealed class LibraryMediaFilterValuesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyCache_ShouldRebuildMediaOptionsFromSnapshotWithoutWritingSettings(bool sonarr)
    {
        MediaFileSnapshot file = new()
        {
            Resolution = "1920x800", VideoCodec = "HEVC", AudioCodec = "AAC", VideoFormat = "HDR10", Container = "matroska"
        };
        AppSettings settings = new()
        {
            RadarrInstances = [new() { Id = "first" }], SonarrInstances = [new() { Id = "first" }],
            LibraryFilterValues = [new() { InstanceId = "first", Genres = ["Old"] }],
            RadarrSnapshots = [new() { InstanceId = "first", Movies = [new() { File = file }] }],
            SonarrSnapshots = [new() { InstanceId = "first", Series = [new() { Seasons = [new() { Episodes = [new() { File = file }] }] }] }]
        };
        Type endpoints = sonarr ? typeof(SonarrEndpoints) : typeof(RadarrEndpoints);
        MethodInfo method = endpoints.GetMethod("PostFilterValuesAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        Type requestType = method.GetParameters()[0].ParameterType;
        object request = Activator.CreateInstance(requestType, true)!;
        requestType.GetProperty("InstanceIds")!.SetValue(request, new[] { "first" });
        Task task = (Task)method.Invoke(null, [request, new SettingsStub(settings)])!;
        await task;
        object result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        object ok = result.GetType().GetProperty("Result")!.GetValue(result)!;
        object value = ok.GetType().GetProperty("Value")!.GetValue(ok)!;
        JsonElement response = JsonSerializer.SerializeToElement(value);
        Assert.Equal("1080p", response.GetProperty("Resolutions")[0].GetString());
        Assert.Equal("HEVC", response.GetProperty("VideoCodecs")[0].GetString());
        Assert.Equal("AAC", response.GetProperty("AudioCodecs")[0].GetString());
        Assert.Equal("HDR10", response.GetProperty("DynamicRanges")[0].GetString());
        Assert.Equal("MKV", response.GetProperty("ContainerFormats")[0].GetString());
        Assert.Equal(0, Assert.Single(settings.LibraryFilterValues).Version);
    }

    private sealed class SettingsStub(AppSettings settings) : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);
        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Reading filter options must not write settings.");
    }
}
