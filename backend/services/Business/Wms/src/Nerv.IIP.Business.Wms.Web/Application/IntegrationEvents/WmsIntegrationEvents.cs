namespace Nerv.IIP.Business.Wms.Web.Application.IntegrationEvents;

public static class WmsIntegrationEventTypes
{
    public const string InboundOrderCompleted = "wms.InboundOrderCompleted";
    public const string OutboundOrderCompleted = "wms.OutboundOrderCompleted";
    public const string CountExecutionCompleted = "wms.CountExecutionCompleted";
    public const string WcsTaskDispatched = "wms.WcsTaskDispatched";
    public const string WcsTaskFailed = "wms.WcsTaskFailed";
}

public static class WmsIntegrationEventSources
{
    public const string BusinessWms = "business-wms";
}

public sealed record WmsIntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string OrganizationId,
    string EnvironmentId,
    string IdempotencyKey,
    WmsIntegrationPayload Payload);

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
    string? DiagnosticMessage);
