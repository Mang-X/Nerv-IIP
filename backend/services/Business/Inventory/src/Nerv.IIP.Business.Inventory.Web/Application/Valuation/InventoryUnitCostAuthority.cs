using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Inventory.Web.Application.Valuation;

public static class InventoryUnitCostAuthorityStatuses
{
    public const string NotRequired = "not-required";
    public const string Available = "available";
    public const string Pending = "pending";
    public const string Rejected = "rejected";
}

public sealed record InventoryUnitCostAuthorityResolution(
    string Status,
    decimal? UnitCost = null,
    string? ReasonCode = null)
{
    public static InventoryUnitCostAuthorityResolution NotRequired() =>
        new(InventoryUnitCostAuthorityStatuses.NotRequired);

    public static InventoryUnitCostAuthorityResolution Available(decimal unitCost) =>
        new(InventoryUnitCostAuthorityStatuses.Available, unitCost);

    public static InventoryUnitCostAuthorityResolution Pending(string reasonCode) =>
        new(InventoryUnitCostAuthorityStatuses.Pending, ReasonCode: reasonCode);

    public static InventoryUnitCostAuthorityResolution Rejected(string reasonCode) =>
        new(InventoryUnitCostAuthorityStatuses.Rejected, ReasonCode: reasonCode);
}

public interface IInventoryUnitCostAuthorityResolver
{
    Task<InventoryUnitCostAuthorityResolution> ResolveAsync(
        InventoryMovementRequestedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken);
}

public sealed class UnavailableInventoryUnitCostAuthorityResolver : IInventoryUnitCostAuthorityResolver
{
    public Task<InventoryUnitCostAuthorityResolution> ResolveAsync(
        InventoryMovementRequestedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var reference = integrationEvent.Payload.UnitCostAuthorityReference;
        return Task.FromResult(
            string.IsNullOrWhiteSpace(reference)
                ? InventoryUnitCostAuthorityResolution.NotRequired()
                : InventoryUnitCostAuthorityResolution.Rejected("authority-resolver-not-configured"));
    }
}

public sealed class HttpInventoryUnitCostAuthorityResolver(
    HttpClient httpClient,
    IInternalServiceTokenProvider tokenProvider) : IInventoryUnitCostAuthorityResolver
{
    public async Task<InventoryUnitCostAuthorityResolution> ResolveAsync(
        InventoryMovementRequestedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        if (string.IsNullOrWhiteSpace(payload.UnitCostAuthorityReference))
        {
            return InventoryUnitCostAuthorityResolution.NotRequired();
        }

        if (!string.Equals(
                payload.UnitCostAuthorityReference,
                InventoryMovementUnitCostAuthorityReferences.MesFinishedGoodsReceipt,
                StringComparison.Ordinal))
        {
            return InventoryUnitCostAuthorityResolution.Rejected("unknown-authority-reference");
        }

        if (string.IsNullOrWhiteSpace(payload.SourceDocumentLineId))
        {
            return InventoryUnitCostAuthorityResolution.Rejected("work-order-scope-missing");
        }

        var requestBody = new MesFinishedGoodsReceiptCostAuthorityRequest(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            payload.SourceDocumentId,
            payload.SourceDocumentLineId,
            payload.IdempotencyKey);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/internal/business-mes/v1/finished-goods-receipt-cost-authority")
        {
            Content = JsonContent.Create(requestBody),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenProvider.BearerToken);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return InventoryUnitCostAuthorityResolution.Pending("authority-not-ready");
            }

            if (!response.IsSuccessStatusCode)
            {
                return InventoryUnitCostAuthorityResolution.Pending("authority-service-unavailable");
            }

            var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<MesFinishedGoodsReceiptCostAuthorityResponse>>(
                cancellationToken);
            var authority = envelope?.Data;
            if (authority is null)
            {
                return InventoryUnitCostAuthorityResolution.Pending("authority-response-empty");
            }

            if (string.Equals(authority.Status, MesFinishedGoodsCostAuthorityStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            {
                return InventoryUnitCostAuthorityResolution.Pending(authority.ReasonCode ?? "authority-pending");
            }

            if (!string.Equals(authority.Status, MesFinishedGoodsCostAuthorityStatuses.Available, StringComparison.OrdinalIgnoreCase) ||
                authority.CapitalizedUnitCost is not > 0m ||
                string.IsNullOrWhiteSpace(authority.ProvenanceEventId))
            {
                return InventoryUnitCostAuthorityResolution.Rejected(authority.ReasonCode ?? "authority-rejected");
            }

            return InventoryUnitCostAuthorityResolution.Available(authority.CapitalizedUnitCost.Value);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return InventoryUnitCostAuthorityResolution.Pending("authority-timeout");
        }
        catch (HttpRequestException)
        {
            return InventoryUnitCostAuthorityResolution.Pending("authority-service-unavailable");
        }
    }

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}

public sealed class InventoryUnitCostAuthorityPendingException(string reasonCode)
    : Exception($"Inventory unit-cost authority is pending: {reasonCode}");
