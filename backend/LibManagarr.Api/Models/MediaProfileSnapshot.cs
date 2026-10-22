namespace LibManagarr.Api.Models;

public sealed class MediaProfileSnapshot
{
    public string Name { get; set; } = string.Empty;

    public int? UpgradeUntilCustomFormatScore { get; set; }
}
