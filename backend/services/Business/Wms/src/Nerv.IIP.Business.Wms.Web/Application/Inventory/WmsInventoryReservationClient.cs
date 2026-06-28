using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Wms.Web.Application.Inventory;

public sealed record WmsInventoryReservationRequest(
    string OrganizationId,
    string EnvironmentId,
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

public sealed record WmsInventoryReservationResult(string ReservationId, decimal ReservedQuantity, decimal AvailableQuantity);

public sealed record WmsInventoryReservationReleaseRequest(string ReservationId, decimal Quantity);

public sealed record WmsInventoryReservationReleaseResult(string ReservationId, decimal OpenQuantity, decimal AvailableQuantity);

public interface IWmsInventoryReservationClient
{
    Task<WmsInventoryReservationResult> ReserveAsync(
        WmsInventoryReservationRequest request,
        CancellationToken cancellationToken);

    Task<WmsInventoryReservationReleaseResult> ReleaseAsync(
        WmsInventoryReservationReleaseRequest request,
        CancellationToken cancellationToken);
}

public sealed class HttpWmsInventoryReservationClient(
    HttpClient httpClient,
    IInternalServiceTokenProvider internalTokenProvider) : IWmsInventoryReservationClient
{
    public async Task<WmsInventoryReservationResult> ReserveAsync(
        WmsInventoryReservationRequest request,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/v1/reservations")
        {
            Content = JsonContent.Create(request),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalTokenProvider.BearerToken);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<WmsInventoryReservationResult>>(cancellationToken);
        if (envelope is null || !envelope.Success || envelope.Data is null)
        {
            throw new KnownException(envelope?.Message ?? "Inventory reservation was rejected without a response payload.");
        }

        return envelope.Data;
    }

    public async Task<WmsInventoryReservationReleaseResult> ReleaseAsync(
        WmsInventoryReservationReleaseRequest request,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/v1/reservations/{Uri.EscapeDataString(request.ReservationId)}/release")
        {
            Content = JsonContent.Create(request),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalTokenProvider.BearerToken);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<WmsInventoryReservationReleaseResult>>(cancellationToken);
        if (envelope is null || !envelope.Success || envelope.Data is null)
        {
            throw new KnownException(envelope?.Message ?? "Inventory reservation release was rejected without a response payload.");
        }

        return envelope.Data;
    }

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
