using System.Text.Json;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class TitleSortKeyBuilderTests
{
    private static JsonDocument ReadCases() => JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "test-data", "title-sorting-cases.json")));

    [Fact]
    public void Build_ShouldMatchSharedKeysWithoutMutatingTitlesOrWords()
    {
        using JsonDocument cases = ReadCases();
        foreach (JsonElement testCase in cases.RootElement.GetProperty("keyCases").EnumerateArray())
        {
            string title = testCase.GetProperty("title").GetString()!;
            List<string> words = testCase.GetProperty("words").EnumerateArray().Select(word => word.GetString()!).ToList();
            string[] original = [.. words];
            TitleSortingSettings settings = new() { Enabled = true, IgnoredLeadingWords = words };
            Assert.Equal(testCase.GetProperty("expected").GetString(), TitleSortKeyBuilder.Build(title, settings));
            settings.Enabled = false;
            Assert.Equal(title, TitleSortKeyBuilder.Build(title, settings));
            Assert.Equal(original, words);
        }
    }

    [Fact]
    public void Build_ShouldUseOnlyTheSpecifiedUnicodeWhitespace()
    {
        using JsonDocument cases = ReadCases();
        foreach (JsonElement code in cases.RootElement.GetProperty("whitespaceCodePoints").EnumerateArray())
        {
            char whitespace = (char)code.GetInt32();
            Assert.True(TitleSortKeyBuilder.IsWhiteSpace(whitespace));
            Assert.Equal("The", TitleSortKeyBuilder.TrimWhiteSpace($"{whitespace}The{whitespace}"));
            Assert.Equal("Office", TitleSortKeyBuilder.Build($"{whitespace}The{whitespace}Office",
                new() { Enabled = true, IgnoredLeadingWords = ["the"] }));
        }
        Assert.False(TitleSortKeyBuilder.IsWhiteSpace('\uFEFF'));
        Assert.False(TitleSortKeyBuilder.IsWhiteSpace('\u200B'));
    }

    [Fact]
    public void Fold_ShouldMatchSharedUnicodeSimpleFoldingCases()
    {
        using JsonDocument cases = ReadCases();
        foreach (JsonElement testCase in cases.RootElement.GetProperty("foldCases").EnumerateArray())
        {
            Assert.Equal(testCase.GetProperty("expected").GetString(),
                UnicodeSimpleCaseFold.Fold(testCase.GetProperty("input").GetString()!));
        }
    }
}
