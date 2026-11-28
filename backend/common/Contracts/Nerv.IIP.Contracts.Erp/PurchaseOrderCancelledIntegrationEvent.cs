using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.Erp;

public sealed record PurchaseOrderCancelledIntegrationEvent(
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
    PurchaseOrderCancelledPayload Payload) : IIntegrationEventEnvelope
{
    public const string Type = ErpIntegrationEventTypes.PurchaseOrderCancelled;
    public const int Version = 1;
    public const string TopicTemplate = "nerv-iip.{deployment-profile}.business-erp.procurement.purchase-order-cancelled.v1";

    public static string Topic(string deploymentEnvironment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentEnvironment);
        return TopicTemplate.Replace(
            "{deployment-profile}", deploymentEnvironment.Trim().ToLowerInvariant(), StringComparison.Ordinal);
    }

    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record PurchaseOrderCancelledPayload(
    string PurchaseOrderId,
    string PurchaseOrderNo,
    string SiteCode,
    int PurchaseOrderVersion,
    string? Reason);
