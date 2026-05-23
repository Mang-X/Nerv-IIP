using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Sdk.Core;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Wms.Web.Application.Inventory;

public interface IInventoryMovementClient
{
    Task<PostInventoryMovementResult> PostMovementAsync(PostInventoryMovementRequest request, CancellationToken cancellationToken);
}

public sealed record PostInventoryMovementRequest(
    string OrganizationId,
    string EnvironmentId,
    string MovementType,
    string SourceService,
    string SourceDocumentId,
    string? SourceDocumentLineId,
    string IdempotencyKey,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    decimal Quantity);

public sealed record PostInventoryMovementResult(string InventoryMovementId);

public sealed record PostInventoryMovementResponse(string MovementId, decimal OnHandQuantity, decimal AvailableQuantity);

public sealed class NoopInventoryMovementClient : IInventoryMovementClient
{
    public Task<PostInventoryMovementResult> PostMovementAsync(PostInventoryMovementRequest request, CancellationToken _)
    {
        return Task.FromResult(new PostInventoryMovementResult($"pending-{request.MovementType}-{request.IdempotencyKey}"));
    }
}

public sealed class HttpInventoryMovementClient(HttpClient httpClient, IInternalServiceTokenProvider? tokenProvider = null) : IInventoryMovementClient
{
    public async Task<PostInventoryMovementResult> PostMovementAsync(PostInventoryMovementRequest request, CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/v1/movements")
        {
            Content = JsonContent.Create(request),
        };
        if (!string.IsNullOrWhiteSpace(tokenProvider?.BearerToken))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenProvider.BearerToken);
        }

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await PlatformApiClient.ReadResponseDataAsync<PostInventoryMovementResponse>(response, cancellationToken);
        return new PostInventoryMovementResult(payload.MovementId);
    }
}

public static class WmsInventoryMovementClientServiceCollectionExtensions
{
    public static IServiceCollection AddWmsInventoryMovementClient(this IServiceCollection services, IConfiguration configuration)
    {
        if (configuration.GetValue<bool>("Inventory:UseNoopClient"))
        {
            services.AddScoped<IInventoryMovementClient, NoopInventoryMovementClient>();
            return services;
        }

        var baseUrl = configuration["Inventory:BaseUrl"]
            ?? configuration["Services:Inventory:BaseUrl"]
            ?? "http://localhost:5109";

        services.AddHttpClient<IInventoryMovementClient, HttpInventoryMovementClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
        });
        return services;
    }
}

public static class InventoryMovementRequestMapping
{
    public static PostInventoryMovementRequest ToInventoryPostRequest(this InventoryMovementRequest request)
    {
        var quantity = request.MovementType is "outbound"
            ? -Math.Abs(request.Quantity)
            : request.Quantity;

        return new PostInventoryMovementRequest(
            request.OrganizationId,
            request.EnvironmentId,
            request.MovementType,
            "wms",
            request.SourceDocumentId,
            request.SourceDocumentLineId,
            request.IdempotencyKey,
            request.SkuCode,
            request.UomCode,
            request.SiteCode,
            request.LocationCode,
            request.LotNo,
            request.SerialNo,
            request.QualityStatus,
            request.OwnerType,
            request.OwnerId,
            quantity);
    }
}
