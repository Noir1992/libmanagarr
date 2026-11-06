using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

/// <summary>
/// In-memory stand-in for the SQLite register, keeping the two rules the database enforces with unique
/// indexes: one active suggestion per pair and one decline per pair. Enough for the services above the
/// store; the database rules themselves are proven in <c>SqliteSymlinkStoreTests</c>.
/// </summary>
internal sealed class InMemorySymlinkStore : ISymlinkStore
{
    private static readonly SymlinkSuggestionStatus[] ActiveStatuses =
    [
        SymlinkSuggestionStatus.Pending,
        SymlinkSuggestionStatus.WaitingForFile,
        SymlinkSuggestionStatus.Creating
    ];

    private readonly Dictionary<string, SymlinkCapabilityCheck> checks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SymlinkLink> links = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SymlinkSuggestion> suggestions = [];
    private readonly Dictionary<SpecialMoviePair, SymlinkRejection> rejections = [];
    private readonly Dictionary<string, SymlinkMatchingState> matchingStates = new(StringComparer.OrdinalIgnoreCase);

    public int Initializes { get; private set; }

    /// <summary>
    /// When set, <see cref="UpsertSuggestionAsync"/> fails with it, so a test can stop a matching pass
    /// halfway and look at what the interrupted run left behind.
    /// </summary>
    public Exception? UpsertThrows { get; set; }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        Initializes += 1;
        return Task.CompletedTask;
    }

    public Task<SymlinkCapabilityCheck?> GetCapabilityCheckAsync(string storageAreaId, CancellationToken cancellationToken) =>
        Task.FromResult(checks.TryGetValue(storageAreaId, out SymlinkCapabilityCheck? check) ? check : null);

    public Task<IReadOnlyList<SymlinkCapabilityCheck>> GetCapabilityChecksAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SymlinkCapabilityCheck>>(checks.Values.OrderBy(check => check.StorageAreaId, StringComparer.OrdinalIgnoreCase).ToList());

    public Task SaveCapabilityCheckAsync(SymlinkCapabilityCheck check, CancellationToken cancellationToken)
    {
        checks[check.StorageAreaId] = check;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SymlinkLink>> GetLinksAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SymlinkLink>>(links.Values.Select(Copy).OrderBy(link => link.Id, StringComparer.OrdinalIgnoreCase).ToList());

    public Task<SymlinkLink?> GetLinkAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(links.TryGetValue(id, out SymlinkLink? link) ? Copy(link) : null);

    public Task InsertLinkAsync(SymlinkLink link, CancellationToken cancellationToken)
    {
        links[link.Id] = Copy(link);
        return Task.CompletedTask;
    }

    public Task<bool> UpdateLinkAsync(SymlinkLink link, long expectedRevision, CancellationToken cancellationToken)
    {
        if (!links.TryGetValue(link.Id, out SymlinkLink? stored) || stored.Revision != expectedRevision) return Task.FromResult(false);
        SymlinkLink copy = Copy(link);
        copy.Revision = stored.Revision + 1;
        links[link.Id] = copy;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteLinkAsync(string id, long expectedRevision, CancellationToken cancellationToken)
    {
        if (!links.TryGetValue(id, out SymlinkLink? stored) || stored.Revision != expectedRevision) return Task.FromResult(false);
        links.Remove(id);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<SymlinkSuggestion>> GetSuggestionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SymlinkSuggestion>>(suggestions.Select(Copy).ToList());

    public Task<SymlinkSuggestion?> GetSuggestionAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(suggestions.FirstOrDefault(suggestion => suggestion.Id == id) is SymlinkSuggestion found ? Copy(found) : null);

    public Task UpsertSuggestionAsync(SymlinkSuggestion suggestion, CancellationToken cancellationToken)
    {
        if (UpsertThrows is not null) throw UpsertThrows;
        SymlinkSuggestion? active = suggestions.FirstOrDefault(existing =>
            existing.Pair == suggestion.Pair && ActiveStatuses.Contains(existing.Status));
        if (active is null)
        {
            suggestions.Add(Copy(suggestion));
            return Task.CompletedTask;
        }

        SymlinkSuggestion updated = Copy(suggestion);
        updated.Id = active.Id;
        updated.CreatedAt = active.CreatedAt;
        updated.Revision = active.Revision + 1;
        suggestions[suggestions.IndexOf(active)] = updated;
        suggestion.Id = active.Id;
        return Task.CompletedTask;
    }

    public Task<bool> UpdateSuggestionAsync(SymlinkSuggestion suggestion, long expectedRevision, CancellationToken cancellationToken)
    {
        int index = suggestions.FindIndex(existing => existing.Id == suggestion.Id && existing.Revision == expectedRevision);
        if (index < 0) return Task.FromResult(false);
        SymlinkSuggestion updated = Copy(suggestion);
        updated.Revision = suggestions[index].Revision + 1;
        suggestions[index] = updated;
        return Task.FromResult(true);
    }

    public Task<SymlinkSuggestion?> FindActiveSuggestionAsync(SpecialMoviePair pair, CancellationToken cancellationToken) =>
        Task.FromResult(suggestions.FirstOrDefault(suggestion => suggestion.Pair == pair && ActiveStatuses.Contains(suggestion.Status)) is SymlinkSuggestion found
            ? Copy(found)
            : null);

    public Task<IReadOnlyList<SymlinkRejection>> GetRejectionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SymlinkRejection>>(rejections.Values.ToList());

    public Task<bool> IsRejectedAsync(SpecialMoviePair pair, CancellationToken cancellationToken) =>
        Task.FromResult(rejections.ContainsKey(pair));

    public Task RejectAsync(SpecialMoviePair pair, string? detail, DateTimeOffset at, CancellationToken cancellationToken)
    {
        rejections.TryAdd(pair, new SymlinkRejection { Pair = pair, Detail = detail, CreatedAt = at });
        return Task.CompletedTask;
    }

    public Task<bool> RemoveRejectionAsync(SpecialMoviePair pair, CancellationToken cancellationToken) =>
        Task.FromResult(rejections.Remove(pair));

    public Task<SymlinkMatchingState?> GetMatchingStateAsync(string instanceId, CancellationToken cancellationToken) =>
        Task.FromResult(matchingStates.TryGetValue(instanceId, out SymlinkMatchingState? state) ? Copy(state) : null);

    public Task<IReadOnlyList<SymlinkMatchingState>> GetMatchingStatesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SymlinkMatchingState>>(matchingStates.Values.Select(Copy).OrderBy(state => state.InstanceId, StringComparer.OrdinalIgnoreCase).ToList());

    public Task SaveMatchingStateAsync(SymlinkMatchingState state, CancellationToken cancellationToken)
    {
        matchingStates[state.InstanceId] = Copy(state);
        return Task.CompletedTask;
    }

    private static SymlinkMatchingState Copy(SymlinkMatchingState source) => new()
    {
        InstanceId = source.InstanceId,
        Kind = source.Kind,
        Signature = source.Signature,
        MatcherVersion = source.MatcherVersion,
        ConfigRevision = source.ConfigRevision,
        CheckedAt = source.CheckedAt
    };

    private static SymlinkLink Copy(SymlinkLink source) => new()
    {
        Id = source.Id,
        Revision = source.Revision,
        RadarrInstanceId = source.RadarrInstanceId,
        RadarrMovieId = source.RadarrMovieId,
        RadarrFileId = source.RadarrFileId,
        SonarrInstanceId = source.SonarrInstanceId,
        SonarrSeriesId = source.SonarrSeriesId,
        SonarrEpisodeId = source.SonarrEpisodeId,
        SeasonNumber = source.SeasonNumber,
        EpisodeNumber = source.EpisodeNumber,
        StorageAreaId = source.StorageAreaId,
        LinkPath = source.LinkPath,
        LinkText = source.LinkText,
        SourcePath = source.SourcePath,
        OperationStatus = source.OperationStatus,
        SetupStep = source.SetupStep,
        SuggestionId = source.SuggestionId,
        SonarrFileId = source.SonarrFileId,
        SonarrScanCommandId = source.SonarrScanCommandId,
        SonarrRenameCommandId = source.SonarrRenameCommandId,
        MonitoringOriginal = source.MonitoringOriginal,
        MonitoringDisabled = source.MonitoringDisabled,
        AutoFollowUp = source.AutoFollowUp,
        DesiredFilename = source.DesiredFilename,
        AppliedFilename = source.AppliedFilename,
        NamingRevision = source.NamingRevision,
        SourceVersion = source.SourceVersion,
        UpdateRevision = source.UpdateRevision,
        ExpectedMetadata = source.ExpectedMetadata,
        ConfirmedMetadata = source.ConfirmedMetadata,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt,
        LastCheckedAt = source.LastCheckedAt,
        LastSuccessfulCheckAt = source.LastSuccessfulCheckAt,
        Findings = [.. source.Findings]
    };

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
}
