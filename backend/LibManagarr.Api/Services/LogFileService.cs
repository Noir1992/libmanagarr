using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public sealed class LogFileService(TimeProvider timeProvider, StoragePaths storagePaths)
{
    private static readonly Regex LogLineRegex = new(
        @"^(?<timestamp>.+?) \[(?<level>[^\]]+)\] \[(?<source>[^\]]+)\] (?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly object syncRoot = new();
    private readonly string configDirectory = storagePaths.ConfigDirectory;
    private LoggingSettings currentSettings = AppSettingsDefaults.Create().Logging;

    public void UpdateSettings(LoggingSettings settings)
    {
        lock (syncRoot)
        {
            currentSettings = Normalize(settings);
            EnsureDirectoryExists(currentLogDirectoryPath);
        }
    }

    public LogLevel MinimumLevel
    {
        get
        {
            lock (syncRoot)
            {
                return currentSettings.MinimumLevel;
            }
        }
    }

    public void Append(
        LogLevel level,
        string source,
        string message,
        Exception? exception,
        DateTimeOffset timestamp)
    {
        lock (syncRoot)
        {
            EnsureDirectoryExists(currentLogDirectoryPath);

            string line = BuildLogLine(level, source, message, exception, timestamp);
            string filePath = Path.Combine(currentLogDirectoryPath, $"{timestamp.LocalDateTime:yyyy-MM-dd}.log");
            File.AppendAllText(filePath, line + Environment.NewLine, Encoding.UTF8);
        }
    }

    public IReadOnlyList<LogEntry> Read(
        DateTimeOffset startDateTime,
        DateTimeOffset endDateTime,
        IReadOnlyCollection<LogLevel>? levels)
    {
        DateTimeOffset normalizedStartDateTime = startDateTime <= endDateTime ? startDateTime : endDateTime;
        DateTimeOffset normalizedEndDateTime = startDateTime <= endDateTime ? endDateTime : startDateTime;
        DateOnly normalizedStartDate = DateOnly.FromDateTime(normalizedStartDateTime.LocalDateTime);
        DateOnly normalizedEndDate = DateOnly.FromDateTime(normalizedEndDateTime.LocalDateTime);
        HashSet<LogLevel>? allowedLevels = levels is null
            ? null
            : new HashSet<LogLevel>(levels);

        lock (syncRoot)
        {
            if (!Directory.Exists(currentLogDirectoryPath))
            {
                return [];
            }

            List<LogEntry> entries = [];
            for (DateOnly date = normalizedStartDate; date <= normalizedEndDate; date = date.AddDays(1))
            {
                string filePath = Path.Combine(currentLogDirectoryPath, $"{date:yyyy-MM-dd}.log");
                if (!File.Exists(filePath))
                {
                    continue;
                }

                foreach (string line in File.ReadLines(filePath, Encoding.UTF8))
                {
                    LogEntry? entry = ParseLine(line);
                    if (entry is null)
                    {
                        continue;
                    }

                    if (entry.Timestamp < normalizedStartDateTime || entry.Timestamp > normalizedEndDateTime)
                    {
                        continue;
                    }

                    if (allowedLevels is not null && !allowedLevels.Contains(entry.Level))
                    {
                        continue;
                    }

                    entries.Add(entry);
                }
            }

            return entries
                .OrderBy((entry) => entry.Timestamp)
                .ToList();
        }
    }

    public void CleanupExpiredLogs()
    {
        lock (syncRoot)
        {
            if (!Directory.Exists(currentLogDirectoryPath))
            {
                return;
            }

            DateOnly cutoffDate = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime)
                .AddDays(1 - currentSettings.RetentionDays);

            foreach (string filePath in Directory.EnumerateFiles(currentLogDirectoryPath, "*.log"))
            {
                DateOnly? fileDate = TryParseFileDate(filePath);
                if (fileDate is null || fileDate.Value >= cutoffDate)
                {
                    continue;
                }

                File.Delete(filePath);
            }
        }
    }

    private string currentLogDirectoryPath
    {
        get => Path.Combine(configDirectory, "logs");
    }

    private static LoggingSettings Normalize(LoggingSettings settings)
    {
        return new LoggingSettings
        {
            Path = "./logs",
            RetentionDays = Math.Max(1, settings.RetentionDays),
            MinimumLevel = settings.MinimumLevel is LogLevel.Debug or LogLevel.Information or LogLevel.Warning or LogLevel.Error
                ? settings.MinimumLevel
                : LogLevel.Information
        };
    }

    private static void EnsureDirectoryExists(string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);
    }

    private static string BuildLogLine(
        LogLevel level,
        string source,
        string message,
        Exception? exception,
        DateTimeOffset timestamp)
    {
        string timestampText = timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
        string normalizedSource = string.IsNullOrWhiteSpace(source) ? "Application" : source.Trim();
        string normalizedMessage = NormalizeMessage(message, exception);

        return $"{timestampText} [{level}] [{normalizedSource}] {normalizedMessage}";
    }

    private static string NormalizeMessage(string message, Exception? exception)
    {
        string normalizedMessage = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();
        if (exception is null)
        {
            return normalizedMessage;
        }

        string exceptionText = exception.ToString()
            .Replace(Environment.NewLine, " | ", StringComparison.Ordinal)
            .Trim();

        if (normalizedMessage.Length == 0)
        {
            return $"Exception: {exceptionText}";
        }

        return $"{normalizedMessage} | Exception: {exceptionText}";
    }

    private static LogEntry? ParseLine(string line)
    {
        Match match = LogLineRegex.Match(line);
        if (!match.Success)
        {
            return null;
        }

        if (!DateTimeOffset.TryParseExact(
                match.Groups["timestamp"].Value,
                "yyyy-MM-dd HH:mm:ss.fff zzz",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTimeOffset timestamp))
        {
            return null;
        }

        if (!Enum.TryParse(match.Groups["level"].Value, out LogLevel level))
        {
            return null;
        }

        return new LogEntry
        {
            Timestamp = timestamp,
            Level = level,
            Source = match.Groups["source"].Value,
            Message = match.Groups["message"].Value
        };
    }

    private static DateOnly? TryParseFileDate(string filePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(filePath);
        return DateOnly.TryParseExact(fileName, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed)
            ? parsed
            : null;
    }
}
