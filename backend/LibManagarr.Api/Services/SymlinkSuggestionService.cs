using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>
/// What one pass did. The counts are what the log and the answer of the „refresh suggestions" action
/// need; the pairs themselves are read back from the store by the caller.
/// </summary>
public sealed record SymlinkMatchingRun(
    int InstancesConsidered,
    int Created,
    int Updated,
    int MarkedStale,
    int SkippedRejected)
{
    /// <summary>A pass that changed nothing is the normal case, not an error, and says so quietly.</summary>
    public bool DidWork => Created + Updated + MarkedStale > 0;
}

/// <summary>
/// Turns the snapshots into durable suggestions. The matcher decides whether a pair is defensible;
/// this service decides what the register has to remember about it: that a pair is reported once, that
/// a decline survives the next sync, that only a movie which has a file can be offered, and that a pair
/// the snapshots no longer contain has to be said out loud instead of being dropped quietly.
/// </summary>
public sealed class SymlinkSuggestionService(
    ISettingsService settingsService,
    ISymlinkStore store,
    TimeProvider clock,
    ILogger<SymlinkSuggestionService> logger)
{
    /// <summary>
    /// Rows that still wait for a decision, and therefore may be rewritten by a later pass.
    /// </summary>
    private static readonly SymlinkSuggestionStatus[] ReportableStatuses =
    [
        SymlinkSuggestionStatus.Pending
    ];

    /// <summary>
    /// One pass at a time. Two passes at once would write the same pair twice, and the second one would
    /// lose against the unique index — so the background run and the manual action share this gate.
    /// </summary>
    private static readonly SemaphoreSlim PassLock = new(1, 1);

    public async Task<SymlinkMatchingRun> RefreshAsync(bool force, CancellationToken cancellationToken)
    {
        using IDisposable operation = await SymlinkOperationGate.EnterAsync(cancellationToken);
        return await RefreshCoreAsync(force, cancellationToken);
    }

    /// <summary>Called under the operation gate after Sonarr cleanup, before the deletion journal is removed.</summary>
    public Task<SymlinkMatchingRun> RefreshAfterDeletionAsync(string linkId, CancellationToken cancellationToken) =>
        RefreshCoreAsync(true, cancellationToken, linkId);

    private async Task<SymlinkMatchingRun> RefreshCoreAsync(bool force, CancellationToken cancellationToken, string? deletedLinkId = null)
    {
        await PassLock.WaitAsync(cancellationToken);
        try
        {
            return await RunAsync(force, cancellationToken, deletedLinkId);
        }
        finally
        {
            PassLock.Release();
        }
    }

    /// <summary>
    /// A decline belongs to the pair, not to the row, so neither a movie upgrade nor another sync can
    /// bring the question back. The row stays as the record that this pair was reported once.
    /// </summary>
    public async Task<bool> RejectAsync(string suggestionId, string? detail, CancellationToken cancellationToken)
    {
        SymlinkSuggestion? suggestion = await store.GetSuggestionAsync(suggestionId, cancellationToken);
        if (suggestion is null) return false;

        DateTimeOffset now = clock.GetUtcNow();
        await store.RejectAsync(suggestion.Pair, string.IsNullOrWhiteSpace(detail) ? null : detail.Trim(), now, cancellationToken);
        if (suggestion.Status == SymlinkSuggestionStatus.Rejected) return true;

        SymlinkSuggestion declined = Copy(suggestion);
        declined.Status = SymlinkSuggestionStatus.Rejected;
        declined.DecidedAt = now;
        declined.UpdatedAt = now;
        return await store.UpdateSuggestionAsync(declined, suggestion.Revision, cancellationToken);
    }

    /// <summary>
    /// Dropping the decline alone would not make the pair visible again, because its row was already
    /// answered. The forced pass re-reports it from today's snapshots, so what comes back carries
    /// today's reasons instead of the stale text the user just declined.
    /// </summary>
    public async Task<bool> ReactivateAsync(SpecialMoviePair pair, CancellationToken cancellationToken)
    {
        if (!await store.RemoveRejectionAsync(pair, cancellationToken)) return false;
        await RefreshAsync(force: true, cancellationToken);
        return true;
    }

    /// <summary>
    /// One pass over the current snapshots. It runs when something a pairing rests on moved — a
    /// snapshot, the matcher, or the path configuration — which is what the stored per-instance
    /// signature is for. <paramref name="force"/> is the „refresh suggestions" action, which re-reads
    /// even when nothing looks different, because the user just asked for it.
    /// </summary>
    private async Task<SymlinkMatchingRun> RunAsync(bool force, CancellationToken cancellationToken, string? deletedLinkId)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        if (!settings.Symlinks.Enabled) return new SymlinkMatchingRun(0, 0, 0, 0, 0);
        List<InstanceSignature> signatures = CollectSignatures(settings);
        Dictionary<string, SymlinkMatchingState> storedStates = [];
        foreach (SymlinkMatchingState state in await store.GetMatchingStatesAsync(cancellationToken))
        {
            storedStates[state.InstanceId] = state;
        }

        if (!force && signatures.All(instance => IsCurrent(storedStates, instance, settings.Symlinks.Revision)))
        {
            logger.LogDebug("Symlink matching found no changed data in {InstanceCount} instances.", signatures.Count);
            return new SymlinkMatchingRun(signatures.Count, 0, 0, 0, 0);
        }

        List<Candidate> candidates = CollectCandidates(settings);
        HashSet<SpecialMoviePair> managed = (await store.GetLinksAsync(cancellationToken))
            .Where(link => link.Id != deletedLinkId || link.OperationStatus != SymlinkOperationStatus.CleanupPending)
            .Select(link => link.Pair)
            .ToHashSet();
        Dictionary<SpecialMoviePair, List<SymlinkSuggestion>> history = [];
        foreach (SymlinkSuggestion suggestion in await store.GetSuggestionsAsync(cancellationToken))
        {
            if (!history.TryGetValue(suggestion.Pair, out List<SymlinkSuggestion>? rows))
            {
                rows = [];
                history[suggestion.Pair] = rows;
            }

            rows.Add(suggestion);
        }

        DateTimeOffset now = clock.GetUtcNow();
        Dictionary<SpecialMoviePair, SpecialMovieMatch> matched = [];
        Dictionary<SpecialMoviePair, List<SymlinkSuggestionAlternative>> alternatives = GroupAlternatives(candidates);
        int created = 0;
        int updated = 0;
        int skippedRejected = 0;

        foreach (Candidate candidate in candidates)
        {
            SpecialMovieMatch match = candidate.Match;
            matched[match.Pair] = match;
            List<SymlinkSuggestion> rows = history.TryGetValue(match.Pair, out List<SymlinkSuggestion>? known) ? known : [];
            // Historic confirmations cannot reserve a pairing after its managed link was deleted.
            if (managed.Contains(match.Pair)) continue;
            if (await store.IsRejectedAsync(match.Pair, cancellationToken))
            {
                skippedRejected += 1;
                continue;
            }

            SymlinkSuggestion? previous = rows.FirstOrDefault(row => ReportableStatuses.Contains(row.Status));
            SymlinkSuggestion suggestion = new()
            {
                Id = previous?.Id ?? UuidV7.Create(),
                MatcherVersion = SpecialMovieMatchingRules.MatcherVersion.ToString(CultureInfo.InvariantCulture),
                Status = SymlinkSuggestionStatus.Pending,
                Pair = match.Pair,
                RadarrFileId = match.RadarrFileId,
                SeasonNumber = match.SeasonNumber,
                EpisodeNumber = match.EpisodeNumber,
                Reasons = [.. match.Reasons],
                Alternatives = alternatives.TryGetValue(match.Pair, out List<SymlinkSuggestionAlternative>? rivals) ? rivals : [],
                ConfigRevision = settings.Symlinks.Revision,
                LinkId = previous?.LinkId,
                CreatedAt = previous?.CreatedAt ?? now,
                UpdatedAt = now,
                DecidedAt = previous?.DecidedAt
            };
            if (candidate.Episode.HasFile)
            {
                suggestion.Reasons.Add(new SymlinkSuggestionReason(
                    SpecialMovieMatchingRules.EpisodeHasFileCode,
                    "Sonarr reports an existing episode file. It is inspected when offers are loaded; no existing file or link is automatically adopted or overwritten."));
            }

            await store.UpsertSuggestionAsync(suggestion, cancellationToken);
            if (previous is null)
            {
                created += 1;
            }
            else
            {
                updated += 1;
            }
        }

        int markedStale = await MarkMissingPairsStaleAsync(history.Values.SelectMany(rows => rows), matched, now, cancellationToken);

        foreach (InstanceSignature instance in signatures)
        {
            await store.SaveMatchingStateAsync(new SymlinkMatchingState
            {
                InstanceId = instance.InstanceId,
                Kind = instance.Kind,
                Signature = instance.Signature,
                MatcherVersion = SpecialMovieMatchingRules.MatcherVersion,
                ConfigRevision = settings.Symlinks.Revision,
                CheckedAt = now
            }, cancellationToken);
        }

        SymlinkMatchingRun run = new(signatures.Count, created, updated, markedStale, skippedRejected);
        logger.LogInformation(
            "Symlink matching ran over {InstanceCount} instances: {Created} new, {Updated} updated, {Stale} stale, {Rejected} declined.",
            run.InstancesConsidered, run.Created, run.Updated, run.MarkedStale, run.SkippedRejected);
        return run;
    }

    /// <summary>
    /// Specials of every Sonarr instance against movies of every Radarr instance. Both directions the
    /// plan asks for fall out of this one loop: a new series brings specials nobody paired before, and
    /// a new movie meets the specials that are already sitting there. A movie without a file is not a
    /// candidate — nothing can be linked yet — but its download changes the instance signature, so a
    /// later pass reports the pair once the file is there.
    /// </summary>
    private static List<Candidate> CollectCandidates(AppSettings settings)
    {
        List<Candidate> candidates = [];
        foreach (SonarrInstanceSnapshot sonarr in settings.SonarrSnapshots)
        {
            foreach (SonarrSeriesSnapshot series in sonarr.Series)
            {
                foreach (SonarrSeasonSnapshot season in series.Seasons.Where(season => season.SeasonNumber == 0))
                {
                    foreach (SonarrEpisodeSnapshot episode in season.Episodes)
                    {
                        foreach (RadarrInstanceSnapshot radarr in settings.RadarrSnapshots)
                        {
                            foreach (RadarrMovieSnapshot movie in radarr.Movies)
                            {
                                SpecialMovieMatch? match = SpecialMovieMatchingRules.Match(
                                    sonarr.InstanceId, series, episode, season.SeasonNumber, radarr.InstanceId, movie);
                                if (match is { MovieHasFile: true }) candidates.Add(new Candidate(match, episode));
                            }
                        }
                    }
                }
            }
        }

        return candidates;
    }

    /// <summary>
    /// Every other movie that claims the same special is a rival of each pair of that special, so the
    /// interface can show the choice instead of a winner the matcher picked on its own.
    /// </summary>
    private static Dictionary<SpecialMoviePair, List<SymlinkSuggestionAlternative>> GroupAlternatives(
        List<Candidate> candidates)
    {
        Dictionary<SpecialMoviePair, List<SymlinkSuggestionAlternative>> rivals = [];
        foreach (IGrouping<SpecialMovieEpisode, Candidate> group in candidates
                     .GroupBy(candidate => new SpecialMovieEpisode(
                         candidate.Match.Pair.SonarrInstanceId,
                         candidate.Match.Pair.SonarrSeriesId,
                         candidate.Match.Pair.SonarrEpisodeId))
                     .Where(group => group.Count() > 1))
        {
            foreach (SpecialMoviePair pair in group.Select(candidate => candidate.Match.Pair))
            {
                rivals[pair] = [.. group
                    .Where(other => other.Match.Pair != pair)
                    .Select(other => new SymlinkSuggestionAlternative(
                        other.Match.Pair.RadarrInstanceId,
                        other.Match.Pair.RadarrMovieId,
                        other.Match.RadarrFileId,
                        "Another movie matches this special too and is already on disk, so one of them has to be chosen."))];
            }
        }

        return rivals;
    }

    /// <summary>
    /// A pair that vanished from the snapshots keeps its row and gets marked, because "the movie is
    /// gone" and "nobody looked yet" have to stay apart in the register.
    /// </summary>
    private async Task<int> MarkMissingPairsStaleAsync(
        IEnumerable<SymlinkSuggestion> existing,
        Dictionary<SpecialMoviePair, SpecialMovieMatch> matched,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        int marked = 0;
        foreach (SymlinkSuggestion suggestion in existing.Where(row => ReportableStatuses.Contains(row.Status)))
        {
            if (matched.ContainsKey(suggestion.Pair)) continue;
            SymlinkSuggestion stale = Copy(suggestion);
            stale.Status = SymlinkSuggestionStatus.Stale;
            stale.Detail = "The current snapshots no longer contain this pair.";
            stale.UpdatedAt = at;
            if (await store.UpdateSuggestionAsync(stale, suggestion.Revision, cancellationToken)) marked += 1;
        }

        return marked;
    }

    private static SymlinkSuggestion Copy(SymlinkSuggestion source) => new()
    {
        Id = source.Id,
        MatcherVersion = source.MatcherVersion,
        Status = source.Status,
        Pair = source.Pair,
        RadarrFileId = source.RadarrFileId,
        SeasonNumber = source.SeasonNumber,
        EpisodeNumber = source.EpisodeNumber,
        Reasons = [.. source.Reasons],
        Alternatives = [.. source.Alternatives],
        Detail = source.Detail,
        ConfigRevision = source.ConfigRevision,
        LinkId = source.LinkId,
        Revision = source.Revision,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt,
        DecidedAt = source.DecidedAt
    };

    private static List<InstanceSignature> CollectSignatures(AppSettings settings)
    {
        List<InstanceSignature> signatures = [];
        foreach (SonarrInstanceSnapshot snapshot in settings.SonarrSnapshots)
        {
            signatures.Add(new InstanceSignature(snapshot.InstanceId, ArrApplicationKind.Sonarr, SonarrSignature(snapshot)));
        }

        foreach (RadarrInstanceSnapshot snapshot in settings.RadarrSnapshots)
        {
            signatures.Add(new InstanceSignature(snapshot.InstanceId, ArrApplicationKind.Radarr, RadarrSignature(snapshot)));
        }

        return signatures;
    }

    private static bool IsCurrent(
        Dictionary<string, SymlinkMatchingState> stored, InstanceSignature instance, int configRevision) =>
        stored.TryGetValue(instance.InstanceId, out SymlinkMatchingState? state)
        && state.Signature == instance.Signature
        && state.MatcherVersion == SpecialMovieMatchingRules.MatcherVersion
        && state.ConfigRevision == configRevision;

    /// <summary>
    /// The season 0 specials and the identity of the series — everything a pairing decision rests on.
    /// The regular seasons are deliberately left out, so an ordinary episode upgrade in Sonarr does not
    /// ask the matcher to look at the specials again.
    /// </summary>
    private static string SonarrSignature(SonarrInstanceSnapshot snapshot)
    {
        StringBuilder builder = new();
        foreach (SonarrSeriesSnapshot series in snapshot.Series.OrderBy(series => series.SeriesId))
        {
            builder.Append('S').Append(series.SeriesId).Append(':').Append(series.Title).Append('\n');
            foreach (SonarrEpisodeSnapshot episode in series.Seasons
                         .Where(season => season.SeasonNumber == 0)
                         .SelectMany(season => season.Episodes)
                         .OrderBy(episode => episode.EpisodeId))
            {
                builder.Append('E').Append(episode.EpisodeId).Append(':').Append(episode.Title)
                    .Append(':').Append(episode.AirDateUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "-")
                    .Append(':').Append(episode.HasFile ? 1 : 0)
                    .Append(':').Append(FileIdentity(episode.File))
                    .Append('\n');
            }
        }

        return Hash(builder);
    }

    private static string RadarrSignature(RadarrInstanceSnapshot snapshot)
    {
        StringBuilder builder = new();
        foreach (RadarrMovieSnapshot movie in snapshot.Movies.OrderBy(movie => movie.MovieId))
        {
            builder.Append('M').Append(movie.MovieId).Append(':').Append(movie.Title)
                .Append(':').Append(string.Join('|', movie.AlternativeTitles))
                .Append(':').Append(movie.AirDateUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "-")
                .Append(':').Append(movie.HasFile ? 1 : 0)
                .Append(':').Append(FileIdentity(movie.File))
                .Append('\n');
        }

        return Hash(builder);
    }

    /// <summary>
    /// The file facts a suggestion would act on. A Radarr upgrade changes the id even when the path
    /// stays the same, and the size is in here because the same path with different bytes is a
    /// different source.
    /// </summary>
    private static string FileIdentity(MediaFileSnapshot file) =>
        string.Join('|',
            file.Id?.ToString(CultureInfo.InvariantCulture) ?? "-",
            file.Path ?? "-",
            file.Size?.ToString(CultureInfo.InvariantCulture) ?? "-",
            file.Duration?.ToString() ?? "-",
            file.ReleaseGroup ?? "-",
            file.CreatedAt?.ToString("O", CultureInfo.InvariantCulture) ?? "-");

    private static string Hash(StringBuilder builder) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));

    private sealed record Candidate(SpecialMovieMatch Match, SonarrEpisodeSnapshot Episode);

    private sealed record SpecialMovieEpisode(string SonarrInstanceId, int SonarrSeriesId, int SonarrEpisodeId);

    private sealed record InstanceSignature(string InstanceId, ArrApplicationKind Kind, string Signature);
}
