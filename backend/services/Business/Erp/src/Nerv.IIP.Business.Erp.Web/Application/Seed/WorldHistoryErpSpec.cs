using Nerv.IIP.Business.Erp.Domain.AggregatesModel.GLAccountAggregate;

namespace Nerv.IIP.Business.Erp.Web.Application.Seed;

/// <summary>
/// L1 背景历史的 **ERP 独有**形状：采购节奏（设定集 §7「约 480 张采购订单，补货节奏与生产量匹配」）
/// 与凭证用的中文科目表。销售侧的跨服务共享形状在 <see cref="WorldHistorySpec"/>。
/// </summary>
public static class WorldHistoryErpSpec
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

    /// <summary>单张历史采购单的确定性内容。</summary>
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

    #region 中文科目表

    public const string ReceivableAccountCode = "1122";
    public const string BankAccountCode = "1002";
    public const string RevenueAccountCode = "6001";

    /// <summary>
    /// 凭证用到的三个科目。ERP 的 <c>ApplicationDbContext</c> 会为凭证行自动补建缺失科目，
    /// 但自动补建的科目名等于科目编码；这里预先建好中文名，避免演示页面出现「1122」当名字。
    /// </summary>
    public static readonly IReadOnlyList<WorldHistoryGlAccount> GlAccounts =
    [
        new(ReceivableAccountCode, "应收账款", GLAccountType.Asset),
        new(BankAccountCode, "银行存款", GLAccountType.Asset),
        new(RevenueAccountCode, "主营业务收入", GLAccountType.Revenue),
    ];

    #endregion
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

public sealed record WorldHistoryGlAccount(string Code, string Name, GLAccountType Type);
