using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace Nerv.IIP.Business.Wms.Web.Application.WcsAdapters;

public sealed record WcsCancellationRequest(
    string OrganizationId,
    string EnvironmentId,
    string AdapterType,
    string ExternalTaskId,
    string Reason,
    string IdempotencyKey);

public interface IWcsCancellationAdapter
{
    Task CancelAsync(WcsCancellationRequest request, CancellationToken cancellationToken);
}

public sealed class HttpWcsCancellationAdapter(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<HttpWcsCancellationAdapter> logger) : IWcsCancellationAdapter
{
    public async Task CancelAsync(WcsCancellationRequest request, CancellationToken cancellationToken)
    {
        var endpoint = configuration[$"Wcs:Adapters:{request.AdapterType}:CancelEndpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            logger.LogWarning(
                "WCS cancellation endpoint is not configured for adapter {AdapterType}; external task {ExternalTaskId} was not sent to a physical adapter.",
                request.AdapterType,
                request.ExternalTaskId);
            return;
        }

        using var response = await httpClient.PostAsJsonAsync(
            endpoint,
            new WcsCancellationHttpPayload(
                request.OrganizationId,
                request.EnvironmentId,
                request.AdapterType,
                request.ExternalTaskId,
                request.Reason,
                request.IdempotencyKey),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        logger.LogInformation(
            "WCS cancellation was sent to adapter {AdapterType}, external task {ExternalTaskId}, org {OrganizationId}, env {EnvironmentId}.",
            request.AdapterType,
            request.ExternalTaskId,
            request.OrganizationId,
            request.EnvironmentId);
    }
}

public sealed record WcsCancellationHttpPayload(
    string OrganizationId,
    string EnvironmentId,
    string AdapterType,
    string ExternalTaskId,
    string Reason,
    string IdempotencyKey);
