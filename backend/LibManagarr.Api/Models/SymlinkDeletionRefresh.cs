namespace LibManagarr.Api.Models;

/// <summary>Library work left after the link and its Sonarr file association have been removed.</summary>
public sealed record SymlinkDeletionRefresh(string LinkId, SpecialMoviePair Pair, SymlinkLink? Link = null);
