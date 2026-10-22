namespace LibManagarr.Api.Options;

public sealed class FrontendOptions
{
    public const string SectionName = "Frontend";

    public string[] AllowedOrigins { get; set; } = [];
}
