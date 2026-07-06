using Nerv.IIP.Business.IndustrialTelemetry.Domain.DomainEvents;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.IntegrationEventConverters;

public sealed class DeviceStateChangedIntegrationEventConverter
    : IIntegrationEventConverter<DeviceStateChangedDomainEvent, DeviceStateChangedIntegrationEvent>
{
    public DeviceStateChangedIntegrationEvent Convert(DeviceStateChangedDomainEvent domainEvent)
    {
        var snapshot = domainEvent.DeviceStateSnapshot;
        return new DeviceStateChangedIntegrationEvent(
            EventIdFactory.New(),
            IndustrialTelemetryIntegrationEventTypes.DeviceStateChanged,
            IndustrialTelemetryIntegrationEventVersions.V1,
            snapshot.OccurredAtUtc,
            IndustrialTelemetryIntegrationEventSources.IndustrialTelemetry,
            $"industrialTelemetry:device-state:{snapshot.OrganizationId}:{snapshot.EnvironmentId}:{snapshot.Id.Id:D}",
            snapshot.Id.Id.ToString("D"),
            snapshot.OrganizationId,
            snapshot.EnvironmentId,
            "system:industrial-telemetry",
            $"industrialTelemetry:device-state:{snapshot.OrganizationId}:{snapshot.EnvironmentId}:{snapshot.DeviceAssetId}:{snapshot.SourceSequence}:{snapshot.Id.Id:D}",
            new DeviceStateChangedPayload(
                snapshot.Id.Id.ToString("D"),
                snapshot.DeviceAssetId,
                snapshot.State,
                snapshot.SourceSequence));
    }
}

public sealed class AlarmRaisedIntegrationEventConverter
    : IIntegrationEventConverter<AlarmRaisedDomainEvent, AlarmRaisedIntegrationEvent>
{
    public AlarmRaisedIntegrationEvent Convert(AlarmRaisedDomainEvent domainEvent)
    {
        var alarm = domainEvent.AlarmEvent;
        return new AlarmRaisedIntegrationEvent(
            EventIdFactory.New(),
            IndustrialTelemetryIntegrationEventTypes.AlarmRaised,
            IndustrialTelemetryIntegrationEventVersions.V1,
            alarm.RaisedAtUtc,
            IndustrialTelemetryIntegrationEventSources.IndustrialTelemetry,
            $"industrialTelemetry:alarm:{alarm.OrganizationId}:{alarm.EnvironmentId}:{alarm.Id.Id:D}",
            alarm.Id.Id.ToString("D"),
            alarm.OrganizationId,
            alarm.EnvironmentId,
            "system:industrial-telemetry",
            $"industrialTelemetry:alarm-raised:{alarm.OrganizationId}:{alarm.EnvironmentId}:{alarm.DeviceAssetId}:{alarm.AlarmCode}:{alarm.ExternalAlarmId}:{alarm.Id.Id:D}",
            new AlarmRaisedPayload(
                alarm.Id.Id.ToString("D"),
                alarm.DeviceAssetId,
                alarm.AlarmCode,
                alarm.Severity,
                alarm.RaisedAtUtc,
                alarm.ExternalAlarmId,
                alarm.Priority,
                alarm.TagKey,
                alarm.ObservedValue,
                alarm.ThresholdValue,
                alarm.UnitCode));
    }
}

public sealed class AlarmClearedIntegrationEventConverter
    : IIntegrationEventConverter<AlarmClearedDomainEvent, AlarmClearedIntegrationEvent>
{
    public AlarmClearedIntegrationEvent Convert(AlarmClearedDomainEvent domainEvent)
    {
        var alarm = domainEvent.AlarmEvent;
        return new AlarmClearedIntegrationEvent(
            EventIdFactory.New(),
            IndustrialTelemetryIntegrationEventTypes.AlarmCleared,
            IndustrialTelemetryIntegrationEventVersions.V1,
            alarm.ClearedAtUtc ?? throw new InvalidOperationException("Alarm clear event requires cleared time."),
            IndustrialTelemetryIntegrationEventSources.IndustrialTelemetry,
            $"industrialTelemetry:alarm:{alarm.OrganizationId}:{alarm.EnvironmentId}:{alarm.Id.Id:D}",
            alarm.Id.Id.ToString("D"),
            alarm.OrganizationId,
            alarm.EnvironmentId,
            "system:industrial-telemetry",
            $"industrialTelemetry:alarm-cleared:{alarm.OrganizationId}:{alarm.EnvironmentId}:{alarm.DeviceAssetId}:{alarm.AlarmCode}:{alarm.ExternalAlarmId}:{alarm.Id.Id:D}",
            new AlarmClearedPayload(
                alarm.Id.Id.ToString("D"),
                alarm.DeviceAssetId,
                alarm.AlarmCode,
                alarm.Severity,
                alarm.RaisedAtUtc,
                alarm.ClearedAtUtc.Value,
                alarm.ExternalAlarmId));
    }
}

public sealed class AlarmEscalatedIntegrationEventConverter
    : IIntegrationEventConverter<AlarmEscalatedDomainEvent, AlarmEscalatedIntegrationEvent>
{
    public AlarmEscalatedIntegrationEvent Convert(AlarmEscalatedDomainEvent domainEvent)
    {
        var alarm = domainEvent.AlarmEvent;
        return new AlarmEscalatedIntegrationEvent(
            EventIdFactory.New(),
            IndustrialTelemetryIntegrationEventTypes.AlarmEscalated,
            IndustrialTelemetryIntegrationEventVersions.V1,
            alarm.EscalatedAtUtc ?? throw new InvalidOperationException("Alarm escalation event requires escalated time."),
            IndustrialTelemetryIntegrationEventSources.IndustrialTelemetry,
            $"industrialTelemetry:alarm:{alarm.OrganizationId}:{alarm.EnvironmentId}:{alarm.Id.Id:D}",
            alarm.Id.Id.ToString("D"),
            alarm.OrganizationId,
            alarm.EnvironmentId,
            "system:industrial-telemetry",
            $"industrialTelemetry:alarm-escalated:{alarm.OrganizationId}:{alarm.EnvironmentId}:{alarm.DeviceAssetId}:{alarm.AlarmCode}:{alarm.ExternalAlarmId}:{alarm.Id.Id:D}",
            new AlarmEscalatedPayload(
                alarm.Id.Id.ToString("D"),
                alarm.DeviceAssetId,
                alarm.AlarmCode,
                alarm.Severity,
                alarm.RaisedAtUtc,
                alarm.EscalatedAtUtc.Value,
                alarm.ExternalAlarmId,
                alarm.EscalationReason ?? "alarm-escalated",
                alarm.EscalationRecipientRefs,
                alarm.Priority));
    }
}

internal static class EventIdFactory
{
    public static string New() => $"evt-{Guid.CreateVersion7():N}";
}
