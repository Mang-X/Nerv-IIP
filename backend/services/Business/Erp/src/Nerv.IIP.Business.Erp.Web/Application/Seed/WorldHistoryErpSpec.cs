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

    #region 经营对象（采购申请 / 询价 / 供应商报价 / 销售机会 / 成本候选）

    /// <summary>采购申请号段：<c>PR-2026-</c> 已被收货单占用，申请单用 <c>PRQ-</c> 前缀。</summary>
    public static string PurchaseRequisitionNo(int index) => $"PRQ-2026-{index:D4}";

    public static string RfqNo(int index) => $"RFQ-2026-{index:D4}";

    /// <summary>同一 RFQ 的多家报价用 -A/-B 区分（供应商按品类内顺序）。</summary>
    public static string SupplierQuotationNo(int index, int supplierOrdinal) =>
        $"SQ-2026-{index:D4}-{(char)('A' + supplierOrdinal)}";

    public static string OpportunityNo(int index) => $"OPP-2026-{index:D4}";
    public static string CostCandidateNo(int index) => $"COST-2026-{index:D4}";

    /// <summary>采购申请的 MRP 建议引用：跨服务只靠业务编码引用 DemandPlanning 的建议流。</summary>
    public static string MrpSuggestionId(int index) => $"MRP-SUG-2026-{index:D4}";

    /// <summary>每 6 张采购单走一次询价→报价流程（框架内直采为主、周期性询比价为辅）。</summary>
    public const int RfqEveryNthPurchase = 6;

    /// <summary>每 40 张销售订单前置一个销售机会（大客户框架/新平台意向）。</summary>
    public const int OpportunityEveryNthSalesOrder = 40;

    /// <summary>每 8 张已收货采购单进入一次成本归集候选。</summary>
    public const int CostCandidateEveryNthReceipt = 8;

    /// <summary>未转化的在途采购申请条数：随规模缩放，至少 3 条（列表页的"待处理"故事）。</summary>
    public static int OpenRequisitionCount(int totalPurchaseOrders) =>
        Math.Max(3, (int)Math.Round(totalPurchaseOrders * 0.03, MidpointRounding.AwayFromZero));

    /// <summary>按物料码回查采购品类（各品类物料码不相交）。</summary>
    public static WorldHistoryPurchaseCategory CategoryOf(string skuCode) =>
        PurchaseCategories.Single(category => category.MaterialSkuCodes.Contains(skuCode));

    #endregion

    #region 应付账款（erp.account_payables）

    /// <summary>
    /// 应付号段（ERP 独有，设定集 §9 二期补登记）。
    /// 与销售侧 <c>AR-2026-#####</c> 显式分段：应付按采购单序号（4 位）编号，一张已收货采购单一条应付。
    /// </summary>
    public const string PayableNumberPrefix = "AP-2026-";

    public static string PayableNo(int index) => $"{PayableNumberPrefix}{index:D4}";

    /// <summary>账期候选（自然日）：与 <c>NET30/NET45/NET60</c> 付款条件一一对应。</summary>
    public static readonly IReadOnlyList<int> PayableTermDays = [30, 45, 60];

    /// <summary>到期后多久算「本该早已付掉」——早于此线的应付默认已付清。</summary>
    public const int PayableSettleGraceDays = 5;

    /// <summary>已过账期仍未付的比例（与供应商对账争议 / 质量索赔挂账）——应付账龄表上的逾期样本。</summary>
    public const double PayableOverdueUnpaidProbability = 0.06;

    /// <summary>刚到期那一档的已付比例（财务按周批量付款，到期当周有先有后）。</summary>
    public const double PayableJustDueSettledProbability = 0.6;

    /// <summary>未到期应付里提前部分付款（预付 30%）的比例。</summary>
    public const double PayablePartialPrepayProbability = 0.15;

    /// <summary>提前部分付款的比例（预付款 30%）。</summary>
    public const decimal PayablePrepayRatio = 0.3m;

    /// <summary>
    /// 单条历史应付的确定性内容：只从**已收货**的采购单派生，
    /// 金额 / 供应商 / 来源单据号逐字取自该采购单的收货事实，因此应付页能一路追到收货单与采购单。
    ///
    /// 注意：<paramref name="asOfDate"/> 只影响付款进度（越老的账越可能已付清），
    /// 不影响号码与金额——所以在更晚的日期重跑时，已落库的应付不会与本函数冲突（幂等按号跳过）。
    /// </summary>
    public static WorldHistoryPayablePlan BuildPayablePlan(WorldHistoryPurchasePlan purchase, DateOnly asOfDate)
    {
        ArgumentNullException.ThrowIfNull(purchase);
        var payableNo = PayableNo(purchase.Index);
        var random = new WorldHistoryRandom($"payable:{payableNo}");
        var termDays = random.Pick(PayableTermDays);
        var invoiceDate = purchase.ReceiptDate;
        var dueDate = invoiceDate.AddDays(termDays);
        var amount = decimal.Round(purchase.TotalAmount, 2);

        decimal paidAmount;
        if (dueDate.AddDays(PayableSettleGraceDays) <= asOfDate)
        {
            paidAmount = random.Chance(PayableOverdueUnpaidProbability) ? 0m : amount;
        }
        else if (dueDate <= asOfDate)
        {
            paidAmount = random.Chance(PayableJustDueSettledProbability) ? amount : 0m;
        }
        else
        {
            paidAmount = random.Chance(PayablePartialPrepayProbability)
                ? decimal.Round(amount * PayablePrepayRatio, 2)
                : 0m;
        }

        return new WorldHistoryPayablePlan(
            Index: purchase.Index,
            PayableNo: payableNo,
            SourceDocumentNo: purchase.PurchaseReceiptNo,
            SupplierCode: purchase.SupplierCode,
            Amount: amount,
            PaidAmount: paidAmount,
            InvoiceDate: invoiceDate,
            DueDate: dueDate,
            PaymentTermCode: FormattableString.Invariant($"NET{termDays}"));
    }

    #endregion

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

/// <summary>单条历史应付账款（派生自一张已收货采购单）。</summary>
public sealed record WorldHistoryPayablePlan(
    int Index,
    string PayableNo,
    string SourceDocumentNo,
    string SupplierCode,
    decimal Amount,
    decimal PaidAmount,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    string PaymentTermCode)
{
    public decimal OpenAmount => Amount - PaidAmount;

    public bool IsSettled => PaidAmount >= Amount;

    public bool IsPartiallyPaid => PaidAmount > 0m && PaidAmount < Amount;
}
