using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.OrderUrgencyAggregate;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Domain.Services;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Business.Scheduling.Web.Application.Urgency;
using Nerv.IIP.Business.Scheduling.Web.Endpoints.Scheduling;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class OrderUrgencyApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Created_plan_exposes_one_cross_page_result_and_audits_priority_changes()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clock = new MutableTimeProvider(Now);
        var service = new OrderUrgencyService(db, clock);
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        problem = problem with
        {
            Orders = problem.Orders.Select((order, index) => order with
            {
                BusinessReference = index == 0 ? "SO-URG-001" : $"SO-URG-{index + 1:000}"
            }).ToArray()
        };
        var handler = new CreateSchedulePlanCommandHandler(
            db, new FiniteCapacityScheduler(), clock,
            new NoopSchedulingEquipmentAvailabilityProvider(), new NoopSchedulingMaterialReadinessProvider(),
            new SchedulingOperationOverrideOverlay(db), service);

        await handler.Handle(new CreateSchedulePlanCommand(problem), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        var byWorkOrder = Assert.Single(await service.ListAsync("org-001", "prod", ["WO-RUSH-REAR-001"], CancellationToken.None));
        var bySalesOrder = Assert.Single(await service.ListAsync("org-001", "prod", ["SO-URG-001"], CancellationToken.None));
        Assert.Equal(byWorkOrder.OrderId, bySalesOrder.OrderId);
        Assert.Equal(byWorkOrder.BusinessReference, bySalesOrder.BusinessReference);
        Assert.Equal(byWorkOrder.Level, bySalesOrder.Level);
        Assert.Equal(byWorkOrder.ExecutionRisk.ReasonCodes, bySalesOrder.ExecutionRisk.ReasonCodes);
        Assert.Equal("order-urgency-v1", byWorkOrder.ModelVersion);
        Assert.NotEmpty(byWorkOrder.ExecutionRisk.ReasonCodes);

        var detail = await service.SetBusinessPriorityAsync(
            "org-001", "prod", "SO-URG-001", BusinessPriorityLevel.P0,
            "user:test", "Customer line-stop escalation", null, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.Equal("critical", detail.Current.Level);
        var change = Assert.Single(detail.BusinessPriorityChanges);
        Assert.Equal("p0", change.NewLevel);
        Assert.Equal("user:test", change.ChangedBy);
        Assert.True(detail.History.Count >= 2);
    }

    [Fact]
    public async Task Read_is_side_effect_free_and_periodic_refresh_records_a_stale_snapshot()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clock = new MutableTimeProvider(Now);
        var service = new OrderUrgencyService(db, clock);
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var handler = new CreateSchedulePlanCommandHandler(
            db, new FiniteCapacityScheduler(), clock,
            new NoopSchedulingEquipmentAvailabilityProvider(), new NoopSchedulingMaterialReadinessProvider(),
            new SchedulingOperationOverrideOverlay(db), service);
        await handler.Handle(new CreateSchedulePlanCommand(problem), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
        var before = await db.OrderUrgencySnapshots.CountAsync();

        clock.UtcNow = Now.AddHours(3);
        await service.ListAsync("org-001", "prod", ["WO-RUSH-REAR-001"], CancellationToken.None);
        Assert.Equal(before, await db.OrderUrgencySnapshots.CountAsync());

        await service.RefreshContextAsync("org-001", "prod", CancellationToken.None);
        var refreshed = await service.ListAsync("org-001", "prod", ["WO-RUSH-REAR-001"], CancellationToken.None);

        Assert.True(Assert.Single(refreshed).ExecutionRisk.IsSourceStale);
        Assert.True(await db.OrderUrgencySnapshots.CountAsync() > before);
    }

    [Fact]
    public async Task Refresh_context_prefetches_order_facts_and_preserves_each_order_result()
    {
        await using var provider = CreateProvider();
        var clock = new MutableTimeProvider(Now);
        OrderUrgencySnapshot[] initialSnapshots;
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var service = new OrderUrgencyService(db, clock);
            var handler = new CreateSchedulePlanCommandHandler(
                db, new FiniteCapacityScheduler(), clock,
                new NoopSchedulingEquipmentAvailabilityProvider(), new NoopSchedulingMaterialReadinessProvider(),
                new SchedulingOperationOverrideOverlay(db), service);

            await handler.Handle(new CreateSchedulePlanCommand(ShockAbsorberSchedulingFixture.CreateProblem()), CancellationToken.None);
            await db.SaveChangesAsync(CancellationToken.None);
            initialSnapshots = await db.OrderUrgencySnapshots.AsNoTracking().ToArrayAsync();
        }

        clock.UtcNow = Now.AddHours(3);
        await using (var refreshScope = provider.CreateAsyncScope())
        {
            var db = refreshScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await new OrderUrgencyService(db, clock).RefreshContextAsync("org-001", "prod", CancellationToken.None);
        }

        await using var assertionScope = provider.CreateAsyncScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var serviceForRead = new OrderUrgencyService(assertionDb, clock);
        var refreshed = await serviceForRead.ListAsync("org-001", "prod", [], CancellationToken.None);
        Assert.Equal(initialSnapshots.Length * 2, await assertionDb.OrderUrgencySnapshots.CountAsync());

        foreach (var initial in initialSnapshots)
        {
            var current = OrderUrgencyContractMapper.Deserialize(initial.ResultJson);
            var observedAt = current.ExecutionRisk.FactsObservedAtUtc;
            var remaining = current.TimeCriticality.EstimatedCompletionUtc > clock.UtcNow
                ? current.TimeCriticality.EstimatedCompletionUtc - clock.UtcNow
                : TimeSpan.Zero;
            var expected = OrderUrgencyContractMapper.ToContract(OrderUrgencyCalculator.Calculate(
                new OrderUrgencyCalculationInput(
                    current.OrderId,
                    current.BusinessReference,
                    clock.UtcNow,
                    current.TimeCriticality.DueUtc,
                    remaining,
                    new BusinessPriorityFact(
                        BusinessPriorityLevel.P2,
                        "authoritative-default",
                        "No manual business-priority override.",
                        DateTimeOffset.UnixEpoch,
                        null,
                        0),
                    current.ExecutionRisk.Facts.Select(fact => new ExecutionRiskFact(
                        fact.ReasonCode,
                        Enum.Parse<ExecutionRiskCategory>(fact.Category, true),
                        fact.IsBlocking,
                        fact.SourceReference,
                        fact.ObservedAtUtc)).ToArray(),
                    current.ExecutionRisk.IsSourceMissing,
                    !observedAt.HasValue || clock.UtcNow - observedAt.Value > TimeSpan.FromHours(2),
                    observedAt,
                    current.InputFingerprint)));
            var actual = Assert.Single(refreshed, result => result.OrderId == initial.OrderId);

            Assert.Equal(expected.OrderId, actual.OrderId);
            Assert.Equal(expected.BusinessReference, actual.BusinessReference);
            Assert.Equal(expected.Level, actual.Level);
            Assert.Equal(expected.BusinessPriority.Level, actual.BusinessPriority.Level);
            Assert.Equal(expected.BusinessPriority.Source, actual.BusinessPriority.Source);
            Assert.Equal(expected.BusinessPriority.Reason, actual.BusinessPriority.Reason);
            Assert.Equal(expected.BusinessPriority.SetAtUtc, actual.BusinessPriority.SetAtUtc);
            Assert.Equal(expected.BusinessPriority.ExpiresAtUtc, actual.BusinessPriority.ExpiresAtUtc);
            Assert.Equal(expected.BusinessPriority.Revision, actual.BusinessPriority.Revision);
            Assert.Equal(expected.BusinessPriority.ReasonCodes, actual.BusinessPriority.ReasonCodes);
            Assert.Equal(expected.TimeCriticality.Level, actual.TimeCriticality.Level);
            Assert.Equal(expected.TimeCriticality.CriticalRatio, actual.TimeCriticality.CriticalRatio);
            Assert.Equal(expected.TimeCriticality.SlackHours, actual.TimeCriticality.SlackHours);
            Assert.Equal(expected.TimeCriticality.ExpectedDelayHours, actual.TimeCriticality.ExpectedDelayHours);
            Assert.Equal(expected.TimeCriticality.DueUtc, actual.TimeCriticality.DueUtc);
            Assert.Equal(expected.TimeCriticality.EstimatedCompletionUtc, actual.TimeCriticality.EstimatedCompletionUtc);
            Assert.Equal(expected.TimeCriticality.RemainingCycleHours, actual.TimeCriticality.RemainingCycleHours);
            Assert.Equal(expected.TimeCriticality.ReasonCodes, actual.TimeCriticality.ReasonCodes);
            Assert.Equal(expected.ExecutionRisk.Level, actual.ExecutionRisk.Level);
            Assert.Equal(expected.ExecutionRisk.IsSourceMissing, actual.ExecutionRisk.IsSourceMissing);
            Assert.Equal(expected.ExecutionRisk.IsSourceStale, actual.ExecutionRisk.IsSourceStale);
            Assert.Equal(expected.ExecutionRisk.FactsObservedAtUtc, actual.ExecutionRisk.FactsObservedAtUtc);
            Assert.Equal(expected.ExecutionRisk.ReasonCodes, actual.ExecutionRisk.ReasonCodes);
            Assert.Equal(expected.ExecutionRisk.Facts, actual.ExecutionRisk.Facts);
        }
    }

    [Fact]
    public async Task Refresh_context_uses_a_persisted_bucket_when_a_new_scope_runs_again()
    {
        await using var provider = CreateProvider();
        var clock = new MutableTimeProvider(Now);
        int initialCount;
        await using (var firstScope = provider.CreateAsyncScope())
        {
            var db = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var service = new OrderUrgencyService(db, clock);
            var handler = new CreateSchedulePlanCommandHandler(
                db, new FiniteCapacityScheduler(), clock,
                new NoopSchedulingEquipmentAvailabilityProvider(), new NoopSchedulingMaterialReadinessProvider(),
                new SchedulingOperationOverrideOverlay(db), service);

            await handler.Handle(new CreateSchedulePlanCommand(ShockAbsorberSchedulingFixture.CreateProblem()), CancellationToken.None);
            await db.SaveChangesAsync(CancellationToken.None);
            initialCount = await db.OrderUrgencySnapshots.CountAsync();
        }

        clock.UtcNow = Now.AddMinutes(5);
        await using (var secondScope = provider.CreateAsyncScope())
        {
            var db = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await new OrderUrgencyService(db, clock).RefreshContextAsync("org-001", "prod", CancellationToken.None);
        }

        await using var assertionScope = provider.CreateAsyncScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(initialCount, await assertionDb.OrderUrgencySnapshots.CountAsync());
    }

    [Fact]
    public void Refresh_worker_tick_interval_is_shorter_than_the_fifteen_minute_calculation_bucket()
    {
        var interval = typeof(OrderUrgencyRefreshWorker)
            .GetField("Interval", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.GetValue(null);

        Assert.IsType<TimeSpan>(interval);
        Assert.True((TimeSpan)interval! < TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task Source_invalidation_within_the_same_bucket_records_a_new_fail_closed_snapshot()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clock = new MutableTimeProvider(Now);
        var service = new OrderUrgencyService(db, clock);
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var handler = new CreateSchedulePlanCommandHandler(
            db, new FiniteCapacityScheduler(), clock,
            new NoopSchedulingEquipmentAvailabilityProvider(), new NoopSchedulingMaterialReadinessProvider(),
            new SchedulingOperationOverrideOverlay(db), service);
        await handler.Handle(new CreateSchedulePlanCommand(problem), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
        var original = Assert.Single(await service.ListAsync(
            "org-001", "prod", ["WO-RUSH-REAR-001"], CancellationToken.None));
        var before = await db.OrderUrgencySnapshots.CountAsync();

        clock.UtcNow = Now.AddMinutes(1);
        db.SchedulePlanInvalidations.Add(SchedulePlanInvalidation.Create(
            "org-001", "prod", "plan-test", "quality-hold-001", "QualityHoldPlaced", "Quality",
            "quality-hold", null, "WO-RUSH-REAR-001", null, null, clock.UtcNow, clock.UtcNow));
        await db.SaveChangesAsync(CancellationToken.None);

        await service.RefreshContextAsync("org-001", "prod", CancellationToken.None);
        var refreshed = Assert.Single(await service.ListAsync(
            "org-001", "prod", ["WO-RUSH-REAR-001"], CancellationToken.None));

        Assert.True(refreshed.ExecutionRisk.IsSourceStale);
        Assert.NotEqual(original.InputFingerprint, refreshed.InputFingerprint);
        Assert.True(await db.OrderUrgencySnapshots.CountAsync() > before);
    }

    [Fact]
    public async Task Missing_order_reference_returns_an_explainable_fail_closed_result()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = new OrderUrgencyService(db, new MutableTimeProvider(Now));

        var item = Assert.Single(await service.ListAsync(
            "org-001", "prod", ["SO-NOT-CAPTURED"], CancellationToken.None));
        var detail = await service.GetAsync(
            "org-001", "prod", "SO-NOT-CAPTURED", CancellationToken.None);

        Assert.Equal("highrisk", item.Level);
        Assert.True(item.ExecutionRisk.IsSourceMissing);
        Assert.True(item.ExecutionRisk.IsSourceStale);
        Assert.Contains("urgency.source.missing", item.ExecutionRisk.ReasonCodes);
        Assert.Contains("urgency.source.stale", item.ExecutionRisk.ReasonCodes);
        Assert.Equal(item.InputFingerprint, detail.Current.InputFingerprint);
    }

    [Fact]
    public async Task Replaying_an_existing_plan_backfills_missing_urgency_snapshots()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clock = new MutableTimeProvider(Now);
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var service = new OrderUrgencyService(db, clock);
        var first = new CreateSchedulePlanCommandHandler(
            db, new FiniteCapacityScheduler(), clock,
            new NoopSchedulingEquipmentAvailabilityProvider(), new NoopSchedulingMaterialReadinessProvider(),
            new SchedulingOperationOverrideOverlay(db), service);
        await first.Handle(new CreateSchedulePlanCommand(problem), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
        db.OrderUrgencySnapshots.RemoveRange(await db.OrderUrgencySnapshots.ToArrayAsync());
        await db.SaveChangesAsync(CancellationToken.None);
        Assert.Empty(await db.OrderUrgencySnapshots.ToArrayAsync());

        var replay = new CreateSchedulePlanCommandHandler(
            db, new FiniteCapacityScheduler(), clock,
            new NoopSchedulingEquipmentAvailabilityProvider(), new NoopSchedulingMaterialReadinessProvider(),
            new SchedulingOperationOverrideOverlay(db), service);
        await replay.Handle(new CreateSchedulePlanCommand(problem), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(problem.Orders.Count, await db.OrderUrgencySnapshots.CountAsync());
    }

    [Fact]
    public async Task Priority_conflict_behavior_translates_unit_of_work_concurrency_failures()
    {
        var behavior = new OrderUrgencyPriorityConflictBehavior();
        var request = new SetOrderUrgencyBusinessPriorityCommand(
            "org-001", "prod", "WO-001", BusinessPriorityLevel.P0,
            "user:test", "line stop", null);

        var exception = await Assert.ThrowsAsync<KnownException>(() => behavior.Handle(
            request,
            _ => throw new DbUpdateConcurrencyException("forced"),
            CancellationToken.None));

        Assert.Contains("concurrently", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("-1")]
    [InlineData("P4")]
    public void Priority_validator_rejects_numeric_and_out_of_range_levels(string level)
    {
        var result = new SetOrderUrgencyBusinessPriorityRequestValidator().Validate(
            new SetOrderUrgencyBusinessPriorityRequest(
                "WO-001", "org-001", "prod", level, "line stop"));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("Level must be P0, P1, P2, or P3.", error.ErrorMessage);
    }

    private static ServiceProvider CreateProvider()
    {
        var databaseName = $"urgency-{Guid.NewGuid():N}";
        var databaseRoot = new InMemoryDatabaseRoot();
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options
            .UseInMemoryDatabase(databaseName, databaseRoot));
        return services.BuildServiceProvider();
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
