using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class ArrPathRulesTests
{
    [Theory]
    [InlineData(@"C:\media\movies", ArrPathSyntax.Windows, @"C:\", "media|movies")]
    [InlineData("C:/media/movies", ArrPathSyntax.Windows, @"C:\", "media|movies")]
    [InlineData(@"\\nas\media\movies", ArrPathSyntax.Windows, @"\\nas\media\", "movies")]
    [InlineData("/media/movies", ArrPathSyntax.Unix, "/", "media|movies")]
    [InlineData("/media//movies/", ArrPathSyntax.Unix, "/", "media|movies")]
    public void TryParse_AcceptsAbsolutePathsInEitherGrammar(string value, ArrPathSyntax syntax, string root, string segments)
    {
        Assert.True(ArrPathRules.TryParse(value, out ParsedPath? parsed, out string? failure));
        Assert.Null(failure);
        Assert.Equal(syntax, parsed!.Syntax);
        Assert.Equal(root, parsed.Root);
        Assert.Equal(segments.Split('|'), parsed.Segments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("media/movies")]
    [InlineData("./media/movies")]
    [InlineData("C:media/movies")]
    [InlineData("/media/../secret")]
    [InlineData(@"C:\media\.\movies")]
    [InlineData(@"\\nas")]
    [InlineData("/media/mov:ies")]
    [InlineData(@"C:\media\movies.")]
    [InlineData(@"C:\media\con")]
    public void TryParse_RefusesPathsThatCannotNameOneShelf(string? value)
    {
        Assert.False(ArrPathRules.TryParse(value, out ParsedPath? parsed, out string? failure));
        Assert.Null(parsed);
        Assert.False(string.IsNullOrWhiteSpace(failure));
    }

    [Fact]
    public void TryParse_KeepsWindowsRulesOnAPathWrittenInWindowsGrammar()
    {
        // A '?' is legal in a POSIX name and illegal in a Windows one, so the grammar of the string —
        // not the host — has to decide which rule applies.
        Assert.True(ArrPathRules.TryParse("/media/mo?vies", out ParsedPath? _, out _));
        Assert.False(ArrPathRules.TryParse(@"C:\media\mo?vies", out ParsedPath? _, out string? failure));
        Assert.NotNull(failure);
    }

    [Fact]
    public void TryParse_RefusesAPathBeyondTheLengthLimit()
    {
        Assert.False(ArrPathRules.TryParse("/" + new string('a', 4001), out ParsedPath? _, out string? failure));
        Assert.Contains("4000", failure);
    }

    [Theory]
    [InlineData(ArrPathSyntax.Windows, @"C:\", new[] { "media", "movies" }, @"C:\media\movies")]
    [InlineData(ArrPathSyntax.Windows, @"\\nas\media\", new[] { "movies" }, @"\\nas\media\movies")]
    [InlineData(ArrPathSyntax.Unix, "/", new[] { "media", "movies" }, "/media/movies")]
    public void Format_RendersWithTheSeparatorsOfItsOwnGrammar(
        ArrPathSyntax syntax, string root, string[] segments, string expected)
    {
        Assert.Equal(expected, new ParsedPath(syntax, root, segments).Format());
    }

    [Theory]
    [InlineData("/media/movies", "/media/movies/S01E01.mkv", true)]
    [InlineData("/media/movies", "/media/movies", true)]
    [InlineData("/media/movies", "/media/movieship/S01E01.mkv", false)]
    [InlineData("/media/movies", "/media", false)]
    [InlineData(@"C:\media\movies", @"c:\MEDIA\Movies\S01E01.mkv", true)]
    [InlineData("/media/movies", @"C:\media\movies\x.mkv", false)]
    public void IsWithinRoot_MatchesOnCompleteSegmentsOnly(string root, string path, bool expected) =>
        Assert.Equal(expected, ArrPathRules.IsWithinRoot(root, path));

    [Fact]
    public void SegmentEquals_IgnoresCaseForWindowsGrammarOnly()
    {
        Assert.True(ArrPathRules.SegmentEquals("Movies", "movies", ArrPathSyntax.Windows));
        Assert.False(ArrPathRules.SegmentEquals("Movies", "movies", ArrPathSyntax.Unix));
    }

    [Fact]
    public void ExpectedSyntax_HoldsTheLibManagarrSideToTheRuntimeGrammar()
    {
        Assert.Equal(ArrPathRules.RuntimeSyntax, ArrPathRules.ExpectedSyntax(PathRole.LibManagarrSide, "/media/movies"));
        Assert.Equal(ArrPathSyntax.Windows, ArrPathRules.ExpectedSyntax(PathRole.InstanceSide, @"C:\media\movies"));
        Assert.Equal(ArrPathSyntax.Unix, ArrPathRules.ExpectedSyntax(PathRole.InstanceSide, "/media/movies"));
    }

    [Fact]
    public void TryMapPrefix_ReplacesTheLongestCoveringPrefix()
    {
        List<ArrPathMapping> mappings =
        [
            Mapping("wide", @"D:\movies", LibManagarr("movies")),
            Mapping("deep", @"D:\movies\4k", LibManagarr("movies-4k"))
        ];

        bool mapped = ArrPathRules.TryMapPrefix(
            mappings, "radarr-1", ArrApplicationKind.Radarr, @"D:\movies\4k\Film (2010)\film.mkv", PathRole.InstanceSide,
            mapping => mapping.InstanceRoot, mapping => mapping.LibManagarrRoot, PathRole.LibManagarrSide,
            out string result, out string? failure, out string? mappingId);

        Assert.True(mapped, failure);
        Assert.Equal(LibManagarr("movies-4k/Film (2010)/film.mkv"), result);
        Assert.Equal("deep", mappingId);
    }

    [Fact]
    public void TryMapPrefix_RefusesEquallyLongPrefixesThatDisagree()
    {
        // Two rules over the same instance root that disagree about the target: the longest-prefix rule
        // has nothing to choose between, so the translation has to refuse rather than guess.
        List<ArrPathMapping> mappings =
        [
            Mapping("first", @"D:\movies", LibManagarr("movies")),
            Mapping("second", @"D:\movies", LibManagarr("other"))
        ];

        bool ambiguous = ArrPathRules.TryMapPrefix(
            mappings, "radarr-1", ArrApplicationKind.Radarr, @"D:\movies\film.mkv", PathRole.InstanceSide,
            mapping => mapping.InstanceRoot, mapping => mapping.LibManagarrRoot, PathRole.LibManagarrSide,
            out _, out string? failure, out _);

        Assert.False(ambiguous);
        Assert.Contains("ambiguous", failure);
    }

    [Fact]
    public void TryMapPrefix_UsesTheOtherGrammarForTheInstanceSide()
    {
        // The instance keeps its own Windows paths even when this process runs on Linux, so the mapping
        // has to accept a Windows source and hand back a path in this process' grammar.
        List<ArrPathMapping> mappings = [Mapping("only", @"\\nas\radarr", LibManagarr("movies"))];

        bool mapped = ArrPathRules.TryMapPrefix(
            mappings, "radarr-1", ArrApplicationKind.Radarr, @"\\nas\radarr\Film (2010)\film.mkv", PathRole.InstanceSide,
            mapping => mapping.InstanceRoot, mapping => mapping.LibManagarrRoot, PathRole.LibManagarrSide,
            out string result, out string? failure, out _);

        Assert.True(mapped, failure);
        Assert.Equal(LibManagarr("movies/Film (2010)/film.mkv"), result);
        Assert.Equal(ArrPathRules.RuntimeSyntax, ArrPathRules.Detect(result));
    }

    [Fact]
    public void TryMapPrefix_ReportsTheGrammarMismatchInsteadOfTranslatingSilently()
    {
        List<ArrPathMapping> mappings = [Mapping("only", LibManagarr("movies"), @"\\nas\radarr")];
        string wrongGrammarSource = ArrPathRules.RuntimeSyntax == ArrPathSyntax.Windows ? "/media/movies/x.mkv" : @"C:\media\movies\x.mkv";

        bool mapped = ArrPathRules.TryMapPrefix(
            mappings, "radarr-1", ArrApplicationKind.Radarr, wrongGrammarSource, PathRole.LibManagarrSide,
            mapping => mapping.LibManagarrRoot, mapping => mapping.InstanceRoot, PathRole.InstanceSide,
            out _, out string? failure, out _);

        Assert.False(mapped);
        Assert.Contains("syntax", failure);
    }

    [Fact]
    public void TryMapPrefix_NamesTheMissingCoverageAndStaysWithinTheInstance()
    {
        List<ArrPathMapping> mappings = [Mapping("only", @"D:\movies", LibManagarr("movies"))];

        bool unmapped = ArrPathRules.TryMapPrefix(
            mappings, "radarr-1", ArrApplicationKind.Radarr, @"E:\other\film.mkv", PathRole.InstanceSide,
            mapping => mapping.InstanceRoot, mapping => mapping.LibManagarrRoot, PathRole.LibManagarrSide,
            out _, out string? failure, out _);
        Assert.False(unmapped);
        Assert.Contains("covers", failure);

        bool otherInstance = ArrPathRules.TryMapPrefix(
            mappings, "radarr-2", ArrApplicationKind.Radarr, @"D:\movies\film.mkv", PathRole.InstanceSide,
            mapping => mapping.InstanceRoot, mapping => mapping.LibManagarrRoot, PathRole.LibManagarrSide,
            out _, out failure, out _);
        Assert.False(otherInstance);
        Assert.Contains("radarr-2", failure);
    }

    [Fact]
    public void TryMapPrefix_RefusesAMappingWhoseTargetIsUnusable()
    {
        ArrPathMapping broken = Mapping("only", @"D:\movies", "media/movies");

        bool mapped = ArrPathRules.TryMapPrefix(
            [broken], "radarr-1", ArrApplicationKind.Radarr, @"D:\movies\film.mkv", PathRole.InstanceSide,
            mapping => mapping.InstanceRoot, mapping => mapping.LibManagarrRoot, PathRole.LibManagarrSide,
            out _, out string? failure, out _);

        Assert.False(mapped);
        Assert.Contains("unusable target", failure);
    }

    /// <summary>A LibManagarr side path is always written in the grammar this process can open.</summary>
    private static string LibManagarr(string relative) =>
        ArrPathRules.RuntimeSyntax == ArrPathSyntax.Windows
            ? @"C:\media\" + relative.Replace('/', '\\')
            : "/media/" + relative;

    private static ArrPathMapping Mapping(string id, string instanceRoot, string libManagarrRoot) => new()
    {
        Id = id,
        InstanceId = "radarr-1",
        Kind = ArrApplicationKind.Radarr,
        InstanceRoot = instanceRoot,
        LibManagarrRoot = libManagarrRoot
    };
}
