using System.Reflection;
using Nerv.IIP.Business.Inventory.Web.Application.Auth;
using Nerv.IIP.Business.Inventory.Web.Endpoints.Inventory;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

// 权限码取值的「第二源」。
//
// ⚠️ 本文件里的权限码一律写成裸字面量，**刻意不引用 InventoryPermissionCodes.* 常量**。这不是待清理的
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
public sealed class InventoryPermissionCodeSecondSourceTests
{
    [Fact]
    public void Inventory_endpoint_permission_codes_match_their_literal_values()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["listInventoryDirectory"] = "business.inventory.ledger.read", // GET /api/inventory/v1/directory
            ["listInventoryLineSideBalances"] = "business.inventory.ledger.read", // GET /api/inventory/v1/line-side-balances
            ["createOrUpdateInventoryLocation"] = "business.inventory.locations.manage", // POST /api/inventory/v1/locations
            ["postInventoryMovement"] = "business.inventory.movements.create", // POST /api/inventory/v1/movements
            ["listInventoryMovements"] = "business.inventory.ledger.read", // GET /api/inventory/v1/movements
            ["getInventoryAvailability"] = "business.inventory.ledger.read", // GET /api/inventory/v1/availability
            ["getInventoryStockBySource"] = "business.inventory.ledger.read", // GET /api/inventory/v1/movements/by-source
            ["createInventoryCountTask"] = "business.inventory.counts.manage", // POST /api/inventory/v1/count-tasks
            ["listInventoryCountTasks"] = "business.inventory.counts.manage", // GET /api/inventory/v1/count-tasks
            ["listInventoryCountAdjustments"] = "business.inventory.counts.manage", // GET /api/inventory/v1/count-adjustments
            ["confirmInventoryCountAdjustment"] = "business.inventory.counts.manage", // POST /api/inventory/v1/count-tasks/{countTaskId}/adjustments
            ["cancelInventoryCountTask"] = "business.inventory.counts.manage", // POST /api/inventory/v1/count-tasks/{countTaskId}/cancel
            ["restartInventoryCountTask"] = "business.inventory.counts.manage", // POST /api/inventory/v1/count-tasks/{countTaskId}/recount
            ["reserveInventoryStock"] = "business.inventory.reservations.manage", // POST /api/inventory/v1/reservations
            ["reserveInventoryStockByFefo"] = "business.inventory.reservations.manage", // POST /api/inventory/v1/reservations/fefo
            ["releaseInventoryReservation"] = "business.inventory.reservations.manage", // POST /api/inventory/v1/reservations/{reservationId}/release
            ["renewInventoryReservation"] = "business.inventory.reservations.manage", // POST /api/inventory/v1/reservations/{reservationId}/renew
            ["listInventoryExpiryAlerts"] = "business.inventory.ledger.read", // GET /api/inventory/v1/expiry-alerts
            ["postInventoryStatusTransfer"] = "business.inventory.movements.create", // POST /api/inventory/v1/status-transfers
        };

        var actual = InventoryEndpointContracts.All.ToDictionary(x => x.OperationId, x => x.PermissionCode, StringComparer.Ordinal);

        Assert.Equal(expected.Count, actual.Count);
        foreach (var (operationId, permissionCode) in expected)
        {
            Assert.True(actual.ContainsKey(operationId), $"端点契约缺少 operationId '{operationId}'。");
            Assert.Equal(permissionCode, actual[operationId]);
        }
    }

    // Inventory 额外一条：ExpiredStockOverride 不出现在任何端点描述符里，它只在 handler 体内经
    // InventoryPermissionContext.HasPermission 对「用户转发权限集」做序数字符串相等比较
    // （InventoryEndpoints.cs:408/503/539）。改掉它的值不会让任何描述符断言变红，
    // 却会让每个用户的过期库存越权静默失效，所以它只能由下面的常量断言兜住。

    [Fact]
    public void Inventory_permission_code_constants_match_their_literal_values()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CountsManage"] = "business.inventory.counts.manage",
            ["ExpiredStockOverride"] = "business.inventory.expired-stock.override",
            ["LedgerRead"] = "business.inventory.ledger.read",
            ["LocationsManage"] = "business.inventory.locations.manage",
            ["MovementsCreate"] = "business.inventory.movements.create",
            ["ReservationsManage"] = "business.inventory.reservations.manage",
        };

        // GetRawConstantValue 读的是被测程序集的元数据，不是本程序集里被内联的副本，
        // 所以这条断言不依赖测试程序集是否被重新编译。
        var actual = typeof(InventoryPermissionCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(x => x.IsLiteral && !x.IsInitOnly && x.FieldType == typeof(string))
            .ToDictionary(x => x.Name, x => (string)x.GetRawConstantValue()!, StringComparer.Ordinal);

        Assert.Equal(expected.Count, actual.Count);
        foreach (var (name, permissionCode) in expected)
        {
            Assert.True(actual.ContainsKey(name), $"InventoryPermissionCodes 缺少常量 '{name}'。");
            Assert.Equal(permissionCode, actual[name]);
        }
    }
}
