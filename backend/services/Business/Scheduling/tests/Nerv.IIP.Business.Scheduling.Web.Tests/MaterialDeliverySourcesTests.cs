using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

// #4094：显式方案、身份隔离与真实剩余执行的独立业务样本。
public sealed class MaterialDeliverySourcesTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Due = Start.AddDays(1);

    [Fact]
    public async Task Selected_plan_and_exact_order_identity_determine_assignment_not_sku_or_latest_plan()
    {
        await using var db = CreateDb();
        await Seed(db, "selected", Start);
        await Seed(db, "latest", Start.AddHours(3));
        var handler = Handler(db);
        var result = await handler.Handle(Query("selected", "wo-1", "s-1"), default);
        Assert.Equal("selected", result.PlanId);
        Assert.Equal(Start, Assert.Single(result.Items).ScheduledStartUtc);
        var other = await handler.Handle(Query("selected", "wo-2", "s-2"), default);
        Assert.Equal(Start.AddHours(1), Assert.Single(other.Items).ScheduledStartUtc);
        var later = await handler.Handle(Query("latest", "wo-1", "s-1"), default);
        Assert.Equal(Start.AddHours(3), Assert.Single(later.Items).ScheduledStartUtc);
        var operation = Assert.Single(result.Items.Single().Operations);
        Assert.Equal(Start.AddHours(-1), operation.EarliestStartUtc);
        Assert.Equal(Start, operation.AssignmentStartUtc);
        Assert.Equal("route-v1", operation.RoutingVersionId);
    }

    [Fact]
    public async Task Partial_completion_uses_net_good_quantity_skips_completed_operation_and_started_setup()
    {
        await using var db = CreateDb();
        await Seed(db, "selected", Start, completedPredecessor: true);
        var result = await Handler(db, partial: true).Handle(Query("selected", "wo-1", "s-1"), default);
        var order = Assert.Single(result.Items);
        var remaining = Assert.Single(order.Operations, x => x.OperationId == "op-wo-1");
        // 10 件，净完工 4 件（5 - 1 冲销）；2 分钟/件，效率 0.5，收尾 3 分钟，已开始不重计 setup。
        Assert.Equal(6m, remaining.RemainingQuantity);
        Assert.Equal(27d, remaining.RemainingMinutes);
        Assert.Equal(0d, order.Operations.Single(x => x.OperationId == "done").RemainingMinutes);
        Assert.Equal(Due.AddMinutes(-27), order.LatestStartUtc);
        Assert.Equal("sales-early", order.TightestDueSourceReference);
        Assert.Equal(new[] { "op-wo-1" }, order.DueBounds.First().CriticalPathOperationIds);
        Assert.Equal(2, order.DueBounds.Count);
    }

    [Theory]
    [InlineData(false, "unscheduled")]
    [InlineData(true, "problem-snapshot-missing")]
    public async Task Unscheduled_or_missing_snapshot_never_invents_assignment_start(bool missingSnapshot, string status)
    {
        await using var db = CreateDb();
        await Seed(db, "selected", Start, missingSnapshot, unscheduled: true);
        var order = Assert.Single((await Handler(db).Handle(Query("selected", "wo-1", "s-1"), default)).Items);
        Assert.Equal(status, order.Status);
        Assert.Null(order.ScheduledStartUtc);
        if (missingSnapshot) Assert.Null(order.LatestStartUtc);
        else Assert.Null(Assert.Single(order.Operations).AssignmentStartUtc);
    }

    [Fact]
    public async Task Unlinked_order_returns_explicit_missing_source_and_identity_mismatch_is_rejected()
    {
        await using var db = CreateDb();
        await Seed(db, "selected", Start);
        var handler = Handler(db);
        var order = Assert.Single((await handler.Handle(Query("selected", null, "s-1"), default)).Items);
        Assert.Equal("work-order-not-linked", order.Status);
        Assert.Null(order.LatestStartUtc);
        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(Query("selected", "wo-1", "s-2"), default));
        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(Query("selected", "wo-1", "s-1") with { OrganizationId = "other" }, default));
        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(Query("selected", "wo-1", "s-1") with { EnvironmentId = "other" }, default));
    }

    private static GetMaterialDeliverySourcesQuery Query(string plan, string? workOrderId, string suggestion) =>
        new(plan, "org-001", "prod", [new(suggestion, workOrderId, [new("sales-early", Due), new("sales-late", Due.AddHours(2))])]);

    private static GetMaterialDeliverySourcesQueryHandler Handler(ApplicationDbContext db, bool partial = false) =>
        new(db, new DetailSender(db), new MesSource(partial), new Engineering(), new MasterData());

    private static async Task Seed(ApplicationDbContext db, string planId, DateTimeOffset start,
        bool missingSnapshot = false, bool unscheduled = false, bool completedPredecessor = false)
    {
        var template = ShockAbsorberSchedulingFixture.CreateProblem();
        var op = template.Orders.First().Operations.First() with
        {
            OperationId = "op-wo-1", OperationSequence = 2, EarliestStartUtc = Start.AddHours(-1),
            DurationMinutes = 100, SetupMinutes = 10, PredecessorOperationIds = completedPredecessor ? ["done"] : []
        };
        var problem = template with { ProblemId = "problem-" + planId, Orders =
        [
            template.Orders.First() with { OrderId = "wo-1", Operations = completedPredecessor ?
                [op with { OperationId = "done", OperationSequence = 1, PredecessorOperationIds = [] }, op] : [op] },
            template.Orders.First() with { OrderId = "wo-2", Operations = [op with { OperationId = "op-wo-2", PredecessorOperationIds = [] }] }
        ] };
        var generated = new FiniteCapacityScheduler().Schedule(problem, planId, Start.AddHours(-2));
        generated = generated with { Assignments = unscheduled ? [] : generated.Assignments.Select(x => x with
            { StartUtc = x.OrderId == "wo-1" ? start : start.AddHours(1), EndUtc = start.AddHours(2) }).ToArray() };
        db.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan("org-001", "prod", SchedulePlanContractMapper.ToDomainSnapshot(generated)));
        if (!missingSnapshot) db.ScheduleProblems.Add(new ScheduleProblemSnapshot(problem.ProblemId, 1,
            "org-001", "prod", "fingerprint", JsonSerializer.Serialize(problem, SchedulingJson.Options),
            problem.HorizonStartUtc, problem.HorizonEndUtc, Start));
        await db.SaveChangesAsync();
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase("material-delivery-" + Guid.NewGuid()).Options, new DetailSender());

    private sealed class MesSource(bool partial) : IMaterialDeliveryMesSourceProvider
    {
        public Task<MaterialDeliveryExecutionOrder?> GetAsync(string org, string env, string id, CancellationToken ct) =>
            Task.FromResult<MaterialDeliveryExecutionOrder?>(new(id, id == "wo-1" ? "s-1" : "s-2", "pv", 10,
                partial ? [new("done", 1, "completed", Start, Start, 10), new("op-" + id, 2, "started", Start.AddHours(-1), Start, 4)] :
                [new("op-" + id, 2, "queued", Start.AddHours(-4), null, 0)]));
    }

    private sealed class Engineering : ISchedulingProblemProductEngineeringClient
    {
        public Task<SchedulingProblemProductionVersionSnapshot> GetProductionVersionRoutingAsync(string org, string env, string id, CancellationToken ct) =>
            Task.FromResult(new SchedulingProblemProductionVersionSnapshot("pv", "same-sku", "route-v1"));
        public Task<SchedulingProblemRoutingSnapshot> GetRoutingAsync(string org, string env, string id, CancellationToken ct) =>
            Task.FromResult(new SchedulingProblemRoutingSnapshot("route", "v1", "same-sku",
                [new(1, "WC", "done", "done", 10, 2, 3), new(2, "WC", "run", "run", 10, 2, 3)]));
    }

    private sealed class MasterData : ISchedulingProblemMasterDataClient
    {
        public Task<SchedulingProblemWorkCenterSnapshot> GetWorkCenterAsync(string org, string env, string code, CancellationToken ct) =>
            Task.FromResult(new SchedulingProblemWorkCenterSnapshot(code, "CAL", 1, [], EfficiencyRate: 0.5m));
        public Task<SchedulingProblemCalendarSnapshot> GetCalendarAsync(string org, string env, string code, DateTimeOffset start, DateTimeOffset end, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<SchedulingProblemDeviceAssetSnapshot>> ListDeviceAssetsAsync(string org, string env, string code, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<SchedulingProblemToolingFactSnapshot>> ResolveToolingFactsAsync(string org, string env, IReadOnlyCollection<SchedulingProblemToolingTransitionSnapshot> transitions, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class DetailSender(ApplicationDbContext? db = null) : IMediator
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) =>
            (TResponse)(object)await new GetSchedulePlanDetailQueryHandler(db!, NullLogger<GetSchedulePlanDetailQueryHandler>.Instance)
                .Handle(Assert.IsType<GetSchedulePlanDetailQuery>(request), ct);
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Publish(object notification, CancellationToken ct = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification => Task.CompletedTask;
    }
}
