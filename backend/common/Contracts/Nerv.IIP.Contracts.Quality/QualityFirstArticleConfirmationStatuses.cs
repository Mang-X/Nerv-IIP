namespace Nerv.IIP.Contracts.Quality;

/// <summary>
/// 首件确认读契约的判定进度取值（#2779）。表达「某工单某工序是否已经有首件检验任务、是否已判定」，
/// 判定结论本身仍使用 <see cref="QualityInspectionDispositionStatuses"/> 的取值。
/// </summary>
public static class QualityFirstArticleConfirmationStatuses
{
    /// <summary>该工单工序当前没有首件检验任务：尚未触发，或该 SKU/工作中心没有生效的首件检验档。</summary>
    public const string NotOpened = "not-opened";

    /// <summary>首件检验任务已开出但尚未判定。</summary>
    public const string Pending = "pending";

    /// <summary>首件检验任务已判定，结论见判定字段。</summary>
    public const string Decided = "decided";
}
