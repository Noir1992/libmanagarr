using Microsoft.Extensions.Logging;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class JsonSettingsServiceTests : IDisposable
{
    private readonly TestStorageEnvironment environment = new();
    private readonly InMemoryHistoryStore historyStore = new();

    [Fact]
    public async Task GetAsync_ShouldCreateAndPersistDefaultSettingsWhenNoFileExists()
    {
        JsonSettingsService service = CreateService();

        AppSettings created = await service.GetAsync(CancellationToken.None);
        AppSettings loaded = await service.GetAsync(CancellationToken.None);

        Assert.Equal(created.Language, loaded.Language);
        Assert.Equal(created.Theme, loaded.Theme);
        Assert.Empty(loaded.SonarrInstances);
        Assert.Empty(loaded.RadarrInstances);
        Assert.True(File.Exists(Path.Combine(environment.ContentRootPath, "data", "settings.json")));
    }

    [Fact]
    public async Task SaveAsync_ShouldNormalizeSettingsAndPreserveSnapshotsAndFilterValuesOnReload()
    {
        StoragePaths storagePaths = new(environment, configDirectory: null);
        LogFileService logs = new(TimeProvider.System, storagePaths);
        JsonSettingsService service = new(storagePaths, logs, historyStore);
        AppSettings settings = new()
        {
            Logging = new() { Path = " custom-logs ", RetentionDays = -1, MinimumLevel = LogLevel.Trace },
            RadarrSnapshots = [new() { InstanceId = "r", Movies = [new() { MovieId = 42, Title = "Movie", File = new() { Size = 123, AudioLanguages = ["Japanese"] } }] }],
            LibraryFilterValues = [new() { Version = LibraryFilterValues.CurrentVersion, InstanceId = "r", AudioLanguages = ["Japanese"] }]
        };

        await service.SaveAsync(settings, CancellationToken.None);
        AppSettings reloaded = await service.GetAsync(CancellationToken.None);

        Assert.Equal("./logs", reloaded.Logging.Path);
        Assert.Equal(1, reloaded.Logging.RetentionDays);
        Assert.Equal(LogLevel.Information, logs.MinimumLevel);
        RadarrMovieSnapshot movie = Assert.Single(Assert.Single(reloaded.RadarrSnapshots).Movies);
        Assert.Equal(42, movie.MovieId);
        Assert.Equal(123, movie.File.Size);
        Assert.Equal("Japanese", Assert.Single(movie.File.AudioLanguages));
        LibraryFilterValues filterValues = Assert.Single(reloaded.LibraryFilterValues);
        Assert.Equal("r", filterValues.InstanceId);
        Assert.Equal("Japanese", Assert.Single(filterValues.AudioLanguages));
        Assert.True(Directory.Exists(Path.Combine(environment.ContentRootPath, "data", "logs")));
        Assert.False(Directory.Exists(Path.Combine(environment.ContentRootPath, "data", "custom-logs")));
    }

    [Fact]
    public async Task SaveAsync_ShouldAllowSavingAfterACancelledWrite()
    {
        JsonSettingsService service = CreateService();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveAsync(new AppSettings(), cancellation.Token));
        await service.SaveAsync(new AppSettings { Logging = new() { RetentionDays = 7 } }, CancellationToken.None);

        Assert.Equal(7, (await service.GetAsync(CancellationToken.None)).Logging.RetentionDays);
    }

    [Fact]
    public async Task GetSnapshotAsync_ShouldShareOneSnapshotAcrossConcurrentReaders()
    {
        JsonSettingsService service = CreateService();
        AppSettings[] readers = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => service.GetSnapshotAsync(CancellationToken.None)));

        Assert.All(readers, reader => Assert.Same(readers[0], reader));
    }

    [Fact]
    public async Task GetAsync_ShouldIsolateNestedEditsFromPublishedSnapshot()
    {
        JsonSettingsService service = CreateService();
        await service.SaveAsync(new() { RadarrSnapshots = [new() { Movies = [new() { Title = "Original" }] }] }, CancellationToken.None);
        AppSettings published = await service.GetSnapshotAsync(CancellationToken.None);
        AppSettings editable = await service.GetAsync(CancellationToken.None);
        editable.RadarrSnapshots[0].Movies[0] = new() { Title = "Edited" };
        editable.Logging.RetentionDays = 99;

        Assert.Equal("Original", published.RadarrSnapshots[0].Movies[0].Title);
        Assert.NotEqual(99, published.Logging.RetentionDays);
        Assert.Equal("Original", (await service.GetAsync(CancellationToken.None)).RadarrSnapshots[0].Movies[0].Title);
    }

    [Fact]
    public async Task SaveAsync_ShouldPublishANewDetachedSnapshotAndPersistIt()
    {
        JsonSettingsService service = CreateService();
        AppSettings old = await service.GetSnapshotAsync(CancellationToken.None);
        AppSettings edited = await service.GetAsync(CancellationToken.None);
        edited.Logging.RetentionDays = 17;
        AppSettings saved = await service.SaveAsync(edited, CancellationToken.None);
        AppSettings published = await service.GetSnapshotAsync(CancellationToken.None);
        saved.Logging.RetentionDays = 99;

        Assert.NotSame(old, published);
        Assert.NotSame(saved, published);
        Assert.Equal(17, published.Logging.RetentionDays);
        Assert.NotEqual(17, old.Logging.RetentionDays);
        Assert.Equal(17, (await CreateService().GetAsync(CancellationToken.None)).Logging.RetentionDays);
    }

    [Fact]
    public async Task SaveAsync_ShouldKeepPublishedDataAndDiskAfterCancellation()
    {
        JsonSettingsService service = CreateService();
        AppSettings original = await service.GetSnapshotAsync(CancellationToken.None);
        string path = Path.Combine(environment.ContentRootPath, "data", "settings.json");
        byte[] before = await File.ReadAllBytesAsync(path);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveAsync(new() { Logging = new() { RetentionDays = 17 } }, cancellation.Token));

        Assert.Same(original, await service.GetSnapshotAsync(CancellationToken.None));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public async Task GetSnapshotAsync_ShouldPersistNormalizationOnceForFutureServiceInstances()
    {
        string directory = Path.Combine(environment.ContentRootPath, "data");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(path, """
            {"sonarrInstances":[{"id":"legacy-id","name":" Sonarr ","baseUrl":"http://sonarr/","apiKey":"test"}]}
            """);

        AppSettings first = await CreateService().GetSnapshotAsync(CancellationToken.None);
        AppSettings second = await CreateService().GetSnapshotAsync(CancellationToken.None);

        Assert.NotEqual("legacy-id", first.SonarrInstances[0].Id);
        Assert.Equal(first.SonarrInstances[0].Id, second.SonarrInstances[0].Id);
        Assert.Equal("Sonarr", second.SonarrInstances[0].Name);
        Assert.Equal("http://sonarr", second.SonarrInstances[0].BaseUrl);
    }

    private JsonSettingsService CreateService()
    {
        StoragePaths storagePaths = new(environment, configDirectory: null);
        return new(storagePaths, new LogFileService(TimeProvider.System, storagePaths), historyStore);
    }

    [Fact]
    public async Task SaveAsync_ShouldKeepSnapshotsOutOfSettingsFile_AndReloadThemThroughHistoryStore()
    {
        JsonSettingsService service = CreateService();
        SonarrInstanceSnapshot snapshot = new()
        {
            InstanceId = "s",
            InstanceName = "Sonarr",
            SyncedAt = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero),
            Series = [new() { SeriesId = 7, Title = "Andor" }]
        };

        await service.SaveAsync(new() { SonarrSnapshots = [snapshot] }, CancellationToken.None);

        string persisted = await File.ReadAllTextAsync(Path.Combine(environment.ContentRootPath, "data", "settings.json"));
        Assert.DoesNotContain("sonarrSnapshots", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Andor", persisted, StringComparison.Ordinal);

        AppSettings reloaded = await CreateService().GetAsync(CancellationToken.None);
        SonarrInstanceSnapshot reloadedSnapshot = Assert.Single(reloaded.SonarrSnapshots);
        Assert.Equal("s", reloadedSnapshot.InstanceId);
        Assert.Equal("Andor", Assert.Single(reloadedSnapshot.Series).Title);
    }

    [Fact]
    public async Task SaveAsync_ShouldKeepFilterValuesOutOfSettingsFile_AndReloadThemThroughHistoryStore()
    {
        JsonSettingsService service = CreateService();
        AppSettings settings = await service.GetAsync(CancellationToken.None);
        settings.LibraryFilterValues = [new() { Version = LibraryFilterValues.CurrentVersion, InstanceId = "s", Genres = ["Drama"] }];

        await service.SaveAsync(settings, CancellationToken.None);

        string persisted = await File.ReadAllTextAsync(Path.Combine(environment.ContentRootPath, "data", "settings.json"));
        Assert.DoesNotContain("libraryFilterValues", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Drama", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("scheduledTasks", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nextRunAt", persisted, StringComparison.OrdinalIgnoreCase);

        AppSettings reloaded = await CreateService().GetAsync(CancellationToken.None);
        LibraryFilterValues filterValues = Assert.Single(reloaded.LibraryFilterValues);
        Assert.Equal("s", filterValues.InstanceId);
        Assert.Equal("Drama", Assert.Single(filterValues.Genres));
    }

    [Fact]
    public async Task TitleSorting_PersistsAcrossRestartAndUnrelatedUpdates()
    {
        ISettingsService service = CreateService();
        await service.UpdateAsync(settings => settings.TitleSorting = new() { Enabled = true, IgnoredLeadingWords = [" The ", "THE", "A"] }, CancellationToken.None);
        await service.UpdateAsync(settings => settings.Theme = ThemeMode.Dark, CancellationToken.None);
        AppSettings reloaded = await CreateService().GetAsync(CancellationToken.None);
        Assert.True(reloaded.TitleSorting.Enabled);
        Assert.Equal(["The", "A"], reloaded.TitleSorting.IgnoredLeadingWords);
        Assert.Equal(ThemeMode.Dark, reloaded.Theme);
        string persisted = await File.ReadAllTextAsync(Path.Combine(environment.ContentRootPath, "data", "settings.json"));
        Assert.Contains("titleSorting", persisted);
    }

    public void Dispose() => environment.Dispose();
}
