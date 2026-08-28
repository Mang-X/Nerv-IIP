namespace Nerv.IIP.Contracts.Quality;

/// <summary>
/// 首件确认读契约的判定进度取值（#2779）。表达「某工单某工序的首件当前处在哪一步」，
/// 判定结论本身仍使用 <see cref="QualityInspectionDispositionStatuses"/> 的取值。
/// 消费方（MES 报工门禁）只应在 <see cref="NotRequired"/>，或 <see cref="Decided"/> 且结论合格时放行。
/// </summary>
public static class QualityFirstArticleConfirmationStatuses
{
    /// <summary>Quality 已掌握该工序事实，且该物料/工作中心没有生效的首件检验档：本工序无需首件。</summary>
    public const string NotRequired = "not-required";

    /// <summary>应有首件却尚无任务：首件档已生效但任务未开出，或该工序的工单发布事实尚未到达 Quality。</summary>
    public const string NotOpened = "not-opened";

    /// <summary>首件检验任务已开出但尚未判定。</summary>
    public const string Pending = "pending";

    /// <summary>首件检验任务已判定，结论见判定字段；发生过复检时表达最近一次复检的结论。</summary>
    public const string Decided = "decided";
}
