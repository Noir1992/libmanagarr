using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using LibManagarr.Api.Endpoints;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;
using LibManagarr.Api.Services.Backup;

namespace LibManagarr.Api.Tests.Endpoints;

public sealed class BackupSizeProblemTests
{
    [Theory]
    [InlineData(SizeUnitSystem.Decimal, "536,87 MB", "1,07 GB")]
    [InlineData(SizeUnitSystem.Binary, "512 MiB", "1 GiB")]
    public async Task Problems_UseLiveSettingsAndKeepExactByteValues(SizeUnitSystem units, string limit, string required)
    {
        SettingsStub settings = new(new() { SizeUnitSystem = units, Language = AppLanguage.De });
        BackupException tooLarge = new(BackupErrorCodes.SizeLimitExceeded, "The backup file exceeds the allowed size.")
        {
            LimitBytes = BackupArchiveFormat.MaxArchiveBytes,
            StatusCode = StatusCodes.Status413PayloadTooLarge
        };
        ProblemHttpResult limitProblem = await ToProblem(tooLarge, settings);
        Assert.Contains(limit, limitProblem.ProblemDetails.Title);
        Assert.Equal(413, limitProblem.StatusCode);
        Assert.Equal(tooLarge.LimitBytes, limitProblem.ProblemDetails.Extensions["limitBytes"]);
        Assert.Equal(tooLarge.Code, limitProblem.ProblemDetails.Extensions["code"]);

        BackupException insufficient = new(BackupErrorCodes.InsufficientDiskSpace, "There is not enough free space.")
        {
            RequiredBytes = 1073741824,
            StatusCode = StatusCodes.Status507InsufficientStorage
        };
        ProblemHttpResult spaceProblem = await ToProblem(insufficient, settings);
        Assert.Contains(required, spaceProblem.ProblemDetails.Title);
        Assert.Equal(507, spaceProblem.StatusCode);
        Assert.Equal(insufficient.RequiredBytes, spaceProblem.ProblemDetails.Extensions["requiredBytes"]);
    }

    [Fact]
    public async Task Problems_LeaveOtherFailuresUnchangedWithoutLoadingSettings()
    {
        SettingsStub settings = new(new());
        BackupException exception = new(BackupErrorCodes.InvalidPassword, "Wrong backup password.");
        ProblemHttpResult problem = await ToProblem(exception, settings);
        Assert.Equal(exception.Message, problem.ProblemDetails.Title);
        Assert.False(problem.ProblemDetails.Extensions.ContainsKey("limitBytes"));
        Assert.Equal(0, settings.ReadCount);
    }

    [Fact]
    public void WithStatus_PreservesByteMetadata()
    {
        BackupException original = new(BackupErrorCodes.SizeLimitExceeded, "Too large.") { LimitBytes = 1234, RequiredBytes = 5678 };
        BackupException changed = original.WithStatus(413);
        Assert.Equal(original.LimitBytes, changed.LimitBytes);
        Assert.Equal(original.RequiredBytes, changed.RequiredBytes);
        Assert.Equal(413, changed.StatusCode);
    }

    private static Task<ProblemHttpResult> ToProblem(BackupException exception, ISettingsService settings) =>
        (Task<ProblemHttpResult>)typeof(BackupEndpoints).GetMethod("BackupProblemAsync", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [exception, settings, CancellationToken.None])!;

    private sealed class SettingsStub(AppSettings settings) : ISettingsService
    {
        public int ReadCount { get; private set; }
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult(settings);
        }
        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
