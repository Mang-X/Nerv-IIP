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
public sealed class PeriodicInspectionPostgresMigrationTests : PeriodicInspectionPostgresTestHarness
{
    [QualityPostgresFact]
    public async Task Quantity_continuation_fairness_migration_backfills_due_cursor_and_allows_terminal_pending_on_postgres()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        var options = CreateOptions();
        await using (var setup = CreateContext(options))
        {
            QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.GetService<IMigrator>().MigrateAsync("20260825064909_AddPeriodicInspectionQuantityContinuationInbox");
        }

        await using (var connection = new NpgsqlConnection(QualityPostgresLaneDatabase.ConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteSqlAsync(connection, """
                INSERT INTO quality.periodic_inspection_operations
                    (id, organization_id, environment_id, work_order_id, operation_id,
                     sku_code, operation_sequence, work_center_id, released_at_utc)
                VALUES
                    ('00000000-0000-0000-0000-000000000901', 'org-001', 'env-dev', 'WO-MIGRATE', 'OP-001',
                     'SKU-FG-1000', 10, 'WC-001', '2026-08-24T01:00:00Z');

                INSERT INTO quality.periodic_inspection_runtime_contexts
                    (id, operation_context_id, organization_id, environment_id, work_order_id, operation_id,
                     sku_code, operation_sequence, work_center_id, released_at_utc, inspection_plan_id,
                     inspection_plan_version, quantity_interval, assigned_team_id, first_activity_at_utc, uom_code,
                     cumulative_good_quantity, quantity_high_water, last_generated_quantity_window_sequence,
                     quantity_generation_anchor_at_utc, status)
                VALUES
                    ('00000000-0000-0000-0000-000000000902', '00000000-0000-0000-0000-000000000901',
                     'org-001', 'env-dev', 'WO-MIGRATE', 'OP-001', 'SKU-FG-1000', 10, 'WC-001',
                     '2026-08-24T01:00:00Z', '00000000-0000-0000-0000-000000000903', 1, 1,
                     'team-quality-001', '2026-08-24T01:10:00Z', 'EA', 257, 257, 256,
                     '2026-08-24T01:10:00Z', 'active');
                """);
        }

        await using (var migrate = CreateContext(options))
        {
            await migrate.Database.MigrateAsync();
        }

        await using var assertion = new NpgsqlConnection(QualityPostgresLaneDatabase.ConnectionString);
        await assertion.OpenAsync();
        await using (var cursorCommand = new NpgsqlCommand(
            "SELECT quantity_continuation_next_attempt_at_utc FROM quality.periodic_inspection_runtime_contexts WHERE id = '00000000-0000-0000-0000-000000000902'",
            assertion))
        {
            Assert.Equal(
                DateTimeOffset.Parse("2026-08-24T01:10:00Z").UtcDateTime,
                await cursorCommand.ExecuteScalarAsync());
        }

        await ExecuteSqlAsync(assertion, """
            UPDATE quality.periodic_inspection_runtime_contexts
            SET status = 'closed', completed_at_utc = '2026-08-24T02:00:00Z'
            WHERE id = '00000000-0000-0000-0000-000000000902';
            """);

        await using var restartedScan = CreateContext(options);
        var terminalCandidate = Assert.Single(
            await new ListPendingPeriodicInspectionQuantityContextsQueryHandler(restartedScan).Handle(
                new ListPendingPeriodicInspectionQuantityContextsQuery(
                    DateTimeOffset.Parse("2026-08-24T02:00:00Z").UtcDateTime,
                    100),
                CancellationToken.None));
        Assert.Equal("WO-MIGRATE", terminalCandidate.WorkOrderId);
    }

    [QualityPostgresFact]
    public async Task Time_watermark_migration_backfills_the_next_due_window_for_existing_active_contexts()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        var options = CreateOptions();
        await using (var setup = CreateContext(options))
        {
            QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.GetService<IMigrator>().MigrateAsync("20260824075912_AddPeriodicInspectionOperationContexts");
        }

