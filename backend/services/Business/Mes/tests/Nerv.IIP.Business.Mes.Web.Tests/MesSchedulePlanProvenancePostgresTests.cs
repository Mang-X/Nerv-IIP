using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.IntegrationEvents;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesSchedulePlanProvenancePostgresTests
{
    [MesRealPostgresFact]
    public async Task Concurrent_revoke_then_release_is_serialized_and_cannot_resurrect_assignment()
    {
        await using var database = await TemporaryDatabase.CreateAsync(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(database.ConnectionString).Options;
        await using (var setup = new ApplicationDbContext(options, new NoopMediator()))
        {
            await setup.Database.MigrateAsync();
            setup.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-001", "SKU-001", "PV-001", 1m, 1, At(8), "PCS"));
            await setup.SaveChangesAsync();
        }

        await using var revokeDb = new ApplicationDbContext(options, new NoopMediator());
        await using var releaseDb = new ApplicationDbContext(options, new NoopMediator());
        var revokeHasLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRevoke = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var revokeCoordinator = new SignalingCoordinator(
            new PostgreSqlMesScheduleReleaseScopeCoordinator(revokeDb),
            revokeHasLock,
            allowRevoke.Task);
        var releaseDeadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var revokeTask = new SchedulePlanRevokedIntegrationEventHandlerForWithdrawDispatch(
            revokeDb,
            new InMemoryIntegrationEventDeadLetterStore(),
            revokeCoordinator).HandleAsync(CreateRevokedEvent(), CancellationToken.None);

        await revokeHasLock.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var releaseTask = new SchedulePlanReleasedIntegrationEventHandlerForDispatch(
            releaseDb,
            releaseDeadLetters,
            new PostgreSqlMesScheduleReleaseScopeCoordinator(releaseDb)).HandleAsync(CreateReleasedEvent(), CancellationToken.None);
        await Task.Delay(200);
        Assert.False(releaseTask.IsCompleted, "Release must wait for the same-scope revoke transaction lock.");
        allowRevoke.SetResult();
        await Task.WhenAll(revokeTask, releaseTask);

        await using var assertion = new ApplicationDbContext(options, new NoopMediator());
        Assert.Empty(await assertion.OperationTasks.ToArrayAsync());
        Assert.Equal(1, (await assertion.ScheduleReleaseWatermarks.SingleAsync()).RevokedReleaseRevision);
        Assert.Contains(await releaseDeadLetters.ListAsync(
            SchedulePlanReleasedIntegrationEventHandlerForDispatch.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None), x => x.FailureCode == "mes.schedulePlanReleased.releaseAlreadyRevoked");
    }

    [MesRealPostgresFact]
    public async Task Schedule_provenance_and_DateTimeOffset_survive_release_and_revoke_on_postgres()
    {
        await using var database = await TemporaryDatabase.CreateAsync(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(database.ConnectionString).Options;
        var scheduledAt = DateTimeOffset.Parse("2026-07-18T02:30:00+00:00");

        await using (var db = new ApplicationDbContext(options, new NoopMediator()))
        {
            await db.Database.MigrateAsync();
            db.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-001", "SKU-001", "PV-001", 1m, 1, scheduledAt.AddHours(4), "PCS"));
            var task = OperationTask.Queue("org-001", "env-dev", "WO-001", "OP-10", 10, "WC-OLD", [], scheduledAt, TimeSpan.FromHours(1));
            task.ApplyScheduleAssignment("WC-1", "DEV-1", scheduledAt, scheduledAt.AddHours(1), scheduledAt, schedulePlanId: "plan-1", scheduleReleaseRevision: 1);
            db.OperationTasks.Add(task);
            db.ScheduleReleaseWatermarks.Add(new ScheduleReleaseWatermark(
                "org-001", "env-dev", "plan-0", 0, scheduledAt.AddMinutes(-1)));
            await db.SaveChangesAsync();
        }

        await using (var db = new ApplicationDbContext(options, new NoopMediator()))
        {
            var task = await db.OperationTasks.SingleAsync();
            var watermark = await db.ScheduleReleaseWatermarks.SingleAsync();
            Assert.Equal("plan-1", task.SchedulePlanId);
            Assert.Equal(1, task.ScheduleReleaseRevision);
            Assert.Equal(scheduledAt, task.ScheduledAtUtc);
            watermark.RecordRevocation("plan-1", 1, scheduledAt);
            task.RevokeScheduleAssignment("plan-1", 1, "explicit");
            await db.SaveChangesAsync();
        }

        await using (var db = new ApplicationDbContext(options, new NoopMediator()))
        {
            var task = await db.OperationTasks.SingleAsync();
            Assert.Null(task.SchedulePlanId);
            Assert.Null(task.ScheduleReleaseRevision);
            Assert.Null(task.ScheduledAtUtc);
            Assert.Equal(OperationTaskLifecycleStatus.ScheduleInvalidated, task.Status);
            var watermark = await db.ScheduleReleaseWatermarks.SingleAsync();
            Assert.Equal("plan-1", watermark.RevokedPlanId);
            Assert.Equal(1, watermark.RevokedReleaseRevision);
            Assert.Equal(scheduledAt, watermark.RevokedAtUtc);
        }
    }

    private sealed class TemporaryDatabase(string adminConnectionString, string databaseName, string connectionString) : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;

        public static async Task<TemporaryDatabase> CreateAsync(string baseConnectionString)
        {
            var databaseName = $"nerv_mes_schedule_{Guid.CreateVersion7():N}";
            var adminConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres" }.ConnectionString;
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await command.ExecuteNonQueryAsync();
            return new TemporaryDatabase(
                adminConnectionString,
                databaseName,
                new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = databaseName }.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static SchedulePlanReleasedIntegrationEvent CreateReleasedEvent() => new(
        "release-concurrent",
        SchedulingIntegrationEventTypes.SchedulePlanReleased,
        SchedulingIntegrationEventVersions.V1,
        At(1),
        SchedulingIntegrationEventSources.BusinessScheduling,
        "corr-001",
        "cause-release",
        "org-001",
        "env-dev",
        "scheduling",
        "release:plan-1:1",
        new SchedulePlanLifecyclePayload(
            "plan-1", "problem-1", 1, "aps-lite-v1", "fingerprint-1", "released",
            [new SchedulePlanAffectedOperationPayload("WO-001", "OP-10", 10, "DEV-1", "WC-1", At(2), At(3))],
            1));

    private static SchedulePlanRevokedIntegrationEvent CreateRevokedEvent() => new(
        "revoke-concurrent",
        SchedulingIntegrationEventTypes.SchedulePlanRevoked,
        SchedulingIntegrationEventVersions.V1,
        At(1),
        SchedulingIntegrationEventSources.BusinessScheduling,
        "corr-001",
        "cause-revoke",
        "org-001",
        "env-dev",
        "scheduling",
        "revoke:plan-1:1",
        new SchedulePlanRevokedPayload(
            "plan-1", "problem-1", 1, "aps-lite-v1", "fingerprint-1", 1, "explicit", null,
            [new SchedulePlanAffectedOperationPayload("WO-001", "OP-10", 10, "DEV-1", "WC-1", At(2), At(3))]));

    private static DateTimeOffset At(int hour) => DateTimeOffset.Parse("2026-07-18T00:00:00Z").AddHours(hour);

    private sealed class SignalingCoordinator(
        IMesScheduleReleaseScopeCoordinator inner,
        TaskCompletionSource lockAcquired,
        Task continueAction) : IMesScheduleReleaseScopeCoordinator
    {
        public Task ExecuteAsync(
            string organizationId,
            string environmentId,
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken)
        {
            return inner.ExecuteAsync(
                organizationId,
                environmentId,
                async ct =>
                {
                    lockAcquired.TrySetResult();
                    await continueAction.WaitAsync(ct);
                    await action(ct);
                },
                cancellationToken);
        }
    }

    private sealed class NoopMediator : IMediator
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
