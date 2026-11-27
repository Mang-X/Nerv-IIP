using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.Erp;

public sealed record PurchaseOrderReleasedV2IntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    PurchaseOrderReleasedV2Payload Payload) : IIntegrationEventEnvelope
{
    public const string Type = ErpIntegrationEventTypes.PurchaseOrderReleased;
    public const int Version = 2;
    public const string TopicTemplate = "nerv-iip.{deployment-profile}.business-erp.procurement.purchase-order-released.v2";

    public static string Topic(string deploymentEnvironment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentEnvironment);
        return TopicTemplate.Replace(
            "{deployment-profile}", deploymentEnvironment.Trim().ToLowerInvariant(), StringComparison.Ordinal);
    }

    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record PurchaseOrderReleasedV2Payload(
    string PurchaseOrderId,
    string PurchaseOrderNo,
    string SupplierCode,
    string SiteCode,
    IReadOnlyList<PurchaseOrderReleasedV2LinePayload> Lines)
{
    // Older release snapshots predate the opt-in and retain Direct authority.
    public PurchaseReceiptInventoryPostingRoute ReceivingAuthority { get; init; } = PurchaseReceiptInventoryPostingRoute.Direct;
}

/// <summary>采购订单原始行号及下达时承诺的采购数量与交期。</summary>
public sealed record PurchaseOrderReleasedV2LinePayload(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal OrderedQuantity,
    DateOnly PromisedDate);
