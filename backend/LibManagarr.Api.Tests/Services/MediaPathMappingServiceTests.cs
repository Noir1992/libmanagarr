using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class MediaPathMappingServiceTests
{
    private const string RadarrInstance = "radarr-1";
    private const string SonarrInstance = "sonarr-1";

    [Fact]
    public async Task TranslateToLibManagarrPath_TranslatesTheInstanceView()
    {
        MediaPathMappingService service = CreateService(Setup());

        PathMappingResult result = await service.TranslateToLibManagarrPathAsync(
            RadarrInstance, ArrApplicationKind.Radarr, @"\\nas\radarr\Film (2010)\film.mkv", CancellationToken.None);

        Assert.True(result.Success, result.Failure);
        Assert.Equal(LibManagarr("movies/Film (2010)/film.mkv"), result.Path);
        Assert.Equal("radarr-mapping", result.MappingId);
    }

    [Fact]
    public async Task TranslateToInstancePath_TranslatesBackIntoTheGrammarOfThatInstance()
    {
        MediaPathMappingService service = CreateService(Setup());

        PathMappingResult result = await service.TranslateToInstancePathAsync(
            SonarrInstance, ArrApplicationKind.Sonarr, LibManagarr("series/Writing S00E01.mkv"), CancellationToken.None);

        Assert.True(result.Success, result.Failure);
        // The Sonarr side is a Windows host even when this process is Linux, so the answer has to stay
        // in Windows grammar; a runtime based conversion would hand Sonarr a path it cannot open.
        Assert.Equal(@"\\tv\sonarr\specials\Writing S00E01.mkv", result.Path);
    }

    [Fact]
    public async Task Translate_ReportsTheMissingMappingInsteadOfGuessingAPath()
    {
        MediaPathMappingService service = CreateService(Setup());

        PathMappingResult result = await service.TranslateToLibManagarrPathAsync(
            "radarr-9", ArrApplicationKind.Radarr, @"\\nas\radarr\film.mkv", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.Path);
        Assert.Contains("radarr-9", result.Failure);
    }

    [Fact]
    public async Task EnsureWithinStorageArea_AllowsOnlyTheConfiguredRootOfThatKind()
    {
        MediaPathMappingService service = CreateService(Setup());

        Assert.True((await service.EnsureWithinStorageAreaAsync(
            ArrApplicationKind.Radarr, LibManagarr("movies/Film (2010)/film.mkv"), CancellationToken.None)).Success);
        Assert.True((await service.EnsureWithinStorageAreaAsync(
            ArrApplicationKind.Sonarr, LibManagarr("series/Writing S00E01.mkv"), CancellationToken.None)).Success);

        // The same file seen as a series target leaves the series root, which is the line a link may
        // never cross: the movies shelf is not a place a Sonarr episode file belongs.
        PathMappingResult wrongKind = await service.EnsureWithinStorageAreaAsync(
            ArrApplicationKind.Sonarr, LibManagarr("movies/Film (2010)/film.mkv"), CancellationToken.None);
        Assert.False(wrongKind.Success);
        Assert.Contains("series root", wrongKind.Failure);

        PathMappingResult outside = await service.EnsureWithinStorageAreaAsync(
            ArrApplicationKind.Radarr, LibManagarr("downloads/film.mkv"), CancellationToken.None);
        Assert.False(outside.Success);
        Assert.Contains("outside every configured", outside.Failure);
    }

    [Fact]
    public async Task EnsureWithinStorageArea_RefusesAPathThisProcessCannotOpen()
    {
        MediaPathMappingService service = CreateService(Setup());
        string foreignGrammar = ArrPathRules.RuntimeSyntax == ArrPathSyntax.Windows
            ? "/media/movies/film.mkv"
            : @"C:\media\movies\film.mkv";

        PathMappingResult result = await service.EnsureWithinStorageAreaAsync(
            ArrApplicationKind.Radarr, foreignGrammar, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("syntax", result.Failure);
    }

    [Fact]
    public async Task EnsureWithinStorageArea_RequiresAStorageAreaOfThatKind()
    {
        SymlinkSettings configuration = Setup();
        configuration.StorageAreas.Clear();

        PathMappingResult result = await CreateService(configuration).EnsureWithinStorageAreaAsync(
            ArrApplicationKind.Radarr, LibManagarr("movies/film.mkv"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("No storage area with a movie root is configured", result.Failure);
    }

    private static SymlinkSettings Setup() => new()
    {
        StorageAreas =
        [
            new MediaStorageArea
            {
                Id = "area-1",
                Name = "Main",
                MoviesRoot = LibManagarr("movies"),
                SeriesRoot = LibManagarr("series")
            }
        ],
        PathMappings =
        [
            new ArrPathMapping
            {
                Id = "radarr-mapping",
                InstanceId = RadarrInstance,
                Kind = ArrApplicationKind.Radarr,
                InstanceRoot = @"\\nas\radarr",
                LibManagarrRoot = LibManagarr("movies")
            },
            new ArrPathMapping
            {
                Id = "sonarr-mapping",
                InstanceId = SonarrInstance,
                Kind = ArrApplicationKind.Sonarr,
                InstanceRoot = @"\\tv\sonarr\specials",
                LibManagarrRoot = LibManagarr("series")
            }
        ]
    };

    private static MediaPathMappingService CreateService(SymlinkSettings settings) =>
        new(new FixedSettings(new AppSettings { Symlinks = settings }));

    /// <summary>The LibManagarr side is always this process' own grammar, so the fixture has to be too.</summary>
    private static string LibManagarr(string relative) =>
        ArrPathRules.RuntimeSyntax == ArrPathSyntax.Windows
            ? @"C:\media\" + relative.Replace('/', '\\')
            : "/media/" + relative;

    private sealed class FixedSettings(AppSettings settings) : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);
        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken) => Task.FromResult(value);
    }
}
