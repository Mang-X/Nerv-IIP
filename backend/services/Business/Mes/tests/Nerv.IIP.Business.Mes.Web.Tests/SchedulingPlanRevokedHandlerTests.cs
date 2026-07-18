using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class SchedulingPlanRevokedHandlerTests
{
    [Fact]
    public async Task Malformed_revocation_is_dead_lettered_without_throwing_or_recording_inbox()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-schedule-revoke-invalid-{Guid.CreateVersion7():N}")
            .Options;
        await using var dbContext = new ApplicationDbContext(options, new NoopMediator());
        var task = OperationTask.Queue("org-001", "env-dev", "WO-001", "OP-10", 10, "WC-1", [], At(0), TimeSpan.FromHours(1));
        task.ApplyScheduleAssignment("WC-1", "DEV-1", At(1), At(2), At(0), schedulePlanId: "plan-1", scheduleReleaseRevision: 1);
        dbContext.OperationTasks.Add(task);
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new SchedulePlanRevokedIntegrationEventHandlerForWithdrawDispatch(dbContext, deadLetters);

        await handler.HandleAsync(CreateRevoked("revoke-invalid", "plan-1", 0, "explicit", null), CancellationToken.None);

        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            SchedulePlanRevokedIntegrationEventHandlerForWithdrawDispatch.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("mes.schedulePlanRevoked.invalidPayload", deadLetter.FailureCode);
        Assert.Equal("plan-1", task.SchedulePlanId);
    }

    [Fact]
    public async Task Consecutive_releases_late_old_revoke_and_explicit_revoke_are_version_safe_and_replay_idempotent()
    {
        var root = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-schedule-revoke-{Guid.CreateVersion7():N}", root)
            .Options;
        await using var dbContext = new ApplicationDbContext(options, new NoopMediator());
        dbContext.WorkOrders.Add(WorkOrder.Create(
            "org-001", "env-dev", "WO-001", "FG-001", "PV-001", 1m, 1,
            At(8), "PCS", null));
        await dbContext.SaveChangesAsync();

        var releasedHandler = new SchedulePlanReleasedIntegrationEventHandlerForDispatch(
            dbContext, new InMemoryIntegrationEventDeadLetterStore());
        var revokedHandler = new SchedulePlanRevokedIntegrationEventHandlerForWithdrawDispatch(
            dbContext, new InMemoryIntegrationEventDeadLetterStore());

        await releasedHandler.HandleAsync(CreateReleased("release-1", "plan-1", 1, "DEV-1", At(1)), CancellationToken.None);
        await dbContext.SaveChangesAsync();
        await releasedHandler.HandleAsync(CreateReleased("release-2", "plan-2", 2, "DEV-2", At(3)), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        await revokedHandler.HandleAsync(CreateRevoked("revoke-1", "plan-1", 1, "superseded", "plan-2"), CancellationToken.None);

        var afterLateRevoke = await dbContext.OperationTasks.SingleAsync();
        Assert.Equal("plan-2", afterLateRevoke.SchedulePlanId);
        Assert.Equal(2, afterLateRevoke.ScheduleReleaseRevision);
        Assert.Equal("DEV-2", afterLateRevoke.DeviceAssetId);

        var explicitRevoke = CreateRevoked("revoke-2", "plan-2", 2, "explicit", null);
        await revokedHandler.HandleAsync(explicitRevoke, CancellationToken.None);
        await revokedHandler.HandleAsync(explicitRevoke, CancellationToken.None);

        var finalTask = await dbContext.OperationTasks.SingleAsync();
        Assert.Null(finalTask.SchedulePlanId);
        Assert.Null(finalTask.ScheduleReleaseRevision);
        Assert.Null(finalTask.ScheduledAtUtc);
        Assert.Equal(OperationTaskLifecycleStatus.ScheduleInvalidated, finalTask.Status);
        Assert.Equal(4, await dbContext.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Revoke_before_release_persists_scope_watermark_and_prevents_resurrection()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-schedule-revoke-first-{Guid.CreateVersion7():N}")
            .Options;
        await using var dbContext = new ApplicationDbContext(options, new NoopMediator());
        dbContext.WorkOrders.Add(WorkOrder.Create(
            "org-001", "env-dev", "WO-001", "FG-001", "PV-001", 1m, 1, At(8), "PCS", null));
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();

        await new SchedulePlanRevokedIntegrationEventHandlerForWithdrawDispatch(dbContext, deadLetters)
            .HandleAsync(CreateRevoked("revoke-first", "plan-1", 1, "explicit", null), CancellationToken.None);
        await new SchedulePlanReleasedIntegrationEventHandlerForDispatch(dbContext, deadLetters)
            .HandleAsync(CreateReleased("release-late", "plan-1", 1, "DEV-1", At(1)), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Empty(dbContext.OperationTasks);
        Assert.Equal(1, (await dbContext.ScheduleReleaseWatermarks.SingleAsync()).RevokedReleaseRevision);
        Assert.Contains(await deadLetters.ListAsync(
            SchedulePlanReleasedIntegrationEventHandlerForDispatch.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None), x => x.FailureCode == "mes.schedulePlanReleased.releaseAlreadyRevoked");
    }

    private static SchedulePlanReleasedIntegrationEvent CreateReleased(
        string eventId, string planId, long revision, string resourceId, DateTimeOffset startUtc) => new(
        eventId,
        SchedulingIntegrationEventTypes.SchedulePlanReleased,
        SchedulingIntegrationEventVersions.V1,
        startUtc.AddHours(-1),
        SchedulingIntegrationEventSources.BusinessScheduling,
        "corr-001",
        eventId,
        "org-001",
        "env-dev",
        "scheduling",
        $"release:{planId}:{revision}",
        new SchedulePlanLifecyclePayload(
            planId, "problem-001", 1, "aps-lite-v1", $"fingerprint-{revision}", "released",
            [new SchedulePlanAffectedOperationPayload("WO-001", "OP-10", 10, resourceId, "WC-1", startUtc, startUtc.AddHours(1))],
            revision));

    private static SchedulePlanRevokedIntegrationEvent CreateRevoked(
        string eventId, string planId, long revision, string reason, string? supersededByPlanId) => new(
        eventId,
        SchedulingIntegrationEventTypes.SchedulePlanRevoked,
        SchedulingIntegrationEventVersions.V1,
        At(5),
        SchedulingIntegrationEventSources.BusinessScheduling,
        "corr-001",
        eventId,
        "org-001",
        "env-dev",
        "scheduling",
        $"revoke:{planId}:{revision}",
        new SchedulePlanRevokedPayload(
            planId, "problem-001", 1, "aps-lite-v1", $"fingerprint-{revision}", revision, reason,
            supersededByPlanId,
            [new SchedulePlanAffectedOperationPayload("WO-001", "OP-10", 10, "DEV-OLD", "WC-1", At(1), At(2))]));

    private static DateTimeOffset At(int hour) => DateTimeOffset.Parse("2026-07-18T00:00:00Z").AddHours(hour);

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
