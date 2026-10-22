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
/// <b>为什么当初没有与 <c>VoucherFamily</c> 共用一张表</b>（#3278 / S8 已兑现）：
/// <c>VoucherFamily</c> 是「派生凭证号怎么拼」的词表，随 S6/S7 把凭证号改成分配器短号后失去调用点，
/// 已在 S8 连同 <c>ErpVoucherNoPolicy</c> 一起删除；本表是「这张凭证的来源单据是什么」的词表，
/// 它承载的是 S5 那条 <c>(source_type, source_no)</c> 唯一索引的键，与凭证号无关。
/// ⇒ 当初若共用，S8 那次删除会把来源码表一起带走。⭐ 这两张表的**生存期本就不同**，
/// 今天它们看起来相似不是合并的理由。
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
    /// ⭐ 这里**刻意**不与 <see cref="AccountPayable"/> 共用类型。
    /// <b>改前</b>两条路径产出同一个凭证号 <c>JV-AP-{应付单号}</c>（#3278 §A2 点名的设计地雷）。
    /// ⚠️ <b>#3278 / S6（PR #3495）之后那个「同号」前提已不存在</b>：两侧各自从
    /// <c>JournalVoucherNoAllocation</c> 取一个分配器短号。
    ///
    /// ⛔ <b>但「不再同号」不是合并这两个类型的理由</b>，恰恰相反——凭证号从 S6 起只是账面显示值，
    /// 「这张凭证是否已记」完全由 S5 那条
    /// <c>(organization_id, environment_id, source_type, source_no)</c> partial unique index 判定。
    /// 来源列的取值口径是「**真正驱动这张凭证的单据**」：直接应付的驱动单据是应付单，
    /// 发票清账的驱动单据是供应商发票。把两个类型合并会让这两条业务路径落进同一把键，
    /// **互相挡住对方建凭证**，即直接改掉那条唯一索引的语义。
    ///
    /// ⭐ <b>#3278 / S5 换键时本族强度不变（零收窄零放宽），当时的承重理由是</b>：
    /// 两个盖章点 <c>ErpProcurementCommands.cs:1089</c> 与 <c>:1221</c> 走的是**同一个工厂**
    /// <c>FinanceVoucherFactory.ForSupplierInvoiceGrIrClearing</c>，**改前**凭证号由该工厂内联的派生构造
    /// 从 <c>payable.PayableNo</c> 拼出（⚠️ 那个派生入口已随 S8 删除，⛔ 别去源码里找它），
    /// 而两处都为**当前这张发票**新建一张应付单（<c>AccountPayable.Create(..., invoice.InvoiceNo, ...)</c>，
    /// 实读 <c>:1074</c> 与 <c>:1206</c>）⇒ 发票号与应付单号在这两条路径上一一对应，新旧两把键**等强**。
    /// ⚠️ <c>ErpProcurementCommands.cs:1148</c> 的 <c>existingPayableForInvoice</c> 早退守卫确实存在，
    /// 但它挡的是「同一张发票被放行两次」，⛔ 不是这里键强度不变的理由。
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

    /// <summary>
    /// 工单成本资本化。来源单号取触发这次资本化的库存移动号。
    ///
    /// ⭐ #3278 / S5：本族与 <see cref="WorkOrderCostAdjustment"/> 是**同一种收窄**——
    /// 换键前的凭证号是 <c>JV-WOC-{workOrderId}-{movementId}</c>，唯一键是 <c>(WOC, movementId)</c>，
    /// 同样**少了工单号一段**（⚠️ S7 之后凭证号已是分配器短号，那个派生形状只是收窄方向的参照系，
    /// ⛔ 不是今天的凭证号）。结论仍成立：<c>InventoryMovementId</c> 是 Inventory 侧的全局移动标识，
    /// 一次移动只对应一个成品入库事件、也只归属一个工单，所以收窄取不到值。
    /// ⛔ 但别把它读成「本族没有收窄」。
    /// </summary>
    public static JournalVoucherSourceType WorkOrderCapitalization { get; } = new("WOC");

    /// <summary>
    /// 工单成本迟到调整。来源单号取触发这次调整的来源标识（报工单号 / 移动号 / 工序任务修订串）。
    ///
    /// ⭐ #3278 / S5 在这一族上**收窄**了唯一性：换键前的凭证号是
    /// <c>JV-WOCADJ-{workOrderId}-{sourceId}</c>，而唯一键是 <c>(WOCADJ, sourceId)</c>，**少了工单号一段**
    /// （⚠️ S7 之后凭证号已是分配器短号，那个派生形状只是收窄方向的参照系，⛔ 不是今天的凭证号）。
    /// 收窄之所以取不到值，是因为 7 处生产侧取值全部来自「在 (org, env) 内唯一、且只归属一个工单」的单据标识
    /// （扫描面、逐项枚举与失效方向写在
    /// <c>ErpCostAccountingPostgresAcceptanceTests.PostgreSQL_work_order_cost_adjustment_key_drops_the_work_order_segment</c>）。
    ///
    /// ⭐ <b>工序任务那四项的承重依据在生产者侧、不在 Erp 侧</b>：
    /// MES <c>OperationTaskEntityTypeConfiguration.cs:77-78</c> 的
    /// <c>ak_operation_tasks_scope_task = HasAlternateKey(OrganizationId, EnvironmentId, OperationTaskIdValue)</c>
    /// **不含 <c>WorkOrderId</c>** —— 这是数据库层的物理唯一约束。
    /// （Erp 侧 <c>GetOrCreateStateAsync(org, env, OperationTaskId)</c> 不带工单号只证明「Erp 把它当键用」，
    /// 是弱一档的推断，别拿它当承重理由。）
    ///
    /// ⚠️ <b>两条已登记的残余，都不会有门禁为它们转红</b>：
    /// ① **新增** <c>CostVariancePosting.PostLateAdjustmentAsync</c> 调用点时必须重做那次测量——
    ///    传一个「工单内才唯一」的标识会让这条收窄变成真的塌号；
    /// ② 7 个表达式共用同一个 <c>source_type = WOCADJ</c>，但来自**三个互不相干的命名空间**
    ///    （<c>RPT-*</c> 报工号 / GUID 库存移动号 / <c>{taskId}-r{n}</c> 工序任务修订串）。
    ///    **每个命名空间内部**的唯一性各有硬约束，⛔ 但**跨命名空间的互斥没有任何东西保证**——
    ///    今天只靠三种串的形状天然不重叠。任何一侧改号规则都可能让它们撞上。
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
