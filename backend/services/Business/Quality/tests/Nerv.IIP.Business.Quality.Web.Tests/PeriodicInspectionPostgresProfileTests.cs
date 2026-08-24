using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.PeriodicInspectionOperationAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing;
using Npgsql;

namespace Nerv.IIP.Business.Quality.Web.Tests;

[Collection(QualityPostgresLaneDatabase.CollectionName)]
public sealed class PeriodicInspectionPostgresProfileTests
{
    [QualityPostgresFact]
    public async Task Postgres_out_of_order_reversal_duplicate_close_and_restart_converge_without_tasks()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        var options = CreateOptions();
        await using (var setup = CreateContext(options))
        {
            QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();
            setup.InspectionPlans.Add(NewPeriodicPlan());
            await setup.SaveChangesAsync();
        }

        await HandleReportAsync(options, ProductionReport("RPT-REV-001", -30m, true, "RPT-001", "2026-08-24T01:20:00Z"));
        await HandleReportAsync(options, ProductionReport("RPT-001", 100m, false, null, "2026-08-24T01:10:00Z"));
        await HandleReportAsync(options, ProductionReport("RPT-001", 100m, false, null, "2026-08-24T01:10:00Z"));
        await HandleCompletionAsync(options);
        await HandleReleaseAsync(options);

