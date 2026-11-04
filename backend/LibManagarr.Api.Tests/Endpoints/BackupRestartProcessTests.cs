using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LibManagarr.Api.Services.Backup;

namespace LibManagarr.Api.Tests.Endpoints;

/// <summary>Exercises the actual host loop without Docker or an IDE restarting the process for it.</summary>
public sealed class BackupRestartProcessTests
{
    [Fact]
    public async Task Restore_AutomaticallyRebuildsTheHost_AndKeepsTheSessionAcrossRepeatedRestores()
    {
        string directory = Path.Combine(Path.GetTempPath(), "libmanagarr-restart-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        ProcessStartInfo start = new("dotnet")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add($"http://127.0.0.1:{port}");
        start.Environment["LIBMANAGARR_CONFIG_DIR"] = directory;
        start.Environment["Frontend__AllowedOrigins__0"] = "http://localhost:4200";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["Logging__EventLog__LogLevel__Default"] = "None";
        StringBuilder output = new();
        using Process process = new() { StartInfo = start };
        process.OutputDataReceived += (_, e) => { lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { lock (output) output.AppendLine(e.Data); };
        Assert.True(process.Start());
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using HttpClientHandler handler = new() { CookieContainer = new CookieContainer() };
        using HttpClient client = new(handler) { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(3) };
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(75));
        const string accountPassword = "a-strong-local-account-password";
        const string archivePassword = "a-strong-backup-password";
        try
        {
            await WaitForServerAsync(client, "/api/auth/state", process, deadline.Token);
            await SetCsrfAsync(client);
            using HttpResponseMessage setup = await client.PostAsJsonAsync("/api/auth/setup", new
            {
                username = "restart-test", password = accountPassword, confirmPassword = accountPassword
            }, deadline.Token);
            Assert.Equal(HttpStatusCode.Created, setup.StatusCode);

            // Two restores also exercise pool cleanup and disposal of the previous ConsoleLifetime.
            for (int iteration = 0; iteration < 2; iteration++)
            {
                await SetCsrfAsync(client);
                using HttpResponseMessage original = await client.GetAsync("/api/settings/general", deadline.Token);
                original.EnsureSuccessStatusCode();
                string originalSettings = await original.Content.ReadAsStringAsync(deadline.Token);
                using HttpResponseMessage export = await client.PostAsJsonAsync("/api/backups/export", new
                {
                    backupPassword = archivePassword, currentPassword = accountPassword
                }, deadline.Token);
                export.EnsureSuccessStatusCode();
                byte[] archive = await export.Content.ReadAsByteArrayAsync(deadline.Token);
                using HttpResponseMessage changed = await client.PutAsJsonAsync("/api/settings/general", new
                {
                    language = "Fr", theme = "Dark"
                }, deadline.Token);
                changed.EnsureSuccessStatusCode();
                using MultipartFormDataContent upload = new();
                upload.Add(new ByteArrayContent(archive), "file", "test.lmbak");
                upload.Add(new StringContent(archivePassword), "backupPassword");
                using HttpResponseMessage validated = await client.PostAsync("/api/backups/import/validate", upload, deadline.Token);
                validated.EnsureSuccessStatusCode();
                using JsonDocument preview = JsonDocument.Parse(await validated.Content.ReadAsStringAsync(deadline.Token));
                string importId = preview.RootElement.GetProperty("importId").GetString()!;
                using HttpResponseMessage applied = await client.PostAsJsonAsync($"/api/backups/import/{importId}/apply", new
                {
                    currentPassword = accountPassword
                }, deadline.Token);
                applied.EnsureSuccessStatusCode();
                using JsonDocument accepted = JsonDocument.Parse(await applied.Content.ReadAsStringAsync(deadline.Token));
                string operationId = accepted.RootElement.GetProperty("operationId").GetString()!;
                await WaitForRestoreAsync(client, operationId, process, deadline.Token);

                using HttpResponseMessage restored = await client.GetAsync("/api/settings/general", deadline.Token);
                restored.EnsureSuccessStatusCode();
                Assert.Equal(originalSettings, await restored.Content.ReadAsStringAsync(deadline.Token));
                Assert.False(process.HasExited);
            }
        }
        catch (Exception exception)
        {
            lock (output) throw new InvalidOperationException("Restore host integration failed.\n" + output, exception);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task WaitForServerAsync(HttpClient client, string path, Process process, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Assert.False(process.HasExited, "The backend exited instead of starting.");
            try
            {
                using HttpResponseMessage response = await client.GetAsync(path, token);
                if (response.IsSuccessStatusCode) return;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested) { }
            await Task.Delay(100, token);
        }
        token.ThrowIfCancellationRequested();
    }

    private static async Task WaitForRestoreAsync(HttpClient client, string operationId, Process process, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Assert.False(process.HasExited, "The backend exited instead of restarting automatically.");
            try
            {
                using HttpResponseMessage response = await client.GetAsync("/api/backups/status", token);
                if (response.IsSuccessStatusCode)
                {
                    BackupStatusDto? status = await response.Content.ReadFromJsonAsync<BackupStatusDto>(token);
                    if (status?.OperationId == operationId && status.Phase == "Restored") return;
                    Assert.NotEqual("RestoreRolledBack", status?.Phase);
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested) { }
            await Task.Delay(100, token);
        }
        token.ThrowIfCancellationRequested();
    }

    private static async Task SetCsrfAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();
        string cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", cookie.Split(';')[0]["XSRF-TOKEN=".Length..]);
    }
}
