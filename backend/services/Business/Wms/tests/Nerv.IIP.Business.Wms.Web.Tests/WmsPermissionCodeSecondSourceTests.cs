using System.Reflection;
using Nerv.IIP.Business.Wms.Web.Application.Auth;
using Nerv.IIP.Business.Wms.Web.Endpoints.Wms;

namespace Nerv.IIP.Business.Wms.Web.Tests;

// 权限码取值的「第二源」。
//
// ⚠️ 本文件里的权限码一律写成裸字面量，**刻意不引用 WmsPermissionCodes.* 常量**。这不是待清理的
// 技术债，是这些断言唯一的鉴别力来源，动它等于删掉防线，而且删完没有任何门禁会红：
//
//   * 若把右侧换成常量引用，断言两侧就走同一个符号，成为同义反复。此时把某个端点的权限码
//     换成同族但不同值的另一个常量（例如 MesFoundationRead → MesOverviewRead），断言照样通过。
//   * containment 类门禁（scripts/verify-permission-code-producer-consistency.ps1 的
//     `Gateway ⊆ IAM`）也抓不到这种变异：同族两个值通常**都**在 IAM 种子里，
//     变异前后 containment 同样成立。#3094 把值改成常量引用之后，「值写错」在编译期已不可能，
//     剩下的唯一错误形态就是**选错常量**，而选错的那个通常也合法。
//
// 结论：这一族缺陷只能靠一份独立于常量的取值转写来抓。实证见 issue #3172 与 #3094：
// IIoT 的同型变异因为 IndustrialTelemetryEndpointContractTests 里的裸字面量被杀掉，
// 而 Wms 的 `x.PermissionCode == WmsPermissionCodes.ReceiptsRead` 是同义反复，变异存活。
//
// 修改本文件前先读 #3172。新增端点时在这里补一行裸字面量，不要引用常量。
public sealed class WmsPermissionCodeSecondSourceTests
{
    [Fact]
    public void Wms_endpoint_permission_codes_match_their_literal_values()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["createWmsInboundOrder"] = "business.wms.receipts.manage", // POST /api/business/v1/wms/inbound-orders
            ["listWmsInboundOrders"] = "business.wms.receipts.read", // GET /api/business/v1/wms/inbound-orders
            ["assignWmsInboundOrder"] = "business.wms.receipts.manage", // POST /api/business/v1/wms/inbound-orders/{inboundOrderId}/assignment
            ["createWmsPutawayTask"] = "business.wms.receipts.manage", // POST /api/business/v1/wms/inbound-orders/{inboundOrderId}/putaway-tasks
            ["listWmsPutawayTasks"] = "business.wms.receipts.read", // GET /api/business/v1/wms/putaway-tasks
            ["assignWmsPutawayTask"] = "business.wms.receipts.manage", // POST /api/business/v1/wms/putaway-tasks/{warehouseTaskId}/assignment
            ["completeWmsInboundOrder"] = "business.wms.receipts.manage", // POST /api/business/v1/wms/inbound-orders/{inboundOrderId}/complete
            ["retryWmsInboundInventoryPosting"] = "business.wms.receipts.manage", // POST /api/business/v1/wms/inbound-orders/{inboundOrderId}/inventory-posting/retry
            ["cancelWmsInboundOrdersForSource"] = "business.wms.receipts.manage", // POST /api/business/v1/wms/inbound-orders/cancel-by-source
            ["createWmsOutboundOrder"] = "business.wms.shipments.manage", // POST /api/business/v1/wms/outbound-orders
            ["listWmsOutboundOrders"] = "business.wms.shipments.read", // GET /api/business/v1/wms/outbound-orders
            ["assignWmsOutboundOrder"] = "business.wms.shipments.manage", // POST /api/business/v1/wms/outbound-orders/{outboundOrderId}/assignment
            ["createWmsPickingTask"] = "business.wms.shipments.manage", // POST /api/business/v1/wms/outbound-orders/{outboundOrderId}/picking-tasks
            ["listWmsPickingTasks"] = "business.wms.shipments.read", // GET /api/business/v1/wms/picking-tasks
            ["assignWmsPickingTask"] = "business.wms.shipments.manage", // POST /api/business/v1/wms/picking-tasks/{warehouseTaskId}/assignment
            ["listWmsReplenishmentTasks"] = "business.wms.shipments.read", // GET /api/business/v1/wms/replenishment-tasks
            ["recordWmsWarehouseTaskProgress"] = "business.wms.receipts.manage", // POST /api/business/v1/wms/warehouse-tasks/{warehouseTaskId}/progress
            ["completeWmsWarehouseTask"] = "business.wms.receipts.manage", // POST /api/business/v1/wms/warehouse-tasks/{warehouseTaskId}/complete
            ["startWmsPutawayTask"] = "business.wms.receipts.manage", // POST /api/business/v1/wms/putaway-tasks/{warehouseTaskId}/start
            ["recordWmsPutawayTaskProgress"] = "business.wms.receipts.manage", // POST /api/business/v1/wms/putaway-tasks/{warehouseTaskId}/progress
            ["reportWmsPutawayTaskException"] = "business.wms.receipts.manage", // POST /api/business/v1/wms/putaway-tasks/{warehouseTaskId}/exception
            ["completeWmsPutawayTask"] = "business.wms.receipts.manage", // POST /api/business/v1/wms/putaway-tasks/{warehouseTaskId}/complete
            ["startWmsPickingTask"] = "business.wms.shipments.manage", // POST /api/business/v1/wms/picking-tasks/{warehouseTaskId}/start
            ["recordWmsPickingTaskProgress"] = "business.wms.shipments.manage", // POST /api/business/v1/wms/picking-tasks/{warehouseTaskId}/progress
            ["reportWmsPickingTaskException"] = "business.wms.shipments.manage", // POST /api/business/v1/wms/picking-tasks/{warehouseTaskId}/exception
            ["completeWmsPickingTask"] = "business.wms.shipments.manage", // POST /api/business/v1/wms/picking-tasks/{warehouseTaskId}/complete
            ["completeWmsOutboundOrder"] = "business.wms.shipments.manage", // POST /api/business/v1/wms/outbound-orders/{outboundOrderId}/complete
            ["cancelWmsOutboundOrder"] = "business.wms.shipments.manage", // POST /api/business/v1/wms/outbound-orders/{outboundOrderId}/cancel
            ["listWmsBackorderOrders"] = "business.wms.shipments.read", // GET /api/business/v1/wms/backorder-orders
            ["closeWmsBackorderOrder"] = "business.wms.shipments.manage", // POST /api/business/v1/wms/backorder-orders/{backorderOrderId}/close
            ["retryWmsOutboundInventoryPosting"] = "business.wms.shipments.manage", // POST /api/business/v1/wms/outbound-orders/{outboundOrderId}/inventory-posting/retry
            ["createWmsCountExecution"] = "business.inventory.counts.manage", // POST /api/business/v1/wms/count-executions
            ["listWmsCountExecutions"] = "business.wms.counts.read", // GET /api/business/v1/wms/count-executions
            ["assignWmsCountExecution"] = "business.inventory.counts.manage", // POST /api/business/v1/wms/count-executions/{countExecutionId}/assignment
            ["completeWmsCountExecution"] = "business.inventory.counts.manage", // POST /api/business/v1/wms/count-executions/{countExecutionId}/complete
            ["dispatchWmsWcsTask"] = "business.wms.automation.manage", // POST /api/business/v1/wms/wcs-tasks/{warehouseTaskId}/dispatch
            ["completeWmsWcsTask"] = "business.wms.automation.manage", // POST /api/business/v1/wms/wcs-tasks/{externalTaskId}/complete
            ["failWmsWcsTask"] = "business.wms.automation.manage", // POST /api/business/v1/wms/wcs-tasks/{externalTaskId}/fail
            ["listWmsWcsTasks"] = "business.wms.automation.manage", // GET /api/business/v1/wms/wcs-tasks
            ["listWmsWcsDispatchCircuits"] = "business.wms.automation.manage", // GET /api/business/v1/wms/wcs-dispatch-circuits
            ["resetWmsWcsDispatchCircuit"] = "business.wms.automation.manage", // POST /api/business/v1/wms/wcs-dispatch-circuits/reset
            ["listWmsReceivingQualityGates"] = "business.wms.receipts.read", // GET /api/business/v1/wms/receiving-quality-gates
            ["listWmsSupplierReturnRequests"] = "business.wms.receipts.read", // GET /api/business/v1/wms/supplier-return-requests
            ["listWmsOperationalCandidates"] = "business.wms.receipts.read", // GET /api/business/v1/wms/operational-candidates
            ["provisionWmsWorkPool"] = "business.wms.work-pools.manage", // POST /api/business/v1/wms/work-pools
            ["addWmsWorkPoolMember"] = "business.wms.work-pools.manage", // POST /api/business/v1/wms/work-pools/{poolCode}/members
            ["getWmsReceiptWorkScopes"] = "business.wms.receipts.read", // GET /api/business/v1/wms/work-scopes/receipts
            ["getWmsShipmentWorkScopes"] = "business.wms.shipments.read", // GET /api/business/v1/wms/work-scopes/shipments
            ["getWmsCountWorkScopes"] = "business.wms.counts.read", // GET /api/business/v1/wms/work-scopes/counts
        };

        var actual = WmsEndpointContracts.All.ToDictionary(x => x.OperationId, x => x.PermissionCode, StringComparer.Ordinal);

        Assert.Equal(expected.Count, actual.Count);
        foreach (var (operationId, permissionCode) in expected)
        {
            Assert.True(actual.ContainsKey(operationId), $"端点契约缺少 operationId '{operationId}'。");
            Assert.Equal(permissionCode, actual[operationId]);
        }
    }

    [Fact]
    public void Wms_permission_code_constants_match_their_literal_values()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AutomationManage"] = "business.wms.automation.manage",
            ["CountsRead"] = "business.wms.counts.read",
            ["InventoryCountsManage"] = "business.inventory.counts.manage",
            ["ReceiptsManage"] = "business.wms.receipts.manage",
            ["ReceiptsRead"] = "business.wms.receipts.read",
            ["ShipmentsManage"] = "business.wms.shipments.manage",
            ["ShipmentsRead"] = "business.wms.shipments.read",
            ["WorkPoolsManage"] = "business.wms.work-pools.manage",
        };

        // GetRawConstantValue 读的是被测程序集的元数据，不是本程序集里被内联的副本，
        // 所以这条断言不依赖测试程序集是否被重新编译。
        var actual = typeof(WmsPermissionCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(x => x.IsLiteral && !x.IsInitOnly && x.FieldType == typeof(string))
            .ToDictionary(x => x.Name, x => (string)x.GetRawConstantValue()!, StringComparer.Ordinal);

        Assert.Equal(expected.Count, actual.Count);
        foreach (var (name, permissionCode) in expected)
        {
            Assert.True(actual.ContainsKey(name), $"WmsPermissionCodes 缺少常量 '{name}'。");
            Assert.Equal(permissionCode, actual[name]);
        }
    }
}
