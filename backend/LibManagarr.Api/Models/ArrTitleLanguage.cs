namespace LibManagarr.Api.Models;

public sealed class ArrTitleLanguage
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class ArrTitleTranslation
{
    public ArrTitleLanguage? Language { get; set; }
    public string? Title { get; set; }
}
