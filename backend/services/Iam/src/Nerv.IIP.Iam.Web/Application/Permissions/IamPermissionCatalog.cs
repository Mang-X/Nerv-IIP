using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.Iam.Domain;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Permissions;

public sealed record PermissionCatalogResponse(IReadOnlyList<PermissionCatalogItemResponse> Items);
public sealed record PermissionCatalogItemResponse(string Code, string Domain, string Description, bool Seeded);

public static class IamPermissionCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [NervIipPermissionCodes.IamUsersRead] = "Read IAM users.",
        [NervIipPermissionCodes.IamUsersManage] = "Create, update, disable and reset IAM users.",
        [NervIipPermissionCodes.IamRolesRead] = "Read IAM roles and permission catalog.",
        [NervIipPermissionCodes.IamRolesManage] = "Create IAM roles and update role permissions.",
        [NervIipPermissionCodes.IamSessionsRead] = "Read IAM user sessions.",
        [NervIipPermissionCodes.IamSessionsRevoke] = "Revoke IAM user sessions.",
        [NervIipPermissionCodes.IamSecurityAuditRead] = "Read IAM security audit records.",
        [NervIipPermissionCodes.ConnectorsRegistrationsWrite] = "Register connector hosts.",
        [NervIipPermissionCodes.ConnectorsHeartbeatsWrite] = "Write connector host heartbeats.",
        [NervIipPermissionCodes.ConnectorsStateSnapshotsWrite] = "Write connector host state snapshots.",
        [NervIipPermissionCodes.AppHubInstancesRead] = "Read AppHub application instances.",
        [NervIipPermissionCodes.FilesUpload] = "Upload files.",
        [NervIipPermissionCodes.FilesRead] = "Read file metadata.",
        [NervIipPermissionCodes.FilesDownloadGrantsCreate] = "Create file download grants.",
        [NervIipPermissionCodes.FilesArchive] = "Archive files.",
        [NervIipPermissionCodes.OpsTasksCreate] = "Create operation tasks.",
        [NervIipPermissionCodes.OpsTasksRead] = "Read operation tasks.",
        [NervIipPermissionCodes.OpsResultsWrite] = "Write operation results.",
        [NervIipPermissionCodes.OpsAuditRead] = "Read operation audit records.",
        [NervIipPermissionCodes.ObservabilityLogsRead] = "Query centralized platform logs.",
        [NervIipPermissionCodes.NotificationIntentsSubmit] = "Submit notification intents.",
        [NervIipPermissionCodes.NotificationDlqRead] = "Read notification integration event dead letters.",
        [NervIipPermissionCodes.NotificationDlqManage] = "Replay and ignore notification integration event dead letters.",
        [NervIipPermissionCodes.NotificationMessagesRead] = "Read notification messages.",
        [NervIipPermissionCodes.NotificationMessagesMarkRead] = "Mark notification messages as read.",
        [NervIipPermissionCodes.NotificationTasksRead] = "Read notification tasks.",
        [NervIipPermissionCodes.NotificationDeliveryManage] = "Manage notification delivery preferences, subscriptions and recipient channel bindings.",
        [NervIipPermissionCodes.QualityInspectionPlansManage] = "Create, activate and supersede quality inspection plans.",
        [NervIipPermissionCodes.QualityInspectionRecordsCreate] = "Create quality inspection records.",
        [NervIipPermissionCodes.QualityInspectionRecordsRead] = "Read quality inspection plans and records.",
        [NervIipPermissionCodes.QualityNcrRead] = "Read quality nonconformance reports.",
        [NervIipPermissionCodes.QualityNcrManage] = "Create, disposition and close quality nonconformance reports.",
        [NervIipPermissionCodes.InventoryLocationsManage] = "Create and manage inventory locations.",
        [NervIipPermissionCodes.InventoryMovementsCreate] = "Create inventory stock movements.",
        [NervIipPermissionCodes.InventoryLedgerRead] = "Read inventory ledger balances and reports.",
        [NervIipPermissionCodes.InventoryCountsManage] = "Create and complete inventory counts.",
        [NervIipPermissionCodes.InventoryExpiredStockOverride] = "Override the expiry block when issuing or reserving expired stock.",
        [NervIipPermissionCodes.MesFoundationRead] = "Read MES foundation readiness.",
        [NervIipPermissionCodes.MesOverviewRead] = "Read MES execution overview.",
        [NervIipPermissionCodes.MesPlansRead] = "Read MES production plans and readiness.",
        [NervIipPermissionCodes.MesWorkOrdersRead] = "Read MES work orders.",
        [NervIipPermissionCodes.MesWorkOrdersManage] = "Create, release and close MES work orders.",
        [NervIipPermissionCodes.MesMaterialsRead] = "Read MES material readiness and issue requests.",
        [NervIipPermissionCodes.MesMaterialsManage] = "Create MES material issue requests and confirm line-side receipts.",
        [NervIipPermissionCodes.MesDispatchRead] = "Read MES dispatch tasks.",
        [NervIipPermissionCodes.MesDispatchManage] = "Assign MES dispatch tasks.",
        [NervIipPermissionCodes.MesOperationsRead] = "Read MES operation tasks and WIP summaries.",
        [NervIipPermissionCodes.MesOperationsManage] = "Start, pause, resume and complete MES operation tasks.",
        [NervIipPermissionCodes.MesReportingRead] = "Read MES production reports.",
        [NervIipPermissionCodes.MesReportingWrite] = "Submit MES production reports.",
        [NervIipPermissionCodes.MesQualityRead] = "Read MES in-process quality context.",
        [NervIipPermissionCodes.MesQualityWrite] = "Record MES in-process defects.",
        [NervIipPermissionCodes.MesReceiptsRead] = "Read MES finished-goods receipt requests.",
        [NervIipPermissionCodes.MesReceiptsManage] = "Create MES finished-goods receipt requests.",
        [NervIipPermissionCodes.MesDowntimeRead] = "Read MES downtime events.",
        [NervIipPermissionCodes.MesDowntimeManage] = "Record and recover MES downtime events.",
        [NervIipPermissionCodes.MesHandoversRead] = "Read MES shift handovers.",
        [NervIipPermissionCodes.MesHandoversManage] = "Create and accept MES shift handovers.",
        [NervIipPermissionCodes.MesTraceabilityRead] = "Read MES work order, batch and material-lot traceability.",
        [NervIipPermissionCodes.MesSchedulesRead] = "Read MES schedule versions.",
        [NervIipPermissionCodes.MesSchedulesManage] = "Run and manage MES schedule versions.",
        [NervIipPermissionCodes.MesCapacityRead] = "Read MES capacity impact records.",
        [NervIipPermissionCodes.PlanningDemandsRead] = "Read demand sources for MPS and MRP.",
        [NervIipPermissionCodes.PlanningDemandsManage] = "Create and adjust demand sources.",
        [NervIipPermissionCodes.PlanningMpsRead] = "Read master production schedule buckets.",
        [NervIipPermissionCodes.PlanningMpsManage] = "Create, update and review master production schedule buckets.",
        [NervIipPermissionCodes.PlanningMpsRelease] = "Release reviewed master production schedule buckets into MRP input.",
        [NervIipPermissionCodes.PlanningMrpRead] = "Read MPS, MRP runs and pegging.",
        [NervIipPermissionCodes.PlanningMrpRun] = "Run MPS and MRP calculations.",
        [NervIipPermissionCodes.PlanningSuggestionsManage] = "Accept, reject or close planning suggestions.",
        [NervIipPermissionCodes.BarcodeTemplatesManage] = "Manage barcode rules and label templates.",
        [NervIipPermissionCodes.BarcodePrint] = "Generate and print labels.",
        [NervIipPermissionCodes.BarcodeScansWrite] = "Write barcode scan records.",
        [NervIipPermissionCodes.ApprovalsRead] = "Read business approval templates, chains and tasks.",
        [NervIipPermissionCodes.ApprovalsManage] = "Create and resolve business approval chains.",
        [NervIipPermissionCodes.ErpProcurementRead] = "Read ERP procurement requisitions, RFQs, quotations, purchase orders and receipts.",
        [NervIipPermissionCodes.ErpProcurementManage] = "Create and progress ERP procurement documents.",
        [NervIipPermissionCodes.ErpSalesRead] = "Read ERP opportunities, quotations, sales orders and delivery requests.",
        [NervIipPermissionCodes.ErpSalesManage] = "Create and progress ERP sales documents.",
        [NervIipPermissionCodes.ErpFinanceRead] = "Read ERP payables, receivables, vouchers and finance summaries.",
        [NervIipPermissionCodes.ErpFinanceManage] = "Create ERP finance candidates and post balanced vouchers.",
        [NervIipPermissionCodes.SchedulingPlansRead] = "Read APS lite scheduling plans, resource loads, conflicts and Gantt DTOs.",
        [NervIipPermissionCodes.SchedulingPlansManage] = "Preview and generate APS lite scheduling plans.",
        [NervIipPermissionCodes.SchedulingPlansRelease] = "Release generated APS lite scheduling plans for downstream MES consumption.",
        [NervIipPermissionCodes.WmsReceiptsRead] = "Read WMS receipts, inbound orders and putaway tasks.",
        [NervIipPermissionCodes.WmsReceiptsManage] = "Create and complete WMS receipt and putaway work.",
        [NervIipPermissionCodes.WmsShipmentsRead] = "Read WMS shipments, outbound orders and picking tasks.",
        [NervIipPermissionCodes.WmsShipmentsManage] = "Create and complete WMS shipment and picking work.",
        [NervIipPermissionCodes.WmsCountsRead] = "Read WMS count executions and operational candidates.",
        [NervIipPermissionCodes.WmsAutomationManage] = "Dispatch and complete WMS automation tasks.",
        [NervIipPermissionCodes.WmsWorkPoolsManage] = "Provision WMS field work pools and their operator memberships.",
        [NervIipPermissionCodes.IiotTagsManage] = "Manage IndustrialTelemetry tag mappings and sampling policy.",
        [NervIipPermissionCodes.IiotAlarmRulesManage] = "Manage IndustrialTelemetry alarm rule thresholds.",
        [NervIipPermissionCodes.IiotTelemetryRead] = "Read IndustrialTelemetry device snapshots and summaries.",
        [NervIipPermissionCodes.IiotTelemetryWrite] = "Write IndustrialTelemetry samples and device state snapshots.",
        [NervIipPermissionCodes.IiotDeviceControlWrite] = "Submit approval-gated IndustrialTelemetry device control commands.",
        [NervIipPermissionCodes.IiotDeviceControlManage] = "Maintain IndustrialTelemetry device control channel bindings (device to connector host/instance routing).",
        [NervIipPermissionCodes.IiotDeviceControlRead] = "Read IndustrialTelemetry device control command result and history.",
        [NervIipPermissionCodes.IiotAlarmsRead] = "Read IndustrialTelemetry alarm events.",
        [NervIipPermissionCodes.IiotAlarmsWrite] = "Raise and clear IndustrialTelemetry alarms.",
        [NervIipPermissionCodes.MaintenanceWorkOrdersRead] = "Read maintenance work orders and downtime facts.",
        [NervIipPermissionCodes.MaintenanceWorkOrdersManage] = "Create, update and complete maintenance work orders.",
        [NervIipPermissionCodes.MaintenancePlansRead] = "Read maintenance plans and inspections.",
        [NervIipPermissionCodes.MaintenancePlansManage] = "Create and manage maintenance plans and inspections.",
        [NervIipPermissionCodes.MaintenanceDowntimeReasonsRead] = "Read the maintenance downtime reason reference vocabulary.",
        [NervIipPermissionCodes.EngineeringDocumentsRead] = "Read engineering documents and current SOP documents.",
        [NervIipPermissionCodes.EngineeringDocumentsManage] = "Register engineering documents and publish SOP documents.",
        [NervIipPermissionCodes.EngineeringItemsRead] = "Read engineering item revisions.",
        [NervIipPermissionCodes.EngineeringItemsManage] = "Create engineering item revisions.",
        [NervIipPermissionCodes.EngineeringBomsRead] = "Read engineering and manufacturing BOMs, explosions, where-used and BOM diffs.",
        [NervIipPermissionCodes.EngineeringBomsManage] = "Release engineering and manufacturing BOMs.",
        [NervIipPermissionCodes.EngineeringRoutingsRead] = "Read engineering routings.",
        [NervIipPermissionCodes.EngineeringRoutingsManage] = "Release engineering routings.",
        [NervIipPermissionCodes.EngineeringStandardOperationsRead] = "Read standard operations.",
        [NervIipPermissionCodes.EngineeringStandardOperationsManage] = "Create, update and archive standard operations.",
        [NervIipPermissionCodes.EngineeringChangesRead] = "Read engineering changes and preview change impact.",
        [NervIipPermissionCodes.EngineeringChangesManage] = "Release, reschedule and cancel scheduled engineering changes.",
        [NervIipPermissionCodes.EngineeringProductionVersionsRead] = "Read production versions and their routing snapshots.",
        [NervIipPermissionCodes.EngineeringProductionVersionsManage] = "Create, update and archive production versions.",
        [NervIipPermissionCodes.MasterDataProductsRead] = "Read product categories.",
        [NervIipPermissionCodes.MasterDataProductsManage] = "Create SKUs, units of measure and UOM conversions, and manage product categories.",
        [NervIipPermissionCodes.MasterDataPartnersRead] = "Read business partner credit.",
        [NervIipPermissionCodes.MasterDataPartnersManage] = "Create business partners.",
        [NervIipPermissionCodes.MasterDataResourcesRead] = "Read organizational and operational master data such as workers, teams, skills, tooling assets and code rules.",
        [NervIipPermissionCodes.MasterDataResourcesManage] = "Create and manage organizational and operational master data such as departments, teams, workshops, workers, shifts, work centers, device and tooling assets, skills and code rules."
    };

    private static readonly HashSet<string> SeedCodes = NervIipSeedPermissions.All.ToHashSet(StringComparer.Ordinal);

    public static PermissionCatalogResponse List()
    {
        var items = NervIipSeedPermissions.All
            .Select(code => new PermissionCatalogItemResponse(
                code,
                GetDomain(code),
                Descriptions.GetValueOrDefault(code, code),
                true))
            .ToArray();
        return new PermissionCatalogResponse(items);
    }

    public static string[] EnsureSeeded(IEnumerable<string> permissionCodes)
    {
        var codes = permissionCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var unknown = codes
            .Where(code => !SeedCodes.Contains(code))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unknown.Length > 0)
        {
            throw new KnownException($"Unknown permission code '{unknown[0]}'.");
        }

        return codes;
    }

    private static string GetDomain(string code)
    {
        var separator = code.IndexOf('.', StringComparison.Ordinal);
        return separator > 0 ? code[..separator] : code;
    }
}
