using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class ExecutionDeviationInvalidationTests
{
    private static readonly DateTimeOffset PlannedStart = new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RecordedAt = new(2026, 9, 22, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("0", 0)]
    [InlineData(" 15 ", 15)]
    public void Execution_deviation_tolerance_resolves_optional_non_negative_minutes(
        string? configured,
        int? expectedMinutes)
    {
        var option = SchedulingExecutionDeviationToleranceResolver.Resolve(configured);

        Assert.Equal(expectedMinutes, option.ToleranceMinutes);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("fifteen")]
    public void Execution_deviation_tolerance_rejects_invalid_values(string configured)
    {
        Assert.Throws<InvalidOperationException>(() =>
            SchedulingExecutionDeviationToleranceResolver.Resolve(configured));
    }

    [Theory]
    [InlineData(15, 0)]
    [InlineData(16, 1)]
    public async Task Exact_operation_start_deviation_invalidates_only_when_strictly_over_tolerance(
        int actualDelayMinutes,
        int expectedInvalidationCount)
    {
        await using var db = CreateDbContext();
        db.SchedulePlans.Add(CreatePlan(
            "plan-target",
            "org-001",
            "env-dev",
            [
                Assignment("WO-001", "OP-010", PlannedStart, PlannedStart.AddHours(1)),
                Assignment("WO-001", "OP-020", PlannedStart.AddHours(1), PlannedStart.AddHours(2)),
            ]));
        db.SchedulePlans.Add(CreatePlan(
            "plan-same-operation-other-work-order",
            "org-001",
            "env-dev",
            [Assignment("WO-OTHER", "OP-010", PlannedStart, PlannedStart.AddHours(1))]));
        db.SchedulePlans.Add(CreatePlan(
            "plan-other-environment",
            "org-001",
            "env-other",
            [Assignment("WO-001", "OP-010", PlannedStart, PlannedStart.AddHours(1))]));
        await db.SaveChangesAsync();
        var handler = new RecordSchedulePlanInvalidationsCommandHandler(db, new FixedTimeProvider(RecordedAt));

        var response = await handler.Handle(DeviationCommand(
            eventId: "evt-start",
            reasonCode: SchedulingPlanInvalidationReasons.OperationStartDelayed,
            milestone: SchedulePlanExecutionMilestone.Started,
            actualAtUtc: PlannedStart.AddMinutes(actualDelayMinutes)), CancellationToken.None);

        Assert.Equal(expectedInvalidationCount, response.MatchedPlanCount);
        Assert.Equal(expectedInvalidationCount, response.RecordedInvalidationCount);
        var invalidations = db.SchedulePlanInvalidations.Local.ToArray();
        Assert.Equal(expectedInvalidationCount, invalidations.Length);
        if (expectedInvalidationCount == 1)
        {
            var invalidation = Assert.Single(invalidations);
            Assert.Equal("plan-target", invalidation.PlanId);
            Assert.Equal("WO-001", invalidation.AffectedWorkOrderId);
            Assert.Equal("OP-010", invalidation.AffectedOperationId);
            var domainEvent = Assert.Single(invalidation.GetDomainEvents()
                .OfType<Nerv.IIP.Business.Scheduling.Domain.DomainEvents.SchedulePlanInvalidatedDomainEvent>());
            Assert.Equal(["OP-010"], domainEvent.Plan.AffectedOperations.Select(x => x.OperationId));
        }
    }

    [Fact]
    public async Task Same_plan_operation_and_deviation_kind_is_recorded_once_across_distinct_source_events()
    {
        await using var db = CreateDbContext();
        db.SchedulePlans.Add(CreatePlan(
            "plan-target",
            "org-001",
            "env-dev",
            [Assignment("WO-001", "OP-010", PlannedStart, PlannedStart.AddHours(1))]));
        await db.SaveChangesAsync();
        var handler = new RecordSchedulePlanInvalidationsCommandHandler(db, new FixedTimeProvider(RecordedAt));

        var first = await handler.Handle(DeviationCommand(
            "evt-start-1",
            SchedulingPlanInvalidationReasons.OperationStartDelayed,
            SchedulePlanExecutionMilestone.Started,
            PlannedStart.AddMinutes(16)), CancellationToken.None);
        await db.SaveChangesAsync();
        var replayedMeaning = await handler.Handle(DeviationCommand(
            "evt-start-2",
            SchedulingPlanInvalidationReasons.OperationStartDelayed,
            SchedulePlanExecutionMilestone.Started,
            PlannedStart.AddMinutes(20)), CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(1, first.RecordedInvalidationCount);
        Assert.Equal(0, replayedMeaning.RecordedInvalidationCount);
        Assert.Single(await db.SchedulePlanInvalidations.ToArrayAsync());
    }

    [Theory]
    [InlineData(15, 0)]
    [InlineData(16, 1)]
    public async Task Exact_operation_completion_deviation_uses_planned_end_and_strict_tolerance(
        int actualDelayMinutes,
        int expectedInvalidationCount)
    {
        await using var db = CreateDbContext();
        var plannedEnd = PlannedStart.AddHours(1);
        db.SchedulePlans.Add(CreatePlan(
            "plan-target",
            "org-001",
            "env-dev",
            [Assignment("WO-001", "OP-010", PlannedStart, plannedEnd)]));
        await db.SaveChangesAsync();
        var handler = new RecordSchedulePlanInvalidationsCommandHandler(db, new FixedTimeProvider(RecordedAt));

        var response = await handler.Handle(DeviationCommand(
            eventId: "evt-complete",
            reasonCode: SchedulingPlanInvalidationReasons.OperationCompletionDelayed,
            milestone: SchedulePlanExecutionMilestone.Completed,
            actualAtUtc: plannedEnd.AddMinutes(actualDelayMinutes)), CancellationToken.None);

        Assert.Equal(expectedInvalidationCount, response.MatchedPlanCount);
        Assert.Equal(expectedInvalidationCount, response.RecordedInvalidationCount);
    }

    [Fact]
    public async Task Started_consumer_persists_projection_inbox_and_precise_deviation_invalidation()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.SchedulePlans.Add(CreatePlan(
            "plan-target",
            "org-001",
            "env-dev",
            [
                Assignment("WO-001", "OP-010", PlannedStart, PlannedStart.AddHours(1)),
                Assignment("WO-001", "OP-020", PlannedStart.AddHours(1), PlannedStart.AddHours(2)),
            ]));
        await db.SaveChangesAsync();
        var handler = new MesOperationTaskStartedIntegrationEventHandlerForProjectExecution(
            db,
            new InMemoryIntegrationEventDeadLetterStore(),
            new NoopOperationExecutionProjectionMutationLock(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            new SchedulingExecutionDeviationToleranceOption(15));

        await handler.HandleAsync(StartedEvent("evt-start", PlannedStart.AddMinutes(16)), CancellationToken.None);

        var projection = await db.OperationExecutionProjections.SingleAsync();
        Assert.Equal(PlannedStart.AddMinutes(16), projection.ActualStartedAtUtc);
        Assert.Single(await db.ProcessedIntegrationEvents.ToArrayAsync());
        var invalidation = await db.SchedulePlanInvalidations.SingleAsync();
        Assert.Equal("plan-target", invalidation.PlanId);
        Assert.Equal(SchedulingPlanInvalidationReasons.OperationStartDelayed, invalidation.ReasonCode);
        Assert.Equal("WO-001", invalidation.AffectedWorkOrderId);
        Assert.Equal("OP-010", invalidation.AffectedOperationId);
    }

    [Fact]
    public async Task Disabled_tolerance_keeps_projection_without_creating_deviation_invalidation()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.SchedulePlans.Add(CreatePlan(
            "plan-target",
            "org-001",
            "env-dev",
            [Assignment("WO-001", "OP-010", PlannedStart, PlannedStart.AddHours(1))]));
        await db.SaveChangesAsync();
        var handler = new MesOperationTaskCompletedIntegrationEventHandlerForProjectExecution(
            db,
            new InMemoryIntegrationEventDeadLetterStore(),
            new NoopOperationExecutionProjectionMutationLock(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            SchedulingExecutionDeviationToleranceOption.Disabled);

        await handler.HandleAsync(CompletedEvent("evt-complete", PlannedStart.AddHours(2)), CancellationToken.None);

        Assert.Equal(PlannedStart.AddHours(2), (await db.OperationExecutionProjections.SingleAsync()).ActualCompletedAtUtc);
        Assert.Single(await db.ProcessedIntegrationEvents.ToArrayAsync());
        Assert.Empty(await db.SchedulePlanInvalidations.ToArrayAsync());
    }

    private static RecordSchedulePlanInvalidationsCommand DeviationCommand(
        string eventId,
        string reasonCode,
        SchedulePlanExecutionMilestone milestone,
        DateTimeOffset actualAtUtc) =>
        new(
            OrganizationId: "org-001",
            EnvironmentId: "env-dev",
            SourceEventId: eventId,
            SourceEventType: "mes.operation-task",
            SourceService: "business-mes",
            OccurredAtUtc: actualAtUtc,
            ReasonCode: reasonCode,
            Scope: SchedulePlanInvalidationScope.ExactWorkOrderOperation,
            ScopeValue: null,
            AffectedWorkOrderId: "WO-001",
            AffectedSkuCode: null,
            AffectedOperationId: "OP-010",
            ExecutionMilestone: milestone,
            ActualExecutionAtUtc: actualAtUtc,
            DeviationToleranceMinutes: 15);

    private static MesOperationTaskStartedIntegrationEvent StartedEvent(string eventId, DateTimeOffset actualAtUtc) =>
        new(
            eventId,
            MesIntegrationEventTypes.OperationTaskStarted,
            MesIntegrationEventVersions.V1,
            actualAtUtc,
            MesIntegrationEventSources.BusinessMes,
            $"corr-{eventId}",
            $"cause-{eventId}",
            "org-001",
            "env-dev",
            "operator",
            eventId,
            new OperationTaskLifecyclePayload("WO-001", "OP-010", 10, "WC-CNC", actualAtUtc));

    private static MesOperationTaskCompletedIntegrationEvent CompletedEvent(string eventId, DateTimeOffset actualAtUtc) =>
        new(
            eventId,
            MesIntegrationEventTypes.OperationTaskCompleted,
            MesIntegrationEventVersions.V1,
            actualAtUtc,
            MesIntegrationEventSources.BusinessMes,
            $"corr-{eventId}",
            $"cause-{eventId}",
            "org-001",
            "env-dev",
            "operator",
            eventId,
            new OperationTaskCompletedPayload(
                "WO-001",
                "OP-010",
                "SKU-001",
                10,
                "WC-CNC",
                100m,
                "PCS",
                false,
                actualAtUtc));

    private static ScheduleAssignmentContract Assignment(
        string workOrderId,
        string operationId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc) =>
        new(
            AssignmentId: $"assign-{workOrderId}-{operationId}",
            OrderId: workOrderId,
            OperationId: operationId,
            OperationSequence: 10,
            ResourceId: "ASSET-CNC-01",
            WorkCenterId: "WC-CNC",
            StartUtc: startUtc,
            EndUtc: endUtc,
            IsLocked: false,
            ExplanationCode: "scheduled");

    private static SchedulePlan CreatePlan(
        string planId,
        string organizationId,
        string environmentId,
        IReadOnlyCollection<ScheduleAssignmentContract> assignments) =>
        SchedulePlan.FromGeneratedPlan(
            organizationId,
            environmentId,
            SchedulePlanContractMapper.ToDomainSnapshot(new SchedulePlanContract(
                ContractVersion: 1,
                PlanId: planId,
                ProblemId: $"problem-{planId}",
                ProblemFingerprint: $"fingerprint-{planId}",
                AlgorithmVersion: "aps-lite-v1",
                Status: SchedulePlanStatusContract.Generated,
                GeneratedAtUtc: PlannedStart.AddHours(-1),
                Metrics: new SchedulePlanMetricsContract(
                    ScheduledOperationCount: assignments.Count,
                    UnscheduledOperationCount: 0,
                    AssignedMinutes: assignments.Count * 60,
                    MakespanMinutes: assignments.Count * 60,
                    TotalTardinessMinutes: 0,
                    LateOperationCount: 0,
                    OnTimeRate: 1m,
                    AverageResourceUtilization: 0m),
                Assignments: assignments,
                ResourceLoads: [],
                Conflicts: [],
                UnscheduledOperations: [],
                ChangeSummary: [],
                GanttItems: [])));

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"execution-deviation-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(RecordedAt));
        services.AddScoped<ISchedulingIntegrationEventContextAccessor, StubSchedulingIntegrationEventContextAccessor>();
        services.AddScoped<SchedulePlanGeneratedIntegrationEventConverter>();
        services.AddScoped<SchedulePlanInvalidatedIntegrationEventConverter>();
        services.AddSingleton<IIntegrationEventPublisher, NoOpIntegrationEventPublisher>();
        services.AddMediatR(configuration => configuration
            .RegisterServicesFromAssembly(typeof(Program).Assembly)
            .AddUnitOfWorkBehaviors());
        services.AddDbContext<ApplicationDbContext>(options => options
            .UseInMemoryDatabase($"execution-deviation-handler-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddUnitOfWork<ApplicationDbContext>();
        return services.BuildServiceProvider();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoopOperationExecutionProjectionMutationLock : IOperationExecutionProjectionMutationLock
    {
        public Task AcquireAsync(
            string organizationId,
            string environmentId,
            string workOrderId,
            string operationId,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubSchedulingIntegrationEventContextAccessor : ISchedulingIntegrationEventContextAccessor
    {
        public SchedulingIntegrationEventContext GetContext() =>
            new("corr-test", "cause-test", "system:test");
    }

    private sealed class NoOpIntegrationEventPublisher : IIntegrationEventPublisher
    {
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(
            TIntegrationEvent integrationEvent,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
