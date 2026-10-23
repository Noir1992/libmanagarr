using Microsoft.Extensions.Logging;

namespace LibManagarr.Api.Models;

public sealed class LogEntry
{
    public DateTimeOffset Timestamp { get; set; }

    public LogLevel Level { get; set; }

    public string Source { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
