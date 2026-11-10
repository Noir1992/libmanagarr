using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Endpoints;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;
using LibManagarr.Api.Tests.Services;

namespace LibManagarr.Api.Tests.Endpoints;

/// <summary>
/// The register as the interface sees it: what a look costs, what the rows say afterwards, and what a write
/// to one of them answers. A read is the stored row plus the names the current snapshots have for its ids,
/// and the two answers that refuse stay distinguishable on purpose — an obstacle that needs a decision and
/// a revision that aged are different answers, and only the second is worth retrying as it is. That the
/// confirm stops at the link and hands the Sonarr half to the register is proven here too, because it is
/// what lets a request answer while an instance is still thinking.
/// </summary>
public sealed class SymlinkLinkEndpointTests
{
    private const string RadarrInstance = SymlinkFixture.RadarrInstanceId;
    private const string SonarrInstance = SymlinkFixture.SonarrInstanceId;
    private const string StorageArea = SymlinkFixture.StorageAreaId;
    private const int ConfigRevision = SymlinkFixture.ConfigRevision;
    private const string SeriesTitle = "Kabaneri of the Iron Fortress";
    private const string SpecialTitle = "Kabaneri of the Iron Fortress: The Battle of Unato";
    private const string EpisodeTitle = "The Battle of Unato";

    private static readonly DateTimeOffset Start = new(2026, 11, 6, 11, 0, 0, TimeSpan.Zero);
    private static readonly SpecialMoviePair Pair =
        new(RadarrInstance, SymlinkFixture.MovieId, SonarrInstance, SymlinkFixture.SeriesId, SymlinkFixture.EpisodeId);

    private readonly InMemorySymlinkStore store = new();
    private readonly SymlinkSetupRequests setupRequests = new();

