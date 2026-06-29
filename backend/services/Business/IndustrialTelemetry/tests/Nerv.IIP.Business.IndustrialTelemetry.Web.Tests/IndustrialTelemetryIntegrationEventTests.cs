using System.Text.Json;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.DomainEvents;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.IndustrialTelemetry;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

public sealed class IndustrialTelemetryIntegrationEventTests
{
    [Fact]
    public void Device_state_changed_event_serializes_required_event_type()
    {
        var snapshot = DeviceStateSnapshot.Record("org-001", "env-dev", "DEV-CNC-01", "running", DateTimeOffset.UtcNow, "seq-state-001");
        var integrationEvent = new DeviceStateChangedIntegrationEventConverter().Convert(new DeviceStateChangedDomainEvent(snapshot));

        var json = JsonSerializer.Serialize(integrationEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(IndustrialTelemetryIntegrationEventTypes.DeviceStateChanged, integrationEvent.EventType);
        Assert.Contains("\"eventType\":\"industrialTelemetry.DeviceStateChanged\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Alarm_events_serialize_required_event_types()
    {
        var alarm = AlarmEvent.Raise(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "OVER_TEMP",
            "critical",
            DateTimeOffset.UtcNow,
            "alarm-ext-001",
            priority: "p1",
            tagKey: "temperature",
            observedValue: 96.5m,
            thresholdValue: 90m,
            unitCode: "celsius");
        var raised = new AlarmRaisedIntegrationEventConverter().Convert(new AlarmRaisedDomainEvent(alarm));
        alarm.Clear(alarm.RaisedAtUtc.AddMinutes(5), "operator-001", "normalized");
        var cleared = new AlarmClearedIntegrationEventConverter().Convert(new AlarmClearedDomainEvent(alarm));

        Assert.Equal(IndustrialTelemetryIntegrationEventTypes.AlarmRaised, raised.EventType);
        Assert.Equal(IndustrialTelemetryIntegrationEventTypes.AlarmCleared, cleared.EventType);
        Assert.Equal(alarm.Id.Id.ToString("D"), raised.Payload.AlarmEventId);
        Assert.Equal("p1", raised.Payload.Priority);
        Assert.Equal("temperature", raised.Payload.TagKey);
        Assert.Equal(96.5m, raised.Payload.ObservedValue);
        Assert.Equal(90m, raised.Payload.ThresholdValue);
        Assert.Equal("celsius", raised.Payload.UnitCode);
        Assert.Equal("industrialTelemetry:alarm-raised:org-001:env-dev:DEV-CNC-01:OVER_TEMP:alarm-ext-001", raised.IdempotencyKey);
        Assert.Equal("industrialTelemetry:alarm-cleared:org-001:env-dev:DEV-CNC-01:OVER_TEMP:alarm-ext-001", cleared.IdempotencyKey);
        Assert.Contains("\"eventType\":\"industrialTelemetry.AlarmRaised\"", JsonSerializer.Serialize(raised, new JsonSerializerOptions(JsonSerializerDefaults.Web)), StringComparison.Ordinal);
        Assert.Contains("\"eventType\":\"industrialTelemetry.AlarmCleared\"", JsonSerializer.Serialize(cleared, new JsonSerializerOptions(JsonSerializerDefaults.Web)), StringComparison.Ordinal);
    }
}
