using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class LibraryChangeHistoryNormalizerTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public void Normalize_ShouldMergeReplacementOnlyWithinTheUpdateWindow(int seconds, bool merged)
    {
        LibraryChangeHistoryEntry deleted = Entry("1-deleted", LibraryChangeType.Deleted, Timestamp, 100);
        LibraryChangeHistoryEntry added = Entry("2-added", LibraryChangeType.Added, Timestamp.AddSeconds(seconds), 250);

        List<LibraryChangeHistoryEntry> result = LibraryChangeHistoryNormalizer.Normalize([added, deleted]);

        if (!merged)
        {
            Assert.Equal(new[] { LibraryChangeType.Deleted, LibraryChangeType.Added }, result.Select(entry => entry.ChangeType));
            return;
        }

        LibraryChangeHistoryEntry update = Assert.Single(result);
        Assert.Equal(LibraryChangeType.Updated, update.ChangeType);
        Assert.Equal(150, update.DeltaSizeBytes);
        Assert.Equal(250, update.SizeBytes);
        Assert.Equal(added.OccurredAt, update.OccurredAt);
        Assert.Equal(added.EntityTitle, update.EntityTitle);
        Assert.Equal(added.FilePath, update.FilePath);
        Assert.Equal($"updated:radarr:r:1:{Timestamp:O}:{added.OccurredAt:O}", update.SourceKey);
        Assert.Equal(LibraryChangeType.Deleted, deleted.ChangeType);
        Assert.Equal(LibraryChangeType.Added, added.ChangeType);
    }

    [Theory]
    [InlineData("instance")]
    [InlineData("application")]
    [InlineData("media")]
    [InlineData("event")]
    public void Normalize_ShouldNotMergeUnrelatedChanges(string difference)
    {
        LibraryChangeHistoryEntry deleted = Entry("deleted", LibraryChangeType.Deleted, Timestamp, 100);
        LibraryChangeHistoryEntry added = Entry("added", LibraryChangeType.Added, Timestamp.AddMinutes(1), 250);
        switch (difference)
        {
            case "instance": added.InstanceId = "other"; break;
            case "application": added.Kind = ArrApplicationKind.Sonarr; break;
            case "media": added.MediaKey = "radarr:r:2"; break;
            case "event": added.ChangeType = LibraryChangeType.Changed; break;
        }

        List<LibraryChangeHistoryEntry> result = LibraryChangeHistoryNormalizer.Normalize([deleted, added]);

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, entry => entry.ChangeType == LibraryChangeType.Updated);
    }

    [Fact]
    public void Normalize_ShouldPreserveLegacyEntriesAndReturnIndependentCopies()
    {
        LibraryChangeHistoryEntry deleted = Entry("first", LibraryChangeType.Deleted, Timestamp, 100);
        LibraryChangeHistoryEntry added = Entry("second", LibraryChangeType.Added, Timestamp.AddMinutes(1), 200);
        deleted.MediaKey = "";
        added.MediaKey = " ";

        List<LibraryChangeHistoryEntry> result = LibraryChangeHistoryNormalizer.Normalize([added, deleted]);

        Assert.Equal(new[] { "legacy:first", "legacy:second" }, result.Select(entry => entry.MediaKey));
        Assert.Equal("", deleted.MediaKey);
        Assert.Equal(" ", added.MediaKey);
        result[0].EntityTitle = "Changed copy";
        Assert.Equal("Title first", deleted.EntityTitle);
    }

    [Fact]
    public void Normalize_ShouldBeIdempotentForAnAlreadyMergedHistory()
    {
        List<LibraryChangeHistoryEntry> first = LibraryChangeHistoryNormalizer.Normalize([
            Entry("deleted", LibraryChangeType.Deleted, Timestamp, 500),
            Entry("added", LibraryChangeType.Added, Timestamp.AddMinutes(1), 200)
        ]);

        LibraryChangeHistoryEntry second = Assert.Single(LibraryChangeHistoryNormalizer.Normalize(first));

        Assert.Equal(first[0].SourceKey, second.SourceKey);
        Assert.Equal(LibraryChangeType.Updated, second.ChangeType);
        Assert.Equal(-300, second.DeltaSizeBytes);
        Assert.NotSame(first[0], second);
        Assert.Empty(LibraryChangeHistoryNormalizer.Normalize([]));
    }

    private static LibraryChangeHistoryEntry Entry(string key, LibraryChangeType type, DateTimeOffset date, long size) => new()
    {
        SourceKey = key, Kind = ArrApplicationKind.Radarr, InstanceId = "r", InstanceName = "Radarr",
        MediaKey = "radarr:r:1", OccurredAt = date, ChangeType = type, SizeBytes = size,
        EntityTitle = $"Title {key}", FilePath = $"{key}.mkv", DeltaSizeBytes = type == LibraryChangeType.Deleted ? -size : size
    };
}
