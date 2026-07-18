using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

// Real-provider guard for RecordSchedulePlanInvalidationsCommandHandler.QueryPlans. The plan filter must
// be a SQL-translatable predicate: a custom method call (IsInvalidatableStatus) inside Where throws
// "could not be translated" on Postgres, which meant the whole AssetUnavailable -> plan invalidation ->
// MES marking chain silently failed on a real database while EF InMemory (client evaluation) kept the
// unit tests green. Gated on NERV_IIP_TEST_POSTGRES like the other *PostgresProfileTests.
public sealed class RecordSchedulePlanInvalidationsPostgresProfileTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [SchedulingPostgresFact]
    public async Task Postgres_records_invalidation_for_a_generated_plan_matched_by_resource()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;

        await using var database = await SchedulingTemporaryDatabase.CreateAsync(connectionString);
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddSchedulingPostgreSqlPersistence(database.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();

        dbContext.SchedulePlans.Add(CreatePlanWithAssignment("plan-a", resourceId: "ASSET-CNC-01"));
        await dbContext.SaveChangesAsync();

        var handler = new RecordSchedulePlanInvalidationsCommandHandler(dbContext, new FixedTimeProvider(FixedNow));
        var response = await handler.Handle(
            new RecordSchedulePlanInvalidationsCommand(
                OrganizationId: "org-001",
                EnvironmentId: "env-dev",
                SourceEventId: "evt-1",
                SourceEventType: "maintenance.AssetUnavailable",
                SourceService: "maintenance",
                OccurredAtUtc: FixedNow,
                ReasonCode: SchedulingPlanInvalidationReasons.EquipmentUnavailable,
                Scope: SchedulePlanInvalidationScope.Resource,
                ScopeValue: "ASSET-CNC-01",
                AffectedWorkOrderId: null,
                AffectedSkuCode: null),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(1, response.MatchedPlanCount);
        Assert.Equal(1, response.RecordedInvalidationCount);
        var invalidation = await dbContext.SchedulePlanInvalidations.SingleAsync(x => x.PlanId == "plan-a");
        Assert.Equal(SchedulingPlanInvalidationReasons.EquipmentUnavailable, invalidation.ReasonCode);
        Assert.Equal("ASSET-CNC-01", invalidation.AffectedResourceId);
    }

    [SchedulingPostgresFact]
    public async Task Postgres_records_generated_calendar_invalidation_without_matching_released_or_other_calendar_plans()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;

        await using var database = await SchedulingTemporaryDatabase.CreateAsync(connectionString);
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddSchedulingPostgreSqlPersistence(database.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();

        dbContext.SchedulePlans.Add(CreatePlanWithAssignment("plan-target", "ASSET-CNC-01", "problem-target"));
        var released = CreatePlanWithAssignment("plan-released", "ASSET-CNC-01", "problem-released");
        released.Release(FixedNow, 1);
        dbContext.SchedulePlans.Add(released);
        dbContext.SchedulePlans.Add(CreatePlanWithAssignment("plan-other", "ASSET-LATHE-01", "problem-other"));
        dbContext.ScheduleProblems.Add(CreateProblemSnapshot("problem-target", "CAL-A", "ASSET-CNC-01"));
        dbContext.ScheduleProblems.Add(CreateProblemSnapshot("problem-released", "CAL-A", "ASSET-CNC-01"));
        dbContext.ScheduleProblems.Add(CreateProblemSnapshot("problem-other", "CAL-B", "ASSET-LATHE-01"));
        await dbContext.SaveChangesAsync();

        var handler = new RecordSchedulePlanInvalidationsCommandHandler(dbContext, new FixedTimeProvider(FixedNow));
        var response = await handler.Handle(
            new RecordSchedulePlanInvalidationsCommand(
                "org-001",
                "env-dev",
                "evt-calendar-1",
                "masterData.WorkCalendarChanged",
                "business-masterdata",
                FixedNow,
                SchedulingPlanInvalidationReasons.WorkCalendarChanged,
                SchedulePlanInvalidationScope.GeneratedCalendar,
                "CAL-A",
                null,
                null),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(1, response.MatchedPlanCount);
        Assert.Equal(1, response.RecordedInvalidationCount);
        Assert.Equal("plan-target", (await dbContext.SchedulePlanInvalidations.SingleAsync()).PlanId);
    }

    [SchedulingPostgresFact]
    public async Task Postgres_calendar_event_handler_changes_the_generated_plan_query_state_once()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;

        await using var database = await SchedulingTemporaryDatabase.CreateAsync(connectionString);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow));
        services.AddScoped<ISchedulingIntegrationEventContextAccessor, StubSchedulingIntegrationEventContextAccessor>();
        services.AddScoped<SchedulePlanGeneratedIntegrationEventConverter>();
        services.AddScoped<ScheduleConflictDetectedIntegrationEventConverter>();
        services.AddScoped<SchedulePlanReleasedIntegrationEventConverter>();
        services.AddScoped<SchedulePlanInvalidatedIntegrationEventConverter>();
        services.AddSingleton<IIntegrationEventPublisher, NoOpIntegrationEventPublisher>();
        services.AddMediatR(configuration => configuration
            .RegisterServicesFromAssembly(typeof(Program).Assembly)
            .AddUnitOfWorkBehaviors());
        services.AddSchedulingPostgreSqlPersistence(database.ConnectionString);
        services.AddUnitOfWork<ApplicationDbContext>();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();

        dbContext.SchedulePlans.Add(CreatePlanWithAssignment("plan-target", "ASSET-CNC-01", "problem-target"));
        var released = CreatePlanWithAssignment("plan-released", "ASSET-CNC-01", "problem-released");
        released.Release(FixedNow, 1);
        dbContext.SchedulePlans.Add(released);
        dbContext.SchedulePlans.Add(CreatePlanWithAssignment("plan-other", "ASSET-LATHE-01", "problem-other"));
        dbContext.ScheduleProblems.Add(CreateProblemSnapshot("problem-target", "CAL-A", "ASSET-CNC-01"));
        dbContext.ScheduleProblems.Add(CreateProblemSnapshot("problem-released", "CAL-A", "ASSET-CNC-01"));
        dbContext.ScheduleProblems.Add(CreateProblemSnapshot("problem-other", "CAL-B", "ASSET-LATHE-01"));
        await dbContext.SaveChangesAsync();

        var handler = new WorkCalendarChangedIntegrationEventHandlerForInvalidateSchedulePlans(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>(),
            NullLogger<WorkCalendarChangedIntegrationEventHandlerForInvalidateSchedulePlans>.Instance);
        var integrationEvent = CreateWorkCalendarChangedEvent();

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var plans = await scope.ServiceProvider.GetRequiredService<ISender>().Send(
            new ListSchedulePlansQuery("org-001", "env-dev"),
            CancellationToken.None);
        Assert.True(plans.Single(x => x.PlanId == "plan-target").IsInvalidated);
        Assert.False(plans.Single(x => x.PlanId == "plan-released").IsInvalidated);
        Assert.False(plans.Single(x => x.PlanId == "plan-other").IsInvalidated);
        Assert.Single(await dbContext.SchedulePlanInvalidations.ToArrayAsync());
        Assert.Single(await dbContext.ProcessedIntegrationEvents.ToArrayAsync());
    }

    private static SchedulePlan CreatePlanWithAssignment(
        string planId,
        string resourceId,
        string problemId = "problem-001")
    {
        return SchedulePlan.FromGeneratedPlan(
            "org-001",
            "env-dev",
            SchedulePlanContractMapper.ToDomainSnapshot(new SchedulePlanContract(
                ContractVersion: 1,
                PlanId: planId,
                ProblemId: problemId,
                ProblemFingerprint: $"fingerprint-{planId}",
                AlgorithmVersion: "aps-lite-v1",
                Status: SchedulePlanStatusContract.Generated,
                GeneratedAtUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                Metrics: new SchedulePlanMetricsContract(1, 0, 60, 60, 0, 0, 1m, 0m),
                Assignments:
                [
                    new ScheduleAssignmentContract(
                        AssignmentId: $"assign-{planId}",
                        OrderId: "WO-001",
                        OperationId: "OP-001",
                        OperationSequence: 10,
                        ResourceId: resourceId,
                        WorkCenterId: "WC-CNC",
                        StartUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                        EndUtc: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                        IsLocked: false,
                        ExplanationCode: "scheduled")
                ],
                ResourceLoads: [],
                Conflicts: [],
                UnscheduledOperations: [],
                ChangeSummary: [],
                GanttItems: [])));
    }

    private static ScheduleProblemSnapshot CreateProblemSnapshot(string problemId, string calendarId, string resourceId)
    {
        var horizonStart = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var horizonEnd = horizonStart.AddHours(8);
        var problem = new SchedulingProblemContract(
            1,
            problemId,
            "org-001",
            "env-dev",
            horizonStart,
            horizonEnd,
            [],
            [new SchedulingResourceContract(resourceId, "WC-CNC", [], 1, calendarId, resourceId)],
            [new SchedulingCalendarContract(calendarId, [new SchedulingTimeWindowContract(horizonStart, horizonEnd, "regular")])],
            [],
            [],
            [],
            []);
        return new ScheduleProblemSnapshot(
            problemId,
            1,
            "org-001",
            "env-dev",
            $"fingerprint-{problemId}",
            JsonSerializer.Serialize(problem, SchedulingJson.Options),
            horizonStart,
            horizonEnd,
            FixedNow);
    }

    private static WorkCalendarChangedIntegrationEvent CreateWorkCalendarChangedEvent()
    {
        return new WorkCalendarChangedIntegrationEvent(
            "evt-masterdata-calendar-postgres-001",
            MasterDataIntegrationEventTypes.WorkCalendarChanged,
            MasterDataIntegrationEventVersions.V1,
            FixedNow,
            MasterDataIntegrationEventSources.BusinessMasterData,
            "corr-masterdata-postgres-001",
            "calendar-CAL-A",
            "org-001",
            "env-dev",
            "system:test",
            "work-calendar-changed:org-001:env-dev:CAL-A",
            new MasterDataChangedPayload("work-calendar", "CAL-A", "active", FixedNow));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubSchedulingIntegrationEventContextAccessor : ISchedulingIntegrationEventContextAccessor
    {
        public SchedulingIntegrationEventContext GetContext()
        {
            return new SchedulingIntegrationEventContext(
                "corr-scheduling-postgres-test",
                "cause-scheduling-postgres-test",
                "system:test");
        }
    }

    private sealed class NoOpIntegrationEventPublisher : IIntegrationEventPublisher
    {
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(
            TIntegrationEvent integrationEvent,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SchedulingPostgresFactAttribute : FactAttribute
{
    public SchedulingPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run Scheduling PostgreSQL profile tests.";
        }
    }
}
