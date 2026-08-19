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
    /// 本码**只覆盖下达**（新建采购订单、采购申请转单、RFQ 转单）。采购订单变更再审批
    /// （<c>RequestPurchaseOrderChangeCommand</c>）自 #1685 起改走独立的
    /// <see cref="PurchaseOrderChange"/>：#1344 收敛时两者共用本码，而变更链与下达链的
    /// <c>documentId</c> 都是采购订单号、<c>documentType</c> 也同为
    /// <see cref="ApprovalDocumentTypes.PurchaseOrder"/>，只有 <c>chainId</c> 不同——审批人收件箱
    /// 出现「同模板同单号两条链」，只能靠发起时间 / 金额分辨（#1344 当时记为已知体验问题）。
    /// 拆出独立模板码后，收件箱待办的「当前步骤」列显示不同步骤名（总经理审批 / 采购变更审批）、
    /// 审批链列表的「模板」列直接显示两个不同的码，该体验问题已消除。
    /// </summary>
    public const string PurchaseOrderRelease = "APT-WB-PO-001";

    /// <summary>
    /// 采购订单**变更**再审批模板（#1685 从 <see cref="PurchaseOrderRelease"/> 拆出，
    /// 沿用种子历史模板的 <c>APT-WB-</c> 号段）。ERP 变更发起侧
    /// （<c>RequestPurchaseOrderChangeCommandHandler</c> 的修订重提 / 变更两个分支）与审批种子
    /// （<c>WorldHistoryApprovalSeedService.SeedTemplatesAsync</c>）逐字共用本码。
    ///
    /// 单据类型**刻意仍是** <see cref="ApprovalDocumentTypes.PurchaseOrder"/>：documentType 描述
    /// 「被审的是哪种单据」，被审对象仍是同一张采购订单；而 ERP 回写消费侧
    /// （<c>ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder</c>）、审批委托的单据范围、
    /// 界面单据类型词表三处都按 documentType 判定，换新值必须三处同步，漏一处即回写静默丢事件
    /// （#1683 同款坑）。区分下达与变更的职责由**模板码**承担，不由单据类型承担。
    ///
    /// 顺带闭合的一个结构性隐患：审批链的待办唯一键
    /// （<c>ApprovalChain.BuildPendingIdentityKey</c>）由 <c>(org, env, templateCode, 单据引用)</c> 构成，
    /// 共用模板码时同一张采购订单的下达链与变更链的待办唯一键完全相同。
    /// </summary>
    public const string PurchaseOrderChange = "APT-WB-PO-002";

    /// <summary>
    /// NCR 处置评审审批模板（世界观历史号段，#1684）。权威取落库事实：种子模板与全部世界观历史
    /// NCR 处置审批链（<c>NCR-2026-####</c>）挂的都是 <c>APT-WB-NCR-001</c>。收敛进契约的原因是
    /// 该码现在参与跨服务确定性回链盐串（<see cref="WorldHistoryNcrDispositionApprovals"/>）：
    /// Approval 侧种子与 Quality 侧回链两边都要逐字引用同一个码，任何一侧写字面量都会让回链静默指空。
    /// </summary>
    public const string NcrDisposition = "APT-WB-NCR-001";

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
