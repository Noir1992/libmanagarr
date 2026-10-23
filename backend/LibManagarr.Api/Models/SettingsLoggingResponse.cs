namespace LibManagarr.Api.Models;

public sealed class SettingsLoggingResponse
{
    public LoggingSettings Logging { get; set; } = new();
}
