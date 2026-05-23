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
            snapshot.Id.Id.ToString("D"),
            snapshot.OrganizationId,
            snapshot.EnvironmentId,
            snapshot.DeviceAssetId,
            snapshot.State,
            snapshot.OccurredAtUtc,
            snapshot.SourceSequence);
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
            alarm.Id.Id.ToString("D"),
            alarm.OrganizationId,
            alarm.EnvironmentId,
            alarm.DeviceAssetId,
            alarm.AlarmCode,
            alarm.Severity,
            alarm.RaisedAtUtc,
            alarm.ExternalAlarmId);
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
            alarm.Id.Id.ToString("D"),
            alarm.OrganizationId,
            alarm.EnvironmentId,
            alarm.DeviceAssetId,
            alarm.AlarmCode,
            alarm.Severity,
            alarm.RaisedAtUtc,
            alarm.ClearedAtUtc ?? throw new InvalidOperationException("Alarm clear event requires cleared time."),
            alarm.ExternalAlarmId);
    }
}

internal static class EventIdFactory
{
    public static string New() => $"evt-{Guid.CreateVersion7():N}";
}
