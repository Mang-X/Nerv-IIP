namespace Nerv.IIP.Contracts.Wms;

public static class WmsIntegrationEventTypes
{
    public const string InboundOrderCompleted = "wms.InboundOrderCompleted";
    public const string OutboundOrderCompleted = "wms.OutboundOrderCompleted";
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
