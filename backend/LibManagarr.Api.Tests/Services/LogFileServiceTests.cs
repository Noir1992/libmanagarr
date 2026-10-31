using Microsoft.Extensions.Logging;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class LogFileServiceTests : IDisposable
{
    private readonly TestStorageEnvironment environment = new();
    private static readonly DateTimeOffset Timestamp = new(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Read_ShouldRoundTripEntriesAndFilterInclusivelyInTimestampOrder()
    {
        LogFileService service = CreateService();
        service.Append(LogLevel.Error, " Sync ", " Last ", null, Timestamp.AddMinutes(1));
        service.Append(LogLevel.Warning, "Task", "First", null, Timestamp);
        service.Append(LogLevel.Information, "Task", "Excluded level", null, Timestamp);
        service.Append(LogLevel.Error, "Task", "Outside range", null, Timestamp.AddMinutes(2));

        IReadOnlyList<LogEntry> result = service.Read(Timestamp.AddMinutes(1), Timestamp, [LogLevel.Warning, LogLevel.Error]);

        Assert.Equal(new[] { "First", "Last" }, result.Select(entry => entry.Message));
        Assert.Equal(new[] { Timestamp, Timestamp.AddMinutes(1) }, result.Select(entry => entry.Timestamp));
        Assert.Equal(new[] { LogLevel.Warning, LogLevel.Error }, result.Select(entry => entry.Level));
        Assert.Equal("Sync", result[1].Source);
        Assert.Empty(service.Read(Timestamp, Timestamp.AddMinutes(2), []));
        Assert.Equal(4, service.Read(Timestamp, Timestamp.AddMinutes(2), null).Count);
    }

    [Fact]
    public void Read_ShouldIgnoreMalformedLinesAndKeepExceptionDetails()
    {
        LogFileService service = CreateService();
        service.Append(LogLevel.Error, " ", " Failed ", new InvalidOperationException("Test failure"), Timestamp);
        string file = Directory.GetFiles(Path.Combine(environment.ContentRootPath, "data", "logs"), "*.log").Single();
        File.AppendAllLines(file, ["broken", "invalid [Error] [Task] Message", "2026-05-10 12:00:00.000 +00:00 [InvalidLevel] [Task] Message"]);

        LogEntry entry = Assert.Single(service.Read(Timestamp, Timestamp, null));

        Assert.Equal("Application", entry.Source);
        Assert.Equal("Failed | Exception: System.InvalidOperationException: Test failure", entry.Message);
    }

    [Fact]
    public void CleanupExpiredLogs_ShouldKeepTheCutoffDayAndUnrecognizedFiles()
    {
        LogFileService service = CreateService();
        service.UpdateSettings(new LoggingSettings { Path = "logs", RetentionDays = 3 });
        string directory = Path.Combine(environment.ContentRootPath, "data", "logs");
        foreach (string name in new[] { "2026-05-07.log", "2026-05-08.log", "2026-05-10.log", "other.log", "2026-05-01.txt" })
            File.WriteAllText(Path.Combine(directory, name), name);

        Assert.Equal(1, service.CleanupExpiredLogs());
        Assert.Equal(0, service.CleanupExpiredLogs());

        Assert.Equal(new[] { "2026-05-01.txt", "2026-05-08.log", "2026-05-10.log", "other.log" },
            Directory.GetFiles(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("custom-logs")]
    public void UpdateSettings_ShouldAlwaysWriteBelowConfiguredDirectory(string legacyPath)
    {
        string configDirectory = Path.Combine(environment.ContentRootPath, "docker-config");
        LogFileService service = new(new FixedTimeProvider(), new StoragePaths(environment, configDirectory));
        service.UpdateSettings(new LoggingSettings { Path = legacyPath });
        service.Append(LogLevel.Information, "Test", "Fixed path", null, Timestamp);

        string file = Assert.Single(Directory.GetFiles(Path.Combine(configDirectory, "logs"), "*.log"));
        Assert.Contains("Fixed path", File.ReadAllText(file));
        Assert.False(Directory.Exists(Path.Combine(environment.ContentRootPath, "outside")));
        Assert.False(Directory.Exists(Path.Combine(configDirectory, "custom-logs")));
    }

    private LogFileService CreateService()
    {
        LogFileService service = new(new FixedTimeProvider(), new StoragePaths(environment, configDirectory: null));
        service.UpdateSettings(new LoggingSettings { Path = "logs" });
        return service;
    }

    public void Dispose() => environment.Dispose();

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Timestamp;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
