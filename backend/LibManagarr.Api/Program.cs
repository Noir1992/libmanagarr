using System.Net;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Endpoints;
using LibManagarr.Api.Models.Auth;
using LibManagarr.Api.Options;
using LibManagarr.Api.Services;
using LibManagarr.Api.Services.Backup;
using System.Threading.RateLimiting;

return await ApplicationHostRunner.RunAsync(restart => RunHostAsync(args, restart));

static async Task<int> RunHostAsync(string[] args, ApplicationRestart restart)
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.ConfigureHttpJsonOptions((options) =>
    {
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
    builder.Services.AddOptions<FrontendOptions>()
        .Bind(builder.Configuration.GetSection(FrontendOptions.SectionName))
        .Validate(
            (options) => options.AllowedOrigins.Length > 0 &&
                options.AllowedOrigins.All((origin) => Uri.TryCreate(origin, UriKind.Absolute, out Uri? _)),
            "At least one valid frontend origin must be configured.")
        .ValidateOnStart();
    builder.Services.AddOptions<AuthOptions>()
        .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
        .ValidateOnStart();
    builder.Services.AddCors();
    builder.Services.AddSingleton(services => new StoragePaths(services.GetRequiredService<IHostEnvironment>(),
        services.GetRequiredService<IConfiguration>()[StoragePaths.ConfigDirectoryEnvironmentVariable]));
    builder.Services.AddSingleton<ISettingsService, JsonSettingsService>();
    builder.Services.AddSingleton<IHistoryStore, SqliteHistoryStore>();
    builder.Services.AddSingleton<IAccountStore, SqliteAccountStore>();
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton(restart);
    builder.Services.AddSingleton<BackupOperationCoordinator>();
    builder.Services.AddSingleton<BackupStatusService>();
    builder.Services.AddSingleton<BackupCaptureService>();
    builder.Services.AddSingleton<BackupExportService>();
    builder.Services.AddSingleton<RestoreJournalStore>();
    builder.Services.AddSingleton<BackupImportRegistry>();
    builder.Services.AddSingleton<BackupImportService>();
    builder.Services.AddSingleton<RestoreBootstrapper>();
    builder.Services.AddSingleton<IPasswordHasher<AccountRecord>, PasswordHasher<AccountRecord>>();
    builder.Services.AddScoped<AccountService>();
    builder.Services.AddScoped<AuthSessionService>();
    builder.Services.AddScoped<TwoFactorService>();
    builder.Services.AddSingleton<LogFileService>();
    builder.Services.AddScoped<ArrConnectionTestService>();
    builder.Services.AddScoped<SnapshotSyncService>();
    builder.Services.AddScoped<ISnapshotSyncService, SnapshotSyncService>();
    builder.Services.AddScoped<CodecStatisticsService>();
    builder.Services.AddScoped<LanguageStatisticsService>();
    builder.Services.AddScoped<LibraryTrendStatisticsService>();
    builder.Services.AddSingleton<CalendarCache>();
    builder.Services.AddScoped<CalendarService>();
    builder.Services.AddSingleton<ReleaseOverviewCache>();
    builder.Services.AddScoped<ReleaseOverviewService>();
    ScheduledTaskCatalog.AddBuiltInScheduledTasks(builder.Services);
    builder.Services.AddSingleton<ScheduledTaskCatalog>();
    builder.Services.AddSingleton<IScheduledTaskStore, SqliteScheduledTaskStore>();
    builder.Services.AddSingleton<ScheduledTaskService>();
    builder.Services.AddSingleton<ScheduledTaskExecutionService>();
    builder.Services.AddHostedService<ScheduledTaskBackgroundService>();
    builder.Services.AddHttpClient();
    builder.Services.AddHttpClient<SonarrClient>();
    builder.Services.AddHttpClient<RadarrClient>();
    builder.Services.AddHttpClient<SeerrClient>();
    builder.Services.AddHttpClient<PosterCacheService>();
    builder.Services.AddSingleton<IPosterCacheService, PosterCacheService>();

    builder.Services.AddHttpClient<GitHubReleasesClient>((client) => client.Timeout = TimeSpan.FromSeconds(10));

    builder.Services.AddDataProtection().SetApplicationName("LibManagarr");
    builder.Services.AddOptions<KeyManagementOptions>().Configure<StoragePaths, ILoggerFactory>((options, paths, loggerFactory) =>
        options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(Path.Combine(paths.ConfigDirectory, "data-protection-keys")), loggerFactory));

    bool requireSecureCookies = builder.Configuration.GetValue(
        $"{AuthOptions.SectionName}:{nameof(AuthOptions.RequireSecureCookies)}",
        false);
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie((options) =>
        {
            options.Cookie.Name = "LibManagarr.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
            options.Cookie.SecurePolicy = requireSecureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = AuthSessionService.SessionLifetime;
            options.SlidingExpiration = false;
            options.EventsType = typeof(AuthCookieEvents);
        });
    builder.Services.AddScoped<AuthCookieEvents>();
    builder.Services.AddAuthorization((options) =>
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    });
    builder.Services.AddAntiforgery((options) =>
    {
        options.HeaderName = "X-XSRF-TOKEN";
        options.Cookie.SameSite = SameSiteMode.Lax;
    });
    // The backup archive is the only multipart body this API accepts and it is deliberately larger than
    // the default form ceiling; the import route bounds the archive itself and refuses anything above it.
    builder.Services.Configure<FormOptions>((options) =>
        options.MultipartBodyLengthLimit = BackupArchiveFormat.MaxArchiveBytes + BackupUploadReader.BodyAllowance);
    builder.Services.AddRateLimiter((options) =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        // One window per source address. The framework creates it on the first request from that
        // address and retires the ones that stayed idle; the permit count inside it comes from the
        // settings with every request, so a change there acts without a restart.
        options.AddPolicy("auth", (httpContext) =>
        {
            ISettingsService settingsService = httpContext.RequestServices.GetRequiredService<ISettingsService>();
            TimeProvider timeProvider = httpContext.RequestServices.GetRequiredService<TimeProvider>();
            return RateLimitPartition.Get(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                (_) => new AuthRateLimitWindow(settingsService, timeProvider));
        });
        options.OnRejected = static async (context, cancellationToken) =>
        {
            HttpContext httpContext = context.HttpContext;
            TimeProvider timeProvider = httpContext.RequestServices.GetRequiredService<TimeProvider>();
            // The answer names the time the rejecting window really has left instead of a fixed
            // minute, read from the same clock that counted the requests of that window.
            TimeSpan remaining = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter)
                ? retryAfter
                : AuthRateLimitWindow.WindowLength;
            await AuthProblems.TooManyRequests(remaining, httpContext, timeProvider.GetUtcNow()).ExecuteAsync(httpContext);
        };
    });

    builder.Logging.SetMinimumLevel(LogLevel.Trace);
    builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
    builder.Logging.AddFilter("System", LogLevel.Warning);
    builder.Services.AddSingleton<ILoggerProvider, FileLoggerProvider>();

    await using var app = builder.Build();

    // Replacing settings and library data belongs to a process that does not use them yet, so a
    // prepared import is taken over at this point: before migrations, before the first settings read,
    // before the productive data protection provider and before the scheduler could write anything.
    RestoreJournal? restoreJournal = app.Services.GetRequiredService<RestoreJournalStore>().Read();
    RestoreBootstrapOutcome restoreOutcome = app.Services.GetRequiredService<RestoreBootstrapper>().Run(CancellationToken.None);
    if (restoreOutcome == RestoreBootstrapOutcome.Failed)
    {
        // Starting on a half imported state would present a broken installation as a working one. The
        // log names the files to move back by hand.
        return 1;
    }

    app.Services.GetRequiredService<BackupStatusService>().Update(restoreOutcome switch
        {
            RestoreBootstrapOutcome.Applied => BackupOperationPhase.Restored,
            RestoreBootstrapOutcome.RolledBack => BackupOperationPhase.RestoreRolledBack,
            _ => BackupOperationPhase.Idle
        }, operationId: restoreJournal?.OperationId);

    StoragePaths storagePaths = app.Services.GetRequiredService<StoragePaths>();
    BackupStaging.CleanAbandonedProcessStaging(storagePaths.BackupsDirectory,
        app.Services.GetRequiredService<RestoreJournalStore>().Read()?.StagingDirectory);
    BackupStaging.TrimRollbackCopies(storagePaths.BackupsDirectory);

    // Import before hosted services start and before HTTP requests can mutate configuration.
    await app.Services.GetRequiredService<IScheduledTaskStore>().InitializeAsync(CancellationToken.None);

    FrontendOptions frontendOptions = app.Services.GetRequiredService<IOptions<FrontendOptions>>().Value;

    // X-Forwarded-* is honoured only for proxies the operator listed explicitly; everything
    // else keeps the raw connection address so rate limits and cookie security cannot be spoofed.
    string[] trustedProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
    if (trustedProxies.Length > 0)
    {
        ForwardedHeadersOptions forwardedHeadersOptions = new()
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        };
        forwardedHeadersOptions.KnownIPNetworks.Clear();
        forwardedHeadersOptions.KnownProxies.Clear();
        foreach (string proxy in trustedProxies)
        {
            forwardedHeadersOptions.KnownProxies.Add(IPAddress.Parse(proxy));
        }

        app.UseForwardedHeaders(forwardedHeadersOptions);
    }

    app.UseCors((policy) =>
    {
        policy.WithOrigins(frontendOptions.AllowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });

    // The release image bundles the built Angular app into wwwroot; local dev keeps serving
    // the SPA from `ng serve` and never has this file.
    bool spaInstalled = File.Exists(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html"));

    if (spaInstalled)
    {
        app.UseDefaultFiles();
        app.UseStaticFiles();
    }

    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
        await next(context);
    });
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseMiddleware<ApiAntiforgeryMiddleware>();
    app.UseRateLimiter();

    app.MapAuthEndpoints();
    app.MapAccountEndpoints();
    app.MapSettingsEndpoints();
    app.MapLogsEndpoints();
    app.MapSonarrEndpoints();
    app.MapRadarrEndpoints();
    app.MapStatisticsEndpoints();
    app.MapRequestEndpoints();
    app.MapCalendarEndpoints();
    app.MapReleaseEndpoints();
    app.MapBackupEndpoints();

    // Unknown API paths must answer with a plain 404 problem instead of falling through to the
    // SPA shell with status 200.
    app.Map("/api/{**unknownApiPath}", () => TypedResults.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "The requested API endpoint does not exist.")).AllowAnonymous();

    if (spaInstalled)
    {
        app.MapFallbackToFile("index.html").AllowAnonymous();
    }

    await app.StartAsync();
    await app.WaitForShutdownAsync();
    return 0;
}

public partial class Program
{
    protected Program()
    {
    }
}
