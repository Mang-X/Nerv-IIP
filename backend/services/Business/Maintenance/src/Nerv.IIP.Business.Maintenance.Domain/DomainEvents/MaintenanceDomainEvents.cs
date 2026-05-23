using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceInspectionAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;

namespace Nerv.IIP.Business.Maintenance.Domain.DomainEvents;

public sealed record MaintenanceWorkOrderOpenedDomainEvent(MaintenanceWorkOrder WorkOrder) : IDomainEvent;

public sealed record AssetUnavailableDomainEvent(MaintenanceWorkOrder WorkOrder, string Reason, DateTimeOffset FromUtc) : IDomainEvent;

public sealed record MaintenanceWorkOrderCompletedDomainEvent(MaintenanceWorkOrder WorkOrder) : IDomainEvent;

public sealed record AssetRestoredDomainEvent(MaintenanceWorkOrder WorkOrder, DateTimeOffset RestoredAtUtc) : IDomainEvent;

public sealed record MaintenancePlanCreatedDomainEvent(MaintenancePlan Plan) : IDomainEvent;

public sealed record MaintenanceInspectionRecordedDomainEvent(MaintenanceInspection Inspection) : IDomainEvent;
