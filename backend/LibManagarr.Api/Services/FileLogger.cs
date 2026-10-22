using Microsoft.Extensions.Logging;

namespace LibManagarr.Api.Services;

public sealed class FileLogger(LogFileService logFileService, string categoryName) : ILogger
{
    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= GetMinimumLevel();
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        string message = formatter(state, exception);
        logFileService.Append(logLevel, GetSourceName(categoryName), message, exception, DateTimeOffset.Now);
    }

    private LogLevel GetMinimumLevel()
    {
        return logFileService.MinimumLevel;
    }

    private static string GetSourceName(string category)
    {
        int lastSeparatorIndex = category.LastIndexOf('.');
        return lastSeparatorIndex >= 0 && lastSeparatorIndex < category.Length - 1
            ? category[(lastSeparatorIndex + 1)..]
            : category;
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
