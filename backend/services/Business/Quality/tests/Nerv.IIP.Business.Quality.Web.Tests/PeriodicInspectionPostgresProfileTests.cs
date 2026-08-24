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

    [QualityPostgresFact]
    public async Task Postgres_migration_enforces_all_periodic_inspection_unique_and_check_constraints()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        var options = CreateOptions();
        await using (var setup = CreateContext(options))
        {
            QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();
        }

        await using var connection = new NpgsqlConnection(QualityPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await ExecuteSqlAsync(connection, """
            INSERT INTO quality.periodic_inspection_operations
                (id, organization_id, environment_id, work_order_id, operation_id)
            VALUES
                ('00000000-0000-0000-0000-000000000001', 'org-001', 'env-dev', 'WO-BASE', 'OP-BASE');

            INSERT INTO quality.periodic_inspection_production_reports
                (id, operation_context_id, report_no, work_center_id, good_quantity, uom_code,
                 reported_at_utc, is_reversal, reversed_report_no)
            VALUES
                ('00000000-0000-0000-0000-000000000101',
                 '00000000-0000-0000-0000-000000000001', 'RPT-BASE', 'WC-001', 10, 'EA',
                 '2026-08-24T01:10:00Z', false, NULL);

            INSERT INTO quality.periodic_inspection_runtime_contexts
                (id, operation_context_id, organization_id, environment_id, work_order_id, operation_id,
                 sku_code, operation_sequence, work_center_id, released_at_utc, inspection_plan_id,
                 inspection_plan_version, time_interval_hours, quantity_interval, assigned_inspector_user_id,
                 assigned_team_id, first_activity_at_utc, uom_code, cumulative_good_quantity,
                 quantity_high_water, status, completed_at_utc)
            VALUES
                ('00000000-0000-0000-0000-000000000201',
                 '00000000-0000-0000-0000-000000000001', 'org-001', 'env-dev', 'WO-BASE', 'OP-BASE',
                 'SKU-FG-1000', 10, 'WC-001', '2026-08-24T01:00:00Z',
                 '00000000-0000-0000-0000-000000000301', 1, 2, 100, NULL, 'team-quality-001',
                 '2026-08-24T01:10:00Z', 'EA', 10, 10, 'active', NULL);
            """);

        var cases = new[]
        {
            new ConstraintViolationCase(
                "operation scope identity",
                PostgresErrorCodes.UniqueViolation,
                "ux_periodic_inspection_operations_scope_operation",
                """
                INSERT INTO quality.periodic_inspection_operations
                    (id, organization_id, environment_id, work_order_id, operation_id)
                VALUES
                    ('00000000-0000-0000-0000-000000000002', 'org-001', 'env-dev', 'WO-BASE', 'OP-BASE');
                """),
            new ConstraintViolationCase(
                "production report identity",
                PostgresErrorCodes.UniqueViolation,
                "ux_periodic_inspection_reports_operation_report",
                """
                INSERT INTO quality.periodic_inspection_production_reports
                    (id, operation_context_id, report_no, work_center_id, good_quantity, uom_code,
                     reported_at_utc, is_reversal, reversed_report_no)
                VALUES
                    ('00000000-0000-0000-0000-000000000102',
                     '00000000-0000-0000-0000-000000000001', 'RPT-BASE', 'WC-001', 20, 'EA',
                     '2026-08-24T01:20:00Z', false, NULL);
                """),
            new ConstraintViolationCase(
                "runtime context identity",
                PostgresErrorCodes.UniqueViolation,
                "ux_periodic_inspection_runtime_scope_plan_operation",
                """
                INSERT INTO quality.periodic_inspection_runtime_contexts
                    (id, operation_context_id, organization_id, environment_id, work_order_id, operation_id,
                     sku_code, operation_sequence, work_center_id, released_at_utc, inspection_plan_id,
                     inspection_plan_version, time_interval_hours, quantity_interval, assigned_inspector_user_id,
                     assigned_team_id, first_activity_at_utc, uom_code, cumulative_good_quantity,
                     quantity_high_water, status, completed_at_utc)
                VALUES
                    ('00000000-0000-0000-0000-000000000202',
                     '00000000-0000-0000-0000-000000000001', 'org-001', 'env-dev', 'WO-BASE', 'OP-BASE',
                     'SKU-FG-1000', 10, 'WC-001', '2026-08-24T01:00:00Z',
                     '00000000-0000-0000-0000-000000000301', 1, 2, 100, NULL, 'team-quality-001',
                     '2026-08-24T01:10:00Z', 'EA', 10, 10, 'active', NULL);
                """),
            new ConstraintViolationCase(
                "release snapshot completeness",
                PostgresErrorCodes.CheckViolation,
                "ck_periodic_inspection_operations_release_snapshot",
                """
                INSERT INTO quality.periodic_inspection_operations
                    (id, organization_id, environment_id, work_order_id, operation_id, sku_code)
                VALUES
                    ('00000000-0000-0000-0000-000000000003', 'org-001', 'env-dev',
                     'WO-RELEASE-INVALID', 'OP-001', 'SKU-FG-1000');
                """),
            new ConstraintViolationCase(
                "completion snapshot completeness",
                PostgresErrorCodes.CheckViolation,
                "ck_periodic_inspection_operations_completion_snapshot",
                """
                INSERT INTO quality.periodic_inspection_operations
                    (id, organization_id, environment_id, work_order_id, operation_id, completion_sku_code)
                VALUES
                    ('00000000-0000-0000-0000-000000000004', 'org-001', 'env-dev',
                     'WO-COMPLETION-INVALID', 'OP-001', 'SKU-FG-1000');
                """),
            new ConstraintViolationCase(
                "completion time ordering",
                PostgresErrorCodes.CheckViolation,
                "ck_periodic_inspection_operations_completion_time",
                """
                INSERT INTO quality.periodic_inspection_operations
                    (id, organization_id, environment_id, work_order_id, operation_id,
                     sku_code, operation_sequence, work_center_id, released_at_utc,
                     completion_sku_code, completion_operation_sequence, completion_work_center_id,
                     completion_uom_code, completed_at_utc)
                VALUES
                    ('00000000-0000-0000-0000-000000000005', 'org-001', 'env-dev', 'WO-TIME-INVALID', 'OP-001',
                     'SKU-FG-1000', 10, 'WC-001', '2026-08-24T02:00:00Z',
                     'SKU-FG-1000', 10, 'WC-001', 'EA', '2026-08-24T01:00:00Z');
                """),
            new ConstraintViolationCase(
                "reversal facts",
                PostgresErrorCodes.CheckViolation,
                "ck_periodic_inspection_reports_reversal",
                """
                INSERT INTO quality.periodic_inspection_production_reports
                    (id, operation_context_id, report_no, work_center_id, good_quantity, uom_code,
                     reported_at_utc, is_reversal, reversed_report_no)
                VALUES
                    ('00000000-0000-0000-0000-000000000103',
                     '00000000-0000-0000-0000-000000000001', 'RPT-REVERSAL-INVALID', 'WC-001', 1, 'EA',
                     '2026-08-24T01:30:00Z', true, 'RPT-BASE');
                """),
            new ConstraintViolationCase(
                "runtime interval",
                PostgresErrorCodes.CheckViolation,
                "ck_periodic_inspection_runtime_interval",
                RuntimeContextInsert(
                    "00000000-0000-0000-0000-000000000203",
                    "00000000-0000-0000-0000-000000000302",
                    "WO-INTERVAL-INVALID",
                    "NULL", "NULL", "NULL", "NULL", "0", "'active'", "NULL")),
            new ConstraintViolationCase(
                "runtime assignment",
                PostgresErrorCodes.CheckViolation,
                "ck_periodic_inspection_runtime_assignment",
                RuntimeContextInsert(
                    "00000000-0000-0000-0000-000000000204",
                    "00000000-0000-0000-0000-000000000303",
                    "WO-ASSIGNMENT-INVALID",
                    "2", "100", "'user-quality-001'", "'team-quality-001'", "0", "'active'", "NULL")),
            new ConstraintViolationCase(
                "runtime status",
                PostgresErrorCodes.CheckViolation,
                "ck_periodic_inspection_runtime_status",
                RuntimeContextInsert(
                    "00000000-0000-0000-0000-000000000205",
                    "00000000-0000-0000-0000-000000000304",
                    "WO-STATUS-INVALID",
                    "2", "100", "NULL", "NULL", "0", "'active'", "'2026-08-24T04:00:00Z'")),
            new ConstraintViolationCase(
                "runtime high water",
                PostgresErrorCodes.CheckViolation,
                "ck_periodic_inspection_runtime_high_water",
                RuntimeContextInsert(
                    "00000000-0000-0000-0000-000000000206",
                    "00000000-0000-0000-0000-000000000305",
                    "WO-HIGH-WATER-INVALID",
                    "2", "100", "NULL", "NULL", "-1", "'active'", "NULL"))
        };

        var failures = new List<string>();
        foreach (var testCase in cases)
        {
            var failure = await ObserveConstraintViolationAsync(connection, testCase);
            if (failure is not null)
            {
                failures.Add(failure);
            }
        }

        Assert.Empty(failures);
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

    private static async Task ExecuteSqlAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ObserveConstraintViolationAsync(
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

    private static string RuntimeContextInsert(
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

    private sealed record ConstraintViolationCase(
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
