using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Inventory.Web.Application.Valuation;

public static class InventoryUnitCostAuthorityStatuses
{
    // NotRequired is retained only for legacy non-MES movement producers that do not
    // participate in the MES finished-goods authority protocol.
    public const string NotRequired = "not-required";
    public const string Available = MesFinishedGoodsCostAuthorityStatuses.Available;
    public const string Pending = MesFinishedGoodsCostAuthorityStatuses.Pending;
    public const string Rejected = MesFinishedGoodsCostAuthorityStatuses.Rejected;
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
                ? RequiresMesFinishedGoodsAuthority(integrationEvent)
                    ? InventoryUnitCostAuthorityResolution.Pending("authority-reference-missing")
                    : InventoryUnitCostAuthorityResolution.NotRequired()
                : InventoryUnitCostAuthorityResolution.Rejected("authority-resolver-not-configured"));
    }

    internal static bool RequiresMesFinishedGoodsAuthority(
        InventoryMovementRequestedIntegrationEvent integrationEvent)
    {
        return string.Equals(
                integrationEvent.SourceService,
                InventoryIntegrationEventSources.BusinessMes,
                StringComparison.Ordinal)
            && integrationEvent.Payload.IdempotencyKey.StartsWith(
                "mes:finished-goods-receipt:",
                StringComparison.Ordinal);
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
            return UnavailableInventoryUnitCostAuthorityResolver.RequiresMesFinishedGoodsAuthority(integrationEvent)
                ? InventoryUnitCostAuthorityResolution.Pending("authority-reference-missing")
                : InventoryUnitCostAuthorityResolution.NotRequired();
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

            var authority = await response.Content.ReadFromJsonAsync<MesFinishedGoodsReceiptCostAuthorityResponse>(
                cancellationToken);
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
}

public sealed class InventoryUnitCostAuthorityPendingException(string reasonCode)
    : Exception($"Inventory unit-cost authority is pending: {reasonCode}");
