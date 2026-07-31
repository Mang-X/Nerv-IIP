namespace Nerv.IIP.Contracts.Approval;

/// <summary>
/// 跨服务共用的审批模板码（<c>templateCode</c>）**唯一事实来源**。
///
/// 审批模板码是自由字符串、没有 CodeSet 兜底，于是「发起侧（ERP）、种子侧（Approval）、
/// 界面侧」三方各写各的字面量时，类型系统一个字也拦不住：#1344 里 ERP 转采购订单 / 发起 RFQ
/// 硬编码 <c>erp-purchase-order-release</c>，而种子模板落库的是 <c>APT-WB-PO-001</c>——
/// 该码从未有模板落库，转单 / RFQ 在种子态**必 400**「Approval template was not found」
/// （第六例词表错配，与 #1327 NCR 三方错配同构）。
///
/// 因此凡是发起 / 种子 / 校验按模板码判定的地方一律引用本类型的常量，禁止再写字面量
/// （与 <c>ApprovalDecisions</c>「取值权威只有一份」同一姿势，#1313）。
/// </summary>
public static class ApprovalTemplateCodes
{
    /// <summary>
    /// 采购订单下达审批模板。权威取**落库事实**：种子模板与全部世界观历史采购审批链
    /// （<c>PO-2026-####</c>）挂的都是 <c>APT-WB-PO-001</c>；ERP 侧旧字面量
    /// <c>erp-purchase-order-release</c> 从未有模板落库，弃用（见 <see cref="PurchaseOrderReleaseAliases"/>）。
    /// 采购订单变更再审批（<c>RequestPurchaseOrderChangeCommand</c>）也走本模板：同一单据类型
    /// （purchase-order）、同一审批人（总经理），种子只此一张采购模板，变更链与下达链靠
    /// <c>documentId</c> / <c>chainId</c> 区分；未来若需要独立变更流程再拆码值。
    /// </summary>
    public const string PurchaseOrderRelease = "APT-WB-PO-001";

    /// <summary>销售订单「信用解冻」审批模板（#1290 / #1305）：ERP 发起侧与种子逐字共用。</summary>
    public const string SalesCreditRelease = "erp-sales-credit-release";

    /// <summary>
    /// 采购订单下达审批模板码的**受理集合**：权威码值 + 历史别名。
    ///
    /// 别名只用于「读既有链」的向后兼容（本次收敛前 ERP 发起侧写的是
    /// <c>erp-purchase-order-release</c> / 变更侧写的是 <c>erp-purchase-order-change</c>，
    /// 走查曾经由 API 手建过同码模板绕行）；新发起的链一律用 <see cref="PurchaseOrderRelease"/>。
    /// </summary>
    public static readonly IReadOnlySet<string> PurchaseOrderReleaseAliases = new HashSet<string>(
        [PurchaseOrderRelease, "erp-purchase-order-release", "erp-purchase-order-change"],
        StringComparer.OrdinalIgnoreCase);
}
