namespace Nerv.IIP.Business.Inventory.Web.Application.Seed;

/// <summary>
/// L1 背景历史的 **采购节奏共享形状**（设定集 §7「约 480 张采购订单，补货节奏与生产量匹配」）。
///
/// 这是二期（质量 / 库存 / 仓储 / 条码标签）与一期 ERP 之间的第三个共享契约：
/// 四侧用同一 <c>(asOfDate, scale)</c> 调用 <see cref="BuildPurchasePlans"/>，必须逐字段得到
/// 与 ERP <c>WorldHistoryErpSpec</c> 相同的采购计划表，于是「采购单 <c>PO-2026-####</c> →
/// 收货单 <c>PR-2026-####</c> → 来料检验 → 上架 → 原料批次 <c>LOT-PO-2026-####</c>」
/// 这条链在四个库里指向同一批事实，而不需要任何跨服务查询或外键。
///
/// 与 <see cref="WorldHistorySpec"/> 一样按同一字面量重复声明，各侧有黄金向量测试防止漂移。
/// 一期 ERP 侧的对应字面量在 <c>WorldHistoryErpSpec</c>（含凭证科目表，本类型不复制）。
/// </summary>
public static class WorldHistoryProcurementSpec
{
    /// <summary>
    /// 采购单量按销售订单量的 15% 派生——这就是「补货节奏与生产量匹配」的实现：
    /// 春节低谷、月末冲量的曲线自动传导到采购侧，无需第二套节奏参数。
    /// 3200 单销售 → 约 480 张采购单。
    /// </summary>
    public const double PurchaseToSalesRatio = 0.15;

    public static int WeeklyPurchaseOrderVolume(int weekIndex, double scale)
    {
        var sales = WorldHistoryCalendar.WeeklyOrderVolume(weekIndex, scale);
        return Math.Max(1, (int)Math.Round(sales * PurchaseToSalesRatio, MidpointRounding.AwayFromZero));
    }

    public static int TotalPurchaseOrders(DateOnly asOfDate, double scale)
    {
        var total = 0;
        var weeks = WorldHistoryCalendar.WeekCount(asOfDate);
        for (var week = 0; week < weeks; week++)
        {
            total += WeeklyPurchaseOrderVolume(week, scale);
        }

        return total;
    }

    /// <summary>
    /// 采购品类 → 供应商（L0 <c>WorldBibleSpec.Suppliers</c> 的 10 家）与代表性物料。
    /// 引擎只**引用**这些编码，不创建供应商主数据。
    /// </summary>
    public static readonly IReadOnlyList<WorldHistoryPurchaseCategory> PurchaseCategories =
    [
        new("棒料", ["SUP-WB-BAR-01", "SUP-WB-BAR-02"], ["RM-BAR-01", "RM-BAR-02", "RM-BAR-03", "RM-BAR-04"], "kg", 6m, 12m, 800, 4000, 200),
        new("钢管", ["SUP-WB-TUB-01"], ["RM-TUB-01", "RM-TUB-02", "RM-TUB-03", "RM-TUB-04"], "kg", 9m, 16m, 600, 3000, 200),
        new("弹簧", ["SUP-WB-SPR-01", "SUP-WB-SPR-02"], ["RM-SPR-01", "RM-SPR-02", "RM-SPR-03", "RM-SPR-04", "RM-SPR-05", "RM-SPR-06"], "pcs", 22m, 38m, 400, 2400, 100),
        new("密封件", ["SUP-WB-SEL-01", "SUP-WB-SEL-02"], ["RM-SEL-01", "RM-SEL-02", "RM-SEL-03", "RM-SEL-04"], "pcs", 3m, 7m, 1000, 6000, 500),
        new("减振油", ["SUP-WB-OIL-01"], ["RM-OIL-01", "RM-OIL-02"], "l", 14m, 21m, 300, 1500, 100),
        new("包材", ["SUP-WB-PKG-01", "SUP-WB-PKG-02"], ["PK-BOX-01", "PK-BOX-02", "PK-BOX-03", "PK-BOX-04", "PK-PLT-01", "PK-FLM-01"], "pcs", 2m, 9m, 500, 3000, 250),
    ];

    /// <summary>采购品类抽样权重：结构件用量最大，包材次之。</summary>
    public static readonly IReadOnlyList<int> PurchaseCategoryWeights = [5, 5, 4, 3, 2, 3];

