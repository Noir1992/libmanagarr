namespace LibManagarr.Api.Models;

public sealed class TitleSortingSettings
{
    public bool Enabled { get; set; }
    public List<string> IgnoredLeadingWords { get; set; } = [];
}
