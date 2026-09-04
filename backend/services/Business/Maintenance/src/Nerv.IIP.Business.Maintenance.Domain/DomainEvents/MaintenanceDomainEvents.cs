using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceInspectionAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;

namespace Nerv.IIP.Business.Maintenance.Domain.DomainEvents;

public sealed record MaintenanceWorkOrderOpenedDomainEvent(MaintenanceWorkOrder WorkOrder) : IDomainEvent;

public sealed record AssetUnavailableDomainEvent(MaintenanceWorkOrder WorkOrder, string Reason, DateTimeOffset FromUtc) : IDomainEvent;

/// <summary>
/// v2 入口按 organization/environment 动态 <c>downtime-reason</c> 目录精确命中后标记的不可用事实（#2964 C/D 阶段）。
/// <paramref name="ReasonCode"/> 是请求原值（不 trim、不改大小写）。它与 <see cref="AssetUnavailableDomainEvent"/> 是两个
/// 独立的领域事实：v1 自由文本事实只发布 v1 envelope；本事实由 Web 层 publisher 双发 v1 companion + v2 canonical envelope。
/// </summary>
public sealed record AssetUnavailableByReasonCodeDomainEvent(MaintenanceWorkOrder WorkOrder, string ReasonCode, DateTimeOffset FromUtc) : IDomainEvent;

public sealed record MaintenanceWorkOrderCompletedDomainEvent(MaintenanceWorkOrder WorkOrder) : IDomainEvent;

public sealed record AssetRestoredDomainEvent(MaintenanceWorkOrder WorkOrder, DateTimeOffset RestoredAtUtc) : IDomainEvent;

public sealed record MaintenanceWorkOrderAlarmClearedDomainEvent(MaintenanceWorkOrder WorkOrder, DateTimeOffset ClearedAtUtc) : IDomainEvent;

public sealed record MaintenanceSparePartIssuedDomainEvent(MaintenanceWorkOrder WorkOrder, SparePartLine SparePartLine) : IDomainEvent;

public sealed record MaintenancePlanCreatedDomainEvent(MaintenancePlan Plan) : IDomainEvent;

public sealed record MaintenanceInspectionRecordedDomainEvent(MaintenanceInspection Inspection) : IDomainEvent;
