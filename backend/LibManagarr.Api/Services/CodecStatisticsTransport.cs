using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>Transfers each file's metadata once, with category membership expressed as file IDs.</summary>
public static class CodecStatisticsTransport
{
    public static CodecStatisticsResponse Compact(CodecStatisticsResponse response)
    {
        List<CodecMediaFileItem> files = [];
        Dictionary<FileKey, int> ids = [];
        int Register(CodecMediaFileItem file)
        {
            FileKey key = new(file.Title, file.Source, file.InstanceName, file.TitleSlug, file.SizeBytes, file.RelativePath, file.RootTitle);
            if (ids.TryGetValue(key, out int existing)) return existing;
            int id = files.Count;
            ids.Add(key, id);
            files.Add(file);
            return id;
        }
        IReadOnlyList<CodecStatisticItem> Codecs(IReadOnlyList<CodecStatisticItem> items) => items.Select(item => new CodecStatisticItem
        {
            Label = item.Label, Count = item.Count, Percentage = item.Percentage, TotalSizeBytes = item.TotalSizeBytes,
            FileIds = item.MediaFiles.Select(Register).ToArray()
        }).ToArray();
        IReadOnlyList<LanguageStatisticItem> Languages(IReadOnlyList<LanguageStatisticItem> items) => items.Select(item => new LanguageStatisticItem
        {
            Label = item.Label, Count = item.Count, Percentage = item.Percentage, TotalSizeBytes = item.TotalSizeBytes,
            FileIds = item.MediaFiles.Select(file => Register(new CodecMediaFileItem
            {
                Title = file.Title, Source = file.Source, InstanceName = file.InstanceName, TitleSlug = file.TitleSlug,
                SizeBytes = file.SizeBytes, RelativePath = file.RelativePath, RootTitle = file.RootTitle
            })).ToArray()
        }).ToArray();
        return new()
        {
            ContainerFormats = Codecs(response.ContainerFormats), Resolutions = Codecs(response.Resolutions),
            DynamicRanges = Codecs(response.DynamicRanges), VideoCodecs = Codecs(response.VideoCodecs),
            AudioCodecs = Codecs(response.AudioCodecs), VideoBitrates = Codecs(response.VideoBitrates),
            AudioLanguages = Languages(response.AudioLanguages), SubtitleLanguages = Languages(response.SubtitleLanguages),
            Files = files, FilterValues = response.FilterValues, Counts = response.Counts
        };
    }

    private sealed record FileKey(string Title, string Source, string InstanceName, string? TitleSlug, long? SizeBytes, string? RelativePath, string RootTitle);
}
