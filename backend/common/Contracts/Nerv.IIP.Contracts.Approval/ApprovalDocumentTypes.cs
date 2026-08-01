namespace Nerv.IIP.Contracts.Approval;

/// <summary>
/// 审批单据类型（<c>documentType</c>）的**跨服务唯一事实来源**。
///
/// 审批链的 <c>documentType</c> 是自由字符串、没有 CodeSet 兜底，于是「发起侧、种子侧、
/// 消费侧」三方各写各的字面量时，类型系统一个字也拦不住：#1327 里 UI 发 <c>quality-ncr</c>、
/// 种子模板挂 <c>ncr-disposition</c>、Quality 的审批白名单又只认 <c>quality-ncr</c>，
/// 于是种子态下 NCR 处置审批**结构性走不通**（走查只能靠临时新建模板绕行）。
///
/// 因此凡是需要按 documentType 判定的地方一律引用本类型的常量，禁止再写字面量
/// （与 <c>ApprovalDecisions</c>「取值权威只有一份」同一姿势，#1313）。
/// </summary>
public static class ApprovalDocumentTypes
{
    /// <summary>NCR 处置审批。语义即「不合格品处置」这一被审对象，也是种子模板/历史链/委托已落库的码值。</summary>
    public const string NcrDisposition = "ncr-disposition";

    /// <summary>CAPA 关闭审批。</summary>
    public const string CapaClosure = "corrective-action-closure";

    /// <summary>采购订单审批。</summary>
    public const string PurchaseOrder = "purchase-order";

    /// <summary>
    /// 盘点差异审批（#1344）。Inventory 发起侧、审批种子模板、Inventory 回写消费侧三方共用——
    /// 审批模板按 <c>(templateCode, documentType)</c> 双条件命中，任一漂移即 400 / 回写静默丢事件。
    /// </summary>
    public const string StockCountVariance = "inventory-count-variance";

    /// <summary>工程变更单；审批模板、工程变更发起面和发布校验共用。</summary>
    public const string EngineeringChangeOrder = "engineering-change-order";

    /// <summary>
    /// NCR 处置审批的**受理集合**：权威码值 + 历史别名。
    ///
    /// 别名只用于「读既有链」的向后兼容（本次收敛前由界面发起的链落的是 <c>quality-ncr</c>），
    /// 新发起的链一律用 <see cref="NcrDisposition"/>。
    /// </summary>
    public static readonly IReadOnlySet<string> NcrDispositionAliases = new HashSet<string>(
        [NcrDisposition, "nonconformance-report-disposition", "nonconformance-report", "quality-ncr"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>CAPA 关闭审批的受理集合：权威码值 + 历史别名。</summary>
    public static readonly IReadOnlySet<string> CapaClosureAliases = new HashSet<string>(
        [CapaClosure, "quality-capa-closure", "quality-capa"],
        StringComparer.OrdinalIgnoreCase);
}