    public SymlinkLinkEndpointTests()
    {
        // Only an area whose probe passed for the current configuration may hold a link, so every test
        // starts from that answer. The in-memory register answers synchronously, which is what lets the
        // fixture seed it from its constructor.
        store.SaveCapabilityCheckAsync(new SymlinkCapabilityCheck
        {
            StorageAreaId = StorageArea,
            Status = SymlinkCapabilityStatus.Available,
            Detail = "File symlinks work in the series root.",
            CheckedAt = Start,
            ConfigRevision = ConfigRevision
        }, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Preview_DescribesTheEntryConfirmWouldWriteAndChangesNothing()
    {
        FakeLinkFileSystem files = new();

        SymlinkLinkPlanDto plan = await Preview(new StubArrHandler(SymlinkFixture.HappyRoutes()), files);

        Assert.True(plan.Runnable);
        Assert.Empty(plan.Blockers);
        Assert.Empty(plan.Warnings);
        SymlinkLinkDto draft = plan.Draft;
        // A preview describes an intention, so it never pretends to be a registered entry.
        Assert.Equal(string.Empty, draft.Id);
        Assert.Equal(SymlinkFixture.ExpectedLinkPath(), draft.LinkPath);
        Assert.Equal(SymlinkFixture.SourcePath(), draft.SourcePath);
        Assert.Equal(StorageArea, draft.StorageAreaId);
        Assert.Equal(SymlinkFixture.RadarrFileId, draft.RadarrFileId);
        Assert.Equal(SpecialTitle, draft.RadarrMovieTitle);
        Assert.Equal(SeriesTitle, draft.SonarrSeriesTitle);
        Assert.Equal(EpisodeTitle, draft.SonarrEpisodeTitle);
        Assert.Equal(SymlinkOperationStatus.Pending, draft.OperationStatus);
        Assert.Equal(SymlinkSetupStep.LinkCreated, draft.SetupStep);
        // Looking is free: neither a row nor a file came out of it, so it is repeatable and never half done.
        Assert.Empty(files.CreatedLinks);
        Assert.Empty(await store.GetLinksAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Preview_AndTheConfirmRefuseTheSamePairWithTheSameReason()
    {
        Dictionary<string, List<string>> routes = SymlinkFixture.HappyRoutes();
        routes["GET /api/v3/episode/5"] =
        [
            $$"""{ "id": {{SymlinkFixture.EpisodeId}}, "seriesId": {{SymlinkFixture.SeriesId}}, "seasonNumber": 0, "episodeNumber": 5, "hasFile": true, "episodeFileId": 88, "monitored": true }"""
        ];
        FakeLinkFileSystem files = new();

        SymlinkLinkPlanDto plan = await Preview(new StubArrHandler(routes), files);

        Assert.False(plan.Runnable);
        SymlinkFinding blocker = Assert.Single(plan.Blockers);
        Assert.Equal("sonarr-episode-has-file", blocker.Code);

        // A blocker is not something the interface can walk past by calling the other action.
        await SeedSuggestionAsync();
        Results<Accepted<SymlinkLinkDto>, NotFound, ProblemHttpResult> confirmed =
            await Confirm(new StubArrHandler(routes), files);

        ProblemHttpResult problem = Assert.IsType<ProblemHttpResult>(confirmed.Result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.ProblemDetails.Status);
        Assert.Equal("The link cannot be created", problem.ProblemDetails.Title);
        // The preview says no and the confirm says the same no, with the same reason, so the page can show
        // one obstacle instead of two different answers for the same pair.
        Assert.Equal(blocker.Detail, problem.ProblemDetails.Detail);
        Assert.Empty(files.CreatedLinks);
        Assert.Empty(await store.GetLinksAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Confirm_WritesTheLinkAnswersWhereTheRowLivesAndLeavesTheSonarrHalfToTheRegister()
    {
        await SeedSuggestionAsync();
        FakeLinkFileSystem files = new();
        files.Entries[SymlinkFixture.SourcePath()] = null;

        Results<Accepted<SymlinkLinkDto>, NotFound, ProblemHttpResult> result =
            await Confirm(new StubArrHandler(SymlinkFixture.HappyRoutes()), files);

        Accepted<SymlinkLinkDto> accepted = Assert.IsType<Accepted<SymlinkLinkDto>>(result.Result);
        SymlinkLinkDto dto = accepted.Value!;
        // The id is what the page follows up on, so the answer has to point at the row it just reserved.
        Assert.Equal($"/api/settings/symlinks/links/{dto.Id}", accepted.Location);
        Assert.Equal(Pair.RadarrMovieId, dto.RadarrMovieId);
        // The pair and its file come from the stored suggestion, not from anything the request repeated.
        Assert.Equal(SymlinkFixture.RadarrFileId, dto.RadarrFileId);
        Assert.Equal("suggestion-1", dto.SuggestionId);
        Assert.Equal(SpecialTitle, dto.RadarrMovieTitle);
        Assert.Equal(EpisodeTitle, dto.SonarrEpisodeTitle);
        Assert.Equal(SymlinkFixture.ExpectedLinkPath(), dto.LinkPath);
        // Only the link itself is proven by this call; the Sonarr steps run on from the row.
        Assert.Equal(SymlinkOperationStatus.Active, dto.OperationStatus);
        Assert.Equal(SymlinkSetupStep.MonitoringDisabled, dto.SetupStep);
        Assert.Equal([SymlinkFixture.ExpectedLinkPath()], files.CreatedLinks);
        SymlinkSuggestion decided = Assert.Single(await store.GetSuggestionsAsync(CancellationToken.None));
        Assert.Equal(SymlinkSuggestionStatus.Completed, decided.Status);
        Assert.Equal(dto.Id, decided.LinkId);
        // And the handover really happens: the request answered without waiting for an instance.
        Assert.True(await IsPassRequestedAsync());
    }

    [Fact]
    public async Task Confirm_AnUnknownSuggestionIsMissingAndNoInstanceIsAsked()
    {
        FakeLinkFileSystem files = new();
        StubArrHandler handler = new(SymlinkFixture.HappyRoutes());

        Results<Accepted<SymlinkLinkDto>, NotFound, ProblemHttpResult> result = await Confirm(handler, files);

        // Nothing was reserved, so there is no pair to look up either: an unknown row must not cost two
        // instance calls on every typo.
        Assert.IsType<NotFound>(result.Result);
        Assert.Empty(handler.Requests);
        Assert.Empty(files.CreatedLinks);
        Assert.Empty(await store.GetLinksAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Links_AreTheStoredRowsNewestFirstWithTheNamesTheSnapshotsStillHave()
    {
        await SeedRowAsync("link-1", at: Start);
        // A pair whose movie left the Radarr library keeps its row and shows without a name: the absence is
        // the finding, and the register has to stay readable enough to act on it.
        await SeedRowAsync("link-2", movieId: 71, at: Start + TimeSpan.FromMinutes(5));

        IReadOnlyList<SymlinkLinkDto> rows = (await Invoke<Ok<IReadOnlyList<SymlinkLinkDto>>>(
            "GetLinksAsync", new FixedSettings(Settings()), store, CancellationToken.None)).Value!;

        Assert.Equal(["link-2", "link-1"], rows.Select(row => row.Id));
        SymlinkLinkDto gone = rows[0];
        Assert.Null(gone.RadarrMovieTitle);
        Assert.Equal(71, gone.RadarrMovieId);
        SymlinkLinkDto known = rows[1];
        Assert.Equal(SpecialTitle, known.RadarrMovieTitle);
        Assert.Equal(SeriesTitle, known.SonarrSeriesTitle);
        Assert.Equal(EpisodeTitle, known.SonarrEpisodeTitle);
        Assert.Equal(SymlinkFixture.ExpectedLinkPath(), known.LinkPath);
        Assert.Equal(SymlinkOperationStatus.Active, known.OperationStatus);
        Assert.Equal(SymlinkSetupStep.Completed, known.SetupStep);
        Assert.True(known.AutoFollowUp);
        Assert.Equal(Start, known.CreatedAt);
    }

    [Fact]
    public async Task Link_ItsFindingsAreAnsweredWithTheRowAndAnUnknownIdIsMissing()
    {
        await SeedRowAsync("link-1", operation: SymlinkOperationStatus.Conflict, setupStep: SymlinkSetupStep.MetadataMatched);

        Results<Ok<SymlinkLinkDto>, NotFound> found = await Invoke<Results<Ok<SymlinkLinkDto>, NotFound>>(
            "GetLinkAsync", "link-1", new FixedSettings(Settings()), store, CancellationToken.None);
        SymlinkLinkDto dto = Assert.IsType<Ok<SymlinkLinkDto>>(found.Result).Value!;

        // What stopped is part of the entry, not a separate list: the row is where a person reads it.
        Assert.Equal(["link-path-not-translatable"], dto.Findings.Select(finding => finding.Code));
        Assert.Equal(SymlinkOperationStatus.Conflict, dto.OperationStatus);

        Results<Ok<SymlinkLinkDto>, NotFound> missing = await Invoke<Results<Ok<SymlinkLinkDto>, NotFound>>(
            "GetLinkAsync", "link-9", new FixedSettings(Settings()), store, CancellationToken.None);
        Assert.IsType<NotFound>(missing.Result);
    }

    [Fact]
    public async Task FollowUp_TurnsFollowingLaterUpgradesOffForOneEntry()
    {
        await SeedRowAsync("link-1");

        Results<Ok<SymlinkLinkDto>, NotFound, ProblemHttpResult> result = await Invoke<Results<Ok<SymlinkLinkDto>, NotFound, ProblemHttpResult>>(
            "SetFollowUpAsync",
            "link-1",
            new SetSymlinkFollowUpRequest { AutoFollowUp = false, Revision = 0 },
            new FixedSettings(Settings()),
            Links(new NeverAskedHandler(), new FakeLinkFileSystem()),
            CancellationToken.None);

        SymlinkLinkDto dto = Assert.IsType<Ok<SymlinkLinkDto>>(result.Result).Value!;
        Assert.False(dto.AutoFollowUp);
        // The answer carries the revision the next write has to present.
        Assert.Equal(1, dto.Revision);
        Assert.False((await StoredAsync("link-1")).AutoFollowUp);
    }

    [Fact]
    public async Task FollowUp_TurningFollowingOnForAnEntryThatIsBehindQueuesItAtOnce()
    {
        // The library answers with another file than the one this entry's link was made for.
        await SeedRowAsync(
            "link-1",
            autoFollowUp: false,
            radarrFileId: 4320,
            sourceVersion: SymlinkFixture.SourceVersion(4320, SymlinkFixture.SourceSize));

        Results<Ok<SymlinkLinkDto>, NotFound, ProblemHttpResult> result = await Invoke<Results<Ok<SymlinkLinkDto>, NotFound, ProblemHttpResult>>(
            "SetFollowUpAsync",
            "link-1",
            new SetSymlinkFollowUpRequest { AutoFollowUp = true, Revision = 0 },
            new FixedSettings(Settings()),
            Links(new NeverAskedHandler(), new FakeLinkFileSystem()),
            CancellationToken.None);

        // "Keep following this source" is everything a behind entry needs: it is owed its new file from this
        // write on instead of waiting for the library to be synchronized a second time. The snapshots are
        // what said so, so no instance was asked — the handler here refuses to answer.
        SymlinkLinkDto dto = Assert.IsType<Ok<SymlinkLinkDto>>(result.Result).Value!;
        Assert.True(dto.AutoFollowUp);
        Assert.Equal(SymlinkOperationStatus.Updating, dto.OperationStatus);
        Assert.Equal(SymlinkSetupStep.LinkCreated, dto.SetupStep);
        SymlinkLink stored = await StoredAsync("link-1");
        Assert.Equal(SymlinkOperationStatus.Updating, stored.OperationStatus);
        Assert.Equal(["source-replaced"], stored.Findings.Select(finding => finding.Code));
    }

    [Fact]
    public async Task FollowUp_AViewThatAgedIsRefusedAndTheEntryKeepsItsAnswer()
    {
        await SeedRowAsync("link-1");

        Results<Ok<SymlinkLinkDto>, NotFound, ProblemHttpResult> result = await Invoke<Results<Ok<SymlinkLinkDto>, NotFound, ProblemHttpResult>>(
            "SetFollowUpAsync",
            "link-1",
            new SetSymlinkFollowUpRequest { AutoFollowUp = false, Revision = 7 },
            new FixedSettings(Settings()),
            Links(new NeverAskedHandler(), new FakeLinkFileSystem()),
            CancellationToken.None);

        // An outdated view is refused rather than applied, or the change somebody else made would be
        // silently overwritten by the stale one.
        ProblemHttpResult problem = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.ProblemDetails.Status);
        Assert.True((await StoredAsync("link-1")).AutoFollowUp);
    }

    [Fact]
    public async Task Check_LooksAtTheRowItWasAskedForAndAnswersWithWhatTheRegisterKept()
    {
        SymlinkLink row = await SeedRowAsync(
            "link-1",
            sourceVersion: SymlinkFixture.SourceVersion(SymlinkFixture.RadarrFileId, SymlinkFixture.SourceSize));
        // The shelf moved underneath the entry: the film is still there, only the link is gone.
        FakeLinkFileSystem files = new();
        files.Entries[row.SourcePath] = null;

        Results<Ok<SymlinkLinkDto>, NotFound> result = await Invoke<Results<Ok<SymlinkLinkDto>, NotFound>>(
            "CheckLinkAsync", "link-1", new FixedSettings(Settings()), Health(files), CancellationToken.None);

        SymlinkLinkDto dto = Assert.IsType<Ok<SymlinkLinkDto>>(result.Result).Value!;
        Assert.Equal("link-1", dto.Id);
        // What the look found is on the row it looked at, so the answer the interface gets is the row —
        // the same one a scheduled pass would have left behind. The row of this fixture never recorded the
        // file information it was made from, so saying so rides along with the lost link.
        Assert.Contains("link-lost", dto.Findings.Select(finding => finding.Code));
        // The stamps come out of the register rather than from the caller's own clock, because the page
        // shows them as the entry's history and not as the moment somebody clicked.
        Assert.Equal(Start, dto.LastCheckedAt);
        Assert.Null(dto.LastSuccessfulCheckAt);
        SymlinkLink stored = await StoredAsync("link-1");
        Assert.Equal(dto.Findings.Select(finding => finding.Code), stored.Findings.Select(finding => finding.Code));
        Assert.Equal(Start, stored.LastCheckedAt);
    }

    [Fact]
    public async Task Check_AnUnknownRowHasNoAnswerAndWritesNothing()
    {
        FakeLinkFileSystem files = new();

        Results<Ok<SymlinkLinkDto>, NotFound> result = await Invoke<Results<Ok<SymlinkLinkDto>, NotFound>>(
            "CheckLinkAsync", "link-9", new FixedSettings(Settings()), Health(files), CancellationToken.None);

        Assert.IsType<NotFound>(result.Result);
        // A look that found no row leaves no trace — not a file, not a row, not a claim about one.
        Assert.Empty(files.CreatedLinks);
        Assert.Empty(await store.GetLinksAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Delete_RemovesOnlyTheLinkWithoutRejectingThePair()
    {
        SymlinkLink row = await SeedRowAsync("link-1");
        FakeLinkFileSystem files = new();
        files.Entries[row.LinkPath] = row.LinkText;
        files.Entries[row.SourcePath] = null;

        Results<NoContent, NotFound, ProblemHttpResult> result = await Invoke<Results<NoContent, NotFound, ProblemHttpResult>>(
            "DeleteLinkAsync", "link-1", 0L, Links(new NeverAskedHandler(), files), CancellationToken.None);

        Assert.IsType<NoContent>(result.Result);
        Assert.Equal([row.LinkPath], files.Removed);
        // The source stays untouched while the row tracks the pending Sonarr cleanup.
        Assert.True(files.Entries.ContainsKey(row.SourcePath));
        Assert.Equal(SymlinkOperationStatus.CleanupPending, (await StoredAsync("link-1")).OperationStatus);
        // Deleting a link does not reject the pairing.
        Assert.Empty(await store.GetRejectionsAsync(CancellationToken.None));
        SymlinkService cleanup = Links(new StubArrHandler(SymlinkFixture.HappyRoutes()), files);
        Assert.Equal(SymlinkSetupOutcome.Completed, await cleanup.FinishDeletionAsync(await StoredAsync("link-1"), CancellationToken.None));
        Assert.Empty(await store.GetRejectionsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Delete_AViewThatAgedLeavesTheLinkStandingWhereItIs()
    {
        SymlinkLink row = await SeedRowAsync("link-1");
        FakeLinkFileSystem files = new();
        files.Entries[row.LinkPath] = row.LinkText;

        Results<NoContent, NotFound, ProblemHttpResult> result = await Invoke<Results<NoContent, NotFound, ProblemHttpResult>>(
            "DeleteLinkAsync", "link-1", 7L, Links(new NeverAskedHandler(), files), CancellationToken.None);

        ProblemHttpResult problem = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.ProblemDetails.Status);
        Assert.Empty(files.Removed);
        Assert.Equal(SymlinkOperationStatus.Active, (await StoredAsync("link-1")).OperationStatus);
    }

    [Fact]
    public async Task Writes_AnUnknownEntryIsMissingAndNothingIsRemoved()
    {
        FakeLinkFileSystem files = new();

        Results<Ok<SymlinkLinkDto>, NotFound, ProblemHttpResult> followUp =
            await Invoke<Results<Ok<SymlinkLinkDto>, NotFound, ProblemHttpResult>>(
                "SetFollowUpAsync",
                "link-9",
                new SetSymlinkFollowUpRequest { AutoFollowUp = false, Revision = 0 },
                new FixedSettings(Settings()),
                Links(new NeverAskedHandler(), files),
                CancellationToken.None);
        Results<NoContent, NotFound, ProblemHttpResult> delete = await Invoke<Results<NoContent, NotFound, ProblemHttpResult>>(
            "DeleteLinkAsync", "link-9", 0L, Links(new NeverAskedHandler(), files), CancellationToken.None);

        Assert.IsType<NotFound>(followUp.Result);
        Assert.IsType<NotFound>(delete.Result);
        Assert.Empty(files.Removed);
    }

    [Fact]
    public async Task Routes_AreMountedInTheAuthorizedSettingsGroupAndTheSetupSignalIsShared()
    {
        using AuthTestAppFactory factory = new();
        HttpClient cookieless = factory.CreateCookielessClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await cookieless.GetAsync("/api/settings/symlinks/links")).StatusCode);

        // Resolving the runner proves the whole chain a background pass needs — the register, the file
        // system and the Sonarr side — because a missing registration would otherwise only show up when a
        // row is actually carried forward.
        using IServiceScope scope = factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SymlinkSetupRunner>());
        // The signal that a row owes work is one per process, while every pass gets its own services.
        Assert.Same(
            factory.Services.GetRequiredService<SymlinkSetupRequests>(),
            scope.ServiceProvider.GetRequiredService<SymlinkSetupRequests>());
        using IServiceScope nextPass = factory.Services.CreateScope();
        Assert.NotSame(
            nextPass.ServiceProvider.GetRequiredService<SymlinkSetupRunner>(),
            scope.ServiceProvider.GetRequiredService<SymlinkSetupRunner>());
    }

    private async Task<SymlinkLinkPlanDto> Preview(StubArrHandler handler, FakeLinkFileSystem files) =>
        (await Invoke<Ok<SymlinkLinkPlanDto>>(
            "PreviewLinkAsync",
            new PreviewSymlinkLinkRequest
            {
                RadarrInstanceId = Pair.RadarrInstanceId,
                RadarrMovieId = Pair.RadarrMovieId,
                SonarrInstanceId = Pair.SonarrInstanceId,
                SonarrSeriesId = Pair.SonarrSeriesId,
                SonarrEpisodeId = Pair.SonarrEpisodeId,
                RadarrFileId = SymlinkFixture.RadarrFileId
            },
            new FixedSettings(Settings()),
            Links(handler, files),
            CancellationToken.None)).Value!;

    private async Task<Results<Accepted<SymlinkLinkDto>, NotFound, ProblemHttpResult>> Confirm(
        StubArrHandler handler,
        FakeLinkFileSystem files) =>
        await Invoke<Results<Accepted<SymlinkLinkDto>, NotFound, ProblemHttpResult>>(
            "ConfirmSuggestionAsync",
            "suggestion-1",
            new ConfirmSuggestionRequest { ConfigRevision = ConfigRevision },
            new FixedSettings(Settings()),
            Links(handler, files),
            store,
            CancellationToken.None);

    private SymlinkService Links(HttpMessageHandler handler, FakeLinkFileSystem files)
    {
        FixedSettings settings = new(Settings());
        return new SymlinkService(
            store,
            settings,
            new MediaPathMappingService(settings),
            new SymlinkCapabilityService(store, settings, files, new FakeTimeProvider(Start), NullLogger<SymlinkCapabilityService>.Instance),
            setupRequests,
            new RadarrClient(new HttpClient(handler)),
            new SonarrClient(new HttpClient(handler)),
            files,
            new FakeTimeProvider(Start),
            NullLogger<SymlinkService>.Instance);
    }

    /// <summary>
    /// The same look the scheduled task runs, built the way the interface route builds it: out of the
    /// register, the stored snapshots and the file system, with no instance asked for anything.
    /// </summary>
    private SymlinkHealthService Health(FakeLinkFileSystem files) =>
        new(
            store,
            new FixedSettings(Settings()),
            Links(new NeverAskedHandler(), files),
            files,
            setupRequests,
            new FakeTimeProvider(Start),
            NullLogger<SymlinkHealthService>.Instance);

    /// <summary>
    /// Whether a setup pass was asked for. The signal holds one pending request, so one that was written
    /// is read at once while a missing one leaves the wait open — which is what the timeout is for.
    /// </summary>
    private async Task<bool> IsPassRequestedAsync()
    {
        Task signaled = setupRequests.WaitForRequestAsync(CancellationToken.None);
        return await Task.WhenAny(signaled, Task.Delay(TimeSpan.FromSeconds(1))) == signaled;
    }

    private async Task SeedSuggestionAsync() =>
        await store.UpsertSuggestionAsync(new SymlinkSuggestion
        {
            Id = "suggestion-1",
            MatcherVersion = "1",
            Status = SymlinkSuggestionStatus.Pending,
            Pair = Pair,
            RadarrFileId = SymlinkFixture.RadarrFileId,
            SeasonNumber = 0,
            EpisodeNumber = SymlinkFixture.EpisodeId,
            ConfigRevision = ConfigRevision,
            CreatedAt = Start,
            UpdatedAt = Start
        }, CancellationToken.None);

    private async Task<SymlinkLink> SeedRowAsync(
        string id,
        int movieId = SymlinkFixture.MovieId,
        DateTimeOffset? at = null,
        SymlinkOperationStatus operation = SymlinkOperationStatus.Active,
        SymlinkSetupStep? setupStep = SymlinkSetupStep.Completed,
        bool autoFollowUp = true,
        int radarrFileId = SymlinkFixture.RadarrFileId,
        string? sourceVersion = null)
    {
        SymlinkLink row = new()
        {
            Id = id,
            RadarrInstanceId = RadarrInstance,
            RadarrMovieId = movieId,
            RadarrFileId = radarrFileId,
            SonarrInstanceId = SonarrInstance,
            SonarrSeriesId = SymlinkFixture.SeriesId,
            SonarrEpisodeId = SymlinkFixture.EpisodeId,
            SeasonNumber = 0,
            EpisodeNumber = SymlinkFixture.EpisodeId,
            StorageAreaId = StorageArea,
            LinkPath = SymlinkFixture.ExpectedLinkPath(),
            LinkText = Path.GetRelativePath(
                Path.GetDirectoryName(SymlinkFixture.ExpectedLinkPath())!, SymlinkFixture.SourcePath()),
            SourcePath = SymlinkFixture.SourcePath(),
            OperationStatus = operation,
            SetupStep = setupStep,
            AutoFollowUp = autoFollowUp,
            SourceVersion = sourceVersion,
            CreatedAt = at ?? Start,
            UpdatedAt = at ?? Start,
            Findings = operation == SymlinkOperationStatus.Conflict
                ? [new SymlinkFinding("link-path-not-translatable", "The path of the series moved.", Start)]
                : []
        };
        await store.InsertLinkAsync(row, CancellationToken.None);
        return row;
    }

    private async Task<SymlinkLink> StoredAsync(string id) =>
        await store.GetLinkAsync(id, CancellationToken.None) ?? throw new InvalidOperationException($"Entry {id} is missing.");

    private static Task<T> Invoke<T>(string method, params object?[] arguments) =>
        (Task<T>)typeof(SymlinkEndpoints).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments)!;

    /// <summary>
    /// The working path configuration plus the snapshots the names in the answers come from — the two halves
    /// a register row is read back out of.
    /// </summary>
    private static AppSettings Settings()
    {
        AppSettings settings = SymlinkFixture.Settings();
        settings.SonarrSnapshots =
        [
            new SonarrInstanceSnapshot
            {
                InstanceId = SonarrInstance,
                InstanceName = "Sonarr",
                Series =
                [
                    new SonarrSeriesSnapshot
                    {
                        SeriesId = SymlinkFixture.SeriesId,
                        Title = SeriesTitle,
                        Seasons =
                        [
                            new SonarrSeasonSnapshot
                            {
                                SeasonNumber = 0,
                                Episodes =
                                [
                                    new SonarrEpisodeSnapshot
                                    {
                                        EpisodeId = SymlinkFixture.EpisodeId,
                                        EpisodeNumber = SymlinkFixture.EpisodeId,
                                        Title = EpisodeTitle,
                                        HasFile = true
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        ];
        settings.RadarrSnapshots =
        [
            new RadarrInstanceSnapshot
            {
                InstanceId = RadarrInstance,
                InstanceName = "Radarr",
                Movies =
                [
                    new RadarrMovieSnapshot
                    {
                        MovieId = SymlinkFixture.MovieId,
                        Title = SpecialTitle,
                        HasFile = true,
                        File = new MediaFileSnapshot
                        {
                            Id = SymlinkFixture.RadarrFileId,
                            Size = SymlinkFixture.SourceSize,
                            Path = SymlinkFixture.MoviesInstance($"{SymlinkFixture.MovieFolder}/{SymlinkFixture.SourceFileName}")
                        }
                    }
                ]
            }
        ];
        return settings;
    }
}
