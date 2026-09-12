using Nerv.IIP.Business.ProductEngineering.Web.Application.Seed;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

/// <summary>
/// 跨域不变量的**产出侧**：成品 BOM 引用到的外购物料集合必须与登记清单逐项相同。
///
/// 消费侧（Inventory 与 Erp 两份 <c>WorldHistoryPurchasedMaterialCoverageTests</c>，
/// 其余 4 个服务由世界史副本圈门禁传递覆盖，边界说明见那两份文件的类型注释）
/// 用同一份清单断言「BOM 要求 ⊆ 采购能供」。两侧清单是逐字相同的副本：
/// BOM 公式一旦新增或改写物料族，本文件先红，失败信息点名必须同步的那六份采购品类表——
/// #3138 的成因正是这一步从来没有门禁（<c>RM-ACC-*</c> 与 <c>PK-LBL-03</c> 进了 BOM 却没进采购）。
/// </summary>
public sealed class WorldBibleBomMaterialCoverageTests
{
    /// <summary>与消费侧 <c>WorldHistoryPurchasedMaterialCoverageTests.RequiredPurchasedSkuCodes</c> 逐字相同。</summary>
    public static readonly IReadOnlyList<string> RequiredPurchasedSkuCodes =
    [
        "PK-BOX-01", "PK-BOX-02", "PK-BOX-03", "PK-BOX-04",
        "PK-LBL-03",
        "RM-ACC-01", "RM-ACC-02", "RM-ACC-03", "RM-ACC-04", "RM-ACC-05",
        "RM-ACC-06", "RM-ACC-07", "RM-ACC-08", "RM-ACC-09", "RM-ACC-10",
        "RM-OIL-01", "RM-OIL-02",
        "RM-SEL-01", "RM-SEL-02", "RM-SEL-03", "RM-SEL-04",
        "RM-SPR-01", "RM-SPR-02", "RM-SPR-03", "RM-SPR-04", "RM-SPR-05", "RM-SPR-06",
    ];

    /// <summary>自制半成品：活塞杆 6 + 缸筒 6 + 阀系总成 6 = 18 项，由生产链路产生，不走采购。</summary>
    public const int InHouseComponentSkuCount = 18;

    [Fact]
    public void Manufacturing_boms_reference_exactly_the_registered_purchased_material_set()
    {
        var components = WorldBibleSpec.Products
            .SelectMany(product => new[] { WorldBibleSpec.V1Revision, WorldBibleSpec.V2Revision }
                .SelectMany(revision => product.ManufacturingLines(revision)))
            .Select(line => line.ComponentSkuCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // 正向判据先行：先证明被过滤的两侧都真的有料，再谈相等——
        // 若 StartsWith 谓词写反，下面的 purchased 会变成 18 项自制件而不是空集，断言仍然会红。
        Assert.NotEmpty(components);
        var inHouse = components.Where(sku => sku.StartsWith("SF-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(InHouseComponentSkuCount, inHouse.Length);

        var purchased = components
            .Where(sku => !sku.StartsWith("SF-", StringComparison.Ordinal))
            .OrderBy(sku => sku, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            RequiredPurchasedSkuCodes,
            purchased);
    }

    [Fact]
    public void Every_manufacturing_bom_line_is_either_in_house_or_registered_as_purchased()
    {
        var registered = RequiredPurchasedSkuCodes.ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(registered);

        var lines = WorldBibleSpec.Products
            .SelectMany(product => new[] { WorldBibleSpec.V1Revision, WorldBibleSpec.V2Revision }
                .SelectMany(revision => product.ManufacturingLines(revision)))
            .ToArray();

        // 11 行 × 24 张成品 × 2 个修订。行数先钉住，避免「BOM 变空 ⇒ Assert.All 空跑」这种假绿。
        Assert.Equal(11 * 24 * 2, lines.Length);
        Assert.All(
            lines,
            line => Assert.True(
                line.ComponentSkuCode.StartsWith("SF-", StringComparison.Ordinal)
                    || registered.Contains(line.ComponentSkuCode),
                $"{line.ComponentSkuCode} 既不是自制半成品，也没登记进外购物料清单；必须同步 6 份采购品类字面量（5 份 WorldHistoryProcurementSpec + Erp 的 WorldHistoryErpSpec）。"));
    }
}
