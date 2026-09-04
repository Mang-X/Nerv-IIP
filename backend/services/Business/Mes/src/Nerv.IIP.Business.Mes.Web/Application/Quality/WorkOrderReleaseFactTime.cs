namespace Nerv.IIP.Business.Mes.Web.Application.Quality;

/// <summary>
/// 发给 Quality 的工单发布时刻口径。直投（#3117）与存量回填（#3000）共用本方法，两条路径不各写一份。
///
/// Quality 的 <c>PeriodicInspectionOperation.ApplyRelease</c> 要求发布时刻不晚于它已掌握的任何一条报工，
/// 否则整封发布事实被判为无效业务事实进死信；而 MES 的工单在 <c>created</c> 状态就能开工报工（#3113），
/// 「已有报工的工单事后补下达」按发布动作那一刻记时刻必然触犯该守卫。
/// Quality 收到的报工是 MES 这批报工的子集，因此按 MES 侧最早报工取下界对 Quality 一定成立。
///
/// 工序完工事实（<c>CompletedAtUtc</c>）同受该守卫约束，但不需要单独进这个下界：
/// 完工时刻只由报工命令给出（<c>MesProductionCommands</c> 把 <c>request.ReportedAtUtc</c> 交给
/// <c>OperationActualTimeSettlementCoordinator.CompleteAsync</c>），它本身就是某一条报工的时刻，
/// 因此恒不早于最早报工。
/// </summary>
internal static class WorkOrderReleaseFactTime
{
    /// <param name="candidate">
    /// 本路径能拿到的发布时刻候选：直投用调用方给的下达时刻，回填没有任何发布时刻可用、
    /// 用「该工单最早工序建单时刻」重建。**两条路径的唯一差别就在这一个参数。**
    /// </param>
    /// <param name="earliestReportedAtUtc">该工单最早报工时刻；没有报工时为 <c>null</c>。</param>
    public static DateTimeOffset LowerBound(DateTimeOffset candidate, DateTimeOffset? earliestReportedAtUtc)
        => earliestReportedAtUtc is { } earliest && earliest < candidate ? earliest : candidate;
}
