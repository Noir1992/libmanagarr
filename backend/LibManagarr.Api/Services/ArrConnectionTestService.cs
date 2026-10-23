using Microsoft.AspNetCore.Http;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public sealed class ArrConnectionTestService(
    SonarrClient sonarrClient,
    RadarrClient radarrClient,
    SeerrClient seerrClient,
    ILogger<ArrConnectionTestService> logger)
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(5);

    private static SeerrInstanceSettings MapSeerrInstance(ArrInstanceSettings instance)
    {
        return new SeerrInstanceSettings
        {
            Id = instance.Id,
            BaseUrl = instance.BaseUrl,
            ApiKey = instance.ApiKey
        };
    }

    public async Task<TestConnectionResult> TestAsync(
        TestConnectionRequest request,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(ConnectionTimeout);

        try
        {
            switch (request.Kind)
            {
                case ArrApplicationKind.Sonarr:
                    await sonarrClient.TestConnectionAsync(request.Instance, timeoutSource.Token);
                    break;
                case ArrApplicationKind.Radarr:
                    await radarrClient.TestConnectionAsync(request.Instance, timeoutSource.Token);
                    break;
                case ArrApplicationKind.Seerr:
                    await seerrClient.TestConnectionAsync(MapSeerrInstance(request.Instance), timeoutSource.Token);
                    break;
                default:
                    throw new ApiProblemException(
                        StatusCodes.Status400BadRequest,
                        "Invalid application kind",
                        "Unsupported application kind.");
            }

            return new TestConnectionResult
            {
                Message = "Connection successful."
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new ApiProblemException(
                StatusCodes.Status504GatewayTimeout,
                "Connection timeout",
                "Connection test timed out after 5 seconds.");
        }
        catch (ApiProblemException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "Connection test failed for {ApplicationKind} instance {InstanceName}.",
                request.Kind,
                request.Instance.Name);

            throw new ApiProblemException(
                StatusCodes.Status502BadGateway,
                "Connection failed",
                "Connection could not be established. Check the base URL and API key.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Connection test failed for {ApplicationKind} instance {InstanceName}.",
                request.Kind,
                request.Instance.Name);

            throw new ApiProblemException(
                StatusCodes.Status502BadGateway,
                "Connection failed",
                "Connection could not be established. Check the base URL and API key.");
        }
    }
}
