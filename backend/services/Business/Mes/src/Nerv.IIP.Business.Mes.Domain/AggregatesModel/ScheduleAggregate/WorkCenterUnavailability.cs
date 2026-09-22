namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;

public partial record WorkCenterUnavailabilityId : IGuidStronglyTypedId;

public sealed class WorkCenterUnavailability : Entity<WorkCenterUnavailabilityId>, IAggregateRoot
{
    private WorkCenterUnavailability()
    {
    }

    private WorkCenterUnavailability(
        string? organizationId,
        string? environmentId,
        string downtimeEventNo,
        string workCenterId,
        DateTimeOffset fromUtc,
        DateTimeOffset? toUtc,
        string reason,
        string? deviceAssetId)
    {
        OrganizationId = string.IsNullOrWhiteSpace(organizationId) ? null : organizationId.Trim();
        EnvironmentId = string.IsNullOrWhiteSpace(environmentId) ? null : environmentId.Trim();
        DowntimeEventNo = DomainGuard.Required(downtimeEventNo, nameof(downtimeEventNo));
        WorkCenterId = DomainGuard.Required(workCenterId, nameof(workCenterId));
        FromUtc = fromUtc;
        ToUtc = toUtc;
        Reason = DomainGuard.Required(reason, nameof(reason));
        DeviceAssetId = string.IsNullOrWhiteSpace(deviceAssetId) ? null : deviceAssetId.Trim();
    }

    public string WorkCenterId { get; private set; } = string.Empty;
    public string DowntimeEventNo { get; private set; } = string.Empty;
    public string? OrganizationId { get; private set; }
    public string? EnvironmentId { get; private set; }
    public DateTimeOffset FromUtc { get; private set; }
    public DateTimeOffset? ToUtc { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string? DeviceAssetId { get; private set; }

    public static WorkCenterUnavailability Open(
        string? organizationId,
        string? environmentId,
        string downtimeEventNo,
        string workCenterId,
        DateTimeOffset fromUtc,
        DateTimeOffset? toUtc,
        string reason,
        string? deviceAssetId)
    {
        return new WorkCenterUnavailability(organizationId, environmentId, downtimeEventNo, workCenterId, fromUtc, toUtc, reason, deviceAssetId);
    }

    /// <summary>
    /// 确认停机恢复：把这段不可用窗口的结束时刻定在 <paramref name="restoredAtUtc"/>。
    ///
    /// <para><b>口径：首次恢复为准</b>（#3343 裁定）。恢复时刻是**现场事实**，所以本方法
    /// 收调用方给的时刻而**不是**服务端现铸（<c>RecordDowntimeEventCommand</c> 同样允许补录
    /// 一段已经结束的停机）；但一段停机只结束一次，已有结束时刻的窗口不再接受第二次写入。</para>
    ///
    /// <para><b>为什么是「首次」而不是「最后一次」——这条不变量在本实体上早就有人在执行了。</b>
    /// <c>Close</c> 的另外两个生产调用点（<c>PersistentMesPlanningStore.CloseUnavailabilityAsync</c>
    /// 的两个重载，服务于 CAP 消费者 <c>AssetRestoredIntegrationEventHandlerForReschedule</c>）
    /// 取数谓词里**写着 <c>x.ToUtc == null</c>**——它们从一开始就只关闭尚未关闭的窗口。
    /// 缺陷路径 <c>ConfirmDowntimeRecoveryCommandHandler</c> 按 <c>DowntimeEventNo</c>/<c>Id</c>
    /// 精确定位、**不带**该谓词，于是绕过了它。本守卫是把这条既有不变量收进聚合，
    /// 不是新引入一种偏好；「最后一次为准」反而会与那两个既存调用点的谓词相矛盾。
    /// **连带事实**：正因为那两处谓词已经滤掉了已关闭窗口，第一条守卫在 CAP 那条路径上**不可达**，
    /// 加它不会把设备恢复事件推进死信。第二条守卫在 CAP 路径上**是可达的**（跨服务载荷的
    /// <c>RestoredAtUtc</c> 可以早于 <c>FromUtc</c>），那封事件会重试耗尽后进死信台账——
    /// 这是刻意的：显式死信优于静默落一个负时长窗口。</para>
    ///
    /// <para><b>形态照同域近邻 <c>ChangeoverRecord.Complete</c></b>（同样是「有开始时刻的现场窗口 +
    /// 可空结束时刻」、同样是工作台上点一次「结束」、同样收调用方时刻）：两条守卫一一对应，
    /// 且都排在赋值**之前**——域方法先改状态再 <c>throw</c> 会被 UoW 回滚，那种写法在真机上
    /// 永远看不到被拒的状态。</para>
    ///
    /// <para><b>本方法不负责「时刻不得落在未来」</b>：那是信任边界的事，本仓由
    /// <c>WorkOrderReleaseFactTime.UntrustedCandidate</c> 承担（#3117），这里不重复夹。</para>
    /// </summary>
    public void Close(DateTimeOffset restoredAtUtc)
    {
        if (ToUtc is not null)
        {
            throw new KnownException("该停机事件已恢复，不能重复恢复。");
        }

        if (restoredAtUtc < FromUtc)
        {
            throw new KnownException("停机恢复时间不能早于停机开始时间。");
        }

        ToUtc = restoredAtUtc;
    }
}
