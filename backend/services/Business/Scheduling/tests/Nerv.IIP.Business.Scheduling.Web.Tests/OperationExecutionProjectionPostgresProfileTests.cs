using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

[Collection(SchedulingPostgresLaneDatabase.CollectionName)]
public sealed class OperationExecutionProjectionPostgresProfileTests
{
    [SchedulingPostgresFact]
    public async Task Migration_and_consumer_transaction_persist_one_projection_for_a_replayed_report()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = CreateProvider();

        await using (var migrationScope = provider.CreateAsyncScope())
        {
            var db = migrationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            SchedulingPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
            await db.Database.MigrateAsync();
        }

        var integrationEvent = ProductionReport();
        await ConsumeAsync(provider, integrationEvent);
        await ConsumeAsync(provider, integrationEvent);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var projection = await verifyDb.OperationExecutionProjections.AsNoTracking().SingleAsync();
        Assert.Equal(("org-001", "env-dev", "wo-001", "op-010"),
            (projection.OrganizationId, projection.EnvironmentId, projection.WorkOrderId, projection.OperationId));
        Assert.Equal(7m, projection.CompletedQuantity);
        Assert.Equal("evt-report", projection.LatestSourceEventId);
        Assert.Single(await verifyDb.ProcessedIntegrationEvents.AsNoTracking().ToArrayAsync());
    }

    private static async Task ConsumeAsync(
        ServiceProvider provider,
        ProductionReportRecordedIntegrationEvent integrationEvent)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var handler = new ProductionReportRecordedIntegrationEventHandlerForProjectExecution(
            db,
            new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(db),
            scope.ServiceProvider.GetRequiredService<IOperationExecutionProjectionMutationLock>());

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await transaction.CommitAsync();
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddSchedulingPostgreSqlPersistence(SchedulingPostgresLaneDatabase.ConnectionString);
        return services.BuildServiceProvider();
    }

    private static ProductionReportRecordedIntegrationEvent ProductionReport()
    {
        var reportedAtUtc = new DateTimeOffset(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);
        return new ProductionReportRecordedIntegrationEvent(
            "evt-report",
            MesIntegrationEventTypes.ProductionReportRecorded,
            MesIntegrationEventVersions.V1,
            reportedAtUtc,
            MesIntegrationEventSources.BusinessMes,
            "corr-report",
            "cause-report",
            "org-001",
            "env-dev",
            "operator",
            "production-report:RPT-001",
            new ProductionReportRecordedPayload(
                "RPT-001",
                "wo-001",
                "op-010",
                "wc-001",
                null,
                7m,
                2m,
                1m,
                "PCS",
                null,
                reportedAtUtc,
                false));
    }
}
