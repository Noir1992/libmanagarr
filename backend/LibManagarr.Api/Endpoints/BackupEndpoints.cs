using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using LibManagarr.Api.Models.Auth;
using LibManagarr.Api.Models.Backup;
using LibManagarr.Api.Services;
using LibManagarr.Api.Services.Backup;

namespace LibManagarr.Api.Endpoints;

/// <summary>
/// Export and import of settings and library data. Exporting and applying each start with the
/// identity proof the account area uses for a password change, so neither reading a backup out of
/// the installation nor overwriting it can be done by whoever left a browser tab open; checking an
/// archive needs only its own password and changes nothing. A confirmed restore restarts the host
/// automatically, releasing all state before a fresh host takes over settings and library data.
/// </summary>
public static class BackupEndpoints
{
    public static IEndpointRouteBuilder MapBackupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/backups");
        group.RequireAuthorization();
        group.AddEndpointFilter(static async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });

        group.MapGet("/status", GetStatusAsync);
        group.MapPost("/export", ExportAsync).RequireRateLimiting("auth");
        group.MapPost("/import/validate", ValidateImportAsync).DisableAntiforgery().RequireRateLimiting("auth");
        group.MapPost("/import/{importId}/apply", ApplyImportAsync).RequireRateLimiting("auth");
        group.MapDelete("/import/{importId}", DiscardImportAsync);

        return endpoints;
    }

    /// <summary>
    /// Reachable while the installation waits for its restart, unlike every route that reads
    /// settings or databases: those are refused once writers are paused for a restore.
    /// </summary>
    private static async Task<Ok<BackupStatusDto>> GetStatusAsync(BackupStatusService status)
    {
        await Task.CompletedTask;
        return TypedResults.Ok(status.Snapshot());
    }

    private static async Task<IResult> ExportAsync(
        BackupExportRequest request,
        HttpContext httpContext,
        ClaimsPrincipal user,
        AccountService accountService,
        BackupExportService exportService,
        CancellationToken cancellationToken)
    {
        if (!TryGetSession(user, out int accountId)) return AuthProblems.NoAccount();
        ProblemHttpResult? identityFailure = await VerifyIdentityAsync(
            request.CurrentPassword, request.Code, request.UseRecoveryCode, accountId, accountService, httpContext, cancellationToken);
        if (identityFailure is not null) return identityFailure;

        try
        {
            BackupExportResult archive = await exportService.ExportAsync(request.BackupPassword, cancellationToken);
            return new BackupDownloadResult(archive);
        }
        catch (BackupException exception)
        {
            return BackupProblem(exception);
        }
    }

    /// <summary>
    /// Takes the upload apart into a protected staging tree before anything is decrypted, so the
    /// size ceiling and the allowed field names already hold on the bytes that arrived on the wire.
    /// The archive is then checked end to end inside its own staging tree; any failure leaves the
    /// live installation untouched and the uploaded bytes are removed again with the request.
    /// </summary>
    private static async Task<IResult> ValidateImportAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        StoragePaths paths,
        BackupImportService importService,
        CancellationToken cancellationToken)
    {
        if (!TryGetSession(user, out int accountId)) return AuthProblems.NoAccount();
        // The server side ceiling is the archive limit; the transport must not reject a file that is
        // still inside it. Raised per request so every other route keeps its normal bound.
        if (httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize)
        {
            bodySize.MaxRequestBodySize = BackupArchiveFormat.MaxArchiveBytes + BackupUploadReader.BodyAllowance;
        }

        string stagingRoot = BackupStaging.Create(paths.BackupsDirectory, "upload");
        try
        {
            BackupUpload upload = await BackupUploadReader.ReadAsync(httpContext.Request, stagingRoot, cancellationToken);
            BackupImportPreview preview = await importService.ValidateAsync(
                upload.ArchivePath, upload.BackupPassword, accountId, AuthSessionService.HashSessionToken(httpContext), cancellationToken);
            return TypedResults.Ok(preview);
        }
        catch (BackupException exception)
        {
            return BackupProblem(exception);
        }
        finally
        {
            BackupStaging.TryDelete(stagingRoot);
        }
    }

    private static async Task<IResult> ApplyImportAsync(
        string importId,
        BackupApplyRequest request,
        HttpContext httpContext,
        ClaimsPrincipal user,
        AccountService accountService,
        BackupImportService importService,
        IHostApplicationLifetime lifetime,
        ApplicationRestart restart,
        CancellationToken cancellationToken)
    {
        if (!TryGetSession(user, out int accountId)) return AuthProblems.NoAccount();
        ProblemHttpResult? identityFailure = await VerifyIdentityAsync(
            request.CurrentPassword, request.Code, request.UseRecoveryCode, accountId, accountService, httpContext, cancellationToken);
        if (identityFailure is not null) return identityFailure;

        try
        {
            RestoreJournal journal = await importService.PrepareRestoreAsync(
                importId, accountId, AuthSessionService.HashSessionToken(httpContext), Guid.NewGuid().ToString("N"), cancellationToken);

            // The answer has to reach the browser before the shutdown begins, so stopping is
            // requested once the response has been written rather than right away.
            httpContext.Response.RegisterForDispose(new RestartOnDispose(lifetime, restart));
            return TypedResults.Ok(new BackupApplyResultDto(journal.OperationId, journal.PreparedAt, RestartRequired: true));
        }
        catch (BackupException exception)
        {
            return BackupProblem(exception);
        }
    }

    private static IResult DiscardImportAsync(
        string importId,
        HttpContext httpContext,
        ClaimsPrincipal user,
        BackupImportService importService)
    {
        if (!TryGetSession(user, out int accountId)) return AuthProblems.NoAccount();
        try
        {
            importService.Discard(importId, accountId, AuthSessionService.HashSessionToken(httpContext));
            return TypedResults.NoContent();
        }
        catch (BackupException exception)
        {
            return BackupProblem(exception);
        }
    }

    /// <summary>
    /// Password first, then the second factor, exactly as in the account area: a wrong password must
    /// never burn a recovery code, and an account without a second factor is proved by the password
    /// alone - demanding a code it cannot have would lock the route shut. The shared proof counts a
    /// failed server-side check once and reports the state it produced, so this route is protected
    /// against guessing like a password change is and the endpoint only translates the outcome.
    /// </summary>
    private static async Task<ProblemHttpResult?> VerifyIdentityAsync(
        string? currentPassword, string? code, bool useRecoveryCode, int accountId,
        AccountService accountService, HttpContext httpContext, CancellationToken cancellationToken)
    {
        AccountRecord? account = await accountService.GetAccountAsync(cancellationToken);
        if (account is null || account.Id != accountId) return AuthProblems.NoAccount();

        (SecondFactorProbe? _, AccountOperationResult result) = await accountService.VerifyIdentityProofAsync(
            account, currentPassword, code, useRecoveryCode, cancellationToken);
        return result.Status switch
        {
            AccountOperationStatus.Success => null,
            AccountOperationStatus.LockedOut => AuthProblems.LockedOut(result.LockedUntil!.Value, httpContext, accountService.GetUtcNow()),
            AccountOperationStatus.SecondFactorRequired => IdentityProblem("A current two-factor code or recovery code is required.", AuthFailureKinds.SecondFactor, result.RemainingAttempts),
            AccountOperationStatus.InvalidSecondFactor => IdentityProblem("The two-factor code is incorrect or already used.", AuthFailureKinds.SecondFactor, result.RemainingAttempts),
            _ => IdentityProblem("The current password is incorrect.", AuthFailureKinds.BackupPassword, result.RemainingAttempts)
        };
    }

    // The credential this form asked for, never the archive password: a wrong archive password is a
    // separate error of its own and cannot lock the local account, so it carries no identity state.
    private static ProblemHttpResult IdentityProblem(string title, string failureKind, int? remainingAttempts) =>
        AuthProblems.ProofFailure(StatusCodes.Status400BadRequest, title, BackupErrorCodes.IdentityVerificationFailed, failureKind, remainingAttempts);

    private static ProblemHttpResult BackupProblem(BackupException exception) =>
        AuthProblems.Problem(exception.StatusCode, exception.Message, exception.Code);

    private static bool TryGetSession(ClaimsPrincipal user, out int accountId) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out accountId);

    /// <summary>
    /// Streams the archive and deletes it with the response, so an interrupted download cannot leave
    /// a readable backup lying around in the config directory.
    /// </summary>
    private sealed class BackupDownloadResult(BackupExportResult archive) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            try
            {
                httpContext.Response.ContentType = BackupArchiveFormat.DownloadContentType;
                httpContext.Response.ContentLength = archive.SizeBytes;
                httpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"{archive.FileName}\"";
                await using FileStream stream = archive.OpenRead();
                await stream.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
            }
            finally
            {
                archive.Dispose();
            }
        }
    }

    private sealed class RestartOnDispose(IHostApplicationLifetime lifetime, ApplicationRestart restart) : IDisposable
    {
        public void Dispose()
        {
            restart.Request();
            lifetime.StopApplication();
        }
    }
}
