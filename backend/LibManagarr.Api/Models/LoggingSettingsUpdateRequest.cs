namespace LibManagarr.Api.Models;

public sealed class LoggingSettingsUpdateRequest
{
    public LoggingSettings Logging { get; set; } = new();
}
