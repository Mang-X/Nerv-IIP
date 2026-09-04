namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;

/// <summary>
/// 发给 Quality 的工单**发布事实时刻**。它不是一个可以随手写的 <see cref="DateTimeOffset"/>：
/// 这个值必须**不晚于** MES 已经掌握的该工单任何一条**既有活动**——报工，或工序完工。
///
/// <para><b>为什么。</b>Quality 的 <c>PeriodicInspectionOperation</c> 有**三**处守卫都拿它当下界：
/// <c>ApplyRelease</c> 对「已有报工早于发布时刻」抛出、对「已有完工早于发布时刻」抛出，
/// <c>ApplyProductionReport</c> 对**此后的每一条**报工同样判「报工早于发布」抛出。
/// 任一处抛出都是整封发布事实进死信。
/// 而 MES 的工单在 <c>created</c> 状态就能开工、报工、乃至完工（#3113），
/// 「已有活动的工单事后补下达」按发布动作那一刻记时刻必然触犯它们（#3117）。</para>
///
/// <para><b>这个类型实际强度是多少（按实测写，不按期望写）。</b>
/// <list type="bullet">
/// <item>**被编译器关死的**：本类型只有一个私有构造与**一个**公开工厂 <see cref="NotLaterThan"/>；
/// <see cref="Value"/> 是只读自动属性（`with { Value = ... }` 实测 <c>CS0200</c>）；
/// 无 <c>implicit</c>/<c>explicit operator</c>。它是**引用类型**且本程序集 <c>Nullable=enable</c> +
/// <c>TreatWarningsAsErrors=true</c>，因此 <c>null</c> / <c>default</c> 传进
/// <see cref="WorkOrderReleasedDomainEvent"/> 是**编译错误**，不是一个悄悄进死信的值。
/// （曾经是 <c>readonly record struct</c>：那时 <c>default(T).Value</c> = <c>default(DateTimeOffset)</c>，
/// 恰好命中 Quality <c>PeriodicInspectionIntegrationEventHandlers</c> 的
/// <c>payload.ReleasedAtUtc == default</c> → 整封进死信。改成引用类型正是为了让编译器接住这一格。）</item>
/// <item>**没有被编译器关死的**：<see cref="NotLaterThan"/> 的第二参可以传 <c>null</c>，
/// 而 <c>null</c>（真的没有既有活动）与 <c>null</c>（压根没去查）在类型层面**不可区分**。
/// 编译器强制的是「**交出一个显式的下界参数**」，不是「**你确实去查过**」——后者由调用方自证，
/// 每个传 <c>null</c> 的调用点都必须在注释里写明它凭什么。**别把这句读成「编译器强制取下界」。**</item>
/// </list></para>
///
/// <para><b>「不得落在未来」是另一半，只在信任边界上收。</b>
/// 发布时刻若落在未来，该工序此后的**每一条**报工都会被判「报工早于发布」进死信。
/// 未来值只可能来自**外部输入**——HTTP 请求体的 <c>ReleasedAtUtc</c>、跨服务载荷的 <c>RequestedAtUtc</c>。
/// 它由 <see cref="UntrustedCandidate"/> 承担。
/// **这一半的强度低于上一半，如实写明**：<see cref="UntrustedCandidate"/> 是 <c>public static</c>、
/// 返回裸 <see cref="DateTimeOffset"/>，新增入口**不调它也能编译**；
/// 「当前只有两个信任边界、且都调了它」是**某一时点的枚举事实**（见该方法注释），不是被维持的性质。</para>
/// </summary>
public sealed record WorkOrderReleaseFactTime
{
    private WorkOrderReleaseFactTime(DateTimeOffset value) => Value = value;

    /// <summary>发布事实时刻的取值。</summary>
    public DateTimeOffset Value { get; }

