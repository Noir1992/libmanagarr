using System.Text.Json;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class TitleSortingSettingsTests
{
    [Fact]
    public void Normalize_EnablesOnlyNonEmptyListsRegardlessOfLegacySwitch()
    {
        Assert.True(TitleSortingSettingsRules.Normalize(new() { Enabled = false, IgnoredLeadingWords = ["The"] }).Enabled);
        Assert.False(TitleSortingSettingsRules.Normalize(new() { Enabled = true, IgnoredLeadingWords = [] }).Enabled);
    }

    [Fact]
    public void Normalize_UsesSharedReferenceCasesAndCopiesLists()
    {
        using JsonDocument fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "test-data", "title-sorting-cases.json")));
        foreach (JsonElement fixture in fixtures.RootElement.GetProperty("normalizationCases").EnumerateArray())
        {
            List<string> words = fixture.GetProperty("words").Deserialize<List<string>>()!;
            TitleSortingSettings normalized = TitleSortingSettingsRules.Normalize(new() { Enabled = true, IgnoredLeadingWords = words });
            Assert.Equal(fixture.GetProperty("expected").Deserialize<string[]>(), normalized.IgnoredLeadingWords);
            Assert.NotSame(words, normalized.IgnoredLeadingWords);
            Assert.True(normalized.Enabled);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("The Office")]
    [InlineData("The\u0085Office")]
    [InlineData("The\u0000")]
    public void Validate_InvalidWordHasConcreteFieldError(string word)
    {
        var errors = TitleSortingSettingsRules.Validate(new() { IgnoredLeadingWords = [word] });
        Assert.Contains("titleSorting.ignoredLeadingWords[0]", errors.Keys);
    }

    [Fact]
    public void Validate_CountsUnicodeCodepointsAndRejectsNullAndLimits()
    {
        string astral = string.Concat(Enumerable.Repeat("😀", 64));
        Assert.Empty(TitleSortingSettingsRules.Validate(new() { IgnoredLeadingWords = [astral] }));
        Assert.Contains("titleSorting.ignoredLeadingWords[0]", TitleSortingSettingsRules.Validate(new() { IgnoredLeadingWords = [astral + "😀"] }).Keys);
        Assert.Contains("titleSorting.ignoredLeadingWords[0]", TitleSortingSettingsRules.Validate(new() { IgnoredLeadingWords = [null!] }).Keys);
        Assert.Contains("titleSorting.ignoredLeadingWords", TitleSortingSettingsRules.Validate(new() { IgnoredLeadingWords = null! }).Keys);
        Assert.Contains("titleSorting.ignoredLeadingWords", TitleSortingSettingsRules.Validate(new() { IgnoredLeadingWords = Enumerable.Repeat("The", 101).ToList() }).Keys);
        Assert.Contains("titleSorting", TitleSortingSettingsRules.Validate(null).Keys);
    }

    [Fact]
    public void Normalize_OldOrManuallyEditedSettingsUseSafeDefaults()
    {
        AppSettings old = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.False(AppSettingsNormalizer.Normalize(old).TitleSorting.Enabled);
        Assert.Empty(AppSettingsNormalizer.Normalize(old).TitleSorting.IgnoredLeadingWords);
        Assert.Empty(TitleSortingSettingsRules.Normalize(new() { IgnoredLeadingWords = [null!, "", " "] }).IgnoredLeadingWords);
    }
}
