using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Tests.Clients;

public sealed class MediaFileLanguageHelperTests
{
    [Theory]
    [InlineData("jap", "Japanese")]
    [InlineData("jp", "Japanese")]
    [InlineData("ja", "Japanese")]
    [InlineData("jpn", "Japanese")]
    [InlineData("hin", "Hindi")]
    [InlineData("hi", "Hindi")]
    [InlineData("tha", "Thai")]
    [InlineData("hu", "Hungarian")]
    [InlineData("hun", "Hungarian")]
    [InlineData("rum", "Romanian")]
    [InlineData("ron", "Romanian")]
    [InlineData("slo", "Slovak")]
    [InlineData("slk", "Slovak")]
    [InlineData("per", "Persian")]
    [InlineData("fas", "Persian")]
    [InlineData("may", "Malay")]
    [InlineData("msa", "Malay")]
    [InlineData("alb", "Albanian")]
    [InlineData("sqi", "Albanian")]
    [InlineData("wel", "Welsh")]
    [InlineData("cym", "Welsh")]
    [InlineData("ice", "Icelandic")]
    [InlineData("isl", "Icelandic")]
    [InlineData("vie", "Vietnamese")]
    [InlineData("ind", "Indonesian")]
    public void CollectLanguageNames_ShouldMergeCodesAndNamesAcrossBothInputFormats(string code, string name)
    {
        Assert.Equal([name], MediaFileLanguageHelper.CollectLanguageNames($" {code.ToUpperInvariant()} / {name} ", code));
        Assert.Equal([name], MediaFileLanguageHelper.CollectLanguageNames(
            new List<LanguageResource> { new() { Name = code }, new() { Name = name } }));
    }

    [Fact]
    public void CollectLanguageNames_ShouldKeepUnrecognizedLabelsAndUnknownValues()
    {
        Assert.Equal(["Custom language", "Unknown"], MediaFileLanguageHelper.CollectLanguageNames("Custom language", "N/A", "unknown"));
    }

    [Fact]
    public void CollectLanguageNames_ShouldUseFallbackWhenPrimaryLanguagesAreMissing()
    {
        List<string> languages = MediaFileLanguageHelper.CollectLanguageNames(
            [],
            [
                new LanguageResource { Name = "English" },
                new LanguageResource { Name = "German" }
            ]);

        Assert.Equal(["English", "German"], languages);
    }

    [Fact]
    public void CollectLanguageNames_ShouldDeduplicateAndTrimNamesAcrossSources()
    {
        List<string> languages = MediaFileLanguageHelper.CollectLanguageNames(
            [
                new LanguageResource { Name = " English " },
                new LanguageResource { Name = "unknown" }
            ],
            [
                new LanguageResource { Name = "english" },
                new LanguageResource { Name = null }
            ]);

        Assert.Equal(["English", "Unknown"], languages);
    }

    [Fact]
    public void CollectLanguageNames_ShouldMergeCommonLanguageAliases()
    {
        List<string> languages = MediaFileLanguageHelper.CollectLanguageNames("ger", "German", "deu", "ENG", "english");

        Assert.Equal(["German", "English"], languages);
    }
}