    /// <summary>单张历史采购单的确定性内容（与 ERP <c>WorldHistoryErpSpec.BuildPurchasePlan</c> 同字面量）。</summary>
    public static WorldHistoryPurchasePlan BuildPurchasePlan(int index, DateOnly orderDate, DateOnly asOfDate)
    {
        var purchaseOrderNo = WorldHistorySpec.PurchaseOrderNo(index);
        var random = new WorldHistoryRandom(purchaseOrderNo);
        var category = random.PickWeighted(PurchaseCategories, PurchaseCategoryWeights);
        var supplierCode = random.Pick(category.SupplierCodes);
        var skuCode = random.Pick(category.MaterialSkuCodes);
        var quantity = random.NextQuantity(category.MinQuantity, category.MaxQuantity, category.QuantityStep);
        var unitPrice = category.MinUnitPrice +
            (random.NextInt(0, 21) * (category.MaxUnitPrice - category.MinUnitPrice) / 20m);

        var snapped = WorldHistoryCalendar.SnapToWorkingDay(orderDate);
        var promisedDate = WorldHistoryCalendar.AddWorkingDays(snapped, random.NextInt(6, 19));
        var receiptDate = WorldHistoryCalendar.AddWorkingDays(snapped, random.NextInt(5, 21));
        if (receiptDate > asOfDate)
        {
            receiptDate = snapped;
        }

        return new WorldHistoryPurchasePlan(
            Index: index,
            PurchaseOrderNo: purchaseOrderNo,
            PurchaseReceiptNo: WorldHistorySpec.PurchaseReceiptNo(index),
            SupplierCode: supplierCode,
            SkuCode: skuCode,
            UomCode: category.UomCode,
            Quantity: quantity,
            UnitPrice: decimal.Round(unitPrice, 2),
            OrderDate: snapped,
            PromisedDate: promisedDate,
            ReceiptDate: receiptDate,
            // 约 12% 的采购单尚未收货（在途），其余走完「收货→检验合格」链路。
            IsReceived: !random.Chance(0.12));
    }

    /// <summary>
    /// 全量采购计划表。与一期 ERP <c>WorldHistorySeedService.BuildPurchasePlans</c> 同字面量：
    /// 两侧必须逐字段得到同一张表，否则来料检验、上架任务与原料批次会指向不存在的收货单。
    /// </summary>
    public static IReadOnlyList<WorldHistoryPurchasePlan> BuildPurchasePlans(DateOnly asOfDate, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        var plans = new List<WorldHistoryPurchasePlan>();
        var weeks = WorldHistoryCalendar.WeekCount(asOfDate);
        var index = 0;
        for (var week = 0; week < weeks; week++)
        {
            var volume = WeeklyPurchaseOrderVolume(week, scale);
            var weekStart = WorldHistoryCalendar.WeekStart(week);
            for (var slot = 0; slot < volume; slot++)
            {
                index++;
                var candidate = weekStart.AddDays(Math.Min(slot * 6 / Math.Max(volume, 1), 5));
                var orderDate = candidate > asOfDate ? asOfDate : candidate;
                plans.Add(BuildPurchasePlan(index, orderDate, asOfDate));
            }
        }

        return plans;
    }

    /// <summary>原料批次号：与一期 ERP 收货行上的 <c>LOT-{采购单号}</c> 同构。</summary>
    public static string PurchasedLotNo(string purchaseOrderNo) => $"LOT-{purchaseOrderNo}";
}

public sealed record WorldHistoryPurchaseCategory(
    string Name,
    IReadOnlyList<string> SupplierCodes,
    IReadOnlyList<string> MaterialSkuCodes,
    string UomCode,
    decimal MinUnitPrice,
    decimal MaxUnitPrice,
    int MinQuantity,
    int MaxQuantity,
    int QuantityStep);

public sealed record WorldHistoryPurchasePlan(
    int Index,
    string PurchaseOrderNo,
    string PurchaseReceiptNo,
    string SupplierCode,
    string SkuCode,
    string UomCode,
    decimal Quantity,
    decimal UnitPrice,
    DateOnly OrderDate,
    DateOnly PromisedDate,
    DateOnly ReceiptDate,
    bool IsReceived)
{
    public decimal TotalAmount => Quantity * UnitPrice;
}
