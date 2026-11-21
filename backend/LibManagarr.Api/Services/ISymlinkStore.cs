using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>
/// Durable state of the symlink feature: the register of managed links, the detected pairs with their
/// decisions, and the probe results. All of it is state about the file system, never the file system
/// itself — nothing here creates, moves or deletes a link.
/// </summary>
public interface ISymlinkStore
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<SymlinkCapabilityCheck?> GetCapabilityCheckAsync(string storageAreaId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SymlinkCapabilityCheck>> GetCapabilityChecksAsync(CancellationToken cancellationToken);

    Task SaveCapabilityCheckAsync(SymlinkCapabilityCheck check, CancellationToken cancellationToken);

    Task<IReadOnlyList<SymlinkLink>> GetLinksAsync(CancellationToken cancellationToken);

    async Task<IReadOnlyList<SymlinkLink>> GetEpisodeLinksAsync(SpecialMoviePair episode, CancellationToken cancellationToken) =>
        (await GetLinksAsync(cancellationToken)).Where(link => link.SonarrInstanceId == episode.SonarrInstanceId
            && link.SonarrSeriesId == episode.SonarrSeriesId && link.SonarrEpisodeId == episode.SonarrEpisodeId).ToArray();

    Task<SymlinkLink?> GetLinkAsync(string id, CancellationToken cancellationToken);

    Task InsertLinkAsync(SymlinkLink link, CancellationToken cancellationToken);

    /// <summary>False when another writer changed the row first; the caller then re-reads instead of guessing.</summary>
    Task<bool> UpdateLinkAsync(SymlinkLink link, long expectedRevision, CancellationToken cancellationToken);

    Task<bool> DeleteLinkAsync(string id, long expectedRevision, CancellationToken cancellationToken);

    /// <summary>Atomically removes a confirmed deletion and queues its library refresh.</summary>
    Task<bool> CompleteDeletionAsync(SymlinkLink link, CancellationToken cancellationToken);

    Task<IReadOnlyList<SymlinkDeletionRefresh>> GetDeletionRefreshesAsync(CancellationToken cancellationToken);

    Task RemoveDeletionRefreshAsync(string linkId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SymlinkSuggestion>> GetSuggestionsAsync(CancellationToken cancellationToken);

    async Task<IReadOnlyList<SymlinkSuggestion>> GetEpisodeSuggestionsAsync(SpecialMoviePair episode, CancellationToken cancellationToken) =>
        (await GetSuggestionsAsync(cancellationToken)).Where(row => row.Pair.SonarrInstanceId == episode.SonarrInstanceId
            && row.Pair.SonarrSeriesId == episode.SonarrSeriesId && row.Pair.SonarrEpisodeId == episode.SonarrEpisodeId).ToArray();

    Task<SymlinkSuggestion?> GetSuggestionAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// One active suggestion per pair at most. An existing pair in an active state is updated in place,
    /// so a repeated sync neither duplicates the row nor forgets that the pair was already reported.
    /// </summary>
    Task UpsertSuggestionAsync(SymlinkSuggestion suggestion, CancellationToken cancellationToken);

    Task<bool> UpdateSuggestionAsync(SymlinkSuggestion suggestion, long expectedRevision, CancellationToken cancellationToken);

    Task<SymlinkSuggestion?> FindActiveSuggestionAsync(SpecialMoviePair pair, CancellationToken cancellationToken);

    Task<IReadOnlyList<SymlinkRejection>> GetRejectionsAsync(CancellationToken cancellationToken);

    Task<bool> IsRejectedAsync(SpecialMoviePair pair, CancellationToken cancellationToken);

    /// <summary>Idempotent: declining a pair that is already declined keeps the original entry and its date.</summary>
    Task RejectAsync(SpecialMoviePair pair, string? detail, DateTimeOffset at, CancellationToken cancellationToken);

    Task<bool> RemoveRejectionAsync(SpecialMoviePair pair, CancellationToken cancellationToken);

    Task<SymlinkMatchingState?> GetMatchingStateAsync(string instanceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SymlinkMatchingState>> GetMatchingStatesAsync(CancellationToken cancellationToken);

    /// <summary>Written only after a pass finished, so an aborted pass stays overdue instead of looking done.</summary>
    Task SaveMatchingStateAsync(SymlinkMatchingState state, CancellationToken cancellationToken);
}
