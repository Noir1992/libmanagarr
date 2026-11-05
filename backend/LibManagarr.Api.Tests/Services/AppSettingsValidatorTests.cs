using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class AppSettingsValidatorTests
{
    [Fact]
    public void Validate_ShouldAllowMissingIds_BecauseNormalizerCreatesThem()
    {
        AppSettings settings = new()
        {
            SonarrInstances =
            [
                new ArrInstanceSettings
                {
                    Name = "Sonarr",
                    BaseUrl = "http://localhost:8989",
                    ApiKey = "secret"
                }
            ]
        };

        Dictionary<string, string[]> validationErrors = AppSettingsValidator.Validate(settings);

        Assert.Empty(validationErrors);
    }

    [Fact]
    public void Validate_ShouldRejectDuplicateIds_WhenIdsAreProvided()
    {
        string duplicateId = UuidV7.Create();
        AppSettings settings = new()
        {
            SonarrInstances =
            [
                new ArrInstanceSettings
                {
                    Id = duplicateId,
                    Name = "Sonarr Main",
                    BaseUrl = "http://localhost:8989",
                    ApiKey = "secret"
                },
                new ArrInstanceSettings
                {
                    Id = duplicateId.ToUpperInvariant(),
                    Name = "Sonarr Backup",
                    BaseUrl = "http://localhost:8990",
                    ApiKey = "secret"
                }
            ]
        };

        Dictionary<string, string[]> validationErrors = AppSettingsValidator.Validate(settings);

        Assert.Single(validationErrors);
        Assert.True(validationErrors.ContainsKey("sonarrInstances[1].id"));
        Assert.Contains("The id must be unique.", validationErrors["sonarrInstances[1].id"]);
    }

    [Fact]
    public void Validate_ShouldRejectDuplicateNames_IgnoringWhitespaceAndCase()
    {
        AppSettings settings = new()
        {
            RadarrInstances =
            [
                new ArrInstanceSettings
                {
                    Name = " Radarr Main ",
                    BaseUrl = "http://localhost:7878",
                    ApiKey = "secret"
                },
                new ArrInstanceSettings
                {
                    Name = "radarr main",
                    BaseUrl = "http://localhost:7879",
                    ApiKey = "secret"
                }
            ]
        };

        Dictionary<string, string[]> validationErrors = AppSettingsValidator.Validate(settings);

        Assert.Single(validationErrors);
        Assert.Contains("The name must be unique.", validationErrors["radarrInstances[1].name"]);
    }

    [Fact]
    public void Validate_ShouldRejectInstanceFieldsExceedingMaxLength()
    {
        string longValue = new string('a', 256);
        AppSettings settings = new()
        {
            SonarrInstances =
            [
                new ArrInstanceSettings
                {
                    Name = longValue,
                    BaseUrl = $"http://localhost/{longValue}",
                    ApiKey = longValue
                }
            ]
        };

        Dictionary<string, string[]> validationErrors = AppSettingsValidator.Validate(settings);

        Assert.Contains("sonarrInstances[0].name", validationErrors.Keys);
        Assert.Contains("sonarrInstances[0].baseUrl", validationErrors.Keys);
        Assert.Contains("sonarrInstances[0].apiKey", validationErrors.Keys);
    }

    [Fact]
    public void ValidateInstanceAtIndex_ShouldOnlyRejectTheTargetInstance()
    {
        AppSettings settings = new()
        {
            SonarrInstances =
            [
                new ArrInstanceSettings
                {
                    Name = "Sonarr One",
                    BaseUrl = "http://localhost:8989",
                    ApiKey = "secret"
                },
                new ArrInstanceSettings
                {
                    Name = "",
                    BaseUrl = "http://localhost:8990",
                    ApiKey = "secret"
                }
            ]
        };

        Dictionary<string, string[]> validationErrors =
            AppSettingsValidator.ValidateInstanceAtIndex(settings.SonarrInstances, 0, "sonarrInstances");

        Assert.Empty(validationErrors);
    }

    [Fact]
    public void ValidateGeneral_ShouldOnlyRejectGeneralSettings()
    {
        AppSettings settings = new()
        {
            Language = (AppLanguage)999,
            Theme = (ThemeMode)999,
            SonarrInstances =
            [
                new ArrInstanceSettings
                {
                    Name = "Sonarr",
                    BaseUrl = "http://localhost:8989",
                    ApiKey = "secret"
                }
            ]
        };

        Dictionary<string, string[]> validationErrors = AppSettingsValidator.ValidateGeneral(settings);

        Assert.Equal(2, validationErrors.Count);
        Assert.Contains("language", validationErrors.Keys);
        Assert.Contains("theme", validationErrors.Keys);
    }

    [Theory]
    [InlineData(0, 5, "authProtection.requestsPerMinute")]
    [InlineData(1001, 5, "authProtection.requestsPerMinute")]
    [InlineData(10, 0, "authProtection.maxFailedAttempts")]
    [InlineData(10, 101, "authProtection.maxFailedAttempts")]
    public void ValidateGeneral_ShouldRejectEachAuthProtectionLimitOutsideItsRange(
        int requestsPerMinute,
        int maxFailedAttempts,
        string expectedKey)
    {
        AppSettings settings = new()
        {
            AuthProtection = new AuthProtectionSettings
            {
                RequestsPerMinute = requestsPerMinute,
                MaxFailedAttempts = maxFailedAttempts
            }
        };

        Dictionary<string, string[]> validationErrors = AppSettingsValidator.ValidateGeneral(settings);

        Assert.Single(validationErrors);
        Assert.Contains(expectedKey, validationErrors.Keys);
    }

    [Fact]
    public void ValidateGeneral_ShouldAllowTheOutermostAuthProtectionLimits()
    {
        AppSettings settings = new()
        {
            AuthProtection = new AuthProtectionSettings
            {
                RequestsPerMinute = 1000,
                MaxFailedAttempts = 100
            }
        };

        Dictionary<string, string[]> validationErrors = AppSettingsValidator.ValidateGeneral(settings);

        Assert.Empty(validationErrors);
    }

    [Fact]
    public void ValidateConnectionRequest_ShouldRejectUnsupportedApplicationKind()
    {
        TestConnectionRequest request = new()
        {
            Kind = (ArrApplicationKind)999,
            Instance = new ArrInstanceSettings
            {
                Name = "Sonarr",
                BaseUrl = "http://localhost:8989",
                ApiKey = "secret"
            }
        };

        Dictionary<string, string[]> validationErrors = AppSettingsValidator.ValidateConnectionRequest(request);

        Assert.True(validationErrors.ContainsKey("kind"));
    }
}
