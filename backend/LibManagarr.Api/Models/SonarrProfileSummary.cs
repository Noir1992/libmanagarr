namespace LibManagarr.Api.Models;

public sealed class SonarrProfileSummary
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public int? UpgradeUntilCustomFormatScore { get; init; }
}
