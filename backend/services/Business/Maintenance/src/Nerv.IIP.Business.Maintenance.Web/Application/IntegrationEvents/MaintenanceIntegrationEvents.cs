namespace Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEvents;

public static class MaintenanceLocalIntegrationEventTypes
{
    public const string WorkOrderOpened = "maintenance.WorkOrderOpened";
    public const string WorkOrderCompleted = "maintenance.WorkOrderCompleted";
}

public sealed record MaintenanceWorkOrderOpenedIntegrationEvent(
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
    MaintenanceWorkOrderOpenedPayload Payload);

public sealed record MaintenanceWorkOrderOpenedPayload(string WorkOrderId, string DeviceAssetId, string? SourceAlarmId, string Priority);

public sealed record MaintenanceWorkOrderCompletedIntegrationEvent(
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
    MaintenanceWorkOrderCompletedPayload Payload);

public sealed record MaintenanceWorkOrderCompletedPayload(string WorkOrderId, string DeviceAssetId, int DowntimeMinutes);
