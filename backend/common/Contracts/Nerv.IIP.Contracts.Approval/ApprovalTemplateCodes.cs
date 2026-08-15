namespace Nerv.IIP.Contracts.Approval;

/// <summary>
/// 跨服务共用的审批模板码（<c>templateCode</c>）**唯一事实来源**。
///
/// 审批模板码是自由字符串、没有 CodeSet 兜底，于是「发起侧（业务服务）、种子侧（Approval）、
/// 界面侧」三方各写各的字面量时，类型系统一个字也拦不住：#1344 里 ERP 转采购订单 / 发起 RFQ
/// 硬编码 <c>erp-purchase-order-release</c>，而种子模板落库的是 <c>APT-WB-PO-001</c>——
/// 该码从未有模板落库，转单 / RFQ 在种子态**必 400**「Approval template was not found」
/// （第六例词表错配，与 #1327 NCR 三方错配同构）；Inventory 盘点差异的
/// <c>COUNT-VARIANCE</c> 是同款第八例。
///
/// 因此凡是发起 / 种子 / 校验按模板码判定的地方一律引用本类型的常量，禁止再写字面量
/// （与 <c>ApprovalDecisions</c>「取值权威只有一份」同一姿势，#1313）。
/// **加一个模板码 = 同时在审批种子里补一张同码同单据类型的模板**，否则种子态照样 400。
/// </summary>
public static class ApprovalTemplateCodes
{
    /// <summary>
    /// 采购订单下达审批模板。权威取**落库事实**：种子模板与全部世界观历史采购审批链
    /// （<c>PO-2026-####</c>）挂的都是 <c>APT-WB-PO-001</c>；ERP 侧旧字面量
    /// <c>erp-purchase-order-release</c> 从未有模板落库，弃用。
    ///
    /// 采购订单变更再审批（<c>RequestPurchaseOrderChangeCommand</c>，旧字面量
    /// <c>erp-purchase-order-change</c> 同样从未落库）也走本模板：同一单据类型
    /// （purchase-order）、同一审批人（总经理），种子只此一张采购模板。
    /// **已知体验问题**：变更链与下达链的 <c>documentId</c> 都是采购订单号，<c>documentId</c>
    /// 不具区分力，只有 <c>chainId</c> 不同——审批人收件箱会出现「同模板同单号两条链」，
    /// 只能靠发起时间 / 金额分辨。要在收件箱上区分变更与下达，须拆出独立的变更模板码
    /// 并由种子补一张同码模板（未纳入本次收敛）。
    /// </summary>
    public const string PurchaseOrderRelease = "APT-WB-PO-001";

    /// <summary>销售订单「信用解冻」审批模板（#1290 / #1305）：ERP 发起侧与种子逐字共用。</summary>
    public const string SalesCreditRelease = "erp-sales-credit-release";

    /// <summary>
    /// 盘点差异审批模板（#1344 扩修，第八例词表错配）。Inventory 的
    /// <c>StockCountAdjustmentApprovalOptions.TemplateCode</c> 此前默认 <c>COUNT-VARIANCE</c>，
    /// 种子无此模板 → 差异超阈值的盘点确认必 400（走查台账 #66 盘点死单成因之一）。
    /// 收敛进 <c>APT-WB-</c> 号段，并由审批种子补齐同码模板。
    /// </summary>
    /// 配套的单据类型是 <see cref="ApprovalDocumentTypes.StockCountVariance"/>。
    public const string StockCountVariance = "APT-WB-CNT-001";

    /// <summary>工程变更发布审批模板；ProductEngineering 发布校验与 Approval seed 逐字共用。</summary>
    public const string EngineeringChangeOrder = "APT-WB-ECO-001";
}
