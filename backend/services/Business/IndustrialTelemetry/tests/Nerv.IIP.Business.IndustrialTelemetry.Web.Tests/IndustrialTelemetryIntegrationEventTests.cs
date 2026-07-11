using System.Text.Json;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetrySummaryAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.DomainEvents;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.IndustrialTelemetry;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

public sealed class IndustrialTelemetryIntegrationEventTests
{
    [Fact]
    public void Production_count_delta_event_keeps_source_sequence_and_reporting_mode_in_stable_envelope()
    {
        var summary = TelemetrySummary.Record(
            "org-001",
            "env-dev",
            "DEV-PACK-01",
            "parts_count",
            DateTimeOffset.Parse("2026-07-11T08:00:00Z"),
            DateTimeOffset.Parse("2026-07-11T08:01:00Z"),
            1,
            103m,
            103m,
            103m,
            "seq-002",
            "opcua",
            "opcua-cell-01");

        var integrationEvent = new TelemetryProductionCountDeltaIntegrationEventConverter().Convert(
            new TelemetryProductionCountDeltaDomainEvent(summary, 3m, "posted", HasActiveAlarm: false));

        Assert.Equal(IndustrialTelemetryIntegrationEventTypes.ProductionCountDeltaRecorded, integrationEvent.EventType);
        Assert.Equal("posted", integrationEvent.Payload.ReportingMode);
        Assert.Equal("seq-002", integrationEvent.Payload.SourceSequence);
        Assert.Equal(3m, integrationEvent.Payload.DeltaQuantity);
        Assert.Equal("industrialTelemetry:production-count:org-001:env-dev:DEV-PACK-01:parts_count:opcua:opcua-cell-01:seq-002", integrationEvent.IdempotencyKey);
    }

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
    public void Device_state_changed_idempotency_key_distinguishes_distinct_source_scoped_snapshots()
    {
        var first = DeviceStateSnapshot.Record(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "running",
            DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
            "shared-seq-001",
            "SCADA-A",
            "opc-ua-cell-01");
        var differentConnector = DeviceStateSnapshot.Record(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "faulted",
            DateTimeOffset.Parse("2026-07-05T08:01:00Z"),
            "shared-seq-001",
            "SCADA-A",
            "opc-ua-cell-02");

        var firstEvent = new DeviceStateChangedIntegrationEventConverter().Convert(new DeviceStateChangedDomainEvent(first));
        var differentConnectorEvent = new DeviceStateChangedIntegrationEventConverter().Convert(new DeviceStateChangedDomainEvent(differentConnector));

        Assert.NotEqual(first.Id, differentConnector.Id);
        Assert.NotEqual(firstEvent.IdempotencyKey, differentConnectorEvent.IdempotencyKey);
        Assert.EndsWith(first.Id.Id.ToString("D"), firstEvent.IdempotencyKey, StringComparison.Ordinal);
        Assert.EndsWith(differentConnector.Id.Id.ToString("D"), differentConnectorEvent.IdempotencyKey, StringComparison.Ordinal);
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
        Assert.Equal($"industrialTelemetry:alarm-raised:org-001:env-dev:DEV-CNC-01:OVER_TEMP:alarm-ext-001:{alarm.Id.Id:D}", raised.IdempotencyKey);
        Assert.Equal($"industrialTelemetry:alarm-cleared:org-001:env-dev:DEV-CNC-01:OVER_TEMP:alarm-ext-001:{alarm.Id.Id:D}", cleared.IdempotencyKey);
        Assert.Contains("\"eventType\":\"industrialTelemetry.AlarmRaised\"", JsonSerializer.Serialize(raised, new JsonSerializerOptions(JsonSerializerDefaults.Web)), StringComparison.Ordinal);
        Assert.Contains("\"eventType\":\"industrialTelemetry.AlarmCleared\"", JsonSerializer.Serialize(cleared, new JsonSerializerOptions(JsonSerializerDefaults.Web)), StringComparison.Ordinal);
    }

    [Fact]
    public void Alarm_escalated_event_serializes_required_event_type_and_recipients()
    {
        var alarm = AlarmEvent.Raise(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "OVER_TEMP",
            "critical",
            DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            "alarm-ext-001",
            priority: "p1");
        alarm.Escalate(
            DateTimeOffset.Parse("2026-07-06T08:05:00Z"),
            "critical-severity",
            ["role:maintenance-manager", "user:lead-001"]);

        var escalated = new AlarmEscalatedIntegrationEventConverter().Convert(new AlarmEscalatedDomainEvent(alarm));
        var json = JsonSerializer.Serialize(escalated, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(IndustrialTelemetryIntegrationEventTypes.AlarmEscalated, escalated.EventType);
        Assert.Equal(alarm.Id.Id.ToString("D"), escalated.Payload.AlarmEventId);
        Assert.Equal("critical-severity", escalated.Payload.EscalationReason);
        Assert.Equal(["role:maintenance-manager", "user:lead-001"], escalated.Payload.RecipientRefs);
        Assert.Equal($"industrialTelemetry:alarm-escalated:org-001:env-dev:DEV-CNC-01:OVER_TEMP:alarm-ext-001:{alarm.Id.Id:D}", escalated.IdempotencyKey);
        Assert.Contains("\"eventType\":\"industrialTelemetry.AlarmEscalated\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Recurrent_alarm_events_with_same_external_alarm_id_have_distinct_idempotency_keys()
    {
        var first = AlarmEvent.Raise(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "OVER_TEMP",
            "critical",
            DateTimeOffset.Parse("2026-07-04T08:00:00Z"),
            "alarm-ext-001",
            priority: null,
            tagKey: "temperature",
            observedValue: 96.5m,
            thresholdValue: 90m,
            unitCode: "celsius");
        var recurrent = AlarmEvent.Raise(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "OVER_TEMP",
            "critical",
            DateTimeOffset.Parse("2026-07-04T09:00:00Z"),
            "alarm-ext-001",
            priority: null,
            tagKey: "temperature",
            observedValue: 97.5m,
            thresholdValue: 90m,
            unitCode: "celsius");

        var firstEvent = new AlarmRaisedIntegrationEventConverter().Convert(new AlarmRaisedDomainEvent(first));
        var recurrentEvent = new AlarmRaisedIntegrationEventConverter().Convert(new AlarmRaisedDomainEvent(recurrent));

        Assert.NotEqual(first.Id, recurrent.Id);
        Assert.NotEqual(firstEvent.IdempotencyKey, recurrentEvent.IdempotencyKey);
        Assert.EndsWith(first.Id.Id.ToString("D"), firstEvent.IdempotencyKey, StringComparison.Ordinal);
        Assert.EndsWith(recurrent.Id.Id.ToString("D"), recurrentEvent.IdempotencyKey, StringComparison.Ordinal);
    }
}
