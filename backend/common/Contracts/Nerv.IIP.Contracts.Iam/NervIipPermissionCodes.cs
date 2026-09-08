namespace Nerv.IIP.Contracts.Iam;

/// <summary>
/// 平台权限码的公开词表。常量名与 <c>BusinessGatewayPermissions</c> 一致，
/// 常量值与 IAM 权限 producer 的全集逐条相等。
/// 全集通过反射公开静态字符串常量获得，不额外维护数组。
/// </summary>
public static class NervIipPermissionCodes
{
    // iam.*
    public const string IamUsersRead = "iam.users.read";
    public const string IamUsersManage = "iam.users.manage";
    public const string IamRolesRead = "iam.roles.read";
    public const string IamRolesManage = "iam.roles.manage";
    public const string IamSessionsRead = "iam.sessions.read";
    public const string IamSessionsRevoke = "iam.sessions.revoke";
    public const string IamSecurityAuditRead = "iam.security-audit.read";

    // connectors.*
    public const string ConnectorsRegistrationsWrite = "connectors.registrations.write";
    public const string ConnectorsHeartbeatsWrite = "connectors.heartbeats.write";
    public const string ConnectorsStateSnapshotsWrite = "connectors.state-snapshots.write";

    // apphub.*
    public const string AppHubInstancesRead = "apphub.instances.read";

    // files.*
    public const string FilesUpload = "files.upload";
    public const string FilesRead = "files.read";
    public const string FilesDownloadGrantsCreate = "files.download-grants.create";
    public const string FilesArchive = "files.archive";

    // ops.*
    public const string OpsTasksCreate = "ops.tasks.create";
    public const string OpsTasksRead = "ops.tasks.read";
    public const string OpsResultsWrite = "ops.results.write";
    public const string OpsAuditRead = "ops.audit.read";

    // observability.*
    public const string ObservabilityLogsRead = "observability.logs.read";

    // business.masterdata.*
    public const string MasterDataProductsRead = "business.masterdata.products.read";
    public const string MasterDataProductsManage = "business.masterdata.products.manage";
    public const string MasterDataPartnersRead = "business.masterdata.partners.read";
    public const string MasterDataPartnersManage = "business.masterdata.partners.manage";
    public const string MasterDataResourcesRead = "business.masterdata.resources.read";
    public const string MasterDataResourcesManage = "business.masterdata.resources.manage";

    // business.quality.*
    public const string QualityInspectionPlansManage = "business.quality.inspection-plans.manage";
    public const string QualityInspectionRecordsCreate = "business.quality.inspection-records.create";
    public const string QualityInspectionRecordsRead = "business.quality.inspection-records.read";
    public const string QualityNcrRead = "business.quality.ncr.read";
    public const string QualityNcrManage = "business.quality.ncr.manage";

    // business.inventory.*
    public const string InventoryLocationsManage = "business.inventory.locations.manage";
    public const string InventoryMovementsCreate = "business.inventory.movements.create";
    public const string InventoryLedgerRead = "business.inventory.ledger.read";
    public const string InventoryCountsManage = "business.inventory.counts.manage";
    public const string InventoryExpiredStockOverride = "business.inventory.expired-stock.override";

    // business.mes.*
    public const string MesFoundationRead = "business.mes.foundation.read";
    public const string MesOverviewRead = "business.mes.overview.read";
    public const string MesPlansRead = "business.mes.plans.read";
    public const string MesWorkOrdersRead = "business.mes.work-orders.read";
    public const string MesWorkOrdersManage = "business.mes.work-orders.manage";
    public const string MesMaterialsRead = "business.mes.materials.read";
    public const string MesMaterialsManage = "business.mes.materials.manage";
    public const string MesDispatchRead = "business.mes.dispatch.read";
    public const string MesDispatchManage = "business.mes.dispatch.manage";
    public const string MesOperationsRead = "business.mes.operations.read";
    public const string MesOperationsManage = "business.mes.operations.manage";
    public const string MesReportingRead = "business.mes.reporting.read";
    public const string MesReportingWrite = "business.mes.reporting.write";
    public const string MesQualityRead = "business.mes.quality.read";
    public const string MesQualityWrite = "business.mes.quality.write";
    public const string MesReceiptsRead = "business.mes.receipts.read";
    public const string MesReceiptsManage = "business.mes.receipts.manage";
    public const string MesDowntimeRead = "business.mes.downtime.read";
    public const string MesDowntimeManage = "business.mes.downtime.manage";
    public const string MesHandoversRead = "business.mes.handovers.read";
    public const string MesHandoversManage = "business.mes.handovers.manage";
    public const string MesTraceabilityRead = "business.mes.traceability.read";
    public const string MesSchedulesRead = "business.mes.schedules.read";
    public const string MesSchedulesManage = "business.mes.schedules.manage";
    public const string MesCapacityRead = "business.mes.capacity.read";

