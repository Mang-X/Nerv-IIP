using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

/// <summary>
/// 领导演示「规模块」的可排性证据：用与 MasterData / ProductEngineering / MES 规模 seed 完全一致的
/// 数据形状（4 道有前后置工序 × 24 台资源 × 5×24h STANDARD 日历）跑真实的 problem 装配与
/// deterministic finite-capacity 算法，证明排产工作台的「批量生成」能真实吃到这批工单并产出方案。
/// 这不是性能基准（那是 MAN-581 / #1050 的 verify-business-scheduling-scale-benchmark.ps1），
/// 只证明形状可排。
/// </summary>
public sealed class LeaderDemoScaleSchedulabilityTests(ITestOutputHelper output)
{
    private const int WorkbenchBatchOrderCount = SchedulingWorkbenchLimits.MaxOrderCount;
    private static readonly DateTimeOffset HorizonStartUtc = new(2026, 7, 27, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset HorizonEndUtc = HorizonStartUtc.AddDays(7);

    private static readonly ScaleStage[] Stages =
    [
        new(10, "WC-SCALE-WELD", "OP-SCALE-WELD", 10, 1, 5),
        new(20, "WC-SCALE-ROD", "OP-SCALE-ROD", 8, 1, 4),
        new(30, "WC-SCALE-SEAL", "OP-SCALE-SEAL", 12, 1, 6),
        new(40, "WC-SCALE-TEST", "OP-SCALE-TEST", 6, 1, 3),
    ];

    private static readonly string[] FinishedSkuCodes =
    [
        "SKU-SCALE-001", "SKU-SCALE-002", "SKU-SCALE-003",
        "SKU-SCALE-004", "SKU-SCALE-005", "SKU-SCALE-006",
    ];

    [Fact]
    public async Task Scale_seed_shape_assembles_into_a_schedulable_problem_and_yields_a_plan()
    {
        var producer = new SchedulingProblemProducer(new ScaleProductEngineeringClient(), new ScaleMasterDataClient());

        var problem = await producer.AssembleAsync(
            new AssembleSchedulingProblemRequest(
                "workbench-scale-evidence",
                "org-001",
                "env-dev",
                HorizonStartUtc,
                HorizonEndUtc,
                CreateSourceOrders(WorkbenchBatchOrderCount)),
            CancellationToken.None);

        Assert.Equal(WorkbenchBatchOrderCount, problem.Orders.Count);
        Assert.Equal(24, problem.Resources.Count);
        Assert.Equal(WorkbenchBatchOrderCount * Stages.Length, problem.Orders.Sum(x => x.Operations.Count));
        // 每张工单的 4 道工序构成真实前后置链：第一道无前置，其余各有恰好一个前置。
        Assert.All(problem.Orders, order =>
        {
            var operations = order.Operations.OrderBy(x => x.OperationSequence).ToArray();
            Assert.Empty(operations[0].PredecessorOperationIds);
            Assert.All(operations.Skip(1), operation => Assert.Single(operation.PredecessorOperationIds));
            // 每道工序都有可用资源，且不带 quality 门禁（规模块路线不要求质检）。
            Assert.Equal(6, operations.Length is 0 ? 0 : operations[0].EligibleResourceIds.Count);
            Assert.All(operations, operation => Assert.Null(operation.QualityBlockReason));
        });

        var plan = new FiniteCapacityScheduler().Schedule(problem, "plan-scale-evidence", HorizonStartUtc);

        output.WriteLine($"scale-plan-orders={problem.Orders.Count}");
        output.WriteLine($"scale-plan-scheduled={plan.Metrics.ScheduledOperationCount}");
        output.WriteLine($"scale-plan-unscheduled={plan.Metrics.UnscheduledOperationCount}");
        output.WriteLine($"scale-plan-on-time-rate={plan.Metrics.OnTimeRate}");
        output.WriteLine($"scale-plan-resource-utilization={plan.Metrics.AverageResourceUtilization}");
        output.WriteLine($"scale-plan-makespan-minutes={plan.Metrics.MakespanMinutes}");

        Assert.NotEmpty(plan.Assignments);
        Assert.Equal(plan.Assignments.Count, plan.Metrics.ScheduledOperationCount);
        // 绝大多数工序必须真正排进去，否则演示看到的仍是空方案。
        Assert.True(
            plan.Metrics.ScheduledOperationCount >= (int)(problem.Orders.Sum(x => x.Operations.Count) * 0.9),
            $"Only {plan.Metrics.ScheduledOperationCount} operations were scheduled; the leader-demo scale shape must be predominantly schedulable.");
        Assert.All(plan.Assignments, assignment =>
        {
            Assert.True(assignment.StartUtc >= HorizonStartUtc);
            Assert.True(assignment.EndUtc <= HorizonEndUtc);
            Assert.StartsWith("DEV-SCALE-", assignment.ResourceId, StringComparison.Ordinal);
        });
        Assert.NotEmpty(plan.ResourceLoads);
    }

    private static SchedulingProblemSourceOrder[] CreateSourceOrders(int orderCount)
    {
        return Enumerable.Range(1, orderCount).Select(index =>
        {
            var skuCode = FinishedSkuCodes[(index - 1) % FinishedSkuCodes.Length];
            var quantity = 20m + ((index - 1) % 5) * 10m;
            var isRush = index % 29 == 0;
            return new SchedulingProblemSourceOrder(
                $"WO-SCALE-{index:D5}",
                skuCode,
                quantity,
                HorizonStartUtc.AddDays(14 + ((index - 1) % 29)).AddHours(18),
                isRush ? 100 : 1 + (index % 9),
                isRush,
                HorizonStartUtc,
                $"ROUTING-SCALE-{(index - 1) % FinishedSkuCodes.Length + 1:D3}:1",
                BusinessReference: $"WO-SCALE-{index:D5}");
        }).ToArray();
    }

    private sealed record ScaleStage(
        int Sequence,
        string WorkCenterCode,
        string OperationCode,
        int SetupMinutes,
        int RunMinutes,
        int TeardownMinutes);

    private sealed class ScaleProductEngineeringClient : ISchedulingProblemProductEngineeringClient
    {
        public Task<SchedulingProblemRoutingSnapshot> GetRoutingAsync(
            string organizationId,
            string environmentId,
            string routingVersionId,
            CancellationToken cancellationToken)
        {
            var routingCode = routingVersionId.Split(':')[0];
            var skuCode = $"SKU-SCALE-{routingCode["ROUTING-SCALE-".Length..]}";
            return Task.FromResult(new SchedulingProblemRoutingSnapshot(
                routingCode,
                "1",
                skuCode,
                Stages.Select(stage => new SchedulingProblemRoutingOperationSnapshot(
                    stage.Sequence,
                    stage.WorkCenterCode,
                    stage.OperationCode,
                    stage.OperationCode,
                    stage.SetupMinutes,
                    stage.RunMinutes,
                    stage.TeardownMinutes,
                    RequiresQualityInspection: false)).ToArray()));
        }
    }

    private sealed class ScaleMasterDataClient : ISchedulingProblemMasterDataClient
    {
        public Task<SchedulingProblemWorkCenterSnapshot> GetWorkCenterAsync(
            string organizationId,
            string environmentId,
            string workCenterCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SchedulingProblemWorkCenterSnapshot(workCenterCode, "STANDARD", 6, [workCenterCode]));

        public Task<SchedulingProblemCalendarSnapshot> GetCalendarAsync(
            string organizationId,
            string environmentId,
            string calendarCode,
            DateTimeOffset horizonStartUtc,
            DateTimeOffset horizonEndUtc,
            CancellationToken cancellationToken)
        {
            // MasterData 基础 seed 的 STANDARD 日历：周一到周五，DAY 08–20 + NIGHT 20–08。
            var windows = new List<SchedulingProblemShiftWindowSnapshot>();
            for (var day = horizonStartUtc.UtcDateTime.Date; day <= horizonEndUtc.UtcDateTime.Date; day = day.AddDays(1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    continue;
                }

                AddWindow(windows, day.AddHours(8), day.AddHours(20), "DAY", horizonStartUtc, horizonEndUtc);
                AddWindow(windows, day.AddHours(20), day.AddDays(1).AddHours(8), "NIGHT", horizonStartUtc, horizonEndUtc);
            }

            return Task.FromResult(new SchedulingProblemCalendarSnapshot(calendarCode, windows));
        }

        public Task<IReadOnlyCollection<SchedulingProblemDeviceAssetSnapshot>> ListDeviceAssetsAsync(
            string organizationId,
            string environmentId,
            string workCenterCode,
            CancellationToken cancellationToken)
        {
            var suffix = workCenterCode["WC-SCALE-".Length..];
            IReadOnlyCollection<SchedulingProblemDeviceAssetSnapshot> devices = Enumerable.Range(1, 6)
                .Select(index => new SchedulingProblemDeviceAssetSnapshot($"DEV-SCALE-{suffix}-{index:D2}", workCenterCode))
                .ToArray();
            return Task.FromResult(devices);
        }

        public Task<IReadOnlyCollection<SchedulingProblemToolingFactSnapshot>> ResolveToolingFactsAsync(
            string organizationId,
            string environmentId,
            IReadOnlyCollection<SchedulingProblemToolingTransitionSnapshot> transitions,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<SchedulingProblemToolingFactSnapshot>>([]);

        private static void AddWindow(
            ICollection<SchedulingProblemShiftWindowSnapshot> windows,
            DateTime start,
            DateTime end,
            string reasonCode,
            DateTimeOffset horizonStartUtc,
            DateTimeOffset horizonEndUtc)
        {
            var startUtc = new DateTimeOffset(start, TimeSpan.Zero);
            var endUtc = new DateTimeOffset(end, TimeSpan.Zero);
            var clippedStart = startUtc < horizonStartUtc ? horizonStartUtc : startUtc;
            var clippedEnd = endUtc > horizonEndUtc ? horizonEndUtc : endUtc;
            if (clippedEnd > clippedStart)
            {
                windows.Add(new SchedulingProblemShiftWindowSnapshot(clippedStart, clippedEnd, reasonCode));
            }
        }
    }
}
