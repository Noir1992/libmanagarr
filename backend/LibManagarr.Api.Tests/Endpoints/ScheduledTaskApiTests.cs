using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LibManagarr.Api.Tests.Endpoints;

[Collection("auth-host")]
public sealed class ScheduledTaskApiTests
{
    private const string TaskRoute = "/api/settings/tasks/arrDataSync";

    [Fact]
    public async Task TaskRoutes_RequireAuthenticationAndCsrf()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/settings/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(TaskRoute + "/run", null)).StatusCode);
        await SetupAsync(client);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        using HttpResponseMessage response = await client.PutAsJsonAsync(TaskRoute, Configuration());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(TaskRoute + "/run", null)).StatusCode);
    }

    [Fact]
    public async Task AuthorizedTaskContract_ListsUpdatesAndRunsIndividualTasks_RejectsStaleAndProtectedChanges()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await SetupAsync(client);
        JsonArray tasks = (await client.GetFromJsonAsync<JsonArray>("/api/settings/tasks"))!;
        Assert.Equal(3, tasks.Count);
        JsonObject sync = tasks.Single(task => task!["taskKey"]!.GetValue<string>() == "arrDataSync")!.AsObject();
        JsonObject cleanup = tasks.Single(task => task!["taskKey"]!.GetValue<string>() == "cleanup")!.AsObject();
        JsonObject health = tasks.Single(task => task!["taskKey"]!.GetValue<string>() == "symlinkHealthCheck")!.AsObject();
        Assert.True(sync["canRunManually"]!.GetValue<bool>());
        Assert.True(sync["enabled"]!.GetValue<bool>());
        Assert.True(cleanup["readOnly"]!.GetValue<bool>());
        Assert.True(cleanup["canRunManually"]!.GetValue<bool>());
        Assert.True(health["enabled"]!.GetValue<bool>());
        Assert.False(health["readOnly"]!.GetValue<bool>());
        Assert.True(health["canRunManually"]!.GetValue<bool>());
        Assert.Equal(0, sync["revision"]!.GetValue<int>());
        JsonObject payload = Configuration();
        using HttpResponseMessage update = await client.PutAsJsonAsync(TaskRoute, payload);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        JsonObject updated = (await update.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(1, updated["revision"]!.GetValue<int>());
        Assert.True(updated["enabled"]!.GetValue<bool>());
        Assert.True(updated["nextRunAt"]!.GetValue<DateTimeOffset>() > factory.FakeTime.GetUtcNow());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(TaskRoute, payload)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/settings/tasks/cleanup", Configuration())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/api/settings/tasks/unknown", payload)).StatusCode);
        using HttpResponseMessage cleanupRun = await client.PostAsync("/api/settings/tasks/cleanup/run", null);
        Assert.Equal(HttpStatusCode.OK, cleanupRun.StatusCode);
        JsonObject completedCleanup = (await cleanupRun.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(factory.FakeTime.GetUtcNow(), completedCleanup["lastRunAt"]!.GetValue<DateTimeOffset>());
        Assert.Equal(cleanup["nextRunAt"]!.GetValue<DateTimeOffset>(), completedCleanup["nextRunAt"]!.GetValue<DateTimeOffset>());
        Assert.True(completedCleanup["readOnly"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/settings/tasks/unknown/run", null)).StatusCode);
        string settingsFile = Path.Combine(factory.ConfigDirectory, "settings.json");
        string beforeRun = await File.ReadAllTextAsync(settingsFile);
        using HttpResponseMessage run = await client.PostAsync(TaskRoute + "/run", null);
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        JsonObject completed = (await run.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(factory.FakeTime.GetUtcNow(), completed["lastRunAt"]!.GetValue<DateTimeOffset>());
        Assert.Equal(beforeRun, await File.ReadAllTextAsync(settingsFile));
        Assert.False((await client.GetFromJsonAsync<JsonObject>("/api/settings"))!.ContainsKey("scheduledTasks"));
        // The ambiguous previous routes are gone.
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/settings/tasks/run", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/api/settings/tasks", payload)).StatusCode);
    }

    [Theory]
    [InlineData("intervalValue", "0")]
    [InlineData("intervalValue", "1001")]
    [InlineData("timeHour", "24")]
    [InlineData("timeMinute", "-1")]
    [InlineData("everyNDays", "3651")]
    [InlineData("scheduleMode", "99")]
    [InlineData("intervalUnit", "99")]
    [InlineData("revision", "-1")]
    [InlineData("lastRunAt", "\"2020-01-01T00:00:00Z\"")]
    [InlineData("nextRunAt", "null")]
    [InlineData("readOnly", "false")]
    [InlineData("canRunManually", "true")]
    [InlineData("taskKey", "\"cleanup\"")]
    public async Task InvalidOrStoreOwnedInput_IsRejectedWithoutMutation(string key, string value)
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await SetupAsync(client);
        JsonObject payload = Configuration();
        payload[key] = JsonNode.Parse(value);
        using HttpResponseMessage response = await client.PutAsJsonAsync(TaskRoute, payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonArray tasks = (await client.GetFromJsonAsync<JsonArray>("/api/settings/tasks"))!;
        Assert.All(tasks, task => Assert.Equal(0, task!["revision"]!.GetValue<int>()));
    }

    [Theory]
    [InlineData("/api/settings")]
    [InlineData("/api/settings/general")]
    public async Task GeneralSettings_RejectLegacyTaskPayload(string route)
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await SetupAsync(client);
        using HttpResponseMessage response = await client.PutAsJsonAsync(route,
            new { language = "En", theme = "System", scheduledTasks = new { arrDataSync = new { enabled = true } } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("scheduledTasks", out _));
    }

    private static JsonObject Configuration() => new()
    {
        ["revision"] = 0, ["enabled"] = true, ["scheduleMode"] = "Interval", ["intervalUnit"] = "Hour",
        ["intervalValue"] = 2, ["timeHour"] = 0, ["timeMinute"] = 0, ["everyNDays"] = 1
    };

    private static async Task SetupAsync(HttpClient client)
    {
        await SetCsrfAsync(client);
        using HttpResponseMessage setup = await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "testuser", password = "correct-horse-battery-staple", confirmPassword = "correct-horse-battery-staple"
        });
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);
        await SetCsrfAsync(client);
    }

    private static async Task SetCsrfAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/auth/csrf");
        string cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", cookie.Split(';')[0]["XSRF-TOKEN=".Length..]);
    }
}
