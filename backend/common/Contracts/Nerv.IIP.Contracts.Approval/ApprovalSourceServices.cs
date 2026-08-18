namespace Nerv.IIP.Contracts.Approval;

/// <summary>
/// 审批来源服务（<c>sourceService</c>）的**跨服务唯一事实来源**。
///
/// <c>sourceService</c> 是自由字符串、没有 CodeSet 兜底，而且**发起时完全不参与校验**：
/// <c>StartApprovalChainCommandHandler</c> 找模板的谓词只有
/// <c>(organizationId, environmentId, templateCode, documentType)</c> 四元组，
/// <c>sourceService</c> 不在其中——词表写错了发起侧一路绿灯，只在**回写消费侧**按来源分流时静默失效
/// （消费者不匹配即 <c>return</c>，无日志、无异常、无死信）。
///
/// #1683 即此形态：世界观种子给采购审批链写的是 <c>erp</c>，ERP 回写消费侧只认 <c>business-erp</c>，
/// 于是「采购审批通过但订单永停 pending 且全链路无任何报错」——走查靠肉眼才发现。
///
/// 因此凡是按来源服务判定 / 落库 / 发起的地方一律引用本类型的常量，禁止再写字面量
/// （与 <c>ApprovalDocumentTypes</c>「取值权威只有一份」同一姿势）。
/// </summary>
public static class ApprovalSourceServices
{
    public const string ProductEngineering = "product-engineering";

    /// <summary>
    /// ERP 业务服务。**三方共用**：ERP 发起侧（采购订单下达 / 变更再审批 / 销售信用解冻）、
    /// 审批种子侧（<c>WorldHistoryApprovalSpec.PurchaseSourceService</c>）、
    /// ERP 回写消费侧（<c>ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder</c>）——
    /// 任一侧漂移即回写静默丢事件（#1683：种子写 <c>erp</c>、消费侧认 <c>business-erp</c>，
    /// 采购审批通过后订单永停 pending）。
    ///
    /// 注意与 <c>Nerv.IIP.Contracts.Erp.ErpIntegrationEventSources.BusinessErp</c> 区分：
    /// 那个是**集成事件信封**的发布方标识（<c>IntegrationEvent.SourceService</c>），
    /// 本常量是**审批单据引用**里的来源业务服务（<c>ApprovalDocumentReference.SourceService</c>），
    /// 两者字面量恰好相同但语义不同，不可互相引用。
    /// </summary>
    public const string BusinessErp = "business-erp";
}
