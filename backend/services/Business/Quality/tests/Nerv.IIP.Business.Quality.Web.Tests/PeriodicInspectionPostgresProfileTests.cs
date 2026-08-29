using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.Business.Quality.Domain;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.PeriodicInspectionOperationAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionTasks;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing;
using Npgsql;
using NetCorePal.Extensions.DependencyInjection;

namespace Nerv.IIP.Business.Quality.Web.Tests;

[Collection(QualityPostgresLaneDatabase.CollectionName)]
public abstract class PeriodicInspectionPostgresTestHarness
{    protected static async Task HandleReportAsync(
        DbContextOptions<ApplicationDbContext> options,
        ProductionReportRecordedIntegrationEvent integrationEvent)
    {
        await using var db = CreateContext(options);
        await new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            db,
            new PeriodicInspectionOperationScopeCoordinator(db),
            new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(integrationEvent, CancellationToken.None);
    }

    protected static async Task HandleCompletionAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = CreateContext(options);
        await new MesOperationTaskCompletedIntegrationEventHandlerForClosePeriodicInspection(
            db,
            new PeriodicInspectionOperationScopeCoordinator(db),
            new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(OperationCompleted(), CancellationToken.None);
    }

    protected static async Task HandleReleaseAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = CreateContext(options);
        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            db,
            new PeriodicInspectionOperationScopeCoordinator(db),
            new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(WorkOrderReleased(), CancellationToken.None);
    }

    protected static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                QualityPostgresLaneDatabase.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", QualityFacts.Schema))
            .Options;

    protected static ApplicationDbContext CreateContext(DbContextOptions<ApplicationDbContext> options) =>
        new(options, new NoopMediator());

    protected static async Task ExecuteSqlAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    protected static async Task<string?> ObserveConstraintViolationAsync(
        NpgsqlConnection connection,
        ConstraintViolationCase testCase)
    {
        try
        {
            await ExecuteSqlAsync(connection, testCase.Sql);
            return $"{testCase.Name}: statement was accepted";
        }
        catch (PostgresException exception)
        {
            return exception.SqlState == testCase.SqlState
                   && exception.ConstraintName == testCase.ConstraintName
                ? null
                : $"{testCase.Name}: expected {testCase.SqlState}/{testCase.ConstraintName}, "
                  + $"observed {exception.SqlState}/{exception.ConstraintName}";
        }
    }

    protected static string RuntimeContextInsert(
        string id,
        string inspectionPlanId,
        string workOrderId,
        string timeIntervalHours,
        string quantityInterval,
        string assignedInspectorUserId,
        string assignedTeamId,
        string quantityHighWater,
        string status,
        string completedAtUtc) => $$"""
        INSERT INTO quality.periodic_inspection_runtime_contexts
            (id, operation_context_id, organization_id, environment_id, work_order_id, operation_id,
             sku_code, operation_sequence, work_center_id, released_at_utc, inspection_plan_id,
             inspection_plan_version, time_interval_hours, quantity_interval, assigned_inspector_user_id,
             assigned_team_id, first_activity_at_utc, uom_code, cumulative_good_quantity,
             quantity_high_water, status, completed_at_utc)
        VALUES
            ('{{id}}', '00000000-0000-0000-0000-000000000001', 'org-001', 'env-dev',
             '{{workOrderId}}', 'OP-001', 'SKU-FG-1000', 10, 'WC-001', '2026-08-24T01:00:00Z',
             '{{inspectionPlanId}}', 1, {{timeIntervalHours}}, {{quantityInterval}},
             {{assignedInspectorUserId}}, {{assignedTeamId}}, NULL, NULL, 0,
             {{quantityHighWater}}, {{status}}, {{completedAtUtc}});
        """;

    protected static InspectionPlan NewPeriodicPlan(decimal quantityInterval = 100m)
    {
        var plan = InspectionPlan.Create(
            "org-001", "env-dev", "IQP-PERIODIC-PG-001", "operation", "SKU-FG-1000", null, "WC-001", null, "mes-operation",
            timeIntervalHours: 2m,
            quantityInterval,
            assignedTeamId: "team-quality-001");
        plan.AddCharacteristic("appearance", "Appearance", "visual", "critical", true, "zero-defect");
        plan.Activate();
        return plan;
    }

    protected static WorkOrderReleasedIntegrationEvent WorkOrderReleased() => new(
        "evt-release-pg-001", MesIntegrationEventTypes.WorkOrderReleased, MesIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-08-24T01:00:00Z"), MesIntegrationEventSources.BusinessMes,
        "corr-release-pg-001", "WO-001", "org-001", "env-dev", "system:mes",
        "mes:work-order-released:org-001:env-dev:WO-001",
        new WorkOrderReleasedPayload(
            "WO-001", "SKU-FG-1000", 1000m, DateTimeOffset.Parse("2026-08-24T01:00:00Z"),
            [new ReleasedOperationPayload("OP-001", 10, "WC-001")]));

    protected static ProductionReportRecordedIntegrationEvent ProductionReport(
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

    protected static MesOperationTaskCompletedIntegrationEvent OperationCompleted() => new(
        "evt-complete-pg-001", MesIntegrationEventTypes.OperationTaskCompleted, MesIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-08-24T04:00:00Z"), MesIntegrationEventSources.BusinessMes,
        "corr-complete-pg-001", "WO-001", "org-001", "env-dev", "system:mes",
        "mes:operation-completed:org-001:env-dev:WO-001:OP-001",
        new OperationTaskCompletedPayload(
            "WO-001", "OP-001", "SKU-FG-1000", 10, "WC-001", 1000m, "EA", false,
            DateTimeOffset.Parse("2026-08-24T04:00:00Z")));

    protected static async Task WaitForAdvisoryWaitersAsync(
        int expected = 1,
        IReadOnlyCollection<Task>? competingTasks = null)
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
            condition: $"{expected} PostgreSQL advisory-lock waiter(s) for the Quality periodic-inspection operation scope",
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
            isSatisfied: waiters => waiters >= expected || (competingTasks?.Any(task => task.IsCompleted) ?? false),
            describe: waiters => $"advisoryLockWaiters={waiters}; expected>={expected}; "
                + $"taskStatuses={string.Join(',', competingTasks?.Select(task => task.Status) ?? [])}",
            options: new EventuallyOptions(
                TimeSpan.FromSeconds(15),
                TimeSpan.FromMilliseconds(50),
                [QualityPostgresLaneDatabase.ConnectionString]));

        if (competingTasks?.Any(task => task.IsCompleted) ?? false)
        {
            await Task.WhenAll(competingTasks);
            throw new InvalidOperationException("A competing generator completed before reaching the controlled advisory-lock boundary.");
        }
    }

    protected sealed record ConstraintViolationCase(
        string Name,
        string SqlState,
        string ConstraintName,
        string Sql);

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
