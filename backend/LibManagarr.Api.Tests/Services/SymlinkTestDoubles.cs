using System.Net;
using System.Text;
using System.Text.Json;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

/// <summary>
/// The doubles the managed-link tests share: the settings that stand still, the Arr answers a read-only
/// service needs, the scripted file system and the path grammar both instances and this process speak.
/// Real symlink behaviour is deliberately absent from the file-system double, because a fake that claims
/// to keep a link proves nothing about a file system that has to.
/// </summary>
internal sealed record RecordedRequest(string Method, string Path);

internal sealed class FixedSettings(AppSettings settings) : ISettingsService
{
    public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);
    public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken) => Task.FromResult(value);
}

/// <summary>Answers every read from its own route, which is all a service that only reads needs.</summary>
internal sealed class StubArrHandler(Dictionary<string, List<string>> jsonByRoute) : HttpMessageHandler
{
    private readonly Dictionary<string, int> reads = new(StringComparer.OrdinalIgnoreCase);

    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>Set to make an instance unreachable, which is a state of its own.</summary>
    public Exception? Throws { get; init; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (Throws is not null) throw Throws;

        string path = request.RequestUri!.AbsolutePath;
        Requests.Add(new RecordedRequest(request.Method.Method, path));
        string key = $"{request.Method} {path}";
        if (!jsonByRoute.TryGetValue(key, out List<string>? script))
        {
            return Response("{}");
        }

        int index = Math.Min(reads.TryGetValue(key, out int seen) ? seen : 0, script.Count - 1);
        reads[key] = index + 1;
        if (request.Content is not null)
        {
            _ = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        return Response(script[index]);
    }

    private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}

/// <summary>For a test whose subject must not speak to an instance at all.</summary>
internal sealed class NeverAskedHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException($"An instance was asked for {request.Method} {request.RequestUri?.AbsolutePath}.");
}

/// <summary>
/// Which entries stand where, which of them are links, and what writing or removing one does to that.
/// </summary>
internal sealed class FakeLinkFileSystem : ISymlinkFileSystem
{
    /// <summary>Path to stored link content; a null content is an entry that is not a link.</summary>
    public Dictionary<string, string?> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> CreatedLinks { get; } = [];

    public List<string> CreatedFolders { get; } = [];

    public List<string> Removed { get; } = [];

    /// <summary>Paths whose write is refused, the way a mount or a missing privilege refuses.</summary>
    public List<string> RefusedWrites { get; } = [];

    /// <summary>Paths whose write fails for a reason no operation here accounts for.</summary>
    public List<string> BrokenPaths { get; } = [];

    /// <summary>Paths whose read is refused, the way the rights of a process refuse it.</summary>
    public List<string> RefusedReads { get; } = [];

    /// <summary>Paths that cannot be opened although the entry is listed, the way a gone target answers.</summary>
    public List<string> UnreadablePaths { get; } = [];

    /// <summary>Roots that cannot be listed, the way an unmounted share answers.</summary>
    public List<string> UnreadableRoots { get; } = [];

    public bool EntryExists(string path) => Entries.ContainsKey(path);

    public bool IsSymlink(string path) => Entries.TryGetValue(path, out string? text) && text is not null;

    public string? ReadLinkTarget(string path) => Entries.TryGetValue(path, out string? text) ? text : null;

    public void CreateFileSymlink(string linkPath, string targetPath)
    {
        if (RefusedWrites.Contains(linkPath))
        {
            throw new UnauthorizedAccessException($"Writing '{linkPath}' was denied.");
        }

        if (BrokenPaths.Contains(linkPath))
        {
            throw new InvalidOperationException($"Nothing could ever be written at '{linkPath}'.");
        }

        CreatedLinks.Add(linkPath);
        Entries[linkPath] = targetPath;
    }

    public void CreateDirectory(string path) => CreatedFolders.Add(path);

    public void DeleteEntry(string path)
    {
        Removed.Add(path);
        Entries.Remove(path);
    }

    public byte[] ReadAllBytes(string path) => throw new NotSupportedException("This service never reads file content.");

    /// <summary>
    /// Opening without reading, which is what the health check asks for. The three refusals are separate
    /// because the check reports a missing right, a broken target and an unreadable root apart from each
    /// other, and a test has to be able to ask for each of them on its own.
    /// </summary>
    public void OpenForRead(string path)
    {
        if (RefusedReads.Contains(path))
        {
            throw new UnauthorizedAccessException($"Reading '{path}' was denied.");
        }

        if (UnreadablePaths.Contains(path))
        {
            throw new IOException($"'{path}' could not be opened for reading.");
        }

        if (!Entries.ContainsKey(path))
        {
            throw new FileNotFoundException($"'{path}' is not there.");
        }
    }

    public void WriteAllBytes(string path, byte[] content) => throw new NotSupportedException("This service never writes file content.");