    /// <summary>
    /// **唯一**的公开构造口径：<c>发布事实时刻 = min(候选时刻, 该工单最早既有活动时刻)</c>。
    /// 直投（#3117）与存量回填（#3000）共用本方法，两条路径不各写一份，
    /// **差别只在候选**：直投用调用方给的下达时刻，回填没有任何发布时刻可用、
    /// 用「该工单最早工序建单时刻」重建。
    ///
    /// <para>Quality 掌握的活动是 MES 这批活动的子集，因此按 MES 侧最早活动取下界对 Quality 一定成立。</para>
    /// </summary>
    /// <param name="candidateAtUtc">本路径能拿到的发布时刻候选。</param>
    /// <param name="earliestExistingActivityAtUtc">
    /// 该工单**最早既有活动**时刻，取「最早报工」与「最早工序完工」中更早者；两者都没有时为 <c>null</c>。
    ///
    /// <para><b>为什么完工也必须进来（#3117 第三轮补）。</b>完工时刻**不是**总由报工命令给出：
    /// <c>OperationActualTimeSettlementCoordinator.CompleteAsync</c> 有**两个**生产调用点——
    /// <c>MesProductionCommands</c>（报工带完工，时刻 = <c>ReportedAtUtc</c>，同事务落一条报工），
    /// 以及 <c>MesWorkbenchCommands</c> 的工序动作 <c>"complete"</c>
    /// （时刻 = <c>ChangedAtUtc</c>，<c>pendingProductionReportNos</c> 传 <c>[]</c>，**不产生任何报工行**）。
    /// 后者使「零报工、却已有完工」成为可达状态：只按报工取下界时 MES 侧查不到任何活动、
    /// 发布事实取调用方时刻，随后被 Quality 的完工守卫判冲突整封进死信——
    /// 与本票要修的缺陷同型，只是换了一面。**上一版把这一面排除掉的理由只点名了两个调用点中的一个。**</para>
    /// </param>
    public static WorkOrderReleaseFactTime NotLaterThan(
        DateTimeOffset candidateAtUtc,
        DateTimeOffset? earliestExistingActivityAtUtc)
        => new(earliestExistingActivityAtUtc is { } earliest && earliest < candidateAtUtc
            ? earliest
            : candidateAtUtc);

    /// <summary>
    /// **信任边界上**对外部给来的发布时刻候选做的唯一处理：夹到不晚于 <paramref name="nowUtc"/>。
    ///
    /// <para>发布是一件**已经发生**的事，它的时刻不可能在未来；而 HTTP 请求体与跨服务载荷都可能因
    /// 数据错误或对端时钟漂移给出未来时刻，随后该工序的每一条报工都进死信（#3117 的缺陷换个入口重演）。
    /// 定义只写在这里一处，边界共用；边界以内的任何一层都不再重复夹。</para>
    ///
    /// <para><b>当前调用点（枚举事实，非编译期性质）：</b><c>ReleaseWorkOrderEndpoint</c>（HTTP 请求体）与
    /// <c>NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder</c>（跨服务载荷）。
    /// 新增外部入口的人**不调本方法也能编译**——这一格没有编译期防线，靠的是这条注释与两条用例。</para>
    ///
    /// <para><b>还有第三条外部输入在影响发布事实时刻，但它不经过本方法（如实登记，非疏漏）：</b>
    /// 工序动作端点的 <c>req.ChangedAtUtc</c>（<c>MesEndpoints</c> 里 start/pause/resume/complete
    /// 四个动作共用、只做 <c>?? GetUtcNow()</c> 空值回落、**不夹**）。它经
    /// <c>OperationTask.ExistingEndUtc</c> → 「最早既有活动」→ 发布事实时刻 → 信封 <c>OccurredAtUtc</c>。
    /// **未来值不构成风险**（<see cref="NotLaterThan"/> 只取更早者），
    /// **但任意回拨的过去值会把发布事实时刻拉早**。是否夹紧未在 #3117 内裁定。</para>
    ///
    /// <para><b>为什么不把 <c>CancelWorkOrderEndpoint</c> 也算进这份枚举</b>（它同样是 <c>?? GetUtcNow()</c>、同样未夹）：
    /// 取消写进 <c>ExistingEndUtc</c> 的那条路径**在系统层到不了发布时刻**——
    /// <c>OperationTask.Cancel</c> 的生产调用点恰 1 处，它整单取消工单与全部工序，
    /// 而 <c>WorkOrder.ThrowIfCannotRelease</c> 拒掉 <c>Cancelled</c>。
    /// 故这份枚举以「**能到达发布事实时刻的外部输入**」为口径，不是「所有未夹的时刻字段」。</para>
    ///
    /// <para>返回裸 <see cref="DateTimeOffset"/> 而不是本类型：它只处理取值里的一项，
    /// 结果仍要交给 <see cref="NotLaterThan"/> 与既有活动下界合并，不能单独充当发布事实时刻。</para>
    /// </summary>
    public static DateTimeOffset UntrustedCandidate(DateTimeOffset candidateAtUtc, DateTimeOffset nowUtc)
        => candidateAtUtc < nowUtc ? candidateAtUtc : nowUtc;
}
