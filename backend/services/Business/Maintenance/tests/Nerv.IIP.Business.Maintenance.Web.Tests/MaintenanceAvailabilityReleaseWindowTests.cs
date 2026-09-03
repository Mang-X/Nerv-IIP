using MediatR;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;
using Nerv.IIP.Contracts.EquipmentRuntime;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

/// <summary>
/// 设备可用性窗口读面表达的是**历史占用记录**：占用窗口 <c>[AssetUnavailableFromUtc, 释放时刻]</c>
/// 与查询窗口求交。「释放时刻」是唯一判据，由完工（<c>CompletedAtUtc</c>）、取消（<c>CancelledAtUtc</c>）
/// 与报警清除（<c>AlarmClearedAtUtc</c>）派生；三者皆无即尚未释放。
///
/// 回归背景：谓词曾写成 <c>Status == Open || CompletedAtUtc != null</c>，
/// 把 <c>MaintenanceWorkOrderStatus</c> 九态里的五态（Accepted / InProgress / Paused /
/// WaitingForParts / Cancelled）整段从读面上抹掉。后果不止界面漏显：该读面还是
/// <c>MaintenanceUnavailableWindowRuntimeHoursProvider</c> 的降级来源，
/// 「设备运行工时 = 窗口时长 − 不可用时长」会把正在维修的小时数算成运行。
///
/// 这里按**状态全集**参数化，而不是按被点名的状态列举：
/// <see cref="ReleaseExpectations"/> 缺任何一个枚举值，<see cref="Every_work_order_status_is_classified"/> 即红。
/// </summary>
public sealed class MaintenanceAvailabilityReleaseWindowTests
{
    /// <summary>
    /// 每个工单状态在可用窗口读面上的预期：是否已释放（决定窗口右边界取释放时刻还是查询窗口末端）。
    /// 新增枚举值必须在此归类，否则完备性用例红。
    /// </summary>
    private static readonly IReadOnlyDictionary<MaintenanceWorkOrderStatus, bool> ReleaseExpectations =
        new Dictionary<MaintenanceWorkOrderStatus, bool>
        {
            [MaintenanceWorkOrderStatus.Open] = false,
            [MaintenanceWorkOrderStatus.Accepted] = false,
            [MaintenanceWorkOrderStatus.InProgress] = false,
            [MaintenanceWorkOrderStatus.Paused] = false,
            [MaintenanceWorkOrderStatus.WaitingForParts] = false,
            [MaintenanceWorkOrderStatus.Completed] = true,
            [MaintenanceWorkOrderStatus.Verified] = true,
            [MaintenanceWorkOrderStatus.Closed] = true,
            [MaintenanceWorkOrderStatus.Cancelled] = true,
        };

    public static TheoryData<MaintenanceWorkOrderStatus> AllStatuses()
    {
        var data = new TheoryData<MaintenanceWorkOrderStatus>();
        foreach (var status in ReleaseExpectations.Keys)
        {
            data.Add(status);
        }

        return data;
    }

    [Fact]
    public void Every_work_order_status_is_classified()
    {
        Assert.Equal(
            Enum.GetValues<MaintenanceWorkOrderStatus>().OrderBy(x => x).ToArray(),
            ReleaseExpectations.Keys.OrderBy(x => x).ToArray());
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

        var releasedAtUtc = workOrder.CompletedAtUtc ?? workOrder.CancelledAtUtc;
        if (ReleaseExpectations[status])
        {
            Assert.NotNull(releasedAtUtc);
            Assert.Equal(releasedAtUtc!.Value, window.EndUtc);
            Assert.True(window.EndUtc < windowEndUtc, "已释放的工单窗口右边界必须落在释放时刻，而不是查询窗口末端。");
        }
        else
        {
            Assert.Null(releasedAtUtc);
            Assert.Equal(windowEndUtc, window.EndUtc);
        }
    }

    /// <summary>
    /// 报警清除是释放来源之一（报警单的占用随报警消失），但工单完工后右边界必须取完工时刻 ——
    /// 两个来源同时存在时先后次序不能倒过来，否则窗口会在设备还没修完时提前收口。
    /// </summary>
    [Fact]
    public async Task Completion_wins_over_alarm_clear_when_both_release_sources_exist()
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
        FinishWorkOrder(workOrder);
        dbContext.MaintenanceWorkOrders.Add(workOrder);
        await dbContext.SaveChangesAsync();

        var response = await QueryAvailabilityAsync(dbContext, windowStartUtc, windowEndUtc);

        var window = Assert.Single(response.Items);
        Assert.Equal(unavailableFromUtc, window.StartUtc);
        Assert.Equal(workOrder.CompletedAtUtc, window.EndUtc);
        Assert.True(window.EndUtc > now.AddHours(-1), "完工时刻晚于报警清除时刻，右边界不得回落到报警清除。");
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
