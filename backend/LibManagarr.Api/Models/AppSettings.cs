namespace LibManagarr.Api.Models;

public sealed class AppSettings
{
    public AppLanguage Language { get; set; } = AppLanguage.En;

    public ThemeMode Theme { get; set; } = ThemeMode.System;

    public List<ArrInstanceSettings> SonarrInstances { get; set; } = [];

    public List<ArrInstanceSettings> RadarrInstances { get; set; } = [];

    public SeerrInstanceSettings? Seerr { get; set; }

    public List<SonarrInstanceSnapshot> SonarrSnapshots { get; set; } = [];

    public List<RadarrInstanceSnapshot> RadarrSnapshots { get; set; } = [];

    public List<LibraryFilterValues> LibraryFilterValues { get; set; } = [];

    public TitleSortingSettings TitleSorting { get; set; } = new();

    public LoggingSettings Logging { get; set; } = new();

    public AuthProtectionSettings AuthProtection { get; set; } = new();
}