    public void MoveEntry(string sourcePath, string destinationPath) =>
        throw new NotSupportedException("Creating a link never moves anything else.");

    /// <summary>
    /// The entries stored below a root, so a health pass can ask whether a whole share is gone or only the
    /// file below one link is. An unreadable root says so the way a mount does.
    /// </summary>
    public IReadOnlyList<string> ListEntries(string directoryPath)
    {
        if (UnreadableRoots.Contains(directoryPath))
        {
            throw new IOException($"'{directoryPath}' could not be listed.");
        }

        return [.. Entries.Keys.Where(entry =>
            string.Equals(Path.GetDirectoryName(entry), directoryPath, StringComparison.OrdinalIgnoreCase))];
    }
}

/// <summary>
/// The pair every managed-link test is about, the configuration that lets it be linked and the instance
/// answers that make it runnable. Shared because the service and the register's answers have to be tested
/// against the same working situation, and a second copy of it would drift from the first.
/// </summary>
internal static class SymlinkFixture
{
    public const string RadarrInstanceId = "radarr-1";
    public const string SonarrInstanceId = "sonarr-1";
    public const string StorageAreaId = "main";
    public const int ConfigRevision = 4;
    public const int MovieId = 70;
    public const int RadarrFileId = 4321;
    public const int SeriesId = 12;
    public const int EpisodeId = 5;
    public const string MovieFolder = "The Battle of Unato (2016)";
    public const string SeriesFolder = "Kabaneri (2016)";
    public const string SourceFileName = "The.Battle.of.Unato.2016.1080p.mkv";
    public const string LinkName = "Kabaneri (2016) - S00E05.mkv";
    public const long SourceSize = 5_368_709_120;

    /// <summary>The upgrade every follow-up test is about: another file of the same movie, and the Sonarr file the entry's own link became.</summary>
    public const int UpgradedRadarrFileId = 4322;
    public const long UpgradedSize = 6_442_450_944;
    public const int SonarrEpisodeFileId = 990;
    public const string UpgradedFileName = "The.Battle.of.Unato.2016.1080p.REPACK.mkv";

    /// <summary>The four reads a pair that has nothing in its way gets answered with.</summary>
    public static Dictionary<string, List<string>> HappyRoutes() => new()
    {
        ["GET /api/v3/movie/70"] =
        [
            $$"""{ "id": {{MovieId}}, "title": "The Battle of Unato", "hasFile": true, "movieFileId": {{RadarrFileId}} }"""
        ],
        ["GET /api/v3/moviefile"] =
        [
            $$"""
              [ { "id": {{RadarrFileId}}, "movieId": {{MovieId}},
                  "path": {{Quote(MoviesInstance($"{MovieFolder}/{SourceFileName}"))}},
                  "size": 5368709120, "releaseGroup": "WFFF",
                  "languages": [ { "name": "German" } ],
                  "quality": { "quality": { "name": "WEBDL-1080p", "source": "web", "resolution": 1080 } } } ]
              """
        ],
        ["GET /api/v3/episode/5"] =
        [
            $$"""{ "id": {{EpisodeId}}, "seriesId": {{SeriesId}}, "seasonNumber": 0, "episodeNumber": 5, "hasFile": false, "monitored": true }"""
        ],
        ["GET /api/v3/series/12"] =
        [
            $$"""{ "id": {{SeriesId}}, "title": "Kabaneri of the Iron Fortress", "path": {{Quote(SeriesInstance(SeriesFolder))}} }"""
        ]
    };

    /// <summary>
    /// The same pair after Radarr replaced the file: the movie answers with another file, and the Sonarr
    /// episode carries the file this entry's own link became. The second half is what makes the follow-up
    /// a replacement instead of a second source in front of the episode.
    /// </summary>
    public static Dictionary<string, List<string>> UpgradeRoutes()
    {
        Dictionary<string, List<string>> routes = HappyRoutes();
        routes["GET /api/v3/movie/70"] =
        [
            $$"""{ "id": {{MovieId}}, "title": "The Battle of Unato", "hasFile": true, "movieFileId": {{UpgradedRadarrFileId}} }"""
        ];
        routes["GET /api/v3/moviefile"] =
        [
            $$"""
              [ { "id": {{UpgradedRadarrFileId}}, "movieId": {{MovieId}},
                  "path": {{Quote(MoviesInstance($"{MovieFolder}/{UpgradedFileName}"))}},
                  "size": {{UpgradedSize}}, "releaseGroup": "ION",
                  "languages": [ { "name": "German" } ],
                  "quality": { "quality": { "name": "WEBDL-1080p", "source": "web", "resolution": 1080 } } } ]
              """
        ];
        routes["GET /api/v3/episode/5"] =
        [
            $$"""{ "id": {{EpisodeId}}, "seriesId": {{SeriesId}}, "seasonNumber": 0, "episodeNumber": 5, "hasFile": true, "episodeFileId": {{SonarrEpisodeFileId}}, "monitored": false }"""
        ];
        return routes;
    }

