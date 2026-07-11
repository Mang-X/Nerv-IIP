using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmRuleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetrySummaryAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;

namespace Nerv.IIP.Business.IndustrialTelemetry.Domain.DomainEvents;

public sealed record TelemetryTagCreatedDomainEvent(TelemetryTag TelemetryTag) : IDomainEvent;

public sealed record AlarmRuleConfiguredDomainEvent(AlarmRule AlarmRule) : IDomainEvent;

public sealed record TelemetrySampleRecordedDomainEvent(TelemetrySummary TelemetrySummary) : IDomainEvent;

public sealed record TelemetryProductionCountDeltaDomainEvent(
    TelemetrySummary TelemetrySummary,
    decimal DeltaQuantity,
    string ReportingMode,
    bool HasActiveAlarm) : IDomainEvent;

public sealed record DeviceStateChangedDomainEvent(DeviceStateSnapshot DeviceStateSnapshot) : IDomainEvent;

public sealed record AlarmRaisedDomainEvent(AlarmEvent AlarmEvent) : IDomainEvent;

public sealed record AlarmClearedDomainEvent(AlarmEvent AlarmEvent) : IDomainEvent;

public sealed record AlarmAcknowledgedDomainEvent(AlarmEvent AlarmEvent) : IDomainEvent;

public sealed record AlarmShelvedDomainEvent(AlarmEvent AlarmEvent) : IDomainEvent;

public sealed record AlarmUnshelvedDomainEvent(AlarmEvent AlarmEvent) : IDomainEvent;

public sealed record AlarmEscalatedDomainEvent(AlarmEvent AlarmEvent) : IDomainEvent;
