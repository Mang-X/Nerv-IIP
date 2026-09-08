namespace Nerv.IIP.Contracts.Quality;

/// <summary>
/// 首件确认读契约的判定进度取值（#2779）。表达「某工单某工序的首件当前处在哪一步」，
/// 判定结论本身仍使用 <see cref="QualityInspectionDispositionStatuses"/> 的取值。
/// </summary>
/// <remarks>
/// 取值划分的判据是**该状态靠什么恢复**，不是「像不像已完成」（#2780）：
/// <see cref="NotOpened"/> 只能靠一次报工恢复（报工事件是首件任务的唯一建单触发点），
/// <see cref="NotSynchronized"/> 只能靠工单发布事实到达恢复（与报工无关）。
/// 两者合成一个取值时，消费方无论放行还是拒绝都必然错一种：拒绝会锁死前者（唯一的恢复动作正好被拒），
/// 放行会漏检后者（恢复动作永不发生，却一直放行）。
/// </remarks>
public static class QualityFirstArticleConfirmationStatuses
{
    /// <summary>Quality 已掌握该工序事实，且该物料/工作中心没有生效的首件检验档：本工序无需首件。</summary>
    public const string NotRequired = "not-required";

    /// <summary>
    /// Quality 已掌握该工序事实、命中生效首件档，但首件任务尚未开出。
    /// 开单的唯一触发点是该工序的报工事件，因此**下一次报工就是首件那一件**。
    /// </summary>
    public const string NotOpened = "not-opened";

    /// <summary>
    /// Quality 尚未掌握该工序事实（工单发布投影未到达），因而无法回答本工序是否需要首件。
    /// 它靠 <c>mes.WorkOrderReleased</c> 到达恢复，不靠报工；消费方必须 fail closed。
    /// </summary>
    public const string NotSynchronized = "not-synchronized";

    /// <summary>首件检验任务已开出但尚未判定。</summary>
    public const string Pending = "pending";

    /// <summary>首件检验任务已判定，结论见判定字段；发生过复检时表达最近一次复检的结论。</summary>
    public const string Decided = "decided";
}
