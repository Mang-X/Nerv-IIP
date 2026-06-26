using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.Wms;

public static class WmsIntegrationEventTypes
{
    public const string InboundOrderCompleted = "wms.InboundOrderCompleted";
    public const string OutboundOrderCompleted = "wms.OutboundOrderCompleted";
    public const string OutboundOrderRequested = "wms.OutboundOrderRequested";
    public const string CountExecutionCompleted = "wms.CountExecutionCompleted";
    public const string WcsTaskDispatched = "wms.WcsTaskDispatched";
    public const string WcsTaskFailed = "wms.WcsTaskFailed";
    public const string WcsTaskCompleted = "wms.WcsTaskCompleted";
}

public static class WmsIntegrationEventVersions
{
    public const int V1 = 1;
}

public static class WmsIntegrationEventSources
{
    public const string BusinessWms = "business-wms";
    public const string BusinessErp = "business-erp";
}

public sealed record WmsIntegrationEvent(
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
    WmsIntegrationPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record WmsIntegrationPayload(
    string PublicReference,
    string? LineReference,
    string? SkuCode,
    string? UomCode,
    string? SiteCode,
    string? LocationCode,
    decimal? Quantity,
    string? Status,
    string? DiagnosticCode,
    string? DiagnosticMessage,
    IReadOnlyCollection<WmsIntegrationPayloadLine>? Lines = null);

public sealed record WmsIntegrationPayloadLine(
    string LineReference,
    string SkuCode,
    string UomCode,
    string? SiteCode,
    string? LocationCode,
    decimal Quantity,
    string? Status);

public sealed record WmsOutboundOrderRequestedIntegrationEvent(
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
    WmsOutboundOrderRequestedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record WmsOutboundOrderRequestedPayload(
    string DeliveryOrderNo,
    string SalesOrderNo,
    string CustomerCode,
    string? SiteCode,
    IReadOnlyCollection<WmsOutboundOrderRequestedLine> Lines);

public sealed record WmsOutboundOrderRequestedLine(
    string SourceLineNo,
    string SkuCode,
    string UomCode,
    string LocationCode,
    string? LotNo,
    decimal Quantity);
