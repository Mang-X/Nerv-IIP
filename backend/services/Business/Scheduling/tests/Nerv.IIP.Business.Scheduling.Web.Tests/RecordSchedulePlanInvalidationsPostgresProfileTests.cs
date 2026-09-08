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
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

// Real-provider guard for RecordSchedulePlanInvalidationsCommandHandler.QueryPlans. The plan filter must
// be a SQL-translatable predicate: a custom method call (IsInvalidatableStatus) inside Where throws
// "could not be translated" on Postgres, which meant the whole AssetUnavailable -> plan invalidation ->
// MES marking chain silently failed on a real database while EF InMemory (client evaluation) kept the
// unit tests green. Gated on NERV_IIP_TEST_POSTGRES like the other *PostgresProfileTests.
[Collection(SchedulingPostgresLaneDatabase.CollectionName)]
public sealed class RecordSchedulePlanInvalidationsPostgresProfileTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [SchedulingPostgresFact]
    public async Task Postgres_records_invalidation_for_a_generated_plan_matched_by_resource()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddSchedulingPostgreSqlPersistence(SchedulingPostgresLaneDatabase.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SchedulingPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
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
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddSchedulingPostgreSqlPersistence(SchedulingPostgresLaneDatabase.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SchedulingPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
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
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
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
        services.AddSchedulingPostgreSqlPersistence(SchedulingPostgresLaneDatabase.ConnectionString);
        services.AddUnitOfWork<ApplicationDbContext>();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SchedulingPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
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

    /// <summary>
    /// #3191 真 provider 守线：检验结论走的 WorkOrderOrOperation 作用域谓词是
    /// <c>Assignments.Any(a =&gt; a.WorkOrderId == v || a.OperationId == v)</c> —— 一条**关联子查询**。
    /// 此前这条通路的唯一验证载体是 EF InMemory（客户端求值），真 Postgres 上的翻译从未被检验，
    /// 而本仓已因同一形状栽过：QueryPlans 里的自定义方法调用在 InMemory 绿、在 Postgres 抛
    /// 「could not be translated」。这里把 || 的**两个分支各走一次**（按工单命中 / 按工序命中）。
    /// </summary>
    [SchedulingPostgresFact]
    public async Task Postgres_translates_the_work_order_or_operation_scope_for_quality_inspection_results()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = BuildEventHandlerProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SchedulingPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();

        dbContext.SchedulePlans.Add(CreatePlanWithAssignment("plan-target", "ASSET-CNC-01", "problem-target"));
        dbContext.SchedulePlans.Add(CreatePlanWithAssignmentIdentity("plan-other", "WO-999", "OP-999", "problem-other"));
        await dbContext.SaveChangesAsync();

        var handler = new QualityInspectionResultIntegrationEventHandlerForInvalidateSchedulePlans(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>(),
            NullLogger<QualityInspectionResultIntegrationEventHandlerForInvalidateSchedulePlans>.Instance);

        // ① 首件复合来源单据 + 结构化工单身份 → 走 assignment.WorkOrderId 那一支。
        await handler.HandleAsync(
            CreateInspectionEvent(
                "evt-quality-first-article",
                QualityIntegrationEventTypes.InspectionRejected,
                sourceType: QualityInspectionSourceTypes.FirstArticle,
                sourceDocumentId: "WO-001:OP-001",
                workOrderId: "WO-001",
                operationTaskId: "OP-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var byWorkOrder = await dbContext.SchedulePlanInvalidations.AsNoTracking().ToArrayAsync();
        var firstArticle = Assert.Single(byWorkOrder);
        Assert.Equal("plan-target", firstArticle.PlanId);
        Assert.Equal("WO-001", firstArticle.AffectedWorkOrderId);
        Assert.Equal(SchedulingPlanInvalidationReasons.QualityBlocked, firstArticle.ReasonCode);

        // ② 周期检复合行号 + 结构化工序身份（无工单号）→ 走 assignment.OperationId 那一支。
        await handler.HandleAsync(
            CreateInspectionEvent(
                "evt-quality-periodic",
                QualityIntegrationEventTypes.InspectionPassed,
                sourceType: QualityInspectionSourceTypes.Operation,
                sourceDocumentId: $"OP-001:periodic-time:{Guid.Empty:D}:1",
                workOrderId: null,
                operationTaskId: "OP-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var periodic = Assert.Single(await dbContext.SchedulePlanInvalidations
            .AsNoTracking()
            .Where(x => x.SourceEventId == "evt-quality-periodic")
            .ToArrayAsync());
        Assert.Equal("plan-target", periodic.PlanId);
        Assert.Equal("OP-001", periodic.AffectedOperationId);
        Assert.Null(periodic.AffectedWorkOrderId);
        Assert.Equal(SchedulingPlanInvalidationReasons.QualityReleased, periodic.ReasonCode);

        // 另一张计划的工单／工序都不匹配，两次都不该被牵连。
        Assert.Empty(await dbContext.SchedulePlanInvalidations
            .AsNoTracking()
            .Where(x => x.PlanId == "plan-other")
            .ToArrayAsync());
    }

    /// <summary>
    /// #3191 真 provider 守线：发布闸门的 <c>ReasonCode != qualityReleased</c> 是一条 SQL 谓词，
    /// 同样只在 InMemory 上验过。裁定的两条行为（qualityBlocked 阻断 / qualityReleased 放行）
    /// 在真库上各走一次。
    /// </summary>
    [SchedulingPostgresFact]
    public async Task Postgres_release_gate_blocks_quality_blocked_and_allows_quality_released()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddSchedulingPostgreSqlPersistence(SchedulingPostgresLaneDatabase.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SchedulingPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();

        dbContext.SchedulePlans.Add(CreatePlanWithAssignment("plan-blocked", "ASSET-CNC-01", "problem-blocked"));
        dbContext.SchedulePlans.Add(CreatePlanWithAssignment("plan-released", "ASSET-CNC-01", "problem-released"));
        dbContext.SchedulePlanInvalidations.Add(CreateQualityInvalidation(
            "plan-blocked", SchedulingPlanInvalidationReasons.QualityBlocked, "quality.InspectionRejected"));
        dbContext.SchedulePlanInvalidations.Add(CreateQualityInvalidation(
            "plan-released", SchedulingPlanInvalidationReasons.QualityReleased, "quality.InspectionPassed"));
        await dbContext.SaveChangesAsync();

        var handler = new ReleaseSchedulePlanCommandHandler(
            dbContext,
            new FixedTimeProvider(FixedNow),
            new NoopScheduleReleaseScopeLock());

        var blocked = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseSchedulePlanCommand("plan-blocked", "org-001", "env-dev"),
            CancellationToken.None));
        Assert.Contains("排程输入变化失效", blocked.Message, StringComparison.Ordinal);

        var released = await handler.Handle(
            new ReleaseSchedulePlanCommand("plan-released", "org-001", "env-dev"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(SchedulePlanStatusContract.Released, released.Status);
        dbContext.ChangeTracker.Clear();
        var persisted = await dbContext.SchedulePlans.AsNoTracking().ToArrayAsync();
        Assert.Equal(SchedulePlanLifecycleStatus.Generated, persisted.Single(x => x.PlanId == "plan-blocked").Status);
        Assert.Equal(SchedulePlanLifecycleStatus.Released, persisted.Single(x => x.PlanId == "plan-released").Status);
        // 放行不等于抹掉留痕。
        Assert.Equal(2, await dbContext.SchedulePlanInvalidations.CountAsync());
    }

    private static ServiceProvider BuildEventHandlerProvider()
    {
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
        services.AddSchedulingPostgreSqlPersistence(SchedulingPostgresLaneDatabase.ConnectionString);
        services.AddUnitOfWork<ApplicationDbContext>();
        return services.BuildServiceProvider();
    }

    private static SchedulePlanInvalidation CreateQualityInvalidation(string planId, string reasonCode, string sourceEventType)
    {
        return SchedulePlanInvalidation.Create(
            "org-001",
            "env-dev",
            planId,
            sourceEventId: $"evt-{planId}",
            sourceEventType: sourceEventType,
            sourceService: "business-quality",
            reasonCode: reasonCode,
            affectedResourceId: null,
            affectedWorkOrderId: "WO-001",
            affectedOperationId: null,
            affectedSkuCode: "SKU-001",
            occurredAtUtc: FixedNow,
            recordedAtUtc: FixedNow);
    }

    private static InspectionResultIntegrationEvent CreateInspectionEvent(
        string eventId,
        string eventType,
        string sourceType,
        string sourceDocumentId,
        string? workOrderId,
        string? operationTaskId)
    {
        var occurredAtUtc = new DateTimeOffset(2026, 6, 1, 9, 10, 0, TimeSpan.Zero);
        return new InspectionResultIntegrationEvent(
            eventId,
            eventType,
            QualityIntegrationEventVersions.V1,
            occurredAtUtc,
            QualityIntegrationEventSources.BusinessQuality,
            $"corr-{eventId}",
            $"cause-{eventId}",
            "org-001",
            "env-dev",
            "system:quality",
            $"quality:inspection:{eventId}",
            new InspectionResultPayload(
                "INS-001",
                null,
                sourceType,
                QualityInspectionSourceServices.Mes,
                sourceDocumentId,
                "SKU-001",
                1,
                eventType == QualityIntegrationEventTypes.InspectionRejected ? "Rejected" : "Passed",
                null,
                [],
                occurredAtUtc,
                WorkOrderId: workOrderId,
                OperationTaskId: operationTaskId));
    }

    private static SchedulePlan CreatePlanWithAssignmentIdentity(
        string planId,
        string workOrderId,
        string operationId,
        string problemId)
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
                        OrderId: workOrderId,
                        OperationId: operationId,
                        OperationSequence: 10,
                        ResourceId: "ASSET-LATHE-01",
                        WorkCenterId: "WC-LATHE",
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

    private sealed class NoopScheduleReleaseScopeLock : IScheduleReleaseScopeLock
    {
        public Task<IAsyncDisposable> AcquireAsync(
            string organizationId,
            string environmentId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IAsyncDisposable>(NoopAsyncDisposable.Instance);
    }

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public static NoopAsyncDisposable Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
