namespace LibManagarr.Api.Models;

public sealed class LanguageMediaFileItem
{
    public string Title { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string InstanceName { get; set; } = string.Empty;

    public string? TitleSlug { get; set; }

    public long? SizeBytes { get; set; }

    public string? RelativePath { get; set; }

    public string RootTitle { get; set; } = string.Empty;
}