        await using (var connection = new NpgsqlConnection(QualityPostgresLaneDatabase.ConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteSqlAsync(connection, """
                INSERT INTO quality.periodic_inspection_operations
                    (id, organization_id, environment_id, work_order_id, operation_id)
                VALUES
                    ('00000000-0000-0000-0000-000000000001', 'org-001', 'env-dev', 'WO-BASE', 'OP-BASE'),
                    ('00000000-0000-0000-0000-000000000002', 'org-001', 'env-dev', 'WO-CLOSED', 'OP-CLOSED');

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
                     '00000000-0000-0000-0000-000000000301', 1, 2.5, 100, NULL, 'team-quality-001',
                     '2026-08-24T01:10:00Z', 'EA', 10, 10, 'active', NULL),
                    ('00000000-0000-0000-0000-000000000202',
                     '00000000-0000-0000-0000-000000000002', 'org-001', 'env-dev', 'WO-CLOSED', 'OP-CLOSED',
                     'SKU-FG-1000', 10, 'WC-001', '2026-08-24T01:00:00Z',
                     '00000000-0000-0000-0000-000000000302', 1, 2.5, 100, NULL, 'team-quality-001',
                     '2026-08-24T01:10:00Z', 'EA', 10, 10, 'closed', '2026-08-24T02:00:00Z');
                """);
        }

        await using (var migrate = CreateContext(options))
        {
            await migrate.Database.MigrateAsync();
        }

        await using var assertion = new NpgsqlConnection(QualityPostgresLaneDatabase.ConnectionString);
        await assertion.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT next_time_window_at_utc FROM quality.periodic_inspection_runtime_contexts WHERE id = '00000000-0000-0000-0000-000000000201'",
            assertion);
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-24T03:40:00Z").UtcDateTime,
            await command.ExecuteScalarAsync());
        await using var closedCommand = new NpgsqlCommand(
            "SELECT next_time_window_at_utc FROM quality.periodic_inspection_runtime_contexts WHERE id = '00000000-0000-0000-0000-000000000202'",
            assertion);
        Assert.Equal(DBNull.Value, await closedCommand.ExecuteScalarAsync());
        await using var commentCommand = new NpgsqlCommand(
            "SELECT obj_description('quality.periodic_inspection_runtime_contexts'::regclass)",
            assertion);
        Assert.Equal(
            "Frozen per-plan periodic inspection runtime contexts with quantity/time watermarks and periodic task generation state.",
            await commentCommand.ExecuteScalarAsync());
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
                    "2", "100", "NULL", "NULL", "-1", "'active'", "NULL")),
            new ConstraintViolationCase(
                "runtime time watermark",
                PostgresErrorCodes.CheckViolation,
                "ck_periodic_inspection_runtime_time_watermark",
                """
                UPDATE quality.periodic_inspection_runtime_contexts
                SET last_generated_time_window_sequence = 1,
                    time_schedule_anchor_at_utc = NULL
                WHERE id = '00000000-0000-0000-0000-000000000201';
                """),
            new ConstraintViolationCase(
                "runtime quantity watermark",
                PostgresErrorCodes.CheckViolation,
                "ck_periodic_inspection_runtime_quantity_watermark",
                """
                UPDATE quality.periodic_inspection_runtime_contexts
                SET last_generated_quantity_window_sequence = -1
                WHERE id = '00000000-0000-0000-0000-000000000201';
                """),
            new ConstraintViolationCase(
                "runtime quantity continuation pairing",
                PostgresErrorCodes.CheckViolation,
                "ck_periodic_inspection_runtime_quantity_continuation",
                """
                UPDATE quality.periodic_inspection_runtime_contexts
                SET quantity_generation_anchor_at_utc = '2026-08-24T01:10:00Z',
                    quantity_continuation_next_attempt_at_utc = NULL
                WHERE id = '00000000-0000-0000-0000-000000000201';
                """)
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
}
