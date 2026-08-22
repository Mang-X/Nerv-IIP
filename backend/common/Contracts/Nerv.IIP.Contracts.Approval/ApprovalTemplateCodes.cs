namespace Nerv.IIP.Contracts.Approval;

/// <summary>
/// 跨服务共用的审批模板码（<c>templateCode</c>）**唯一事实来源**。
///
/// 本类型只表达产品运行时契约：业务服务发起侧、Approval 产品基线种子与界面侧
/// 必须逐字共用这些中性码，禁止再依赖《工厂世界观设定集》的 <c>APT-WB-*</c> 号段。
/// WorldHistory 旧码是已持久化历史链和确定性回链的冻结边界，仅由
/// <see cref="WorldHistoryNcrDispositionApprovals"/> 和 WorldHistory seed 引用，不得倒流回产品契约。
/// 新增或修改模板码时，必须同时在 Approval 产品种子里补齐同码、同单据类型的模板。
/// </summary>
public static class ApprovalTemplateCodes
{
    /// <summary>
    /// 采购订单下达审批模板（新建、采购申请转单、RFQ 转单）。
    /// </summary>
    public const string PurchaseOrderRelease = "purchase-order-release";

    /// <summary>
    /// 采购订单变更再审批模板。单据类型仍是
    /// <see cref="ApprovalDocumentTypes.PurchaseOrder"/>，下达与变更由模板码区分。
    /// </summary>
    public const string PurchaseOrderChange = "purchase-order-change";

    /// <summary>
    /// NCR 处置评审的产品模板。世界观历史链继续使用冻结旧码，
    /// 确定性回链的盐串不引用本常量。
    /// </summary>
    public const string NcrDisposition = "ncr-disposition";

    /// <summary>销售订单「信用解冻」审批模板（#1290 / #1305）：ERP 发起侧与种子逐字共用。</summary>
    public const string SalesCreditRelease = "erp-sales-credit-release";

    /// <summary>
    /// 盘点差异审批模板（#1344 扩修，第八例词表错配）。Inventory 的
    /// <c>StockCountAdjustmentApprovalOptions.TemplateCode</c> 此前默认 <c>COUNT-VARIANCE</c>，
    /// 种子无此模板 → 差异超阈值的盘点确认必 400（走查台账 #66 盘点死单成因之一）。
    /// 由 Approval 产品种子补齐同码模板。
    /// </summary>
    /// 配套的单据类型是 <see cref="ApprovalDocumentTypes.StockCountVariance"/>。
    public const string StockCountVariance = "stock-count-variance";

    /// <summary>工程变更发布审批模板；ProductEngineering 发布校验与 Approval seed 逐字共用。</summary>
    public const string EngineeringChangeOrder = "engineering-change-order";
}
