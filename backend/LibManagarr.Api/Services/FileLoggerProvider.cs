using Microsoft.Extensions.Logging;

namespace LibManagarr.Api.Services;

public sealed class FileLoggerProvider(LogFileService logFileService) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(logFileService, categoryName);
    }

    public void Dispose()
    {
    }
}
