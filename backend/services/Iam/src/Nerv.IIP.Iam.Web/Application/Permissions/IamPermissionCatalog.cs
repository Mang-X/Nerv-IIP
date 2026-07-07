using Nerv.IIP.Iam.Domain;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Permissions;

public sealed record PermissionCatalogResponse(IReadOnlyList<PermissionCatalogItemResponse> Items);
public sealed record PermissionCatalogItemResponse(string Code, string Domain, string Description, bool Seeded);

public static class IamPermissionCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["iam.users.read"] = "Read IAM users.",
        ["iam.users.manage"] = "Create, update, disable and reset IAM users.",
        ["iam.roles.read"] = "Read IAM roles and permission catalog.",
        ["iam.roles.manage"] = "Create IAM roles and update role permissions.",
        ["iam.sessions.read"] = "Read IAM user sessions.",
        ["iam.sessions.revoke"] = "Revoke IAM user sessions.",
        ["iam.security-audit.read"] = "Read IAM security audit records.",
        ["connectors.registrations.write"] = "Register connector hosts.",
        ["connectors.heartbeats.write"] = "Write connector host heartbeats.",
        ["connectors.state-snapshots.write"] = "Write connector host state snapshots.",
        ["apphub.instances.read"] = "Read AppHub application instances.",
        ["files.upload"] = "Upload files.",
        ["files.read"] = "Read file metadata.",
        ["files.download-grants.create"] = "Create file download grants.",
        ["files.archive"] = "Archive files.",
        ["ops.tasks.create"] = "Create operation tasks.",
        ["ops.tasks.read"] = "Read operation tasks.",
        ["ops.results.write"] = "Write operation results.",
        ["ops.audit.read"] = "Read operation audit records.",
        ["observability.logs.read"] = "Query centralized platform logs.",
        ["notifications.intents.submit"] = "Submit notification intents.",
        ["notifications.dlq.read"] = "Read notification integration event dead letters.",
        ["notifications.dlq.manage"] = "Replay and ignore notification integration event dead letters.",
        ["notifications.messages.read"] = "Read notification messages.",
        ["notifications.messages.mark-read"] = "Mark notification messages as read.",
        ["notifications.tasks.read"] = "Read notification tasks.",
        ["business.quality.inspection-plans.manage"] = "Create, activate and supersede quality inspection plans.",
        ["business.quality.inspection-records.create"] = "Create quality inspection records.",
        ["business.quality.inspection-records.read"] = "Read quality inspection plans and records.",
        ["business.quality.ncr.read"] = "Read quality nonconformance reports.",
        ["business.quality.ncr.manage"] = "Create, disposition and close quality nonconformance reports.",
        ["business.inventory.locations.manage"] = "Create and manage inventory locations.",
        ["business.inventory.movements.create"] = "Create inventory stock movements.",
        ["business.inventory.ledger.read"] = "Read inventory ledger balances and reports.",
        ["business.inventory.counts.manage"] = "Create and complete inventory counts.",
        ["business.mes.foundation.read"] = "Read MES foundation readiness.",
        ["business.mes.overview.read"] = "Read MES execution overview.",
        ["business.mes.plans.read"] = "Read MES production plans and readiness.",
        ["business.mes.work-orders.read"] = "Read MES work orders.",
        ["business.mes.work-orders.manage"] = "Create, release and close MES work orders.",
        ["business.mes.materials.read"] = "Read MES material readiness and issue requests.",
        ["business.mes.materials.manage"] = "Create MES material issue requests and confirm line-side receipts.",
        ["business.mes.dispatch.read"] = "Read MES dispatch tasks.",
        ["business.mes.dispatch.manage"] = "Assign MES dispatch tasks.",
        ["business.mes.operations.read"] = "Read MES operation tasks and WIP summaries.",
        ["business.mes.operations.manage"] = "Start, pause, resume and complete MES operation tasks.",
        ["business.mes.reporting.read"] = "Read MES production reports.",
        ["business.mes.reporting.write"] = "Submit MES production reports.",
        ["business.mes.quality.read"] = "Read MES in-process quality context.",
        ["business.mes.quality.write"] = "Record MES in-process defects.",
        ["business.mes.receipts.read"] = "Read MES finished-goods receipt requests.",
        ["business.mes.receipts.manage"] = "Create MES finished-goods receipt requests.",
        ["business.mes.downtime.read"] = "Read MES downtime events.",
        ["business.mes.downtime.manage"] = "Record and recover MES downtime events.",
        ["business.mes.handovers.read"] = "Read MES shift handovers.",
        ["business.mes.handovers.manage"] = "Create and accept MES shift handovers.",
        ["business.mes.traceability.read"] = "Read MES work order, batch and material-lot traceability.",
        ["business.mes.schedules.read"] = "Read MES schedule versions.",
        ["business.mes.schedules.manage"] = "Run and manage MES schedule versions.",
        ["business.mes.capacity.read"] = "Read MES capacity impact records.",
        ["business.planning.demands.read"] = "Read demand sources for MPS and MRP.",
        ["business.planning.demands.manage"] = "Create and adjust demand sources.",
        ["business.planning.mps.read"] = "Read master production schedule buckets.",
        ["business.planning.mps.manage"] = "Create, update and review master production schedule buckets.",
        ["business.planning.mps.release"] = "Release reviewed master production schedule buckets into MRP input.",
        ["business.planning.mrp.read"] = "Read MPS, MRP runs and pegging.",
        ["business.planning.mrp.run"] = "Run MPS and MRP calculations.",
        ["business.planning.suggestions.manage"] = "Accept, reject or close planning suggestions.",
        ["business.barcodes.templates.manage"] = "Manage barcode rules and label templates.",
        ["business.barcodes.print"] = "Generate and print labels.",
        ["business.barcodes.scans.write"] = "Write barcode scan records.",
        ["business.approvals.read"] = "Read business approval templates, chains and tasks.",
        ["business.approvals.manage"] = "Create and resolve business approval chains.",
        ["business.erp.procurement.read"] = "Read ERP procurement requisitions, RFQs, quotations, purchase orders and receipts.",
        ["business.erp.procurement.manage"] = "Create and progress ERP procurement documents.",
        ["business.erp.sales.read"] = "Read ERP opportunities, quotations, sales orders and delivery requests.",
        ["business.erp.sales.manage"] = "Create and progress ERP sales documents.",
        ["business.erp.finance.read"] = "Read ERP payables, receivables, vouchers and finance summaries.",
        ["business.erp.finance.manage"] = "Create ERP finance candidates and post balanced vouchers.",
        ["business.scheduling.plans.read"] = "Read APS lite scheduling plans, resource loads, conflicts and Gantt DTOs.",
        ["business.scheduling.plans.manage"] = "Preview and generate APS lite scheduling plans.",
        ["business.scheduling.plans.release"] = "Release generated APS lite scheduling plans for downstream MES consumption.",
        ["business.wms.receipts.read"] = "Read WMS receipts, inbound orders and putaway tasks.",
        ["business.wms.receipts.manage"] = "Create and complete WMS receipt and putaway work.",
        ["business.wms.shipments.read"] = "Read WMS shipments, outbound orders and picking tasks.",
        ["business.wms.shipments.manage"] = "Create and complete WMS shipment and picking work.",
        ["business.wms.automation.manage"] = "Dispatch and complete WMS automation tasks.",
        ["business.iiot.tags.manage"] = "Manage IndustrialTelemetry tag mappings and sampling policy.",
        ["business.iiot.alarm-rules.manage"] = "Manage IndustrialTelemetry alarm rule thresholds.",
        ["business.iiot.telemetry.read"] = "Read IndustrialTelemetry device snapshots and summaries.",
        ["business.iiot.telemetry.write"] = "Write IndustrialTelemetry samples and device state snapshots.",
        ["business.iiot.device-control.write"] = "Submit approval-gated IndustrialTelemetry device control commands.",
        ["business.iiot.alarms.read"] = "Read IndustrialTelemetry alarm events.",
        ["business.iiot.alarms.write"] = "Raise and clear IndustrialTelemetry alarms.",
        ["business.maintenance.work-orders.read"] = "Read maintenance work orders and downtime facts.",
        ["business.maintenance.work-orders.manage"] = "Create, update and complete maintenance work orders.",
        ["business.maintenance.plans.read"] = "Read maintenance plans and inspections.",
        ["business.maintenance.plans.manage"] = "Create and manage maintenance plans and inspections."
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
