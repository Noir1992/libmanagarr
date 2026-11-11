using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Endpoints;

/// <summary>
/// The path configuration, the capability test, the suggestions and the register of managed links. Reads
/// come from the settings snapshot and the stored rows, never from a live probe or an Arr call, so opening
/// the page costs nothing and cannot create a file in the media tree. What does change something is the
/// refresh, the two decisions and the register's own actions — and among those only the confirm touches
/// the file system, while everything Sonarr still has to do with the entry runs on from the row.
/// </summary>
public static class SymlinkEndpoints
{
    public static RouteGroupBuilder MapSymlinkEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/symlinks", GetSymlinksAsync);
        group.MapGet("/symlinks/directories", BrowseDirectories);
        group.MapPut("/symlinks", SaveSymlinksAsync);
        group.MapPost("/symlinks/capability-check", CheckCapabilityAsync);
        group.MapPost("/symlinks/storage-areas/{areaId}/capability-test", TestCapabilityAsync);
        group.MapGet("/symlinks/suggestions", GetSuggestionsAsync);
        group.MapPost("/symlinks/suggestions/refresh", RefreshSuggestionsAsync).AddEndpointFilter(RequireEnabledAsync);
        group.MapPost("/symlinks/suggestions/{id}/reject", RejectSuggestionAsync).AddEndpointFilter(RequireEnabledAsync);
        group.MapPost("/symlinks/suggestions/{id}/confirm", ConfirmSuggestionAsync).AddEndpointFilter(RequireEnabledAsync);
        group.MapGet("/symlinks/rejections", GetRejectionsAsync);
        group.MapPost("/symlinks/rejections/reactivate", ReactivateRejectionAsync).AddEndpointFilter(RequireEnabledAsync);
        group.MapPost("/symlinks/preview", PreviewLinkAsync).AddEndpointFilter(RequireEnabledAsync);
        group.MapGet("/symlinks/links", GetLinksAsync);
        group.MapPost("/symlinks/links", SaveLinkAsync).AddEndpointFilter(RequireEnabledAsync);
        group.MapPut("/symlinks/links/{id}", EditLinkAsync).AddEndpointFilter(RequireEnabledAsync);
        group.MapPost("/symlinks/links/{id}/retry", RetryLinkAsync).AddEndpointFilter(RequireEnabledAsync);
        group.MapGet("/symlinks/choices", GetChoicesAsync);
        group.MapGet("/symlinks/links/{id}", GetLinkAsync);
        group.MapPut("/symlinks/links/{id}/follow-up", SetFollowUpAsync).AddEndpointFilter(RequireEnabledAsync);
        group.MapPost("/symlinks/links/{id}/check", CheckLinkAsync).AddEndpointFilter(RequireEnabledAsync);
        group.MapDelete("/symlinks/links/{id}", DeleteLinkAsync).AddEndpointFilter(RequireEnabledAsync);
        return group;
    }

    private static async ValueTask<object?> RequireEnabledAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ISettingsService settings = context.HttpContext.RequestServices.GetRequiredService<ISettingsService>();
        if (!(await settings.GetSnapshotAsync(context.HttpContext.RequestAborted)).Symlinks.Enabled)
            return TypedResults.Problem(statusCode: 409, title: "Symlinks are disabled",
                detail: "Enable symlinks in settings before performing this action.");
        return await next(context);
    }

    private static IResult BrowseDirectories(string? path)
    {
        try { return TypedResults.Ok(SymlinkDirectoryBrowser.Browse(path)); }
        catch (ApiProblemException exception) { return Problem(exception); }
    }

    private static async Task<Ok<object>> GetChoicesAsync(ISettingsService settings, CancellationToken token)
    {
        AppSettings snapshot = await settings.GetSnapshotAsync(token);
        return TypedResults.Ok<object>(new
        {
            Movies = snapshot.RadarrSnapshots.SelectMany(instance => instance.Movies.Where(movie => movie.File.Id.HasValue)
                .Select(movie => new { instance.InstanceId, instance.InstanceName, movie.MovieId, movie.Title, FileId = movie.File.Id, movie.File.Path })),
            Specials = snapshot.SonarrSnapshots.SelectMany(instance => instance.Series.SelectMany(series =>
                series.Seasons.Where(season => season.SeasonNumber == 0).SelectMany(season => season.Episodes.Select(episode => new
                {
                    instance.InstanceId, instance.InstanceName, series.SeriesId, SeriesTitle = series.Title,
                    episode.EpisodeId, episode.EpisodeNumber, episode.Title, episode.HasFile
                }))))
        });
    }

    private static async Task<Results<Accepted<SymlinkLinkDto>, ProblemHttpResult>> SaveLinkAsync(
        SaveSymlinkLinkRequest request, SymlinkService links, ISettingsService settings, CancellationToken cancellationToken)
    {
        using IDisposable lease = await SymlinkOperationGate.EnterAsync(cancellationToken);
        try
        {
            SymlinkLink created = await links.CreateAsync(request.ToPair(), request.RadarrFileId,
                request.ConfigRevision, null, cancellationToken, request.SourceVersion, request.ExistingLink);
            return TypedResults.Accepted($"/api/settings/symlinks/links/{created.Id}",
                LinkDto(created, SnapshotTitles.Collect(await settings.GetSnapshotAsync(cancellationToken))));
        }
        catch (ApiProblemException exception) { return Problem(exception); }
    }

    private static async Task<Results<Accepted<SymlinkLinkDto>, NotFound, ProblemHttpResult>> EditLinkAsync(
        string id, SaveSymlinkLinkRequest request, SymlinkService links, ISettingsService settings, CancellationToken cancellationToken)
    {
        using IDisposable lease = await SymlinkOperationGate.EnterAsync(cancellationToken);
        try
        {
            SymlinkLink? changed = await links.EditAsync(id, request, cancellationToken);
            if (changed is null) return TypedResults.NotFound();
            return TypedResults.Accepted($"/api/settings/symlinks/links/{id}",
                LinkDto(changed, SnapshotTitles.Collect(await settings.GetSnapshotAsync(cancellationToken))));
        }
        catch (ApiProblemException exception) { return Problem(exception); }
    }

    private static async Task<Results<Accepted<SymlinkLinkDto>, NotFound, ProblemHttpResult>> RetryLinkAsync(
        string id, RetrySymlinkRequest request, SymlinkService links, ISettingsService settings, CancellationToken cancellationToken)
    {
        using IDisposable lease = await SymlinkOperationGate.EnterAsync(cancellationToken);
        try
        {
            SymlinkLink? changed = await links.RetryAsync(id, request.Revision, cancellationToken);
            if (changed is null) return TypedResults.NotFound();
            return TypedResults.Accepted($"/api/settings/symlinks/links/{id}",
                LinkDto(changed, SnapshotTitles.Collect(await settings.GetSnapshotAsync(cancellationToken))));
        }
        catch (ApiProblemException exception) { return Problem(exception); }
    }

    private static async Task<Ok<IReadOnlyList<SymlinkSuggestionDto>>> GetSuggestionsAsync(
        ISettingsService settingsService,
        ISymlinkStore store,
        ExistingEpisodeFileInspector inspector,
        CancellationToken cancellationToken)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        if (!settings.Symlinks.Enabled) return TypedResults.Ok<IReadOnlyList<SymlinkSuggestionDto>>([]);
        SnapshotTitles titles = SnapshotTitles.Collect(settings);
        IReadOnlyList<SymlinkSuggestion> suggestions = await store.GetSuggestionsAsync(cancellationToken);

        SymlinkSuggestion[] ordered = suggestions
            .OrderByDescending(suggestion => suggestion.CreatedAt)
            .ThenBy(suggestion => suggestion.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        SymlinkSuggestionDto[] results = new SymlinkSuggestionDto[ordered.Length];
        await Parallel.ForEachAsync(Enumerable.Range(0, ordered.Length),
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, async (index, token) =>
        {
            SymlinkSuggestion suggestion = ordered[index];
            SymlinkSuggestionDto dto = SuggestionDto(suggestion, settings.Symlinks.Revision, titles);
            dto.ExistingFileCheck = await inspector.CheckAsync(settings, suggestion, token);
            results[index] = dto;
        });
        return TypedResults.Ok<IReadOnlyList<SymlinkSuggestionDto>>(results);
    }

    private static async Task<Ok<SymlinkMatchingRun>> RefreshSuggestionsAsync(
        SymlinkSuggestionService matching,
        CancellationToken cancellationToken) =>
        // The manual action re-reads even when nothing looks changed; the stored signatures decide for
        // every other run. A pass only reads snapshots and writes rows, so it stays in this request.
        TypedResults.Ok(await matching.RefreshAsync(force: true, cancellationToken));

    private static async Task<Results<Ok<SymlinkSuggestionDto>, NotFound>> RejectSuggestionAsync(
        string id,
        RejectSuggestionRequest request,
        ISettingsService settingsService,
        SymlinkSuggestionService matching,
        ISymlinkStore store,
        CancellationToken cancellationToken)
    {
        if (!await matching.RejectAsync(id, request.Detail, cancellationToken)) return TypedResults.NotFound();

        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        SymlinkSuggestion? declined = await store.GetSuggestionAsync(id, cancellationToken);
        return declined is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(SuggestionDto(declined, settings.Symlinks.Revision, SnapshotTitles.Collect(settings)));
    }

    /// <summary>
    /// The decision that turns an offered pair into a link. Answered as accepted rather than created,
    /// because what this call proves is only that the file exists: everything Sonarr still has to do with
    /// the entry runs on from the row and is read back from the register.
    /// </summary>
    private static async Task<Results<Accepted<SymlinkLinkDto>, NotFound, ProblemHttpResult>> ConfirmSuggestionAsync(
        string id,
        ConfirmSuggestionRequest request,
        ISettingsService settingsService,
        SymlinkService links,
        ISymlinkStore store,
        CancellationToken cancellationToken)
    {
        using IDisposable lease = await SymlinkOperationGate.EnterAsync(cancellationToken);
        SymlinkSuggestion? suggestion = await store.GetSuggestionAsync(id, cancellationToken);
        if (suggestion is null) return TypedResults.NotFound();

        try
        {
            SymlinkLink link = await links.CreateAsync(
                suggestion.Pair,
                suggestion.RadarrFileId,
                request.ConfigRevision,
                suggestion.Id,
                cancellationToken, request.SourceVersion, request.ExistingLink);

            AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
            return TypedResults.Accepted($"/api/settings/symlinks/links/{link.Id}",
                LinkDto(link, SnapshotTitles.Collect(settings)));
        }
        catch (ApiProblemException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<Ok<IReadOnlyList<SymlinkRejectionDto>>> GetRejectionsAsync(
        ISettingsService settingsService,
        ISymlinkStore store,
        CancellationToken cancellationToken)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        SnapshotTitles titles = SnapshotTitles.Collect(settings);

        return TypedResults.Ok<IReadOnlyList<SymlinkRejectionDto>>((await store.GetRejectionsAsync(cancellationToken))
            .OrderByDescending(rejection => rejection.CreatedAt)
            .Select(rejection => new SymlinkRejectionDto
            {
                RadarrInstanceId = rejection.Pair.RadarrInstanceId,
                RadarrMovieId = rejection.Pair.RadarrMovieId,
                RadarrMovieTitle = titles.MovieTitle(rejection.Pair),
                SonarrInstanceId = rejection.Pair.SonarrInstanceId,
                SonarrSeriesId = rejection.Pair.SonarrSeriesId,
                SonarrSeriesTitle = titles.SeriesTitle(rejection.Pair),
                SonarrEpisodeId = rejection.Pair.SonarrEpisodeId,
                SonarrEpisodeTitle = titles.EpisodeTitle(rejection.Pair),
                Detail = rejection.Detail,
                CreatedAt = rejection.CreatedAt
            })
            .ToList());
    }

    private static async Task<Results<Ok, NotFound>> ReactivateRejectionAsync(
        SpecialMoviePairRequest request,
        SymlinkSuggestionService matching,
        CancellationToken cancellationToken) =>
        await matching.ReactivateAsync(request.ToPair(), cancellationToken) ? TypedResults.Ok() : TypedResults.NotFound();

    /// <summary>
    /// What confirming a pair would do, read from both live instances. It writes neither a row nor a
    /// file, so looking is free, repeatable and never half done.
    /// </summary>
    private static async Task<Ok<SymlinkLinkPlanDto>> PreviewLinkAsync(
        PreviewSymlinkLinkRequest request,
        ISettingsService settingsService,
        SymlinkService links,
        CancellationToken cancellationToken)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        SymlinkLinkPlan plan = await links.PreviewAsync(request.ToPair(), request.RadarrFileId, cancellationToken, request.ReplacesId);

        return TypedResults.Ok(new SymlinkLinkPlanDto
        {
            Runnable = plan.Runnable,
            AdoptsExisting = plan.AdoptsExisting,
            Draft = LinkDto(plan.Draft, SnapshotTitles.Collect(settings)),
            Blockers = [.. plan.Blockers],
            Warnings = [.. plan.Warnings]
        });
    }

    private static async Task<Ok<IReadOnlyList<SymlinkLinkDto>>> GetLinksAsync(
        ISettingsService settingsService,
        ISymlinkStore store,
        CancellationToken cancellationToken)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        SnapshotTitles titles = SnapshotTitles.Collect(settings);

        return TypedResults.Ok<IReadOnlyList<SymlinkLinkDto>>((await store.GetLinksAsync(cancellationToken))
            .OrderByDescending(link => link.CreatedAt)
            .ThenBy(link => link.Id, StringComparer.OrdinalIgnoreCase)
            .Select(link => LinkDto(link, titles))
            .ToList());
    }

    private static async Task<Results<Ok<SymlinkLinkDto>, NotFound>> GetLinkAsync(
        string id,
        ISettingsService settingsService,
        ISymlinkStore store,
        CancellationToken cancellationToken)
    {
        SymlinkLink? link = await store.GetLinkAsync(id, cancellationToken);
        if (link is null) return TypedResults.NotFound();

        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        return TypedResults.Ok(LinkDto(link, SnapshotTitles.Collect(settings)));
    }

    private static async Task<Results<Ok<SymlinkLinkDto>, NotFound, ProblemHttpResult>> SetFollowUpAsync(
        string id,
        SetSymlinkFollowUpRequest request,
        ISettingsService settingsService,
        SymlinkService links,
        CancellationToken cancellationToken)
    {
        using IDisposable lease = await SymlinkOperationGate.EnterAsync(cancellationToken);
        try
        {
            SymlinkLink? updated = await links.SetAutoFollowUpAsync(id, request.AutoFollowUp, request.Revision, cancellationToken);
            if (updated is null) return TypedResults.NotFound();

            AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
            return TypedResults.Ok(LinkDto(updated, SnapshotTitles.Collect(settings)));
        }
        catch (ApiProblemException exception)
        {
            return Problem(exception);
        }
    }

    /// <summary>
    /// One entry examined now, by the same pass the scheduled task runs over the whole register. It is
    /// offered because an operator who just changed a mount wants an answer about that shelf and not the
    /// next night; what it found is on the entry, so the answer is the entry.
    /// </summary>
    private static async Task<Results<Ok<SymlinkLinkDto>, NotFound>> CheckLinkAsync(
        string id,
        ISettingsService settingsService,
        SymlinkHealthService health,
        CancellationToken cancellationToken)
    {
        SymlinkLink? checkedLink = await health.CheckLinkAsync(id, cancellationToken);
        if (checkedLink is null) return TypedResults.NotFound();

        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        return TypedResults.Ok(LinkDto(checkedLink, SnapshotTitles.Collect(settings)));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteLinkAsync(
        string id,
        long revision,
        SymlinkService links,
        CancellationToken cancellationToken)
    {
        using IDisposable lease = await SymlinkOperationGate.EnterAsync(cancellationToken);
        try
        {
            return await links.DeleteAsync(id, revision, cancellationToken)
                ? TypedResults.NoContent()
                : TypedResults.NotFound();
        }
        catch (ApiProblemException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<Ok<SymlinkSettingsDto>> GetSymlinksAsync(
        ISettingsService settingsService,
        ISymlinkStore store,
        CancellationToken cancellationToken)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        return TypedResults.Ok(await BuildAsync(settings, store, cancellationToken));
    }

    private static async Task<Results<Ok<SymlinkSettingsDto>, ValidationProblem>> SaveSymlinksAsync(
        JsonElement request,
        ISettingsService settingsService,
        ISymlinkStore store,
        SymlinkSetupRequests setupRequests,
        SymlinkMatchingRequests matchingRequests,
        SymlinkCapabilityService capability,
        CancellationToken cancellationToken)
    {
        using IDisposable lease = await SymlinkOperationGate.EnterAsync(cancellationToken);
        SymlinkSettings? candidate;
        try
        {
            candidate = request.Deserialize<SymlinkSettings>(new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });
        }
        catch (JsonException)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["symlinks"] = ["Invalid symlink settings."] });
        }

        if (candidate is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["symlinks"] = ["The symlink settings are required."] });
        }

        // The revision is what invalidates stored probe results, so it stays with the server: only the
        // comparison below decides whether a probe answer is still usable.
        using IDisposable updateLease = await settingsService.BeginUpdateAsync(cancellationToken);
        AppSettings current = await settingsService.GetAsync(cancellationToken);
        if (!request.EnumerateObject().Any(property => property.Name.Equals("enabled", StringComparison.OrdinalIgnoreCase)))
            candidate.Enabled = current.Symlinks.Enabled;
        Dictionary<string, string[]> errors =
            SymlinkSettingsRules.Validate(candidate, current.SonarrInstances, current.RadarrInstances);
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

        SymlinkSettings normalized = SymlinkSettingsRules.Normalize(candidate);
        if (normalized.MediaRoot is not null && capability.ValidateMediaRoot(normalized.MediaRoot) is string rootError)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["symlinks.mediaRoot"] = [rootError] });
        IReadOnlyList<SymlinkLink> registered = await store.GetLinksAsync(cancellationToken);
        if (registered.Any(link => !normalized.StorageAreas.Any(area => (normalized.MediaRoot is not null || area.Id == link.StorageAreaId)
                && ArrPathRules.IsWithinRoot(area.SeriesRoot, link.LinkPath) && ArrPathRules.IsWithinRoot(area.MoviesRoot, link.SourcePath))
                || (normalized.MediaRoot is null && (!normalized.PathMappings.Any(mapping => mapping.Kind == ArrApplicationKind.Sonarr && mapping.InstanceId == link.SonarrInstanceId && ArrPathRules.IsWithinRoot(mapping.LibManagarrRoot, link.LinkPath))
                || !normalized.PathMappings.Any(mapping => mapping.Kind == ArrApplicationKind.Radarr && mapping.InstanceId == link.RadarrInstanceId && ArrPathRules.IsWithinRoot(mapping.LibManagarrRoot, link.SourcePath))))))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [normalized.MediaRoot is null ? "storageAreas" : "mediaRoot"] = ["Existing links are outside the configured media directories. Resolve those links before changing the media root."] });
        bool pathsChanged = SymlinkSettingsRules.RequiresNewRevision(current.Symlinks, normalized);
        normalized.Revision = pathsChanged
            ? current.Symlinks.Revision + 1
            : current.Symlinks.Revision;
        bool wasEnabled = current.Symlinks.Enabled;
        current.Symlinks = normalized;
        if ((pathsChanged || normalized.Enabled) && !await capability.CheckConfigurationAsync(current, cancellationToken))
            normalized.Enabled = false;
        bool resuming = !wasEnabled && normalized.Enabled;
        AppSettings saved = await settingsService.SaveAsync(current, cancellationToken);

        if (resuming)
        {
            setupRequests.Request();
            matchingRequests.Request();
        }
        return TypedResults.Ok(await BuildAsync(saved, store, cancellationToken));
    }

    private static async Task<Ok<SymlinkSettingsDto>> CheckCapabilityAsync(
        SymlinkCapabilityService capability, ISymlinkStore store, CancellationToken token) =>
        TypedResults.Ok(await BuildAsync(await capability.CheckAndUpdateAsync(token), store, token));

    private static async Task<Results<Ok<SymlinkCapabilityStateDto>, NotFound>> TestCapabilityAsync(
        string areaId,
        ISettingsService settingsService,
        SymlinkCapabilityService capability,
        CancellationToken cancellationToken)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        if (!settings.Symlinks.StorageAreas.Any(area => string.Equals(area.Id, areaId, StringComparison.OrdinalIgnoreCase)))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(
            StateDto(await capability.ProbeAsync(areaId, cancellationToken), settings.Symlinks.Revision, areaId));
    }

    private static async Task<SymlinkSettingsDto> BuildAsync(
        AppSettings settings,
        ISymlinkStore store,
        CancellationToken cancellationToken)
    {
        SymlinkSettings config = SymlinkSettingsRules.Normalize(settings.Symlinks);
        IReadOnlyList<SymlinkCapabilityCheck> checks = await store.GetCapabilityChecksAsync(cancellationToken);

        return new SymlinkSettingsDto
        {
            Enabled = config.Enabled,
            MediaRoot = config.MediaRoot ?? InferMediaRoot(config),
            StorageAreas = config.StorageAreas,
            PathMappings = config.PathMappings,
            ReaderViews = config.ReaderViews,
            Revision = config.Revision,
            CapabilityChecks = config.StorageAreas
                .Select(area => StateDto(
                    checks.FirstOrDefault(check => string.Equals(check.StorageAreaId, area.Id, StringComparison.OrdinalIgnoreCase)),
                    config.Revision,
                    area.Id))
                .ToList()
        };
    }

    private static string InferMediaRoot(SymlinkSettings config)
    {
        string[] roots = config.StorageAreas.SelectMany(area => new[] { area.MoviesRoot, area.SeriesRoot }).ToArray();
        if (roots.Length == 0 || !ArrPathRules.TryParse(roots[0], out ParsedPath? parsed, out _)) return "/media";
        for (int length = parsed!.Segments.Count; length > 0; length--)
        {
            string candidate = new ParsedPath(parsed.Syntax, parsed.Root, parsed.Segments.Take(length).ToArray()).Format();
            if (roots.All(root => ArrPathRules.IsWithinRoot(candidate, root))) return candidate;
        }
        return "/media";
    }

    private static SymlinkCapabilityStateDto StateDto(
        SymlinkCapabilityCheck? check, int currentRevision, string storageAreaId)
    {
        if (check is null)
        {
            return new SymlinkCapabilityStateDto
            {
                StorageAreaId = storageAreaId,
                Status = SymlinkCapabilityStatus.NotTested,
                Detail = "The symlink test has not run for the current media configuration.",
                Stale = true
            };
        }

        return new SymlinkCapabilityStateDto
        {
            StorageAreaId = check.StorageAreaId,
            Status = check.Status,
            Detail = check.Detail,
            CheckedAt = check.CheckedAt,
            ConfigRevision = check.ConfigRevision,
            // A result from before the last path change says nothing about the paths in use now.
            Stale = !check.IsCurrent(currentRevision)
        };
    }

    private static SymlinkSuggestionDto SuggestionDto(
        SymlinkSuggestion suggestion, int currentRevision, SnapshotTitles titles) => new()
        {
            Id = suggestion.Id,
            MatcherVersion = suggestion.MatcherVersion,
            Status = suggestion.Status,
            RadarrInstanceId = suggestion.Pair.RadarrInstanceId,
            RadarrInstanceName = titles.InstanceName(suggestion.Pair.RadarrInstanceId),
            RadarrMovieId = suggestion.Pair.RadarrMovieId,
            RadarrMovieTitle = titles.MovieTitle(suggestion.Pair),
            RadarrFileId = suggestion.RadarrFileId,
            RadarrFilePath = titles.SourcePath(suggestion.Pair, suggestion.RadarrFileId),
            SonarrInstanceId = suggestion.Pair.SonarrInstanceId,
            SonarrInstanceName = titles.InstanceName(suggestion.Pair.SonarrInstanceId),
            SonarrSeriesId = suggestion.Pair.SonarrSeriesId,
            SonarrSeriesTitle = titles.SeriesTitle(suggestion.Pair),
            SonarrSeriesPath = titles.SeriesPath(suggestion.Pair),
            SonarrEpisodeId = suggestion.Pair.SonarrEpisodeId,
            SonarrEpisodeTitle = titles.EpisodeTitle(suggestion.Pair),
            SeasonNumber = suggestion.SeasonNumber,
            EpisodeNumber = suggestion.EpisodeNumber,
            Reasons = [.. suggestion.Reasons],
            Alternatives = [.. suggestion.Alternatives],
            Detail = suggestion.Detail,
            ConfigRevision = suggestion.ConfigRevision,
            // The same rule as the capability answer: paths that moved since the row was written make
            // its preview untrue, and the view has to say so instead of offering it as runnable.
            Stale = suggestion.ConfigRevision != currentRevision,
            LinkId = suggestion.LinkId,
            Revision = suggestion.Revision,
            CreatedAt = suggestion.CreatedAt,
            UpdatedAt = suggestion.UpdatedAt,
            DecidedAt = suggestion.DecidedAt
        };

    private static SymlinkLinkDto LinkDto(SymlinkLink link, SnapshotTitles titles) => new()
        {
            Id = link.Id,
            RadarrInstanceId = link.RadarrInstanceId,
            RadarrInstanceName = titles.InstanceName(link.RadarrInstanceId),
            RadarrMovieId = link.RadarrMovieId,
            RadarrMovieTitle = titles.MovieTitle(link.Pair),
            RadarrFileId = link.RadarrFileId,
            SonarrInstanceId = link.SonarrInstanceId,
            SonarrInstanceName = titles.InstanceName(link.SonarrInstanceId),
            SonarrSeriesId = link.SonarrSeriesId,
            SonarrSeriesTitle = titles.SeriesTitle(link.Pair),
            SonarrEpisodeId = link.SonarrEpisodeId,
            SonarrFileId = link.SonarrFileId,
            SonarrEpisodeTitle = titles.EpisodeTitle(link.Pair),
            SeasonNumber = link.SeasonNumber,
            EpisodeNumber = link.EpisodeNumber,
            StorageAreaId = link.StorageAreaId,
            LinkPath = link.LinkPath,
            LinkText = link.LinkText,
            SourcePath = link.SourcePath,
            OperationStatus = link.OperationStatus,
            SetupStep = link.SetupStep,
            Findings = [.. link.Findings],
            MonitoringOriginal = link.MonitoringOriginal,
            MonitoringDisabled = link.MonitoringDisabled,
            AutoFollowUp = link.AutoFollowUp,
            DesiredFilename = link.DesiredFilename,
            AppliedFilename = link.AppliedFilename,
            SourceVersion = link.SourceVersion,
            ExpectedMetadata = link.ExpectedMetadata,
            ConfirmedMetadata = link.ConfirmedMetadata,
            SuggestionId = link.SuggestionId,
            Revision = link.Revision,
            CreatedAt = link.CreatedAt,
            UpdatedAt = link.UpdatedAt,
            LastCheckedAt = link.LastCheckedAt,
            LastSuccessfulCheckAt = link.LastSuccessfulCheckAt
        };

    /// <summary>
    /// What the service said is wrong, as the answer it deserves: "something is in the way" and "the
    /// environment is not ready" carry different statuses, and a retry only makes sense for one of them.
    /// </summary>
    private static ProblemHttpResult Problem(ApiProblemException exception) =>
        TypedResults.Problem(new ProblemDetails
        {
            Title = exception.Title,
            Detail = exception.Detail,
            Status = exception.StatusCode
        });

    /// <summary>
    /// Titles taken from the snapshots the stored ids came from, because the row itself keeps only ids.
    /// A pair whose movie or episode is gone keeps its row and shows without a title — that absence is
    /// the finding, not a gap to fill with a guess.
    /// </summary>
    private sealed class SnapshotTitles
    {
        private readonly Dictionary<(string InstanceId, int MovieId), MovieTitles> movies = [];
        private readonly Dictionary<string, string> instances = [];
        public string? InstanceName(string id) => instances.GetValueOrDefault(id);
        private readonly Dictionary<(string InstanceId, int SeriesId, int EpisodeId), EpisodeTitles> episodes = [];

        public static SnapshotTitles Collect(AppSettings settings)
        {
            SnapshotTitles titles = new();
            foreach (ArrInstanceSettings instance in settings.SonarrInstances.Concat(settings.RadarrInstances)) titles.instances[instance.Id] = instance.Name;
            foreach (RadarrInstanceSnapshot radarr in settings.RadarrSnapshots)
            {
                foreach (RadarrMovieSnapshot movie in radarr.Movies)
                {
                    titles.movies[(radarr.InstanceId, movie.MovieId)] = new MovieTitles(movie.Title, movie.File.Id, movie.File.Path);
                }
            }

            foreach (SonarrInstanceSnapshot sonarr in settings.SonarrSnapshots)
            {
                foreach (SonarrSeriesSnapshot series in sonarr.Series)
                {
                    foreach (SonarrSeasonSnapshot season in series.Seasons)
                    {
                        foreach (SonarrEpisodeSnapshot episode in season.Episodes)
                        {
                            titles.episodes[(sonarr.InstanceId, series.SeriesId, episode.EpisodeId)] =
                                new EpisodeTitles(series.Title, episode.Title, series.Path);
                        }
                    }
                }
            }

            return titles;
        }

        public string? MovieTitle(SpecialMoviePair pair) =>
            movies.TryGetValue((pair.RadarrInstanceId, pair.RadarrMovieId), out MovieTitles? found) ? found.Title : null;

        public string? SeriesTitle(SpecialMoviePair pair) =>
            episodes.TryGetValue((pair.SonarrInstanceId, pair.SonarrSeriesId, pair.SonarrEpisodeId), out EpisodeTitles? found)
                ? found.Series
                : null;

        public string? EpisodeTitle(SpecialMoviePair pair) =>
            episodes.TryGetValue((pair.SonarrInstanceId, pair.SonarrSeriesId, pair.SonarrEpisodeId), out EpisodeTitles? found)
                ? found.Episode
                : null;

        public string? SeriesPath(SpecialMoviePair pair) =>
            episodes.TryGetValue((pair.SonarrInstanceId, pair.SonarrSeriesId, pair.SonarrEpisodeId), out EpisodeTitles? found)
                ? found.SeriesPath
                : null;

        /// <summary>
        /// The source file only while the snapshot still reports the very file the row was made for.
        /// After an upgrade the path of the new file would be a lie about what the suggestion offers.
        /// </summary>
        public string? SourcePath(SpecialMoviePair pair, int? fileId) =>
            fileId is not null
            && movies.TryGetValue((pair.RadarrInstanceId, pair.RadarrMovieId), out MovieTitles? movie)
            && movie.FileId == fileId
                ? movie.Path
                : null;

        private sealed record MovieTitles(string Title, int? FileId, string? Path);

        private sealed record EpisodeTitles(string Series, string Episode, string? SeriesPath);
    }
}
