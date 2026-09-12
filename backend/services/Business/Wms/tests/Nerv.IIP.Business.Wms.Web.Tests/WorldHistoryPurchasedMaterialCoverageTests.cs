using Nerv.IIP.Business.Wms.Web.Application.Seed;
using Xunit.Abstractions;
using ProcurementSpec = Nerv.IIP.Business.Wms.Web.Application.Seed.WorldHistoryProcurementSpec;
using PurchasePlanSource = Nerv.IIP.Business.Wms.Web.Application.Seed.WorldHistoryProcurementSpec;

namespace Nerv.IIP.Business.Wms.Web.Tests;

/// <summary>
/// 跨域不变量：**L0 成品 BOM 要求的外购物料，必须被 L1 采购品类表全部供得出来**。
///
/// 缺这条断言的后果已被 #3138 实测到：<c>RM-ACC-01..10</c>（连接环 / 防尘罩 / 紧固件）与
/// <c>PK-LBL-03</c>（成品箱贴）共 11 个 SKU 被每一张 MBOM 引用，却从不出现在
/// <c>PurchaseCategories</c> 里，于是演示库上这 11 个 SKU 的 <c>stock_ledgers</c> **一行都没有**，
/// 实时齐套查询沿 MBOM 11 行去问库存时每张工单都带着永久缺口。
///
/// 本文件在六个持有采购品类副本的服务里各存一份**逐字相同**的副本（仅命名空间与 using 别名不同），
/// 任一侧漏补品类都会在它自己的 lane 里变红，而不是等到演示当场。
/// 期望集合的权威来源是 ProductEngineering <c>WorldBibleSpec</c> 的 BOM 行公式与 MasterData 物料档案；
/// 由 BOM 反推该集合的那一侧断言在 <c>WorldBibleBomMaterialCoverageTests</c>。
/// </summary>
public sealed class WorldHistoryPurchasedMaterialCoverageTests(ITestOutputHelper output)
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 26);

    /// <summary>
    /// 24 张成品 BOM（6 平台 × 前/后 × 左/右）在 V1 + V2 两个修订下引用到的**外购**物料全集，共 27 个 SKU。
    ///
    /// 三项 <c>SF-</c> 半成品（活塞杆 / 缸筒 / 阀系总成）是自制件，由生产链路而非采购链路产生，不在本集合内；
    /// <c>RM-BAR-*</c> / <c>RM-TUB-*</c> / <c>PK-PLT-01</c> / <c>PK-FLM-01</c> 反过来只被采购而不出现在成品 BOM 上，
    /// 所以两侧是**包含**关系而不是相等关系。
    /// </summary>
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

    /// <summary>成品 BOM 外购物料数：4 纸箱 + 1 箱贴 + 10 配件 + 2 减振油 + 4 油封 + 6 弹簧。</summary>
    public const int RequiredPurchasedSkuCount = 27;

    [Fact]
    public void Purchase_categories_supply_every_material_the_world_bible_bom_requires()
    {
        var suppliable = ProcurementSpec.PurchaseCategories
            .SelectMany(category => category.MaterialSkuCodes)
            .ToHashSet(StringComparer.Ordinal);

        // 正向判据先行：Where/Contains 族在谓词写错时会退化成「空集即真」，
        // 所以先把参与比较的两个集合都钉成非空且规模对得上，下面的缺口判定才有鉴别力。
        Assert.Equal(RequiredPurchasedSkuCount, RequiredPurchasedSkuCodes.Count);
        Assert.NotEmpty(suppliable);
        Assert.True(
            suppliable.Count >= RequiredPurchasedSkuCount,
            FormattableString.Invariant(
                $"采购品类可供 {suppliable.Count} 个物料，少于成品 BOM 要求的 {RequiredPurchasedSkuCount} 个，包含关系不可能成立。"));

        var missing = RequiredPurchasedSkuCodes.Where(sku => !suppliable.Contains(sku)).ToArray();
        Assert.True(
            missing.Length == 0,
            $"成品 BOM 要求、但采购品类表供不出的物料：{string.Join(", ", missing)}。这些 SKU 在演示库上会恒为零行库存。");
    }

    [Fact]
    public void Purchase_categories_keep_material_codes_disjoint_and_non_empty()
    {
        // ERP 侧按物料码回查品类用的是 Single(...)，品类间物料码一旦相交会直接抛。
        var all = ProcurementSpec.PurchaseCategories.SelectMany(category => category.MaterialSkuCodes).ToArray();
        Assert.NotEmpty(all);
        Assert.All(
            ProcurementSpec.PurchaseCategories,
            category =>
            {
                Assert.NotEmpty(category.MaterialSkuCodes);
                Assert.NotEmpty(category.SupplierCodes);
            });
        Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ProcurementSpec.PurchaseCategories.Count, ProcurementSpec.PurchaseCategoryWeights.Count);
    }

    [Fact]
    public void Full_scale_history_buys_every_bom_material_at_least_once()
    {
        // 「品类里登记了」只证明抽样池里有它；这条证明全量历史真的抽到过它并且收了货——
        // 品类内按 SKU 均匀抽样，SKU 数多的品类单个 SKU 仍可能颗粒无收。
        var received = PurchasePlanSource.BuildPurchasePlans(AsOfDate, 1.0d)
            .Where(plan => plan.IsReceived)
            .ToArray();
        Assert.NotEmpty(received);

        var purchased = received
            .GroupBy(plan => plan.SkuCode, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (Orders: group.Count(), Quantity: group.Sum(plan => plan.Quantity)),
                StringComparer.Ordinal);

        foreach (var sku in RequiredPurchasedSkuCodes)
        {
            var stat = purchased.TryGetValue(sku, out var found) ? found : (Orders: 0, Quantity: 0m);
            output.WriteLine(FormattableString.Invariant(
                $"world-history-purchased {sku} orders={stat.Orders} quantity={stat.Quantity:0}"));
        }

        Assert.All(
            RequiredPurchasedSkuCodes,
            sku => Assert.True(
                purchased.TryGetValue(sku, out var stat) && stat.Quantity > 0m,
                $"{sku} 在全量历史采购里一张已收货采购单都没有，演示库上它将恒为零行库存。"));
    }
}
