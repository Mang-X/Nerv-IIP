namespace Nerv.IIP.Contracts.Approval;

/// <summary>
/// 审批动作（裁决与链上其他操作）的**跨服务唯一事实来源**（#1857）。
///
/// 本词表原先只存在于 <c>Nerv.IIP.Business.Approval.Domain</c>，跨服务够不着，于是：
/// <list type="bullet">
/// <item>Gateway 契约里 <c>decision</c> 是自由 <c>string</c>（<c>types.gen.ts</c> →
/// <c>ResolveApprovalStepRequest.decision?: string</c>），**类型层拦不住**拼写与大小写漂移；
/// 审批中心此前发的是 <c>'Approve' / 'Reject' / 'Resolve'</c>，一切裁决必 400（#1311），
/// 其中 <c>Resolve</c> 更是后端从未支持过的值；</item>
/// <item>Notification 的审批动作消费侧只能裸写 <c>"withdraw"</c> 来区分「待办」与「消息」。</item>
/// </list>
///
/// 取值一律小写：领域侧裁决时先归一化成小写再落库，
/// <see cref="StepResolutions"/> 的比较也是大小写不敏感的。
/// </summary>
public static class ApprovalDecisions
{
    /// <summary>裁决：通过。</summary>
    public const string Approve = "approve";

    /// <summary>裁决：驳回（链转 <see cref="ApprovalChainStatuses.Rejected"/>，终态）。</summary>
    public const string Reject = "reject";

    /// <summary>裁决：退回发起人（链转 <see cref="ApprovalChainStatuses.Returned"/>）。</summary>
    public const string Return = "return";

    /// <summary>
    /// 发起人撤回（链转 <see cref="ApprovalChainStatuses.Withdrawn"/>）。
    /// Notification 消费侧按本值把审批动作事件分流成「消息」而非「待办」——
    /// 其余动作都要给收件人生成待办。
    /// </summary>
    public const string Withdraw = "withdraw";

    /// <summary>发起人重新提交（链回到 <see cref="ApprovalChainStatuses.Pending"/>）。</summary>
    public const string Resubmit = "resubmit";

    /// <summary>加签：在当前步骤追加审批人。</summary>
    public const string AddSigner = "add_signer";

    /// <summary>
    /// 转办：把当前步骤交给他人。
    ///
    /// 注意与以下**字面量相同但语义不同**者区分，不可互相引用：
    /// Inventory 的库存移动类型 <c>transfer</c>（调拨）与 Quality 检验任务的转派动作 <c>transfer</c>
    /// ——各自域内的取值面，与审批动作只是恰好同值。
    /// </summary>
    public const string Transfer = "transfer";

    /// <summary>
    /// 裁决某一步时允许提交的取值（approve / reject / return）——**唯一权威**。
    /// 聚合的 <c>ResolveStep</c> 与应用层校验器共用这一份，避免校验器比领域更严
    /// （曾经校验器写死小写字面量，"Approve" 被拦成无线索的 400，#1311）。
    /// 大小写不敏感：领域侧本就先归一化成小写再落库。
    /// </summary>
    public static readonly IReadOnlySet<string> StepResolutions =
        new HashSet<string>([Approve, Reject, Return], StringComparer.OrdinalIgnoreCase);
}