        await using var assertion = CreateContext(options);
        var operation = await assertion.PeriodicInspectionOperations
            .AsNoTracking()
            .Include(x => x.ProductionReports)
            .Include(x => x.RuntimeContexts)
            .SingleAsync();
        var context = Assert.Single(operation.RuntimeContexts);
        Assert.Equal(2, operation.ProductionReports.Count);
        Assert.Equal(70m, context.CumulativeGoodQuantity);
        Assert.Equal(100m, context.QuantityHighWater);
        Assert.Equal(DateTimeOffset.Parse("2026-08-24T01:10:00Z").UtcDateTime, context.FirstActivityAtUtc);
        Assert.Equal("closed", context.Status);
        Assert.Empty(await assertion.InspectionTasks.ToListAsync());
    }

    [QualityPostgresFact]
    public async Task Postgres_operation_scope_lock_serializes_concurrent_duplicate_source_creation()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        var options = CreateOptions();
        await using (var setup = CreateContext(options))
        {
            QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();
        }

        await using var firstContext = CreateContext(options);
        await using var secondContext = CreateContext(options);
        var firstCoordinator = new PeriodicInspectionOperationScopeCoordinator(firstContext);
        var secondCoordinator = new PeriodicInspectionOperationScopeCoordinator(secondContext);
        var firstHoldingLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = firstCoordinator.ExecuteAsync(
            "org-001", "env-dev", "WO-CONCURRENT", ["OP-001"],
            async cancellationToken =>
            {
                firstContext.PeriodicInspectionOperations.Add(
                    PeriodicInspectionOperation.CreatePending("org-001", "env-dev", "WO-CONCURRENT", "OP-001"));
                firstHoldingLock.SetResult();
                await allowFirstCommit.Task.WaitAsync(cancellationToken);
            },
            CancellationToken.None);
        await firstHoldingLock.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = secondCoordinator.ExecuteAsync(
            "org-001", "env-dev", "WO-CONCURRENT", ["OP-001"],
            async cancellationToken =>
            {
                var exists = await secondContext.PeriodicInspectionOperations.AnyAsync(
                    x => x.OrganizationId == "org-001"
                        && x.EnvironmentId == "env-dev"
                        && x.WorkOrderId == "WO-CONCURRENT"
                        && x.OperationId == "OP-001",
                    cancellationToken);
                if (!exists)
                {
                    secondContext.PeriodicInspectionOperations.Add(
                        PeriodicInspectionOperation.CreatePending("org-001", "env-dev", "WO-CONCURRENT", "OP-001"));
                }
            },
            CancellationToken.None);

        await WaitForAdvisoryWaiterAsync();
        Assert.False(second.IsCompleted, "The competing operation must be observably parked on the advisory lock.");
        allowFirstCommit.SetResult();
        await Task.WhenAll(first, second);

        await using var assertion = CreateContext(options);
        Assert.Equal(1, await assertion.PeriodicInspectionOperations.CountAsync());
    }

    private static async Task HandleReportAsync(
        DbContextOptions<ApplicationDbContext> options,
        ProductionReportRecordedIntegrationEvent integrationEvent)
    {
        await using var db = CreateContext(options);
        await new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            db,
            new PeriodicInspectionOperationScopeCoordinator(db),
            new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static async Task HandleCompletionAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = CreateContext(options);
        await new MesOperationTaskCompletedIntegrationEventHandlerForClosePeriodicInspection(
            db,
            new PeriodicInspectionOperationScopeCoordinator(db),
            new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(OperationCompleted(), CancellationToken.None);
    }

    private static async Task HandleReleaseAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = CreateContext(options);
        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            db,
            new PeriodicInspectionOperationScopeCoordinator(db),
            new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(WorkOrderReleased(), CancellationToken.None);
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                QualityPostgresLaneDatabase.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", QualityFacts.Schema))
            .Options;

    private static ApplicationDbContext CreateContext(DbContextOptions<ApplicationDbContext> options) =>
        new(options, new NoopMediator());

    private static InspectionPlan NewPeriodicPlan()
    {
        var plan = InspectionPlan.Create(
            "org-001", "env-dev", "IQP-PERIODIC-PG-001", "operation", "SKU-FG-1000", null, "WC-001", null, "mes-operation",
            timeIntervalHours: 2m,
            quantityInterval: 100m,
            assignedTeamId: "team-quality-001");
        plan.AddCharacteristic("appearance", "Appearance", "visual", "critical", true, "zero-defect");
        plan.Activate();
        return plan;
    }

    private static WorkOrderReleasedIntegrationEvent WorkOrderReleased() => new(
        "evt-release-pg-001", MesIntegrationEventTypes.WorkOrderReleased, MesIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-08-24T01:00:00Z"), MesIntegrationEventSources.BusinessMes,
        "corr-release-pg-001", "WO-001", "org-001", "env-dev", "system:mes",
        "mes:work-order-released:org-001:env-dev:WO-001",
        new WorkOrderReleasedPayload(
            "WO-001", "SKU-FG-1000", 1000m, DateTimeOffset.Parse("2026-08-24T01:00:00Z"),
            [new ReleasedOperationPayload("OP-001", 10, "WC-001")]));

    private static ProductionReportRecordedIntegrationEvent ProductionReport(
        string reportNo,
        decimal goodQuantity,
        bool isReversal,
        string? reversedReportNo,
        string reportedAtUtc) => new(
        $"evt-{reportNo}", MesIntegrationEventTypes.ProductionReportRecorded, MesIntegrationEventVersions.V1,
        DateTimeOffset.Parse(reportedAtUtc), MesIntegrationEventSources.BusinessMes,
        $"corr-{reportNo}", "WO-001", "org-001", "env-dev", "system:mes",
        $"mes:production-report-recorded:org-001:env-dev:{reportNo}",
        new ProductionReportRecordedPayload(
            reportNo, "WO-001", "OP-001", "WC-001", null, goodQuantity, 0m, 0m, "EA", null,
            DateTimeOffset.Parse(reportedAtUtc), isReversal, reversedReportNo));

    private static MesOperationTaskCompletedIntegrationEvent OperationCompleted() => new(
        "evt-complete-pg-001", MesIntegrationEventTypes.OperationTaskCompleted, MesIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-08-24T04:00:00Z"), MesIntegrationEventSources.BusinessMes,
        "corr-complete-pg-001", "WO-001", "org-001", "env-dev", "system:mes",
        "mes:operation-completed:org-001:env-dev:WO-001:OP-001",
        new OperationTaskCompletedPayload(
            "WO-001", "OP-001", "SKU-FG-1000", 10, "WC-001", 1000m, "EA", false,
            DateTimeOffset.Parse("2026-08-24T04:00:00Z")));

    private static async Task WaitForAdvisoryWaiterAsync()
    {
        const string sql = """
            SELECT count(*)
            FROM pg_stat_activity
            WHERE datname = current_database()
              AND pid <> pg_backend_pid()
              AND wait_event_type = 'Lock'
              AND wait_event = 'advisory'
            """;
        await Eventually.WaitAsync(
            condition: "one PostgreSQL advisory-lock waiter for the Quality periodic-inspection operation scope",
            observe: async cancellationToken =>
            {
                await using var connection = new NpgsqlConnection(QualityPostgresLaneDatabase.ConnectionString);
                await TestTimeout.RunAsync(
                    "open the Quality advisory-lock probe connection",
                    async token => await connection.OpenAsync(token),
                    TimeSpan.FromSeconds(10),
                    cancellationToken,
                    sensitiveValues: [QualityPostgresLaneDatabase.ConnectionString]);
                await using var command = new NpgsqlCommand(sql, connection);
                return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            },
            isSatisfied: waiters => waiters >= 1,
            describe: waiters => $"advisoryLockWaiters={waiters}; expected>=1",
            options: new EventuallyOptions(
                TimeSpan.FromSeconds(15),
                TimeSpan.FromMilliseconds(50),
                [QualityPostgresLaneDatabase.ConnectionString]));
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
