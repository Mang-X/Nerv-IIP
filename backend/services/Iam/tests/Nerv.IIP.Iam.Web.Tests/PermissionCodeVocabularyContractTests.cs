using System.Reflection;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.Iam.Domain;

namespace Nerv.IIP.Iam.Web.Tests;

/// <summary>
/// #3040 D1：把 <see cref="NervIipPermissionCodes"/> 钉成后续迁移的前置证明。
/// 期望表在本测试内写死，新增/删除/改值常量而不登记即红。
/// </summary>
public sealed class PermissionCodeVocabularyContractTests
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedPermissionCodes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AppHubInstancesRead"] = "apphub.instances.read",
            ["ApprovalsManage"] = "business.approvals.manage",
            ["ApprovalsRead"] = "business.approvals.read",
            ["BarcodePrint"] = "business.barcodes.print",
            ["BarcodeScansWrite"] = "business.barcodes.scans.write",
            ["BarcodeTemplatesManage"] = "business.barcodes.templates.manage",
            ["ConnectorsHeartbeatsWrite"] = "connectors.heartbeats.write",
            ["ConnectorsRegistrationsWrite"] = "connectors.registrations.write",
            ["ConnectorsStateSnapshotsWrite"] = "connectors.state-snapshots.write",
            ["EngineeringBomsManage"] = "business.engineering.boms.manage",
            ["EngineeringBomsRead"] = "business.engineering.boms.read",
            ["EngineeringChangesManage"] = "business.engineering.changes.manage",
            ["EngineeringChangesRead"] = "business.engineering.changes.read",
            ["EngineeringDocumentsManage"] = "business.engineering.documents.manage",
            ["EngineeringDocumentsRead"] = "business.engineering.documents.read",
            ["EngineeringItemsManage"] = "business.engineering.items.manage",
            ["EngineeringItemsRead"] = "business.engineering.items.read",
            ["EngineeringProductionVersionsManage"] = "business.engineering.production-versions.manage",
            ["EngineeringProductionVersionsRead"] = "business.engineering.production-versions.read",
            ["EngineeringRoutingsManage"] = "business.engineering.routings.manage",
            ["EngineeringRoutingsRead"] = "business.engineering.routings.read",
            ["EngineeringStandardOperationsManage"] = "business.engineering.standard-operations.manage",
            ["EngineeringStandardOperationsRead"] = "business.engineering.standard-operations.read",
            ["ErpFinanceManage"] = "business.erp.finance.manage",
            ["ErpFinanceRead"] = "business.erp.finance.read",
            ["ErpProcurementManage"] = "business.erp.procurement.manage",
            ["ErpProcurementRead"] = "business.erp.procurement.read",
            ["ErpSalesManage"] = "business.erp.sales.manage",
            ["ErpSalesRead"] = "business.erp.sales.read",
            ["FilesArchive"] = "files.archive",
            ["FilesDownloadGrantsCreate"] = "files.download-grants.create",
            ["FilesRead"] = "files.read",
            ["FilesUpload"] = "files.upload",
            ["IamRolesManage"] = "iam.roles.manage",
            ["IamRolesRead"] = "iam.roles.read",
            ["IamSecurityAuditRead"] = "iam.security-audit.read",
            ["IamSessionsRead"] = "iam.sessions.read",
            ["IamSessionsRevoke"] = "iam.sessions.revoke",
            ["IamUsersManage"] = "iam.users.manage",
            ["IamUsersRead"] = "iam.users.read",
            ["IiotAlarmRulesManage"] = "business.iiot.alarm-rules.manage",
            ["IiotAlarmsRead"] = "business.iiot.alarms.read",
            ["IiotAlarmsWrite"] = "business.iiot.alarms.write",
            ["IiotDeviceControlManage"] = "business.iiot.device-control.manage",
            ["IiotDeviceControlRead"] = "business.iiot.device-control.read",
            ["IiotDeviceControlWrite"] = "business.iiot.device-control.write",
            ["IiotTagsManage"] = "business.iiot.tags.manage",
            ["IiotTelemetryRead"] = "business.iiot.telemetry.read",
            ["IiotTelemetryWrite"] = "business.iiot.telemetry.write",
            ["InventoryCountsManage"] = "business.inventory.counts.manage",
            ["InventoryExpiredStockOverride"] = "business.inventory.expired-stock.override",
            ["InventoryLedgerRead"] = "business.inventory.ledger.read",
            ["InventoryLocationsManage"] = "business.inventory.locations.manage",
            ["InventoryMovementsCreate"] = "business.inventory.movements.create",
            ["MaintenanceDowntimeReasonsRead"] = "business.maintenance.downtime-reasons.read",
            ["MaintenancePlansManage"] = "business.maintenance.plans.manage",
            ["MaintenancePlansRead"] = "business.maintenance.plans.read",
            ["MaintenanceWorkOrdersManage"] = "business.maintenance.work-orders.manage",
            ["MaintenanceWorkOrdersRead"] = "business.maintenance.work-orders.read",
            ["MasterDataPartnersManage"] = "business.masterdata.partners.manage",
            ["MasterDataPartnersRead"] = "business.masterdata.partners.read",
            ["MasterDataProductsManage"] = "business.masterdata.products.manage",
            ["MasterDataProductsRead"] = "business.masterdata.products.read",
            ["MasterDataResourcesManage"] = "business.masterdata.resources.manage",
            ["MasterDataResourcesRead"] = "business.masterdata.resources.read",
            ["MesCapacityRead"] = "business.mes.capacity.read",
            ["MesDispatchManage"] = "business.mes.dispatch.manage",
            ["MesDispatchRead"] = "business.mes.dispatch.read",
            ["MesDowntimeManage"] = "business.mes.downtime.manage",
            ["MesDowntimeRead"] = "business.mes.downtime.read",
            ["MesFoundationRead"] = "business.mes.foundation.read",
            ["MesHandoversManage"] = "business.mes.handovers.manage",
            ["MesHandoversRead"] = "business.mes.handovers.read",
            ["MesMaterialsManage"] = "business.mes.materials.manage",
            ["MesMaterialsRead"] = "business.mes.materials.read",
            ["MesOperationsManage"] = "business.mes.operations.manage",
            ["MesOperationsRead"] = "business.mes.operations.read",
            ["MesOverviewRead"] = "business.mes.overview.read",
            ["MesPlansRead"] = "business.mes.plans.read",
            ["MesQualityRead"] = "business.mes.quality.read",
            ["MesQualityWrite"] = "business.mes.quality.write",
            ["MesReceiptsManage"] = "business.mes.receipts.manage",
            ["MesReceiptsRead"] = "business.mes.receipts.read",
            ["MesReportingRead"] = "business.mes.reporting.read",
            ["MesReportingWrite"] = "business.mes.reporting.write",
            ["MesSchedulesManage"] = "business.mes.schedules.manage",
            ["MesSchedulesRead"] = "business.mes.schedules.read",
            ["MesTraceabilityRead"] = "business.mes.traceability.read",
            ["MesWorkOrdersManage"] = "business.mes.work-orders.manage",
            ["MesWorkOrdersRead"] = "business.mes.work-orders.read",
            ["NotificationDeliveryManage"] = "notifications.delivery.manage",
            ["NotificationDlqManage"] = "notifications.dlq.manage",
            ["NotificationDlqRead"] = "notifications.dlq.read",
            ["NotificationIntentsSubmit"] = "notifications.intents.submit",
            ["NotificationMessagesMarkRead"] = "notifications.messages.mark-read",
            ["NotificationMessagesRead"] = "notifications.messages.read",
            ["NotificationTasksRead"] = "notifications.tasks.read",
            ["ObservabilityLogsRead"] = "observability.logs.read",
            ["OpsAuditRead"] = "ops.audit.read",
            ["OpsResultsWrite"] = "ops.results.write",
            ["OpsTasksCreate"] = "ops.tasks.create",
            ["OpsTasksRead"] = "ops.tasks.read",
            ["PlanningDemandsManage"] = "business.planning.demands.manage",
            ["PlanningDemandsRead"] = "business.planning.demands.read",
            ["PlanningMpsManage"] = "business.planning.mps.manage",
            ["PlanningMpsRead"] = "business.planning.mps.read",
            ["PlanningMpsRelease"] = "business.planning.mps.release",
            ["PlanningMrpRead"] = "business.planning.mrp.read",
            ["PlanningMrpRun"] = "business.planning.mrp.run",
            ["PlanningSuggestionsManage"] = "business.planning.suggestions.manage",
            ["QualityInspectionPlansManage"] = "business.quality.inspection-plans.manage",
            ["QualityInspectionRecordsCreate"] = "business.quality.inspection-records.create",
            ["QualityInspectionRecordsRead"] = "business.quality.inspection-records.read",
            ["QualityNcrManage"] = "business.quality.ncr.manage",
            ["QualityNcrRead"] = "business.quality.ncr.read",
            ["SchedulingPlansManage"] = "business.scheduling.plans.manage",
            ["SchedulingPlansRead"] = "business.scheduling.plans.read",
            ["SchedulingPlansRelease"] = "business.scheduling.plans.release",
            ["WmsAutomationManage"] = "business.wms.automation.manage",
            ["WmsCountsRead"] = "business.wms.counts.read",
            ["WmsReceiptsManage"] = "business.wms.receipts.manage",
            ["WmsReceiptsRead"] = "business.wms.receipts.read",
            ["WmsShipmentsManage"] = "business.wms.shipments.manage",
            ["WmsShipmentsRead"] = "business.wms.shipments.read",
            ["WmsWorkPoolsManage"] = "business.wms.work-pools.manage",
        };

    [Fact]
    public void Contracts_permission_code_table_matches_the_frozen_vocabulary()
    {
        Assert.Equal(ExpectedPermissionCodes, PublicStringConstantsOf(typeof(NervIipPermissionCodes)));
    }

    [Fact]
    public void Contracts_permission_code_table_equals_the_iam_seed_permission_producer()
    {
        var contractCodes = PublicStringConstantsOf(typeof(NervIipPermissionCodes))
            .Values
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();
        var seedCodes = NervIipSeedPermissions.All
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(seedCodes, contractCodes);
    }

    private static IReadOnlyDictionary<string, string> PublicStringConstantsOf(Type type)
    {
        return type
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .OrderBy(field => field.Name, StringComparer.Ordinal)
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!, StringComparer.Ordinal);
    }
}
