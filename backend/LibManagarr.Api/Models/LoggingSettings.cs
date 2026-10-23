using Microsoft.Extensions.Logging;

namespace LibManagarr.Api.Models;

public sealed class LoggingSettings
{
    public string Path { get; set; } = "./logs";

    public int RetentionDays { get; set; } = 30;

    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;
}
