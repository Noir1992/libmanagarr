namespace LibManagarr.Api.Models;

public sealed class SonarrQualityProfileResource
{
    public int? Id { get; set; }

    public string? Name { get; set; }

    public int? UpgradeUntilCustomFormatScore { get; set; }

    public int? UpgradeUntilScore { get; set; }

    public int? CutoffFormatScore { get; set; }

    public int? FormatCutoff { get; set; }
}
