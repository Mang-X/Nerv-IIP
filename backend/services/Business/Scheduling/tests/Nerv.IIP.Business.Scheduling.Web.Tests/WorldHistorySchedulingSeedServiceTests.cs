using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Business.Scheduling.Web.Application.Seed;
using Nerv.IIP.Contracts.Scheduling;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

/// <summary>
/// L1 背景历史（排产域侧）的常规门禁测试：形状、确定性、幂等、号段隔离、
/// 问题快照可反序列化、以及**排产方案表保持为空**。
///
/// 「最小数据集在引擎口径下能排满 ≥90%」由 #3723 承接：配平工单量 / 路线节拍 / 日历产能
/// 不在本 PR 范围内，排入率的实测读数与阈值断言都归那张票。
/// </summary>
public sealed class WorldHistorySchedulingSeedServiceTests(ITestOutputHelper output)
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 27);

    /// <summary>口径比对用的占位工作中心：时长与工作中心无关，只需装配得起来。</summary>
    private const string WorkCenterCode = "WC-PARITY";

    /// <summary>库写入类用例的规模：足够覆盖全链，又不让 InMemory provider 变慢。</summary>
    private const double SmallScale = 0.05d;

    [Fact]
    public void Full_scale_fact_stream_matches_the_world_bible_shape()
    {
        var facts = WorldHistorySchedulingSpec.BuildSchedulingFacts(AsOfDate, 1.0d);

        output.WriteLine($"scheduling-world-history-problems={facts.Problems.Count}");
        output.WriteLine($"scheduling-world-history-operations={facts.OperationCount}");
        output.WriteLine($"scheduling-world-history-urgencies={facts.Urgencies.Count}");

        // 上线日到 asOfDate 约 29–30 周，每周一个排产问题。
        Assert.InRange(facts.Problems.Count, 25, 35);

        // 单个问题 60 单 × 6–8 工序 ≈ 200–500 道工序：春节低谷周与队尾未满周天然更小，
        // 因此按均值卡形状、按上限卡单个问题。
        Assert.InRange(facts.OperationCount / facts.Problems.Count, 200, 500);
        foreach (var problem in facts.Problems)
        {
            Assert.InRange(problem.Orders.Count, 1, WorldHistorySchedulingSpec.MaxOrdersPerProblem);
            Assert.NotEmpty(problem.Problem.Resources);
            Assert.NotEmpty(problem.Problem.Calendars);
            // 种子只造输入：方案是用户现场生成的，没有已锁定工序，齐套/质量门禁是生成时的实时读数。
            Assert.Empty(problem.Problem.LockedAssignments);
            Assert.Empty(problem.Problem.MaterialReadiness);
            Assert.Empty(problem.Problem.QualityBlocks);
        }

        // 覆盖问题里出现过的每个工单，否则紧急度徽标全部走 MissingContract 兜底。
        Assert.Equal(
            facts.Problems.SelectMany(x => x.Orders).Select(x => x.WorkOrderNo).Distinct(StringComparer.Ordinal).Count(),
            facts.Urgencies.Count);
    }

    /// <summary>
    /// 种子写进问题快照的工序时长，必须与排程运行时算出来的逐道相等——这是本 PR
    /// 「工时口径真对齐」的承重点（#3594），也是 `WorldHistoryMesSpec.OperationMinutes`
    /// 文档注释里那句「同一公式、同一取值口径」的断言化。
    ///
    /// 期望值**不在夹具里手抄**：把世界观的路线常量原样喂给真正的
    /// <see cref="SchedulingProblemProducer"/>，由它自己算出时长当 oracle。
    /// 种子那边只要重新引入并行工位除数、把准备工时算进时长、或加回时长夹取，这里就会红。
    /// </summary>
    [Fact]
    public async Task Seed_operation_durations_match_the_scheduling_runtime_producer()
    {
        // 前提：PE 路线契约里的单件工时是整数（SchedulingProblemRoutingOperationSnapshot.RunMinutes）。
        // 世界观路线取非整数值时两侧口径不可能相等，先把这条前提钉住。
        Assert.All(
            WorldHistoryMesSpec.StandardOperations,
            operation => Assert.Equal(Math.Truncate(operation.RunMinutesPerUnit), operation.RunMinutesPerUnit));

        var facts = WorldHistorySchedulingSpec.BuildSchedulingFacts(AsOfDate, SmallScale);
        var seededOrders = facts.Problems
            .SelectMany(x => x.Problem.Orders)
            .GroupBy(x => x.OrderId, StringComparer.Ordinal)
            .Select(x => x.First())
            .OrderBy(x => x.OrderId, StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(seededOrders);

        var horizonStartUtc = facts.Problems[0].HorizonStartUtc;
        var runtimeProblem = await new SchedulingProblemProducer(
                new WorldHistoryRoutingClient(),
                new WorldHistoryMasterDataClient())
            .AssembleAsync(
                new AssembleSchedulingProblemRequest(
                    "world-history-duration-parity",
                    "org-001",
                    "env-dev",
                    horizonStartUtc,
                    horizonStartUtc.AddDays(WorldHistorySchedulingSpec.HorizonDays),
                    [.. seededOrders.Select(order => new SchedulingProblemSourceOrder(
                        order.OrderId,
                        order.SkuCode,
                        order.Quantity,
                        order.DueUtc,
                        order.Priority,
                        order.IsRush,
                        horizonStartUtc,
                        // 工单号即路线号：喂给 producer 的是世界观路线常量本身。
                        RoutingVersionId: order.OrderId))]),
                CancellationToken.None);

        var runtimeDurations = runtimeProblem.Orders
            .SelectMany(order => order.Operations.Select(operation =>
                (Key: (order.OrderId, operation.OperationSequence), operation.DurationMinutes)))
            .ToDictionary(x => x.Key, x => x.DurationMinutes);

        var compared = 0;
        foreach (var order in seededOrders)
        {
            foreach (var operation in order.Operations)
            {
                Assert.Equal(
                    runtimeDurations[(order.OrderId, operation.OperationSequence)],
                    operation.DurationMinutes);
                compared++;
            }
        }

        output.WriteLine($"duration-parity-operations={compared}");
        Assert.Equal(seededOrders.Sum(x => x.Operations.Count), compared);
    }

    [Fact]
    public void Fact_stream_is_deterministic_for_the_same_inputs()
    {
        var first = WorldHistorySchedulingSpec.BuildSchedulingFacts(AsOfDate, 0.1d);
        var second = WorldHistorySchedulingSpec.BuildSchedulingFacts(AsOfDate, 0.1d);

        Assert.Equal(first.Problems.Count, second.Problems.Count);
        for (var index = 0; index < first.Problems.Count; index++)
        {
            var left = first.Problems[index];
            var right = second.Problems[index];
            Assert.Equal(left.ProblemId, right.ProblemId);
            Assert.Equal(left.ProblemFingerprint, right.ProblemFingerprint);
            Assert.Equal(left.CapturedAtUtc, right.CapturedAtUtc);
            Assert.Equal(
                JsonSerializer.Serialize(left.Problem, SchedulingJson.Options),
                JsonSerializer.Serialize(right.Problem, SchedulingJson.Options));
        }

        // 紧急度事实内嵌一个风险清单（record 相等对内嵌列表走引用比较），逐字段展平后比对。
        Assert.Equal(Flatten(first.Urgencies), Flatten(second.Urgencies));
    }

    private static string[] Flatten(IReadOnlyList<WorldHistoryUrgencyFact> facts) =>
        [.. facts.Select(fact =>
            $"{fact.OrderId}|{fact.BusinessReference}|{fact.CalculatedAtUtc:O}|{fact.CalculationBucketUtc:O}|" +
            $"{fact.DueUtc:O}|{fact.RemainingCycle}|{fact.IsSourceStale}|{fact.FactsObservedAtUtc:O}|" +
            $"{fact.InputFingerprint}|{string.Join(',', fact.ExecutionRisks.Select(risk => risk.ReasonCode))}")];

    [Fact]
    public void Problem_operations_pair_with_the_shared_mes_work_order_and_operation_task_formula()
    {
        var facts = WorldHistorySchedulingSpec.BuildSchedulingFacts(AsOfDate, 0.1d);

        foreach (var order in facts.Problems.SelectMany(x => x.Problem.Orders))
        {
            Assert.StartsWith("WO-2026-", order.OrderId, StringComparison.Ordinal);
            foreach (var operation in order.Operations)
            {
                Assert.Equal(
                    WorldHistoryMesSpec.OperationTaskId(order.OrderId, operation.OperationSequence),
                    operation.OperationId);
                Assert.Equal(
                    WorldHistoryMesSpec.CapabilityCode(
                        WorldHistoryMesSpec.WorkCenterCode(order.SkuCode, operation.OperationSequence)),
                    operation.RequiredCapabilityCode);
                Assert.Contains(operation.PrimaryResourceId, operation.EligibleResourceIds);
            }
        }
    }

    /// <summary>
    /// 种子不再写排产方案（#3594）：只写问题快照（引擎的输入）与订单紧急度快照，
    /// <c>schedule_plans</c> 及其四张明细保持为空直到用户现场生成。
    /// 幂等重跑零写入，且对任意 asOfDate（含周日、春节段、月末冲量窗口）成立。
    /// </summary>
    [Theory]
    [InlineData(2026, 7, 27)]
    [InlineData(2026, 7, 26)]
    [InlineData(2026, 2, 16)]
    [InlineData(2026, 3, 31)]
    public async Task Seed_writes_engine_inputs_only_and_is_idempotent(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        var facts = WorldHistorySchedulingSpec.BuildSchedulingFacts(asOfDate, SmallScale);
        await using var db = CreateDbContext();

        var service = new WorldHistorySeedService(db);
        var first = await service.SeedAsync("org-001", "env-dev", asOfDate, SmallScale);
        var second = await service.SeedAsync("org-001", "env-dev", asOfDate, SmallScale);

        output.WriteLine($"small-scale-{asOfDate:yyyy-MM-dd}-problems={first.ScheduleProblemsWritten}");
        output.WriteLine($"small-scale-{asOfDate:yyyy-MM-dd}-urgencies={first.OrderUrgencySnapshotsWritten}");

        Assert.Equal(facts.Problems.Count, first.ScheduleProblemsWritten);
        Assert.Equal(facts.Urgencies.Count, first.OrderUrgencySnapshotsWritten);
        Assert.Equal(0, second.ScheduleProblemsWritten);
        Assert.Equal(0, second.OrderUrgencySnapshotsWritten);

        // 库终态：排产方案表为空（待用户现场生成），问题快照与紧急度快照按规格完整。
        Assert.Equal(0, await db.SchedulePlans.CountAsync());
        Assert.Equal(facts.Problems.Count, await db.ScheduleProblems.CountAsync());
        Assert.Equal(facts.Urgencies.Count, await db.OrderUrgencySnapshots.CountAsync());

        // 号段格式与保留段隔离。
        var problemIds = await db.ScheduleProblems.Select(x => x.ProblemId).ToArrayAsync();
        Assert.All(problemIds, problemId => Assert.Matches(@"^SPB-2026-\d{4}$", problemId));
        Assert.All(
            problemIds,
            reference => Assert.DoesNotContain(
                WorldHistorySchedulingSpec.ReservedInfixes,
                infix => reference.Contains(infix, StringComparison.Ordinal)));
    }

    /// <summary>
    /// 问题快照是「基于既有问题再排一版」的前提：<c>ProblemJson</c> 反序列化不回来，
    /// <c>CreateSchedulePlanRevisionCommandHandler</c> 会直接抛异常。
    /// </summary>
    [Fact]
    public async Task Problem_snapshots_deserialize_back_into_the_scheduling_contract()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var snapshots = await db.ScheduleProblems.AsNoTracking().ToArrayAsync();
        Assert.NotEmpty(snapshots);
        foreach (var snapshot in snapshots)
        {
            var problem = JsonSerializer.Deserialize<SchedulingProblemContract>(snapshot.ProblemJson, SchedulingJson.Options);
            Assert.NotNull(problem);
            Assert.Equal("org-001", problem.OrganizationId);
            Assert.Equal("env-dev", problem.EnvironmentId);
            Assert.Equal(snapshot.ProblemId, problem.ProblemId);
            Assert.NotEmpty(problem.Orders);
            Assert.NotEmpty(problem.Resources);
            Assert.NotEmpty(problem.Calendars);
            Assert.All(problem.Orders, order => Assert.NotEmpty(order.Operations));
        }
    }

    [Fact]
    public async Task Validator_fails_closed_when_history_rows_are_tampered_with()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var victim = await db.ScheduleProblems.FirstAsync();
        db.ScheduleProblems.Remove(victim);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<WorldHistoryConsistencyException>(() =>
            new WorldHistoryConsistencyValidator(db).ValidateAsync("org-001", "env-dev", AsOfDate, SmallScale));
        Assert.NotEmpty(exception.Failures);
        Assert.StartsWith("World-history scheduling seed validation failed", exception.Message, StringComparison.Ordinal);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"scheduling-world-history-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new WorldHistoryTestMediator());
    }

    /// <summary>把世界观的路线常量按 PE 路线快照的形状交给 producer（工单号即路线号）。</summary>
    private sealed class WorldHistoryRoutingClient : ISchedulingProblemProductEngineeringClient
    {
        public Task<SchedulingProblemRoutingSnapshot> GetRoutingAsync(
            string organizationId,
            string environmentId,
            string routingVersionId,
            CancellationToken cancellationToken)
        {
            var operations = WorldHistoryMesSpec.OperationSequences(routingVersionId)
                .Select(sequence =>
                {
                    var operation = WorldHistoryMesSpec.Operation(sequence);
                    return new SchedulingProblemRoutingOperationSnapshot(
                        sequence,
                        WorkCenterCode,
                        operation.OperationCode,
                        operation.OperationName,
                        operation.SetupMinutes,
                        (int)operation.RunMinutesPerUnit,
                        operation.TeardownMinutes,
                        operation.RequiresQualityInspection);
                })
                .ToArray();
            return Task.FromResult(new SchedulingProblemRoutingSnapshot(
                routingVersionId, "1", routingVersionId, operations));
        }
    }

    /// <summary>
    /// 时长只取决于路线的单件工时、收尾与数量，与工作中心 / 日历 / 设备无关，
    /// 因此这里只给 producer 装配所必需的最小主数据。
    /// </summary>
    private sealed class WorldHistoryMasterDataClient : ISchedulingProblemMasterDataClient
    {
        public Task<SchedulingProblemWorkCenterSnapshot> GetWorkCenterAsync(
            string organizationId,
            string environmentId,
            string workCenterCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SchedulingProblemWorkCenterSnapshot(
                workCenterCode, WorldHistoryMesSpec.CalendarId, 1, [workCenterCode]));

        public Task<SchedulingProblemCalendarSnapshot> GetCalendarAsync(
            string organizationId,
            string environmentId,
            string calendarCode,
            DateTimeOffset horizonStartUtc,
            DateTimeOffset horizonEndUtc,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SchedulingProblemCalendarSnapshot(
                calendarCode, [new SchedulingProblemShiftWindowSnapshot(horizonStartUtc, horizonEndUtc, "parity")]));

        public Task<IReadOnlyCollection<SchedulingProblemDeviceAssetSnapshot>> ListDeviceAssetsAsync(
            string organizationId,
            string environmentId,
            string workCenterCode,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<SchedulingProblemDeviceAssetSnapshot>>(
                [new SchedulingProblemDeviceAssetSnapshot($"DEV-{workCenterCode}", workCenterCode)]);

        public Task<IReadOnlyCollection<SchedulingProblemToolingFactSnapshot>> ResolveToolingFactsAsync(
            string organizationId,
            string environmentId,
            IReadOnlyCollection<SchedulingProblemToolingTransitionSnapshot> transitions,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<SchedulingProblemToolingFactSnapshot>>([]);
    }

    private sealed class WorldHistoryTestMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
