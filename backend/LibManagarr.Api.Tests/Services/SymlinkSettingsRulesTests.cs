using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class SymlinkSettingsRulesTests
{
    // Instance ids are handed out by the normalizer, so a mapping that has to be found again after a
    // normalization round has to name an identifier that round accepts.
    private static readonly ArrInstanceSettings Sonarr = new() { Id = UuidV7.Create(), Name = "Sonarr", BaseUrl = "http://localhost:8989", ApiKey = "k" };
    private static readonly ArrInstanceSettings Radarr = new() { Id = UuidV7.Create(), Name = "Radarr", BaseUrl = "http://localhost:7878", ApiKey = "k" };

    [Fact]
    public void Normalize_TrimsFormatsRootsAndAssignsIdentifiers()
    {
        SymlinkSettings normalized = SymlinkSettingsRules.Normalize(new SymlinkSettings
        {
            StorageAreas = [new MediaStorageArea { Name = "  Main  ", MoviesRoot = "  " + LibManagarr("movies") + "  ", SeriesRoot = LibManagarr("series") }],
            PathMappings = [new ArrPathMapping { InstanceId = " radarr-1 ", Kind = ArrApplicationKind.Radarr, InstanceRoot = @"\\nas\radarr\", LibManagarrRoot = LibManagarr("movies") }],
            ReaderViews = [new PathReaderView { Kind = PathReaderKind.Sonarr, Root = @"\\tv\series" }],
            Revision = -3
        });

        Assert.Equal(0, normalized.Revision);
        MediaStorageArea area = Assert.Single(normalized.StorageAreas);
        Assert.Equal("Main", area.Name);
        Assert.True(UuidV7.IsUuidV7(area.Id));
        Assert.Equal(LibManagarr("movies"), area.MoviesRoot);
        ArrPathMapping mapping = Assert.Single(normalized.PathMappings);
        Assert.Equal("radarr-1", mapping.InstanceId);
        Assert.Equal(@"\\nas\radarr", mapping.InstanceRoot);
        Assert.Equal(PathReaderKind.Sonarr, Assert.Single(normalized.ReaderViews).Kind);
    }

    [Fact]
    public void Normalize_ReplacesAForeignPathWithoutDestroyingIt()
    {
        SymlinkSettings normalized = SymlinkSettingsRules.Normalize(new SymlinkSettings
        {
            StorageAreas = [new MediaStorageArea { Id = "a", Name = "Main", MoviesRoot = "not absolute", SeriesRoot = LibManagarr("series") }]
        });

        Assert.Equal("not absolute", normalized.StorageAreas[0].MoviesRoot);
    }

    [Fact]
    public void Validate_RequiresASection() =>
        AssertKey(SymlinkSettingsRules.Validate(null, [], []), "symlinks");

    [Fact]
    public void Validate_AcceptsAConfigurationOfMatchingRootsAndMappings()
    {
        Dictionary<string, string[]> errors = SymlinkSettingsRules.Validate(Valid(), [Sonarr], [Radarr]);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RefusesDuplicatedAndMissingAreaNames()
    {
        SymlinkSettings settings = Valid();
        settings.StorageAreas.Add(new MediaStorageArea
        {
            Id = "area-2",
            Name = "main",
            MoviesRoot = LibManagarr("movies-disk2"),
            SeriesRoot = LibManagarr("series-disk2")
        });

        AssertKey(SymlinkSettingsRules.Validate(settings, [Sonarr], [Radarr]), "symlinks.storageAreas[1].name");

        SymlinkSettings unnamed = Valid();
        unnamed.StorageAreas[0].Name = "   ";
        AssertKey(SymlinkSettingsRules.Validate(unnamed, [Sonarr], [Radarr]), "symlinks.storageAreas[0].name");
    }

    [Fact]
    public void Validate_RefusesAnAreaWhoseRootsNestIntoEachOther()
    {
        SymlinkSettings settings = Valid();
        settings.StorageAreas[0].SeriesRoot = LibManagarr("movies/series");

        AssertKey(SymlinkSettingsRules.Validate(settings, [Sonarr], [Radarr]), "symlinks.storageAreas[0].seriesRoot");
    }

    [Fact]
    public void Validate_RefusesOverlappingRootsAcrossAreas()
    {
        SymlinkSettings settings = Valid();
        settings.StorageAreas.Add(new MediaStorageArea
        {
            Id = "area-2",
            Name = "Second",
            MoviesRoot = LibManagarr("movies/extra"),
            SeriesRoot = LibManagarr("series-second")
        });

        Dictionary<string, string[]> errors = SymlinkSettingsRules.Validate(settings, [Sonarr], [Radarr]);
        AssertKey(errors, "symlinks.storageAreas[1].moviesRoot");
        Assert.DoesNotContain("symlinks.storageAreas[1].seriesRoot", errors.Keys);
    }

    [Fact]
    public void Validate_RefusesARootOfTheWrongGrammarOrTheFileSystemRoot()
    {
        SymlinkSettings foreign = Valid();
        foreign.StorageAreas[0].MoviesRoot = ArrPathRules.RuntimeSyntax == ArrPathSyntax.Windows
            ? "/media/movies"
            : @"C:\media\movies";
        AssertKey(SymlinkSettingsRules.Validate(foreign, [Sonarr], [Radarr]), "symlinks.storageAreas[0].moviesRoot");

        SymlinkSettings bare = Valid();
        bare.StorageAreas[0].SeriesRoot = ArrPathRules.RuntimeSyntax == ArrPathSyntax.Windows ? "C:\\" : "/";
        Dictionary<string, string[]> errors = SymlinkSettingsRules.Validate(bare, [Sonarr], [Radarr]);
        AssertKey(errors, "symlinks.storageAreas[0].seriesRoot");
        Assert.Contains("below the file system root", errors["symlinks.storageAreas[0].seriesRoot"][0]);
    }

    [Fact]
    public void Validate_RefusesAMappingForAnApplicationThatCannotBeMapped()
    {
        SymlinkSettings settings = Valid();
        settings.PathMappings[0].Kind = ArrApplicationKind.Seerr;

        Dictionary<string, string[]> errors = SymlinkSettingsRules.Validate(settings, [Sonarr], [Radarr]);
        AssertKey(errors, "symlinks.pathMappings[0].kind");
        Assert.Contains("Sonarr", errors["symlinks.pathMappings[0].kind"][0]);
    }

    [Fact]
    public void Validate_RefusesAMappingForAnInstanceThatIsNotConfigured()
    {
        SymlinkSettings settings = Valid();
        settings.PathMappings[0].InstanceId = "gone";

        Dictionary<string, string[]> errors = SymlinkSettingsRules.Validate(settings, [Sonarr], [Radarr]);
        AssertKey(errors, "symlinks.pathMappings[0].instanceId");
        Assert.Contains("Radarr instance", errors["symlinks.pathMappings[0].instanceId"][0]);
    }

    [Fact]
    public void Validate_RefusesAMappingOfTheWrongApplicationKindForThatInstance()
    {
        SymlinkSettings settings = Valid();
        settings.PathMappings[0].InstanceId = Sonarr.Id;

        Dictionary<string, string[]> errors = SymlinkSettingsRules.Validate(settings, [Sonarr], [Radarr]);
        AssertKey(errors, "symlinks.pathMappings[0].instanceId");
        Assert.Contains("instance", errors["symlinks.pathMappings[0].instanceId"][0]);
    }

    [Fact]
    public void Validate_RefusesTwoMappingsThatDisagreeAboutOneInstanceRoot()
    {
        SymlinkSettings settings = Valid();
        settings.PathMappings.Add(new ArrPathMapping
        {
            Id = "radarr-2",
            InstanceId = Radarr.Id,
            Kind = ArrApplicationKind.Radarr,
            InstanceRoot = @"\\NAS\RADARR",
            LibManagarrRoot = LibManagarr("movies-other")
        });

        Dictionary<string, string[]> errors = SymlinkSettingsRules.Validate(settings, [Sonarr], [Radarr]);
        AssertKey(errors, "symlinks.pathMappings[1].instanceRoot");
        Assert.Contains("ambiguous", errors["symlinks.pathMappings[1].instanceRoot"][0]);
    }

    [Fact]
    public void Validate_RefusesAMappingTargetNoStorageAreaCovers()
    {
        SymlinkSettings settings = Valid();
        settings.PathMappings[0].LibManagarrRoot = LibManagarr("series");

        Dictionary<string, string[]> errors = SymlinkSettingsRules.Validate(settings, [Sonarr], [Radarr]);
        AssertKey(errors, "symlinks.pathMappings[0].libManagarrRoot");
        Assert.Contains("outside every configured", errors["symlinks.pathMappings[0].libManagarrRoot"][0]);
    }

    [Fact]
    public void Validate_AllowsOneReaderViewPerKind()
    {
        SymlinkSettings settings = Valid();
        settings.ReaderViews =
        [
            new PathReaderView { Id = "v1", Kind = PathReaderKind.Sonarr, Root = @"\\tv\series" },
            new PathReaderView { Id = "v2", Kind = PathReaderKind.Sonarr, Root = @"\\tv2\series" }
        ];

        AssertKey(SymlinkSettingsRules.Validate(settings, [Sonarr], [Radarr]), "symlinks.readerViews[1].kind");

        settings.ReaderViews = [new PathReaderView { Id = "v1", Kind = PathReaderKind.MediaPlayer, Root = "  " }];
        AssertKey(SymlinkSettingsRules.Validate(settings, [Sonarr], [Radarr]), "symlinks.readerViews[0].root");
    }

    [Fact]
    public void RequiresNewRevision_OnlyFollowsWhatAPathResolvesTo()
    {
        SymlinkSettings stored = Valid();
        SymlinkSettings renamed = Valid();
        renamed.StorageAreas[0].Name = "Renamed shelf";
        SymlinkSettings verified = Valid();
        verified.ReaderViews[0].Verification = PathReaderVerification.Verified;
        SymlinkSettings moved = Valid();
        moved.StorageAreas[0].MoviesRoot = LibManagarr("movies-new");

        Assert.False(SymlinkSettingsRules.RequiresNewRevision(stored, SymlinkSettingsRules.Normalize(stored)));
        Assert.False(SymlinkSettingsRules.RequiresNewRevision(stored, renamed));
        Assert.False(SymlinkSettingsRules.RequiresNewRevision(stored, verified));
        Assert.True(SymlinkSettingsRules.RequiresNewRevision(stored, moved));
    }

    [Fact]
    public void AppSettingsPipeline_NormalizesAndValidatesTheSection()
    {
        AppSettings settings = new()
        {
            SonarrInstances = [Sonarr],
            RadarrInstances = [Radarr],
            Symlinks = Valid()
        };
        settings.Symlinks.StorageAreas[0].Name = "  Main  ";
        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);
        Assert.Equal("Main", normalized.Symlinks.StorageAreas[0].Name);
        Assert.Empty(AppSettingsValidator.Validate(normalized));

        normalized.Symlinks.PathMappings[0].InstanceId = "missing";
        Assert.Contains("symlinks.pathMappings[0].instanceId", AppSettingsValidator.Validate(normalized).Keys);
    }

    private static SymlinkSettings Valid() => new()
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
                Id = "radarr-1",
                InstanceId = Radarr.Id,
                Kind = ArrApplicationKind.Radarr,
                InstanceRoot = @"\\nas\radarr",
                LibManagarrRoot = LibManagarr("movies")
            }
        ],
        ReaderViews = [new PathReaderView { Id = "view-1", Kind = PathReaderKind.Sonarr, Root = @"\\tv\series" }],
        Revision = 4
    };

    private static void AssertKey(Dictionary<string, string[]> errors, string key) =>
        Assert.Contains(key, errors.Keys);

    private static string LibManagarr(string relative) =>
        ArrPathRules.RuntimeSyntax == ArrPathSyntax.Windows
            ? @"C:\media\" + relative.Replace('/', '\\')
            : "/media/" + relative;
}