    /// <summary>One storage area holding both media trees, a mapping per instance and a proven reader view.</summary>
    public static AppSettings Settings() => new()
    {
        RadarrInstances =
        [
            new ArrInstanceSettings
            {
                Id = RadarrInstanceId,
                Name = "Radarr",
                BaseUrl = "http://radarr.local",
                ApiKey = "secret"
            }
        ],
        SonarrInstances =
        [
            new ArrInstanceSettings
            {
                Id = SonarrInstanceId,
                Name = "Sonarr",
                BaseUrl = "http://sonarr.local",
                ApiKey = "secret"
            }
        ],
        Symlinks = new SymlinkSettings
        {
            Revision = ConfigRevision,
            StorageAreas =
            [
                new MediaStorageArea
                {
                    Id = StorageAreaId,
                    Name = "Main",
                    MoviesRoot = SymlinkTestPaths.LibManagarr("movies"),
                    SeriesRoot = SymlinkTestPaths.LibManagarr("series")
                }
            ],
            PathMappings =
            [
                new ArrPathMapping
                {
                    Id = "radarr-movies",
                    InstanceId = RadarrInstanceId,
                    Kind = ArrApplicationKind.Radarr,
                    InstanceRoot = MoviesInstance(string.Empty),
                    LibManagarrRoot = SymlinkTestPaths.LibManagarr("movies")
                },
                new ArrPathMapping
                {
                    Id = "sonarr-series",
                    InstanceId = SonarrInstanceId,
                    Kind = ArrApplicationKind.Sonarr,
                    InstanceRoot = SeriesInstance(string.Empty),
                    LibManagarrRoot = SymlinkTestPaths.LibManagarr("series")
                }
            ],
            ReaderViews =
            [
                new PathReaderView
                {
                    Id = "sonarr-view",
                    Kind = PathReaderKind.Sonarr,
                    Root = SymlinkTestPaths.LibManagarr(string.Empty),
                    Verification = PathReaderVerification.Verified
                }
            ]
        }
    };

    /// <summary>
    /// The library as it names the file the movie has now. A follow-up is decided by this half alone, so a
    /// test says which file the last synchronization left behind and leaves the live paths to the routes.
    /// </summary>
    public static AppSettings SettingsWithLibraryFile(int radarrFileId, long size)
    {
        AppSettings settings = Settings();
        settings.RadarrSnapshots =
        [
            new RadarrInstanceSnapshot
            {
                InstanceId = RadarrInstanceId,
                InstanceName = "Radarr",
                Movies =
                [
                    new RadarrMovieSnapshot
                    {
                        MovieId = MovieId,
                        Title = "The Battle of Unato",
                        HasFile = true,
                        File = new MediaFileSnapshot
                        {
                            Id = radarrFileId,
                            Size = size,
                            Path = MoviesInstance($"{MovieFolder}/{SourceFileName}")
                        }
                    }
                ]
            }
        ];
        return settings;
    }

    /// <summary>The identity the register stores for one file of one instance.</summary>
    public static string SourceVersion(int fileId, long size) => $"radarr:{fileId}:{size}";

    /// <summary>The source as this process sees it, which is the spelling the register stores.</summary>
    public static string SourcePath() =>
        SymlinkTestPaths.LibManagarr($"movies/{MovieFolder}/{SourceFileName}");

    public static string ExpectedLinkPath() =>
        Path.Combine(SymlinkTestPaths.LibManagarr($"series/{SeriesFolder}"), "Specials", LinkName);

    public static string MoviesInstance(string relative) => SymlinkTestPaths.Instance("movies", relative);

    public static string SeriesInstance(string relative) => SymlinkTestPaths.Instance("series", relative);

    private static string Quote(string? value) => SymlinkTestPaths.Quote(value);
}

/// <summary>
/// The two path grammars a link is described in: the one an instance speaks and the one this process
/// speaks, both in the syntax the tests really run in.
/// </summary>
internal static class SymlinkTestPaths
{
    public static string Instance(string shelf, string relative) =>
        ArrPathRules.RuntimeSyntax == ArrPathSyntax.Windows
            ? $@"D:\{shelf}" + (relative.Length == 0 ? string.Empty : "\\" + relative.Replace('/', '\\'))
            : $"/srv/{shelf}" + (relative.Length == 0 ? string.Empty : "/" + relative);

    public static string LibManagarr(string relative) =>
        ArrPathRules.RuntimeSyntax == ArrPathSyntax.Windows
            ? @"C:\media\" + relative.Replace('/', '\\')
            : "/media/" + relative;

    public static string Quote(string? value) => JsonSerializer.Serialize(value);
}
