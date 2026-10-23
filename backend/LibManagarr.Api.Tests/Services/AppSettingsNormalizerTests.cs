using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class AppSettingsNormalizerTests
{
    [Fact]
    public void Normalize_ShouldCreateUuidV7Id_WhenInstanceIdIsMissing()
    {
        AppSettings settings = new()
        {
            SonarrInstances =
            [
                new ArrInstanceSettings
                {
                    Name = " Sonarr ",
                    BaseUrl = " http://localhost:8989/ ",
                    ApiKey = " secret "
                }
            ]
        };

        AppSettings normalizedSettings = AppSettingsNormalizer.Normalize(settings);
        ArrInstanceSettings normalizedInstance = normalizedSettings.SonarrInstances.Single();

        Assert.True(UuidV7.IsUuidV7(normalizedInstance.Id));
        Assert.Equal("Sonarr", normalizedInstance.Name);
        Assert.Equal("http://localhost:8989", normalizedInstance.BaseUrl);
        Assert.Equal("secret", normalizedInstance.ApiKey);
    }

    [Fact]
    public void Normalize_ShouldReplaceNonUuidV7Id_WhenInstanceIdUsesAnotherFormat()
    {
        string oldUuidV4 = "11111111-1111-4111-8111-111111111111";
        AppSettings settings = new()
        {
            RadarrInstances =
            [
                new ArrInstanceSettings
                {
                    Id = oldUuidV4,
                    Name = "Radarr",
                    BaseUrl = "http://localhost:7878",
                    ApiKey = "secret"
                }
            ]
        };

        AppSettings normalizedSettings = AppSettingsNormalizer.Normalize(settings);
        ArrInstanceSettings normalizedInstance = normalizedSettings.RadarrInstances.Single();

        Assert.True(UuidV7.IsUuidV7(normalizedInstance.Id));
        Assert.NotEqual(oldUuidV4, normalizedInstance.Id);
    }

    [Fact]
    public void Normalize_ShouldKeepUuidV7Id_WhenInstanceIdIsAlreadyUuidV7()
    {
        string uuidV7 = UuidV7.Create();
        AppSettings settings = new()
        {
            SonarrInstances =
            [
                new ArrInstanceSettings
                {
                    Id = uuidV7,
                    Name = "Sonarr",
                    BaseUrl = "http://localhost:8989",
                    ApiKey = "secret"
                }
            ]
        };

        AppSettings normalizedSettings = AppSettingsNormalizer.Normalize(settings);

        Assert.Equal(uuidV7, normalizedSettings.SonarrInstances.Single().Id);
    }

    [Fact]
    public void Normalize_ShouldPreservePersistedSnapshots()
    {
        DateTimeOffset syncedAt = new(2026, 5, 7, 14, 0, 0, TimeSpan.Zero);
        AppSettings settings = new()
        {
            SonarrSnapshots =
            [
                new SonarrInstanceSnapshot
                {
                    InstanceId = "sonarr-main",
                    InstanceName = "Sonarr Main",
                    SyncedAt = syncedAt,
                    Series =
                    [
                        new SonarrSeriesSnapshot
                        {
                            SeriesId = 7,
                            Title = "Severance"
                        }
                    ]
                }
            ],
            RadarrSnapshots =
            [
                new RadarrInstanceSnapshot
                {
                    InstanceId = "radarr-main",
                    InstanceName = "Radarr Main",
                    SyncedAt = syncedAt,
                    Movies =
                    [
                        new RadarrMovieSnapshot
                        {
                            MovieId = 8,
                            Title = "Arrival"
                        }
                    ]
                }
            ]
        };

        AppSettings normalizedSettings = AppSettingsNormalizer.Normalize(settings);

        SonarrInstanceSnapshot sonarrSnapshot = Assert.Single(normalizedSettings.SonarrSnapshots);
        RadarrInstanceSnapshot radarrSnapshot = Assert.Single(normalizedSettings.RadarrSnapshots);

        Assert.Equal("sonarr-main", sonarrSnapshot.InstanceId);
        Assert.Equal("Severance", sonarrSnapshot.Series.Single().Title);
        Assert.Equal(syncedAt, sonarrSnapshot.SyncedAt);
        Assert.Equal("radarr-main", radarrSnapshot.InstanceId);
        Assert.Equal("Arrival", radarrSnapshot.Movies.Single().Title);
        Assert.Equal(syncedAt, radarrSnapshot.SyncedAt);
    }
}
