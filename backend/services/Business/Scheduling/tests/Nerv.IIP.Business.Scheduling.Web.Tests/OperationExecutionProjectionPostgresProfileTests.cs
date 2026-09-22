using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

[Collection(SchedulingPostgresLaneDatabase.CollectionName)]
public sealed class OperationExecutionProjectionPostgresProfileTests
{
    [SchedulingPostgresFact]
    public async Task Migration_and_concurrent_consumers_persist_one_projection_without_lost_quantity()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = CreateProvider();

        await using (var migrationScope = provider.CreateAsyncScope())
        {
            var db = migrationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            SchedulingPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
            await db.Database.MigrateAsync();
        }

        var firstReport = ProductionReport("evt-report-1", "RPT-001", 7m);
        var secondReport = ProductionReport("evt-report-2", "RPT-002", 5m);
        var firstApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = ConsumeAsync(provider, firstReport, applied: firstApplied, release: releaseFirst.Task);
        try
        {
            await firstApplied.Task;
            var second = ConsumeAsync(provider, secondReport, started: secondStarted);
            await secondStarted.Task;
            try
            {
                await WaitForBlockedAdvisoryMutationAsync(provider, second);
            }
            finally
            {
                releaseFirst.TrySetResult();
            }

            await Task.WhenAll(first, second);
        }
        finally
        {
            releaseFirst.TrySetResult();
        }

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var projection = await verifyDb.OperationExecutionProjections.AsNoTracking().SingleAsync();
        Assert.Equal(("org-001", "env-dev", "wo-001", "op-010"),
            (projection.OrganizationId, projection.EnvironmentId, projection.WorkOrderId, projection.OperationId));
        Assert.Equal(12m, projection.CompletedQuantity);
        Assert.Equal(2, await verifyDb.ProcessedIntegrationEvents.AsNoTracking().CountAsync());
    }

    private static async Task ConsumeAsync(
        ServiceProvider provider,
        ProductionReportRecordedIntegrationEvent integrationEvent,
        TaskCompletionSource? started = null,
        TaskCompletionSource? applied = null,
        Task? release = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        started?.TrySetResult();
        var handler = new ProductionReportRecordedIntegrationEventHandlerForProjectExecution(
            db,
            new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(db),
            scope.ServiceProvider.GetRequiredService<IOperationExecutionProjectionMutationLock>());

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        applied?.TrySetResult();
        if (release is not null)
        {
            await release;
        }
        await transaction.CommitAsync();
    }

    private static async Task WaitForBlockedAdvisoryMutationAsync(ServiceProvider provider, Task competingTask)
    {
        await Eventually.WaitAsync(
            condition: "the second operation projection consumer is blocked on the PostgreSQL advisory lock",
            observe: async cancellationToken =>
            {
                await using var scope = provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                return await db.Database.SqlQueryRaw<int>("""
                    SELECT COUNT(*)::int AS "Value"
                    FROM pg_locks
                    WHERE locktype = 'advisory'
                      AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
                      AND NOT granted
                    """).SingleAsync(cancellationToken);
            },
            isSatisfied: waiting => waiting > 0 || competingTask.IsCompleted,
            describe: waiting => $"advisoryLockWaiters={waiting}; secondTaskStatus={competingTask.Status}",
            options: new EventuallyOptions(
                Timeout: TimeSpan.FromSeconds(15),
                PollInterval: TimeSpan.FromMilliseconds(50),
                SensitiveValues: [SchedulingPostgresLaneDatabase.ConnectionString]));

        if (competingTask.IsCompleted)
        {
            await competingTask;
            throw new InvalidOperationException(
                "The second consumer completed before reaching the controlled advisory-lock boundary.");
        }
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddSchedulingPostgreSqlPersistence(SchedulingPostgresLaneDatabase.ConnectionString);
        return services.BuildServiceProvider();
    }

    private static ProductionReportRecordedIntegrationEvent ProductionReport(
        string eventId,
        string reportNo,
        decimal goodQuantity)
    {
        var reportedAtUtc = new DateTimeOffset(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);
        return new ProductionReportRecordedIntegrationEvent(
            eventId,
            MesIntegrationEventTypes.ProductionReportRecorded,
            MesIntegrationEventVersions.V1,
            reportedAtUtc,
            MesIntegrationEventSources.BusinessMes,
            $"corr-{reportNo}",
            $"cause-{reportNo}",
            "org-001",
            "env-dev",
            "operator",
            $"production-report:{reportNo}",
            new ProductionReportRecordedPayload(
                reportNo,
                "wo-001",
                "op-010",
                "wc-001",
                null,
                goodQuantity,
                2m,
                1m,
                "PCS",
                null,
                reportedAtUtc,
                false));
    }
}
