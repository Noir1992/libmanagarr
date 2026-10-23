using Microsoft.AspNetCore.Http.HttpResults;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Endpoints;

public static class ScheduledTaskEndpoints
{
    public static RouteGroupBuilder MapScheduledTaskEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/tasks", async (ScheduledTaskService tasks, CancellationToken token) =>
            TypedResults.Ok(await tasks.GetAllAsync(token)));
        group.MapPut("/tasks/{taskKey}", UpdateAsync);
        group.MapPost("/tasks/{taskKey}/run", RunAsync);
        return group;
    }

    private static async Task<Results<Ok<ScheduledTask>, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        string taskKey, ScheduledTaskUpdateRequest request, ScheduledTaskService tasks, CancellationToken token)
    {
        try
        {
            tasks.RequireDefinition(taskKey);
            Dictionary<string, string[]> errors = ScheduledTaskService.Validate(request);
            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
            return TypedResults.Ok(await tasks.UpdateAsync(taskKey, request, token));
        }
        catch (ScheduledTaskException exception)
        {
            return TypedResults.Problem(title: exception.Message, statusCode: exception.StatusCode);
        }
    }

    private static async Task<Results<Ok<ScheduledTask>, ProblemHttpResult>> RunAsync(
        string taskKey, ScheduledTaskExecutionService execution, CancellationToken token)
    {
        try { return TypedResults.Ok((await execution.RunAsync(taskKey, true, token))!); }
        catch (ScheduledTaskException exception)
        {
            return TypedResults.Problem(title: exception.Message, statusCode: exception.StatusCode);
        }
    }
}
