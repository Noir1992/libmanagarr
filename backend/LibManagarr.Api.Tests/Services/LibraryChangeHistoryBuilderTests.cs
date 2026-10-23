using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class LibraryChangeHistoryBuilderTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 5, 1, 12, 0, 0, TimeSpan.FromHours(2));

    [Theory]
    [InlineData(true, "downloadFolderImported", LibraryChangeType.Added, 100)]
    [InlineData(true, "seriesFolderImported", LibraryChangeType.Added, 100)]
    [InlineData(true, "episodeFileRenamed", LibraryChangeType.Changed, 70)]
    [InlineData(true, "episodeFileDeleted", LibraryChangeType.Deleted, -30)]
    [InlineData(false, "downloadFolderImported", LibraryChangeType.Added, 100)]
    [InlineData(false, "movieFolderImported", LibraryChangeType.Added, 100)]
    [InlineData(false, "movieFileRenamed", LibraryChangeType.Changed, 70)]
    [InlineData(false, "movieFileDeleted", LibraryChangeType.Deleted, -30)]
    public void BuildEntries_ShouldMapEventsAndCalculateSizeDeltas(bool sonarr, string eventType, LibraryChangeType changeType, long delta)
    {
        LibraryChangeHistoryEntry entry = Assert.Single(Build(sonarr, eventType, 42, Timestamp));

        string source = sonarr ? "sonarr" : "radarr";
        Assert.Equal($"{source}:42", entry.SourceKey);
        Assert.Equal($"{source}:instance:1", entry.MediaKey);
        Assert.Equal(sonarr ? ArrApplicationKind.Sonarr : ArrApplicationKind.Radarr, entry.Kind);
        Assert.Equal("instance", entry.InstanceId);
        Assert.Equal("Main", entry.InstanceName);
        Assert.Equal(Timestamp, entry.OccurredAt);
        Assert.Equal("Current title", entry.EntityTitle);
        Assert.Equal(sonarr ? "Series title" : null, entry.SeriesTitle);
        Assert.Equal("current.mkv", entry.FilePath);
        Assert.Equal(100, entry.SizeBytes);
        Assert.Equal(changeType, entry.ChangeType);
        Assert.Equal(delta, entry.DeltaSizeBytes);
    }

    [Theory]
    [InlineData(true, "grabbed")]
    [InlineData(false, "grabbed")]
    [InlineData(true, null)]
    [InlineData(false, null)]
    public void BuildEntries_ShouldIgnoreEventsThatDoNotChangeLibraryFiles(bool sonarr, string? eventType)
    {
        Assert.Empty(Build(sonarr, eventType, 42, Timestamp));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BuildEntries_ShouldIgnoreUndatedEvents(bool sonarr)
    {
        Assert.Empty(Build(sonarr, "downloadFolderImported", 42, null));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BuildEntries_ShouldUsePreviousSizeAndSourceTitleForADeletedFile(bool sonarr)
    {
        LibraryChangeHistoryEntry entry = Assert.Single(Build(sonarr, sonarr ? "episodeFileDeleted" : "movieFileDeleted", 42, Timestamp, hasCurrentFile: false));

        Assert.Equal(-30, entry.DeltaSizeBytes);
        Assert.Equal(30, entry.SizeBytes);
        Assert.Equal("History title", entry.EntityTitle);
        Assert.Null(entry.FilePath);
        Assert.Equal(sonarr ? "Series title" : null, entry.SeriesTitle);
    }

    [Fact]
    public void BuildSonarrEntries_ShouldFallBackToTheSeriesOfTheCurrentEpisodeWhenHistoryHasNoSeriesId()
    {
        LibraryChangeHistoryEntry entry = Assert.Single(Build(true, "downloadFolderImported", 42, Timestamp, historySeriesId: null));

        Assert.Equal("Series title", entry.SeriesTitle);
    }

    [Fact]
    public void BuildSonarrEntries_ShouldKeepTheSeriesTitleEmptyWhenTheSeriesIsUnknown()
    {
        LibraryChangeHistoryEntry entry = Assert.Single(Build(true, "downloadFolderImported", 42, Timestamp, hasCurrentFile: false, historySeriesId: 99));

        Assert.Null(entry.SeriesTitle);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BuildEntries_ShouldGenerateAStableFallbackKeyFromTheUtcInstant(bool sonarr)
    {
        LibraryChangeHistoryEntry first = Assert.Single(Build(sonarr, "downloadFolderImported", null, Timestamp));
        LibraryChangeHistoryEntry second = Assert.Single(Build(sonarr, "downloadFolderImported", null, Timestamp.ToUniversalTime()));

        Assert.Equal(first.SourceKey, second.SourceKey);
        Assert.Equal($"{(sonarr ? "sonarr" : "radarr")}:downloadFolderImported:1:{Timestamp.ToUniversalTime():O}:History title", first.SourceKey);
    }

    private static IReadOnlyList<LibraryChangeHistoryEntry> Build(bool sonarr, string? eventType, int? historyId, DateTimeOffset? date, bool hasCurrentFile = true, int? historySeriesId = 1)
    {
        ArrInstanceSettings instance = new() { Id = "instance", Name = "Main" };
        if (sonarr)
        {
            return LibraryChangeHistoryBuilder.BuildSonarrEntries(instance,
                SonarrSnapshot(100, hasCurrentFile), SonarrSnapshot(30, true),
                [new() { Id = historyId, EpisodeId = 1, SeriesId = historySeriesId, EventType = eventType, Date = date, SourceTitle = "History title" }]);
        }
        return LibraryChangeHistoryBuilder.BuildRadarrEntries(instance,
            RadarrSnapshot(100, hasCurrentFile), RadarrSnapshot(30, true),
            [new() { Id = historyId, MovieId = 1, EventType = eventType, Date = date, SourceTitle = "History title" }]);
    }

    private static SonarrInstanceSnapshot SonarrSnapshot(long size, bool hasFile) => new()
    {
        Series = [new() { SeriesId = 1, Title = "Series title", Seasons = [new() { Episodes = [new() { EpisodeId = 1, Title = "Current title", HasFile = hasFile, File = new() { Size = size, RelativePath = "current.mkv" } }] }] }]
    };

    private static RadarrInstanceSnapshot RadarrSnapshot(long size, bool hasFile) => new()
    {
        Movies = [new() { MovieId = 1, Title = "Current title", HasFile = hasFile, File = new() { Size = size, RelativePath = "current.mkv" } }]
    };
}
