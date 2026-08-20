namespace Nerv.IIP.Contracts.Approval;

/// <summary>
/// 审批链状态（<c>ApprovalChain.Status</c>）的**跨服务唯一事实来源**（#1857）。
///
/// 本词表原先只存在于 <c>Nerv.IIP.Business.Approval.Domain</c>，跨服务够不着，
/// 于是所有「读审批链 HTTP 响应、判它是否通过」的下游只能裸写字面量：
/// <c>ProductEngineeringReleaseCommands</c> 的工程变更发布校验、
/// <c>HttpApprovalChainStatusClient</c> 的 NCR 处置 / CAPA 关闭放行判定，
/// 都在裸比 <c>"approved"</c>——这正是 #1683「发起侧与消费侧各写各的字面量、
/// 漂移后静默失效」那一形态的温床（此处漂移的后果是放行判定恒假：
/// 审批明明通过了，发布 / 处置仍被拒，且没有任何日志或异常指向真因）。
///
/// **不可与 <see cref="ApprovalResults"/> 互相引用——同值不同义**：
/// <list type="bullet">
/// <item><see cref="ApprovalResults"/> 是 <c>ApprovalCompletedIntegrationEvent</c> 载荷里的
/// **审批结果**（approved / rejected / returned），随集成事件契约演进；</item>
/// <item>本词表是审批链的**状态机取值**（pending / approved / rejected / returned / withdrawn），
/// 随聚合状态机与已落库历史数据固定，且多出 <c>pending</c> 与 <c>withdrawn</c> 两个结果面根本没有的成员。</item>
/// </list>
/// 两者当前在 approved / rejected / returned 三处恰好同值，但一方改值不得带动另一方。
///
/// 同理，Approval 领域内的 <c>ApprovalStepStatuses</c>（步骤状态，含 <c>skipped</c>）
/// 是**步骤**这一层的状态面，不是链状态，也不可互相引用；它未随本票下沉，
/// 因为目前没有任何跨服务消费面读步骤状态。
/// </summary>
public static class ApprovalChainStatuses
{
    /// <summary>审批中。链创建即为此态，也是撤回后重新提交回到的态。</summary>
    public const string Pending = "pending";

    /// <summary>
    /// 审批通过。**跨服务放行判定的唯一依据**：PE 工程变更发布校验与 Quality 的
    /// NCR 处置 / CAPA 关闭放行都按本值判断链是否通过（大小写不敏感比较）。
    ///
    /// 注意与以下**字面量相同但语义不同**者区分，不可互相引用：
    /// <list type="bullet">
    /// <item><see cref="ApprovalResults.Approved"/>（集成事件载荷里的审批结果，见类型注释）；</item>
    /// <item>Quality 的 MRB 评审决定 <c>approved</c>（<c>NonconformanceReport</c> 的
    /// <c>MrbDecision</c>，质量域内状态机，与审批链无关）；</item>
    /// <item>IndustrialTelemetry 的设备控制命令审批态 <c>approved</c>（Ops 域内状态机）。</item>
    /// </list>
    /// </summary>
    public const string Approved = "approved";

    /// <summary>审批驳回（终态）。</summary>
    public const string Rejected = "rejected";

    /// <summary>审批退回发起人（可重新提交）。</summary>
    public const string Returned = "returned";

    /// <summary>发起人撤回（可重新提交）。<see cref="ApprovalResults"/> 没有对应成员。</summary>
    public const string Withdrawn = "withdrawn";
}
