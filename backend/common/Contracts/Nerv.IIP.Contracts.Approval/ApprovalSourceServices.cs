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

    /// <summary>
    /// Inventory 库存服务。**三方共用**：Inventory 发起侧
    /// （<c>ConfirmStockCountAdjustmentCommandHandler</c> 盘点差异超阈值分支）、
    /// 审批链落库的 <c>ApprovalDocumentReference.SourceService</c>、
    /// Inventory 回写消费侧（<c>ApprovalCompletedIntegrationEventHandlerForStockCountAdjustment</c>）——
    /// 消费侧的分流条件是 <c>(SourceService, DocumentType)</c> 二元组，任一侧漂移即回写静默丢事件
    /// （#1344 只把 <c>DocumentType</c> 提进契约、<c>SourceService</c> 仍是裸字面量，本常量补齐另一半）：
    /// 盘点差异审批通过后调整单永停 <c>pending-approval</c>、账面不动、库存仍冻结，且无日志、无异常、无死信。
    ///
    /// 注意与以下**字面量相同但语义不同**者区分，不可互相引用：
    /// <list type="bullet">
    /// <item>Inventory 的数据库 schema 名 <c>inventory</c>（EF 迁移 / <c>HasDefaultSchema</c>）；</item>
    /// <item><c>StockMovement.Post</c> 的 <c>sourceService</c> 参数（库存流水的上游来源单据服务，
    /// 取值域是 <c>wms</c> / <c>erp</c> / <c>mes</c> 之类，与审批无关）；</item>
    /// <item><c>Nerv.IIP.Business.Quality</c> 的 <c>InspectionRecord</c> 受理来源集合里的 <c>inventory</c>
    /// （质检记录来源）。</item>
    /// </list>
    /// 集成事件**信封**的发布方标识另有其类：<c>InventoryIntegrationEventSources.BusinessInventory</c>
    /// （值为 <c>business-inventory</c>），与本常量既不同值也不同义。
    /// </summary>
    public const string Inventory = "inventory";

    /// <summary>
    /// Quality 质量服务。当前**只有种子侧**在用：世界观 NCR 处置审批链落库的
    /// <c>ApprovalDocumentReference.SourceService</c>（<c>WorldHistoryApprovalSpec.NcrSourceService</c>）。
    /// 质量域目前没有 <c>ApprovalCompletedIntegrationEvent</c> 消费者，因此本值漂移**不会**立刻表现为回写丢事件；
    /// 但它逐字参与 <c>ApprovalChain.BuildPendingIdentityKey</c> 的 SHA256（该键上有唯一索引），
    /// 改值即改历史链的 pending 唯一键，且未来任何质量回写消费侧都必须认同一份取值——因此同样禁止写字面量。
    ///
    /// 注意与以下**字面量相同但语义不同**者区分，不可互相引用：
    /// <list type="bullet">
    /// <item><c>Nerv.IIP.Contracts.Inventory.InventoryMovementSourceServices.Quality</c>
    /// （库存移动请求的发起来源，用于状态转移流水）；</item>
    /// <item><c>InventoryQualityStatuses.Quality</c> / <c>ErpReceiptQualityStatuses.Quality</c>
    /// （库存质量状态码「待检」，是状态不是服务）；</item>
    /// <item>Quality 的数据库 schema 名 <c>quality</c>。</item>
    /// </list>
    /// 集成事件**信封**的发布方标识另有其值（<c>business-quality</c>），与本常量既不同值也不同义。
    /// </summary>
    public const string Quality = "quality";

    /// <summary>
    /// Quality 审批来源服务的**历史别名**（取值 <c>business-quality</c>），
    /// 与 <see cref="ApprovalDocumentTypes.NcrDispositionAliases"/> 同一姿势：
    /// 只用于「读既有链」的向后兼容匹配（<c>HttpApprovalChainStatusClient.QualitySourceServices</c>
    /// 受理集合），**新发起的链一律用 <see cref="Quality"/>**，任何写入面都不得引用本常量。
    ///
    /// 之所以进本词表而不是留作裸字面量：受理集合是「审批来源服务」这一概念的取值面，
    /// 权威只能有一份（#1683 的教训是发起侧与消费侧各写各的字面量、漂移后静默丢事件）。
    ///
    /// 注意与**字面量相同但语义不同**者区分，不可互相引用：
    /// <c>Nerv.IIP.Contracts.Quality.QualityIntegrationEventSources.BusinessQuality</c> 与
    /// <c>Nerv.IIP.Contracts.Inventory.InventoryIntegrationEventSources.BusinessQuality</c>
    /// 是集成事件**信封**的发布方标识（<c>IntegrationEvent.SourceService</c>），
    /// 那两个值随事件信封演进，本常量随审批历史数据固定——一方改值不得带动另一方。
    /// </summary>
    public const string QualityLegacyAlias = "business-quality";
}
