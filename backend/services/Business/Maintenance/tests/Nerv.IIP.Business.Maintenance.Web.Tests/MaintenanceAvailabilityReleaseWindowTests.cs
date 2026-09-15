using MediatR;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;
using Nerv.IIP.Contracts.EquipmentRuntime;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

/// <summary>
/// 设备可用性窗口读面表达的是**历史占用记录**：占用窗口 <c>[AssetUnavailableFromUtc, 释放时刻]</c>
/// 与查询窗口求交。「释放时刻」只取聚合发 <c>AssetRestoredDomainEvent</c> 的那两个位点 ——
/// 完工（<c>CompletedAtUtc</c>）与取消（<c>CancelledAtUtc</c>）；两者皆无即尚未释放。
///
/// 回归背景：谓词曾写成 <c>Status == Open || CompletedAtUtc != null</c>，
/// 把 <c>MaintenanceWorkOrderStatus</c> 九态里的五态（Accepted / InProgress / Paused /
/// WaitingForParts / Cancelled）整段从读面上抹掉。后果不止界面漏显：该读面还是
/// <c>MaintenanceUnavailableWindowRuntimeHoursProvider</c> 的降级来源，
/// 「设备运行工时 = 窗口时长 − 不可用时长」会把正在维修的小时数算成运行。
///
/// 这里按**状态全集**参数化，而不是按被点名的状态列举：用例直接枚举
/// <c>Enum.GetValues&lt;MaintenanceWorkOrderStatus&gt;()</c>，新增枚举值走不到
/// <see cref="CreateUnavailableWorkOrderAt"/> 的到达路径就抛，即红。
/// </summary>
public sealed class MaintenanceAvailabilityReleaseWindowTests
{
    public static TheoryData<MaintenanceWorkOrderStatus> AllStatuses()
    {
        var data = new TheoryData<MaintenanceWorkOrderStatus>();
        foreach (var status in Enum.GetValues<MaintenanceWorkOrderStatus>())
        {
            data.Add(status);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public async Task Unavailable_work_order_occupies_the_window_until_it_is_released(MaintenanceWorkOrderStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        var windowStartUtc = now.AddHours(-3);
        var windowEndUtc = now.AddHours(3);
        var unavailableFromUtc = now.AddHours(-2);

        await using var dbContext = MaintenanceEndpointContractTests.CreateTestDbContext();
        var workOrder = CreateUnavailableWorkOrderAt(status, unavailableFromUtc);
        dbContext.MaintenanceWorkOrders.Add(workOrder);
        await dbContext.SaveChangesAsync();

        var response = await QueryAvailabilityAsync(dbContext, windowStartUtc, windowEndUtc);

        var window = Assert.Single(response.Items);
        Assert.Equal(EquipmentRuntimeAvailabilityStatus.Unavailable, window.AvailabilityStatus);
        Assert.Equal(unavailableFromUtc, window.StartUtc);

        // 期望值取聚合自身的释放时刻，不再另建一张「状态→是否已释放」的表：
        // 那张表本身就是生产代码明令不可用的「按状态枚举列举」口径。
        var releasedAtUtc = workOrder.CompletedAtUtc ?? workOrder.CancelledAtUtc;
        if (releasedAtUtc is null)
        {
            Assert.Equal(windowEndUtc, window.EndUtc);
        }
        else
        {
            Assert.Equal(releasedAtUtc.Value, window.EndUtc);
            Assert.True(window.EndUtc < windowEndUtc, "已释放的工单窗口右边界必须落在释放时刻，而不是查询窗口末端。");
        }
    }

    /// <summary>
    /// 报警清除**不是**资产释放：<c>MarkAlarmCleared</c> 既不清 <c>AssetUnavailable</c>，
    /// 也不发 <c>AssetRestoredDomainEvent</c>（全聚合只有完工与取消两个发射点）。
    /// 清警后工单仍可 <c>Accept</c> / <c>StartWork</c> 继续修 —— 此时领域仍算资产不可用，
    /// 读面若当它已释放就会少扣这段在途停机，正是本票要消除的高估方向。
    /// </summary>
    [Fact]
    public async Task Clearing_the_alarm_does_not_release_an_in_flight_work_order()
    {
        var now = DateTimeOffset.UtcNow;
        var windowStartUtc = now.AddHours(-3);
        var windowEndUtc = now.AddHours(3);
        var unavailableFromUtc = now.AddHours(-2);

        await using var dbContext = MaintenanceEndpointContractTests.CreateTestDbContext();
        var workOrder = MaintenanceWorkOrder.OpenFromAlarm(
            "org-001", "env-dev", "DEV-CNC-01", sourceAlarmId: "WH-DEV-CNC-01-spindle:0001", priority: "high");
        workOrder.MarkAssetUnavailable(unavailableFromUtc, "alarm downtime");
        workOrder.MarkAlarmCleared(now.AddHours(-1));
        workOrder.Accept("tech-001");
        workOrder.StartWork();
        dbContext.MaintenanceWorkOrders.Add(workOrder);
        await dbContext.SaveChangesAsync();

        var response = await QueryAvailabilityAsync(dbContext, windowStartUtc, windowEndUtc);

        var window = Assert.Single(response.Items);
        Assert.Equal(unavailableFromUtc, window.StartUtc);
        Assert.Equal(windowEndUtc, window.EndUtc);

        // 运行工时链是同一份数据的下游：占用一路顶到窗口末端，5 小时必须全额扣减。
        var runtime = await new MaintenanceUnavailableWindowRuntimeHoursProvider(new AvailabilityQuerySender(dbContext))
            .CalculateFallbackAsync("org-001", "env-dev", "DEV-CNC-01", windowStartUtc, windowEndUtc, CancellationToken.None);
        Assert.Equal(1m, Math.Round(runtime.RuntimeHours, 6));
    }

    /// <summary>
    /// 降级口径的设备运行工时消费同一读面：在途工单的停机小时数必须被扣减。
    /// 谓词漏掉在途态时这里返回整段窗口时长（6h），设备正在被修的 5 小时会被算成运行。
    /// </summary>
    [Fact]
    public async Task Fallback_runtime_hours_deduct_downtime_of_an_in_flight_work_order()
    {
        var now = DateTimeOffset.UtcNow;
        var windowStartUtc = now.AddHours(-3);
        var windowEndUtc = now.AddHours(3);
        var unavailableFromUtc = now.AddHours(-2);

        await using var dbContext = MaintenanceEndpointContractTests.CreateTestDbContext();
        var workOrder = CreateUnavailableWorkOrderAt(MaintenanceWorkOrderStatus.InProgress, unavailableFromUtc);
        dbContext.MaintenanceWorkOrders.Add(workOrder);
        await dbContext.SaveChangesAsync();

        var provider = new MaintenanceUnavailableWindowRuntimeHoursProvider(new AvailabilityQuerySender(dbContext));
        var result = await provider.CalculateFallbackAsync(
            "org-001", "env-dev", "DEV-CNC-01", windowStartUtc, windowEndUtc, CancellationToken.None);

        Assert.Equal(AssetRuntimeSources.Fallback, result.RuntimeSource);
        Assert.Equal(1m, Math.Round(result.RuntimeHours, 6));
    }

    private static async Task<EquipmentRuntimeAvailabilityResponse> QueryAvailabilityAsync(
        ApplicationDbContext dbContext,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc)
    {
        return await new QueryMaintenanceAvailabilityWindowsQueryHandler(dbContext).Handle(
            new QueryMaintenanceAvailabilityWindowsQuery(
                new EquipmentRuntimeAvailabilityRequest(
                    "org-001", "env-dev", windowStartUtc, windowEndUtc, ["DEV-CNC-01"], null)),
            CancellationToken.None);
    }

    /// <summary>
    /// 把工单推到目标状态。<c>MarkAssetUnavailable</c> 只在 Open 态可调，所以先登记占用再走状态机。
    /// </summary>
    private static MaintenanceWorkOrder CreateUnavailableWorkOrderAt(
        MaintenanceWorkOrderStatus status,
        DateTimeOffset unavailableFromUtc)
    {
        var workOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "high", "maintenance");
        workOrder.MarkAssetUnavailable(unavailableFromUtc, "repair downtime");
        switch (status)
        {
            case MaintenanceWorkOrderStatus.Open:
                break;
            case MaintenanceWorkOrderStatus.Cancelled:
                workOrder.Cancel();
                break;
            case MaintenanceWorkOrderStatus.Accepted:
                workOrder.Accept("tech-001");
                break;
            case MaintenanceWorkOrderStatus.InProgress:
                workOrder.Accept("tech-001");
                workOrder.StartWork();
                break;
            case MaintenanceWorkOrderStatus.Paused:
                workOrder.Accept("tech-001");
                workOrder.StartWork();
                workOrder.Pause(waitingForParts: false);
                break;
            case MaintenanceWorkOrderStatus.WaitingForParts:
                workOrder.Accept("tech-001");
                workOrder.StartWork();
                workOrder.Pause(waitingForParts: true);
                break;
            case MaintenanceWorkOrderStatus.Completed:
                FinishWorkOrder(workOrder);
                break;
            case MaintenanceWorkOrderStatus.Verified:
                FinishWorkOrder(workOrder);
                workOrder.Verify();
                break;
            case MaintenanceWorkOrderStatus.Closed:
                FinishWorkOrder(workOrder);
                workOrder.Verify();
                workOrder.Close();
                break;
            default:
                throw new NotSupportedException($"未覆盖的工单状态 {status}：新增状态必须补上到达该状态的路径。");
        }

        Assert.Equal(status, workOrder.Status);
        return workOrder;
    }

    private static void FinishWorkOrder(MaintenanceWorkOrder workOrder)
    {
        workOrder.Accept("tech-001");
        workOrder.StartWork();
        workOrder.Finish("已修复", "mechanical-failure", 30, spareParts: null, technicianUserId: "tech-001");
    }

    /// <summary>只转发可用窗口查询：运行工时降级路径就只发这一种请求。</summary>
    private sealed class AvailabilityQuerySender(ApplicationDbContext dbContext) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var query = Assert.IsType<QueryMaintenanceAvailabilityWindowsQuery>(request);
            var response = await new QueryMaintenanceAvailabilityWindowsQueryHandler(dbContext).Handle(query, cancellationToken);
            return (TResponse)(object)response;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException("运行工时降级路径不发无返回值请求。");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("运行工时降级路径不发弱类型请求。");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("运行工时降级路径不发流式请求。");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("运行工时降级路径不发流式请求。");
    }
}
