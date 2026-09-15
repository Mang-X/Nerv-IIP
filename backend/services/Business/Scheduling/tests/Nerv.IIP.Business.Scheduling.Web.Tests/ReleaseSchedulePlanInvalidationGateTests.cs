using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.Scheduling;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class ReleaseSchedulePlanInvalidationGateTests
{
    [Theory]
    [InlineData(typeof(ReleaseSchedulePlanCommandHandler))]
    [InlineData(typeof(RevokeSchedulePlanCommandHandler))]
    public void ScheduleReleaseHandlers_RequireScopeLock(Type handlerType)
    {
        var scopeLock = Assert.Single(handlerType.GetConstructors())
            .GetParameters()
            .Single(x => x.ParameterType == typeof(IScheduleReleaseScopeLock));

        Assert.False(scopeLock.IsOptional);
    }

    private static readonly DateTimeOffset FixedNow = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Release_is_rejected_when_the_plan_has_been_invalidated()
    {
        await using var dbContext = CreateDbContext();
        dbContext.SchedulePlans.Add(CreatePlan("plan-invalid"));
        dbContext.SchedulePlanInvalidations.Add(SchedulePlanInvalidation.Create(
            "org-001",
            "env-dev",
            "plan-invalid",
            sourceEventId: "evt-1",
            sourceEventType: "maintenance.AssetUnavailable",
            sourceService: "maintenance",
            reasonCode: SchedulingPlanInvalidationReasons.EquipmentUnavailable,
            affectedResourceId: "ASSET-CNC-01",
            affectedWorkOrderId: null,
            affectedOperationId: null,
            affectedSkuCode: null,
            occurredAtUtc: FixedNow,
            recordedAtUtc: FixedNow));
        await dbContext.SaveChangesAsync();

        var handler = new ReleaseSchedulePlanCommandHandler(
            dbContext,
            new FixedTimeProvider(FixedNow),
            new NoopScheduleReleaseScopeLock());

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseSchedulePlanCommand("plan-invalid", "org-001", "env-dev"),
            CancellationToken.None));
        Assert.Contains("排程输入变化失效", exception.Message, StringComparison.Ordinal);

        var persisted = await dbContext.SchedulePlans.SingleAsync(x => x.PlanId == "plan-invalid");
        Assert.Equal(SchedulePlanLifecycleStatus.Generated, persisted.Status);
        Assert.Null(persisted.ReleasedAtUtc);
    }

    /// <summary>
    /// #3191 裁定三：闸门按 reason 区分。qualityBlocked 是约束收紧 → 阻断；qualityReleased 是约束放松
    /// （检验合格／质量阻塞解除），它让原计划更可行，拿它阻断等于「因为好消息所以不许发布」，
    /// 且合格是高频事实，会把闸门变成噪声。两条一起断言，删掉 reason 维会同时红一条。
    /// </summary>
    [Fact]
    public async Task Release_is_rejected_when_the_plan_is_invalidated_by_quality_blocked()
    {
        await using var dbContext = CreateDbContext();
        dbContext.SchedulePlans.Add(CreatePlan("plan-quality-blocked"));
        dbContext.SchedulePlanInvalidations.Add(CreateQualityInvalidation(
            "plan-quality-blocked",
            SchedulingPlanInvalidationReasons.QualityBlocked,
            "quality.InspectionRejected"));
        await dbContext.SaveChangesAsync();

        var handler = new ReleaseSchedulePlanCommandHandler(
            dbContext,
            new FixedTimeProvider(FixedNow),
            new NoopScheduleReleaseScopeLock());

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseSchedulePlanCommand("plan-quality-blocked", "org-001", "env-dev"),
            CancellationToken.None));
        Assert.Contains("排程输入变化失效", exception.Message, StringComparison.Ordinal);

        var persisted = await dbContext.SchedulePlans.SingleAsync(x => x.PlanId == "plan-quality-blocked");
        Assert.Equal(SchedulePlanLifecycleStatus.Generated, persisted.Status);
    }

    [Fact]
    public async Task Release_is_allowed_when_the_only_invalidation_is_quality_released()
    {
        await using var dbContext = CreateDbContext();
        dbContext.SchedulePlans.Add(CreatePlan("plan-quality-released"));
        dbContext.SchedulePlanInvalidations.Add(CreateQualityInvalidation(
            "plan-quality-released",
            SchedulingPlanInvalidationReasons.QualityReleased,
            "quality.InspectionPassed"));
        await dbContext.SaveChangesAsync();

        var handler = new ReleaseSchedulePlanCommandHandler(
            dbContext,
            new FixedTimeProvider(FixedNow),
            new NoopScheduleReleaseScopeLock());

        var response = await handler.Handle(
            new ReleaseSchedulePlanCommand("plan-quality-released", "org-001", "env-dev"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(SchedulePlanStatusContract.Released, response.Status);
        // 放行不等于抹掉留痕：那条 qualityReleased 记录仍然在库里，只是不参与阻断判定。
        Assert.Single(await dbContext.SchedulePlanInvalidations
            .Where(x => x.PlanId == "plan-quality-released")
            .ToArrayAsync());
    }

    /// <summary>
    /// 边界：qualityReleased 只免掉自己那一条，同一张计划上并存的其它 reason 照旧阻断——
    /// 否则一条合格结论就能把设备停机造成的失效一并洗掉。
    /// </summary>
    [Fact]
    public async Task Quality_released_does_not_mask_another_blocking_reason_on_the_same_plan()
    {
        await using var dbContext = CreateDbContext();
        dbContext.SchedulePlans.Add(CreatePlan("plan-mixed"));
        dbContext.SchedulePlanInvalidations.Add(CreateQualityInvalidation(
            "plan-mixed",
            SchedulingPlanInvalidationReasons.QualityReleased,
            "quality.InspectionPassed"));
        dbContext.SchedulePlanInvalidations.Add(SchedulePlanInvalidation.Create(
            "org-001",
            "env-dev",
            "plan-mixed",
            sourceEventId: "evt-mixed-asset",
            sourceEventType: "maintenance.AssetUnavailable",
            sourceService: "maintenance",
            reasonCode: SchedulingPlanInvalidationReasons.EquipmentUnavailable,
            affectedResourceId: "ASSET-CNC-01",
            affectedWorkOrderId: null,
            affectedOperationId: null,
            affectedSkuCode: null,
            occurredAtUtc: FixedNow,
            recordedAtUtc: FixedNow));
        await dbContext.SaveChangesAsync();

        var handler = new ReleaseSchedulePlanCommandHandler(
            dbContext,
            new FixedTimeProvider(FixedNow),
            new NoopScheduleReleaseScopeLock());

        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseSchedulePlanCommand("plan-mixed", "org-001", "env-dev"),
            CancellationToken.None));
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

    [Fact]
    public async Task Release_succeeds_for_a_plan_without_invalidations()
    {
        await using var dbContext = CreateDbContext();
        dbContext.SchedulePlans.Add(CreatePlan("plan-clean"));
        await dbContext.SaveChangesAsync();

        var handler = new ReleaseSchedulePlanCommandHandler(
            dbContext,
            new FixedTimeProvider(FixedNow),
            new NoopScheduleReleaseScopeLock());

        var response = await handler.Handle(
            new ReleaseSchedulePlanCommand("plan-clean", "org-001", "env-dev"),
            CancellationToken.None);

        Assert.Equal(SchedulePlanStatusContract.Released, response.Status);
        Assert.Equal(FixedNow, response.ReleasedAtUtc);
    }

    [Fact]
    public async Task Second_release_supersedes_current_plan_before_releasing_successor()
    {
        await using var dbContext = CreateDbContext();
        dbContext.SchedulePlans.AddRange(CreatePlan("plan-v1"), CreatePlan("plan-v2"));
        await dbContext.SaveChangesAsync();
        var handler = new ReleaseSchedulePlanCommandHandler(
            dbContext,
            new FixedTimeProvider(FixedNow),
            new NoopScheduleReleaseScopeLock());

        var first = await handler.Handle(
            new ReleaseSchedulePlanCommand("plan-v1", "org-001", "env-dev"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        var second = await handler.Handle(
            new ReleaseSchedulePlanCommand("plan-v2", "org-001", "env-dev"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var plans = await dbContext.SchedulePlans.OrderBy(x => x.PlanId).ToArrayAsync();
        Assert.Equal(1, first.ReleaseRevision);
        Assert.Equal(2, second.ReleaseRevision);
        Assert.Equal(SchedulePlanLifecycleStatus.Superseded, plans[0].Status);
        Assert.Equal("plan-v2", plans[0].SupersededByPlanId);
        Assert.Equal(SchedulePlanLifecycleStatus.Released, plans[1].Status);
    }

    [Fact]
    public async Task Explicit_revoke_is_idempotent_without_a_successor()
    {
        await using var dbContext = CreateDbContext();
        var plan = CreatePlan("plan-revoke");
        plan.Release(FixedNow, 3);
        plan.ClearDomainEvents();
        dbContext.SchedulePlans.Add(plan);
        await dbContext.SaveChangesAsync();
        var handler = new RevokeSchedulePlanCommandHandler(
            dbContext,
            new FixedTimeProvider(FixedNow.AddHours(1)),
            new NoopScheduleReleaseScopeLock());

        var first = await handler.Handle(
            new RevokeSchedulePlanCommand("plan-revoke", "org-001", "env-dev"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        var second = await handler.Handle(
            new RevokeSchedulePlanCommand("plan-revoke", "org-001", "env-dev"),
            CancellationToken.None);

        Assert.Equal(SchedulePlanStatusContract.Revoked, first.Status);
        Assert.Equal(first.RevokedAtUtc, second.RevokedAtUtc);
        Assert.Equal(3, second.ReleaseRevision);
        Assert.Null(second.SupersededByPlanId);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"scheduling-release-gate-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static SchedulePlan CreatePlan(string planId)
    {
        return SchedulePlan.FromGeneratedPlan(
            "org-001",
            "env-dev",
            SchedulePlanContractMapper.ToDomainSnapshot(new SchedulePlanContract(
                ContractVersion: 1,
                PlanId: planId,
                ProblemId: "problem-001",
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
                        ResourceId: "ASSET-CNC-01",
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoopScheduleReleaseScopeLock : IScheduleReleaseScopeLock
    {
        public Task<IAsyncDisposable> AcquireAsync(
            string organizationId,
            string environmentId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IAsyncDisposable>(NoopAsyncDisposable.Instance);
        }
    }

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public static NoopAsyncDisposable Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoopMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
    }
}
