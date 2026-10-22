namespace LibManagarr.Api.Services;

public static class LibraryUpgradeTarget
{
    public const string Below = "below";
    public const string Reached = "reached";

    public static string? Status(int? score, int? targetScore)
    {
        if (!score.HasValue || !targetScore.HasValue)
            return null;

        return score < targetScore.Value ? Below : Reached;
    }
}