    // business.engineering.*
    public const string EngineeringDocumentsRead = "business.engineering.documents.read";
    public const string EngineeringDocumentsManage = "business.engineering.documents.manage";
    public const string EngineeringItemsRead = "business.engineering.items.read";
    public const string EngineeringItemsManage = "business.engineering.items.manage";
    public const string EngineeringBomsRead = "business.engineering.boms.read";
    public const string EngineeringBomsManage = "business.engineering.boms.manage";
    public const string EngineeringRoutingsRead = "business.engineering.routings.read";
    public const string EngineeringRoutingsManage = "business.engineering.routings.manage";
    public const string EngineeringStandardOperationsRead = "business.engineering.standard-operations.read";
    public const string EngineeringStandardOperationsManage = "business.engineering.standard-operations.manage";
    public const string EngineeringProductionVersionsRead = "business.engineering.production-versions.read";
    public const string EngineeringProductionVersionsManage = "business.engineering.production-versions.manage";
    public const string EngineeringChangesRead = "business.engineering.changes.read";
    public const string EngineeringChangesManage = "business.engineering.changes.manage";

    // business.planning.*
    public const string PlanningDemandsRead = "business.planning.demands.read";
    public const string PlanningDemandsManage = "business.planning.demands.manage";
    public const string PlanningMpsRead = "business.planning.mps.read";
    public const string PlanningMpsManage = "business.planning.mps.manage";
    public const string PlanningMpsRelease = "business.planning.mps.release";
    public const string PlanningMrpRead = "business.planning.mrp.read";
    public const string PlanningMrpRun = "business.planning.mrp.run";
    public const string PlanningSuggestionsManage = "business.planning.suggestions.manage";

    // business.barcodes.*
    public const string BarcodeTemplatesManage = "business.barcodes.templates.manage";
    public const string BarcodePrint = "business.barcodes.print";
    public const string BarcodeScansWrite = "business.barcodes.scans.write";

    // business.approvals.*
    public const string ApprovalsRead = "business.approvals.read";
    public const string ApprovalsManage = "business.approvals.manage";

    // business.erp.*
    public const string ErpProcurementRead = "business.erp.procurement.read";
    public const string ErpProcurementManage = "business.erp.procurement.manage";
    public const string ErpSalesRead = "business.erp.sales.read";
    public const string ErpSalesManage = "business.erp.sales.manage";
    public const string ErpFinanceRead = "business.erp.finance.read";
    public const string ErpFinanceManage = "business.erp.finance.manage";

    // business.scheduling.*
    public const string SchedulingPlansRead = "business.scheduling.plans.read";
    public const string SchedulingPlansManage = "business.scheduling.plans.manage";
    public const string SchedulingPlansRelease = "business.scheduling.plans.release";

    // business.wms.*
    public const string WmsReceiptsRead = "business.wms.receipts.read";
    public const string WmsReceiptsManage = "business.wms.receipts.manage";
    public const string WmsShipmentsRead = "business.wms.shipments.read";
    public const string WmsShipmentsManage = "business.wms.shipments.manage";
    public const string WmsCountsRead = "business.wms.counts.read";
    public const string WmsAutomationManage = "business.wms.automation.manage";
    public const string WmsWorkPoolsManage = "business.wms.work-pools.manage";

    // business.iiot.*
    public const string IiotTagsManage = "business.iiot.tags.manage";
    public const string IiotAlarmRulesManage = "business.iiot.alarm-rules.manage";
    public const string IiotTelemetryRead = "business.iiot.telemetry.read";
    public const string IiotTelemetryWrite = "business.iiot.telemetry.write";
    public const string IiotDeviceControlWrite = "business.iiot.device-control.write";
    public const string IiotDeviceControlManage = "business.iiot.device-control.manage";
    public const string IiotDeviceControlRead = "business.iiot.device-control.read";
    public const string IiotAlarmsRead = "business.iiot.alarms.read";
    public const string IiotAlarmsWrite = "business.iiot.alarms.write";

    // business.maintenance.*
    public const string MaintenanceWorkOrdersRead = "business.maintenance.work-orders.read";
    public const string MaintenanceWorkOrdersManage = "business.maintenance.work-orders.manage";
    public const string MaintenancePlansRead = "business.maintenance.plans.read";
    public const string MaintenancePlansManage = "business.maintenance.plans.manage";
    public const string MaintenanceDowntimeReasonsRead = "business.maintenance.downtime-reasons.read";

    // notifications.*
    public const string NotificationIntentsSubmit = "notifications.intents.submit";
    public const string NotificationDlqRead = "notifications.dlq.read";
    public const string NotificationDlqManage = "notifications.dlq.manage";
    public const string NotificationMessagesRead = "notifications.messages.read";
    public const string NotificationMessagesMarkRead = "notifications.messages.mark-read";
    public const string NotificationTasksRead = "notifications.tasks.read";
    public const string NotificationDeliveryManage = "notifications.delivery.manage";
}
