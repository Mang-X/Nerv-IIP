namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;

/// <summary>
/// 发给 Quality 的工单**发布事实时刻**。它不是一个可以随手写的 <see cref="DateTimeOffset"/>：
/// 这个值必须**不晚于** MES 已经掌握的任何一条同工单报工。
///
/// <para><b>为什么。</b>Quality 的 <c>PeriodicInspectionOperation</c> 两处守卫都拿它当下界：
/// <c>ApplyRelease</c> 对「已有报工早于发布时刻」直接抛出、整封发布事实进死信；
/// <c>ApplyProductionReport</c> 对**此后的每一条**报工同样判「报工早于发布」抛出。
/// 而 MES 的工单在 <c>created</c> 状态就能开工报工（#3113），
/// 「已有报工的工单事后补下达」按发布动作那一刻记时刻必然触犯前者（#3117）；
/// 发布时刻若落在未来，则触犯后者、该工序此后每一条报工都进死信。</para>
///
/// <para><b>为什么由类型承担而不是由注释承担。</b>#3117 之前这条不变量根本不存在（转换器取 <c>UtcNow</c>）；
/// 它被引入的同一刻就有两条发布路径（直投 + #3000 回填）、三个聚合入口。
/// 把它写成裸 <c>DateTimeOffset</c> 加一句 XML 注释，等于让「下一个新增发布入口的人记得读注释」承重。
/// <see cref="WorkOrderReleasedDomainEvent.ReleasedAt"/> 因此声明为本类型：
/// **每一条发布路径都必须先交出报工下界，才拿得到一个能塞进领域事件的值**，由编译器强制，不由人记。</para>
///
/// <para><b>「不得落在未来」是同一条不变量的另一半，只在信任边界上收。</b>
/// 发布时刻若落在未来，该工序此后的**每一条**报工都会被 Quality 判为「报工早于发布」进死信。
/// 但未来值只可能来自**外部输入**——HTTP 请求体的 <c>ReleasedAtUtc</c>、跨服务载荷的 <c>RequestedAtUtc</c>；
/// 仓库内部的常量与种子不跨这条边界。故它由 <see cref="UntrustedCandidate"/> 承担，
/// 只在那两处边界各调一次（<c>ReleaseWorkOrderEndpoint</c> 与
/// <c>NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder</c>），
/// **不是每一层都夹一遍**，也不为内部调用方支付时钟依赖。</para>
///
/// <para><b>关于 <c>default</c>。</b>结构体无法禁掉 <c>default(WorkOrderReleaseFactTime)</c>，
/// 它的 <see cref="Value"/> 是 <see cref="DateTimeOffset.MinValue"/>——**早于任何报工**，
/// 即恰好落在本不变量的安全侧（Quality 两条守卫都不会因它而抛）。
/// 失效方向是「时刻不准」，不是「整封进死信」，故不再为此换成引用类型。</para>
/// </summary>
public readonly record struct WorkOrderReleaseFactTime
{
    private WorkOrderReleaseFactTime(DateTimeOffset value) => Value = value;

    /// <summary>发布事实时刻的取值。</summary>
    public DateTimeOffset Value { get; }

    /// <summary>
    /// 唯一的公开构造口径：<c>发布事实时刻 = min(候选时刻, 最早报工时刻)</c>。
    /// 直投（#3117）与存量回填（#3000）共用本方法，两条路径不各写一份，
    /// **差别只在候选**：直投用调用方给的下达时刻，回填没有任何发布时刻可用、
    /// 用「该工单最早工序建单时刻」重建。
    ///
    /// <para>Quality 收到的报工是 MES 这批报工的子集，因此按 MES 侧最早报工取下界对 Quality 一定成立。</para>
    ///
    /// <para>工序完工事实（<c>CompletedAtUtc</c>）同受 Quality 守卫约束，但不需要单独进这个下界：
    /// 完工时刻只由报工命令给出（<c>MesProductionCommands</c> 把 <c>request.ReportedAtUtc</c> 交给
    /// <c>OperationActualTimeSettlementCoordinator.CompleteAsync</c>），它本身就是某一条报工的时刻，
    /// 因此恒不早于最早报工。</para>
    /// </summary>
    /// <param name="candidateAtUtc">本路径能拿到的发布时刻候选。</param>
    /// <param name="earliestReportedAtUtc">该工单最早报工时刻；没有任何报工时为 <c>null</c>。</param>
    public static WorkOrderReleaseFactTime NotLaterThan(
        DateTimeOffset candidateAtUtc,
        DateTimeOffset? earliestReportedAtUtc)
        => new(earliestReportedAtUtc is { } earliest && earliest < candidateAtUtc ? earliest : candidateAtUtc);

    /// <summary>
    /// 聚合自身的创建时刻。**仅供 <see cref="WorkOrder.MarkReleased()"/>**——那条重载不携带工序、
    /// 也拿不到任何报工集合，聚合自己知道的唯一下界就是「工单不可能早于自己被创建就被发布」。
    /// 它是 <c>internal</c> 的：应用层拿不到这个绕过报工下界的入口。
    /// </summary>
    internal static WorkOrderReleaseFactTime AtAggregateCreation(DateTimeOffset createdAtUtc) => new(createdAtUtc);

    /// <summary>
    /// **信任边界上**对外部给来的发布时刻候选做的唯一处理：夹到不晚于 <paramref name="nowUtc"/>。
    ///
    /// <para>发布是一件**已经发生**的事，它的时刻不可能在未来；而 HTTP 请求体与跨服务载荷都可能因
    /// 数据错误或对端时钟漂移给出未来时刻，随后该工序的每一条报工都进死信（#3117 的缺陷换个入口重演）。
    /// 定义只写在这里一处，两个边界共用；边界以内的任何一层都不再重复夹。</para>
    ///
    /// <para>返回裸 <see cref="DateTimeOffset"/> 而不是本类型：它只处理取值里的一项，
    /// 结果仍要交给 <see cref="NotLaterThan"/> 与报工下界合并，不能单独充当发布事实时刻。</para>
    /// </summary>
    public static DateTimeOffset UntrustedCandidate(DateTimeOffset candidateAtUtc, DateTimeOffset nowUtc)
        => candidateAtUtc < nowUtc ? candidateAtUtc : nowUtc;
}
