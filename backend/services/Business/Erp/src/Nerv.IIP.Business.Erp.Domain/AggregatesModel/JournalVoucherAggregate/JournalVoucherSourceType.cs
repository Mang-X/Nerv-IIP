namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;

/// <summary>
/// 记账凭证的**来源单据类型**码表（GitHub #3278 / S2）。
///
/// **闭集类型，不是字符串**：私有构造 + 静态实例，所以调用方只能从这里挑一个已登记的类型，
/// 既造不出新码，也拼不出带分隔符的复合码。这正是 #3229 留下的那个残余缺陷的反面——
/// owner 在 #3278 的 A2 里明确排除了单列 <c>"{type}:{no}"</c> 的形状（分段歧义），
/// 于是类型与单号各自独立成列，<see cref="Code"/> 只承载类型这一件事。
/// </summary>
/// <remarks>
/// <para>
/// <b>码值稳定性</b>：<see cref="Code"/> 会**落库**（<c>journal_vouchers.source_type</c>），
/// 所以改名等于改存量行的含义；要退役某个类型必须走迁移，不能就地改串。
/// </para>
/// <para>
/// <b>这张登记表是手工的</b>：在本类型里声明一个静态实例却不追加到 <see cref="All"/>，编译期不会拦。
/// 补住这个方向的是 <c>JournalVoucherSourceContractTests</c> 里那条反射对撞断言（含码值互异），
/// 不是这个列表本身。
/// </para>
/// <para>
/// <b>与 <c>VoucherFamily</c>（<c>ErpVoucherNoPolicy.cs</c>）的关系</b>：两张表**故意不共用**。
/// <c>VoucherFamily</c> 是「派生凭证号怎么拼」的词表，按 #3278 的 S6/S7/S8 它会随凭证号改短号一起退役；
/// 本表是「这张凭证的来源单据是什么」的词表，凭证号退役后它仍然承载来源身份。
/// 共用会让 S8 删掉 <c>ErpVoucherNoPolicy</c> 时把来源码表一起带走。
/// </para>
/// </remarks>
public sealed class JournalVoucherSourceType
{
    private JournalVoucherSourceType(string code)
    {
        Code = code;
    }

    /// <summary>直接应付单入账（<c>CreateAccountPayableCommand</c>）。来源单号取应付单号。</summary>
    public static JournalVoucherSourceType AccountPayable { get; } = new("AP");

    /// <summary>
    /// 供应商发票 GR/IR 清账（发票匹配 / 放开付款冻结两条命令）。来源单号取**发票号**。
    ///
    /// ⭐ 这里**刻意**不与 <see cref="AccountPayable"/> 共用类型：两条路径今天产出同一个凭证号
    /// <c>JV-AP-{应付单号}</c>（#3278 §A2 点名的设计地雷）。来源列按「真正驱动这张凭证的单据」取值，
    /// 直接应付的驱动单据是应付单，发票清账的驱动单据是供应商发票，于是 <c>(类型, 单号)</c> 天然不相撞。
    /// </summary>
    public static JournalVoucherSourceType SupplierInvoice { get; } = new("SUPPINV");

    /// <summary>应收单入账。来源单号取应收单号。</summary>
    public static JournalVoucherSourceType AccountReceivable { get; } = new("AR");

    /// <summary>成本待定档入账。来源单号取待定档号。</summary>
    public static JournalVoucherSourceType CostCandidate { get; } = new("COST");

    /// <summary>付款执行（批准即执行 / 先批准后执行两条入口）。来源单号取付款执行单号。</summary>
    public static JournalVoucherSourceType PaymentExecution { get; } = new("APPAY");

    /// <summary>收款（登记即匹配 / 先登记后匹配两条入口）。来源单号取收款单号。</summary>
    public static JournalVoucherSourceType CashReceipt { get; } = new("ARCOL");

    /// <summary>采购收货 GR/IR 暂估。来源单号取采购收货单号。</summary>
    public static JournalVoucherSourceType GoodsReceiptIrAccrual { get; } = new("GRIR");

    /// <summary>采购退货。来源单号取采购退货单号。</summary>
    public static JournalVoucherSourceType PurchaseReturn { get; } = new("PRTN");

    /// <summary>客户红字通知单。来源单号取红字通知单号。</summary>
    public static JournalVoucherSourceType CreditNote { get; } = new("CN");

    /// <summary>工单成本资本化。来源单号取触发这次资本化的库存移动号。</summary>
    public static JournalVoucherSourceType WorkOrderCapitalization { get; } = new("WOC");

    /// <summary>
    /// 工单成本迟到调整。来源单号取触发这次调整的来源标识（报工单号 / 移动号 / 工序任务修订串）。
    ///
    /// ⭐ #3278 / S5 在这一族上**收窄**了唯一性：今天的凭证号是
    /// <c>JV-WOCADJ-{workOrderId}-{sourceId}</c>，而唯一键是 <c>(WOCADJ, sourceId)</c>，**少了工单号一段**。
    /// 收窄之所以取不到值，是因为 7 处生产侧取值全部来自「在 (org, env) 内唯一、且只归属一个工单」的单据标识
    /// （扫描面、逐项枚举与失效方向写在
    /// <c>ErpCostAccountingPostgresAcceptanceTests.PostgreSQL_work_order_cost_adjustment_key_drops_the_work_order_segment</c>）。
    /// ⚠️ **新增** <c>CostVariancePosting.PostLateAdjustmentAsync</c> 调用点时必须重做那次测量：
    /// 传一个「工单内才唯一」的标识会让这条收窄变成真的塌号，且没有任何门禁会为此转红。
    /// </summary>
    public static JournalVoucherSourceType WorkOrderCostAdjustment { get; } = new("WOCADJ");

    /// <summary>
    /// 手工凭证（<c>PostJournalVoucherCommand</c>）。没有上游单据，来源单号取凭证号自身——
    /// 这与国内财务实务一致：手工凭证的「来源」就是这张凭证本身。
    /// </summary>
    public static JournalVoucherSourceType Manual { get; } = new("MANUAL");

    /// <summary>
    /// 全部已登记类型。契约用例从这里枚举，并与反射枚举到的静态实例对撞。
    /// </summary>
    public static IReadOnlyList<JournalVoucherSourceType> All { get; } =
    [
        AccountPayable,
        SupplierInvoice,
        AccountReceivable,
        CostCandidate,
        PaymentExecution,
        CashReceipt,
        GoodsReceiptIrAccrual,
        PurchaseReturn,
        CreditNote,
        WorkOrderCapitalization,
        WorkOrderCostAdjustment,
        Manual,
    ];

    /// <summary>落库到 <c>journal_vouchers.source_type</c> 的码值。</summary>
    public string Code { get; }

    public override string ToString() => Code;
}
