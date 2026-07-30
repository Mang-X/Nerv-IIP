using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Nerv.IIP.Business.Wms.Domain;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Seed;
using Nerv.IIP.Testing.PostgreSql;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsWorkAssignmentMigrationPostgresTests
{
    private const string PreviousMigration = "20260724015043_AddOutboundOrderConcurrencyToken";
    private const string WorkAssignmentMigration = "20260729205928_CompleteWmsWorkPoolExecutionBoundary";
    private static readonly DateOnly AsOfDate = new(2026, 7, 26);
    private static readonly Guid ActiveWarehouseTaskId =
        Guid.Parse("01982f4d-3f80-7000-8000-000000000001");
    private static readonly Guid ActiveWcsTaskId =
        Guid.Parse("01982f4d-3f80-7000-8000-000000000002");
    private static readonly Guid TerminalWarehouseTaskId =
        Guid.Parse("01982f4d-3f80-7000-8000-000000000003");
    private static readonly Guid TerminalWcsTaskId =
        Guid.Parse("01982f4d-3f80-7000-8000-000000000004");
    private static readonly DateTime ActiveDispatchedAtUtc =
        new(2026, 7, 20, 1, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime TerminalDispatchedAtUtc =
        new(2026, 7, 20, 2, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Migration_script_backfills_legacy_execution_channel_and_versions_without_invalid_defaults()
    {
        using var db = CreateContext(
            "Host=localhost;Database=nerv_iip_wms_migration_script;Username=nerv;Password=nerv");
        var script = db.GetService<IMigrator>().GenerateScript(PreviousMigration, WorkAssignmentMigration);

        Assert.Contains(
            """UPDATE "wms"."warehouse_tasks" SET "execution_channel" = 'LegacyUnclaimed'""",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            """UPDATE "wms"."warehouse_tasks" SET "version" = 1""",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            """UPDATE "wms"."inbound_orders" SET "version" = 1""",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            """UPDATE "wms"."count_executions" SET "version" = 1""",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("DEFAULT ''", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DEFAULT 0", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_script_blocks_ambiguous_legacy_wcs_claims_and_backfills_unambiguous_history()
    {
        using var db = CreateContext(
            "Host=localhost;Database=nerv_iip_wms_migration_script;Username=nerv;Password=nerv");
        var script = db.GetService<IMigrator>().GenerateScript(PreviousMigration, WorkAssignmentMigration);

        Assert.Contains(
            "WMS work-assignment migration blocked",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            """HAVING COUNT(*) > 1""",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            """SET "execution_channel" = 'Wcs'""",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"execution_claimed_by\" = wcs.\"id\"::text",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"execution_claimed_at_utc\" = wcs.\"dispatched_at_utc\"",
            script,
            StringComparison.Ordinal);

        var wcsBackfill = script.IndexOf(
            """SET "execution_channel" = 'Wcs'""",
            StringComparison.Ordinal);
        var legacyFallback = script.IndexOf(
            """SET "execution_channel" = 'LegacyUnclaimed'""",
            StringComparison.Ordinal);
        var ambiguityGuard = script.IndexOf(
            "WMS work-assignment migration blocked",
            StringComparison.Ordinal);
        var legacyIndexDrop = script.IndexOf(
            "IX_wcs_tasks_warehouse_task_id_adapter_type",
            StringComparison.Ordinal);
        Assert.True(ambiguityGuard >= 0 && legacyIndexDrop > ambiguityGuard);
        Assert.True(wcsBackfill >= 0 && legacyFallback > wcsBackfill);
    }

    [WmsWorkAssignmentPostgresFact]
    public async Task Migration_upgrades_legacy_rows_to_safe_assignment_boundary_and_keeps_seed_idempotent()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            adminConnectionString,
            "nerv_wms_assignment_test");
        await using var db = CreateContext(database.ConnectionString);
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(PreviousMigration);
        await SeedLegacyRowsAsync(database.ConnectionString);
        await migrator.MigrateAsync();

        var persisted = await ReadLegacyBoundaryAsync(database.ConnectionString);
        Assert.Equal(nameof(WarehouseTaskExecutionChannel.LegacyUnclaimed), persisted.ExecutionChannel);
        Assert.Equal(1, persisted.WarehouseTaskVersion);
        Assert.Equal(1, persisted.InboundOrderVersion);
        Assert.Equal(1, persisted.CountExecutionVersion);
        Assert.Null(persisted.WarehouseTaskPoolCode);
        Assert.Null(persisted.WarehouseTaskOperatorId);
        Assert.Null(persisted.InboundOrderPoolCode);
        Assert.Null(persisted.InboundOrderOperatorId);
        Assert.Null(persisted.CountExecutionPoolCode);
        Assert.Null(persisted.CountExecutionOperatorId);

        db.ChangeTracker.Clear();
        var legacyTask = await db.WarehouseTasks.SingleAsync(x => x.TaskNo == "TASK-LEGACY-001");
        var activeTask = await db.WarehouseTasks.SingleAsync(x => x.TaskNo == "TASK-WCS-ACTIVE-001");
        var terminalTask = await db.WarehouseTasks.SingleAsync(x => x.TaskNo == "TASK-WCS-TERMINAL-001");
        var legacyInbound = await db.InboundOrders.SingleAsync(x => x.InboundOrderNo == "IN-LEGACY-001");
        var legacyCount = await db.CountExecutions.SingleAsync(x => x.CountNo == "COUNT-LEGACY-001");
        Assert.Equal(WarehouseTaskExecutionChannel.LegacyUnclaimed, legacyTask.ExecutionChannel);
        Assert.Equal(WarehouseTaskExecutionChannel.Wcs, activeTask.ExecutionChannel);
        Assert.Equal(ActiveWcsTaskId.ToString(), activeTask.ExecutionClaimedBy);
        Assert.Equal(ActiveDispatchedAtUtc, activeTask.ExecutionClaimedAtUtc);
        Assert.Equal(WarehouseTaskStatus.Open, activeTask.Status);
        Assert.Equal(WarehouseTaskExecutionChannel.Wcs, terminalTask.ExecutionChannel);
        Assert.Equal(TerminalWcsTaskId.ToString(), terminalTask.ExecutionClaimedBy);
        Assert.Equal(TerminalDispatchedAtUtc, terminalTask.ExecutionClaimedAtUtc);
        Assert.Equal(WarehouseTaskStatus.Completed, terminalTask.Status);
        Assert.Equal(1, legacyTask.Version);
        Assert.Equal(1, legacyInbound.Version);
        Assert.Equal(1, legacyCount.Version);
        var executionError = Assert.Throws<InvalidOperationException>(
            () => legacyTask.Start("emp049", legacyTask.Version));
        Assert.Contains("no persisted work-pool assignment", executionError.Message, StringComparison.Ordinal);

        db.ChangeTracker.Clear();
        var seed = new WorldHistorySeedService(db);
        var first = await seed.SeedAsync("org-seed", "env-seed", AsOfDate, 0.01d);
        var second = await seed.SeedAsync("org-seed", "env-seed", AsOfDate, 0.01d);

        Assert.True(first.InboundOrdersWritten > 0);
        Assert.True(first.OutboundOrdersWritten > 0);
        Assert.True(first.WarehouseTasksWritten > 0);
        Assert.Equal(0, second.InboundOrdersWritten);
        Assert.Equal(0, second.OutboundOrdersWritten);
        Assert.Equal(0, second.WarehouseTasksWritten);
        Assert.Equal(0, second.InventoryMovementRequestsWritten);
    }

    [WmsWorkAssignmentPostgresFact]
    public async Task Migration_blocks_ambiguous_multi_adapter_history_before_changing_legacy_schema()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            adminConnectionString,
            "nerv_wms_assignment_conflict_test");
        await using var db = CreateContext(database.ConnectionString);
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(PreviousMigration);
        await SeedAmbiguousLegacyWcsRowsAsync(database.ConnectionString);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
        Assert.Contains(
            "WMS work-assignment migration blocked",
            exception.MessageText,
            StringComparison.Ordinal);
        Assert.Contains(
            "Resolve each conflict to one auditable WCS record",
            exception.Hint,
            StringComparison.Ordinal);

        var preserved = await ReadAmbiguousLegacyStateAsync(database.ConnectionString);
        Assert.Equal(2, preserved.WcsTaskCount);
        Assert.True(preserved.LegacyCompositeIndexExists);
        Assert.False(preserved.ExecutionChannelColumnExists);
        Assert.False(preserved.WorkAssignmentMigrationApplied);
    }

    private static ApplicationDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", WmsFacts.Schema))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static async Task SeedLegacyRowsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO wms.warehouse_tasks (
                id, task_type, organization_id, environment_id, task_no, source_order_no,
                source_order_line_no, sku_code, uom_code, site_code, from_location_code,
                to_location_code, planned_quantity, executed_quantity, status, created_at_utc,
                completed_at_utc)
            VALUES (
                @warehouse_task_id, 'Putaway', 'org-legacy', 'env-legacy', 'TASK-LEGACY-001',
                'IN-LEGACY-001', 'LINE-001', 'SKU-001', 'PCS', 'SITE-001', 'STAGE-001',
                'BIN-001', 10, 0, 'Open', @created_at_utc, NULL),
                (
                @active_warehouse_task_id, 'Putaway', 'org-legacy', 'env-legacy', 'TASK-WCS-ACTIVE-001',
                'IN-WCS-ACTIVE-001', 'LINE-001', 'SKU-002', 'PCS', 'SITE-001', 'STAGE-001',
                'BIN-002', 12, 0, 'Open', @created_at_utc, NULL),
                (
                @terminal_warehouse_task_id, 'Picking', 'org-legacy', 'env-legacy', 'TASK-WCS-TERMINAL-001',
                'OUT-WCS-TERMINAL-001', 'LINE-001', 'SKU-003', 'PCS', 'SITE-001', 'BIN-003',
                'STAGE-OUT', 8, 8, 'Completed', @created_at_utc, @terminal_completed_at_utc);

            INSERT INTO wms.wcs_tasks (
                id, warehouse_task_id, adapter_type, device_id, external_task_id, payload_json,
                status, attempt_count, dispatched_at_utc, completed_at_utc, organization_id,
                environment_id, is_terminal_failure)
            VALUES (
                @active_wcs_task_id, @active_warehouse_task_id, 'agv', 'AGV-01',
                'WCS-ACTIVE-001', '{}', 'Dispatched', 1, @active_dispatched_at_utc, NULL,
                'org-legacy', 'env-legacy', FALSE),
                (
                @terminal_wcs_task_id, @terminal_warehouse_task_id, 'conveyor', 'CV-01',
                'WCS-TERMINAL-001', '{"result":"done"}', 'Completed', 1,
                @terminal_dispatched_at_utc, @terminal_completed_at_utc,
                'org-legacy', 'env-legacy', FALSE);

            INSERT INTO wms.inbound_orders (
                id, organization_id, environment_id, inbound_order_no, source_document_type,
                source_document_id, site_code, status, created_at_utc)
            VALUES (
                @inbound_order_id, 'org-legacy', 'env-legacy', 'IN-LEGACY-001',
                'purchase-receipt', 'PO-LEGACY-001', 'SITE-001', 'Open', @created_at_utc);

            INSERT INTO wms.count_executions (
                id, organization_id, environment_id, count_no, sku_code, uom_code, site_code,
                location_code, expected_quantity, status, created_at_utc)
            VALUES (
                @count_execution_id, 'org-legacy', 'env-legacy', 'COUNT-LEGACY-001',
                'SKU-001', 'PCS', 'SITE-001', 'BIN-001', 10, 'Open', @created_at_utc);
            """,
            connection);
        command.Parameters.AddWithValue("warehouse_task_id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("active_warehouse_task_id", ActiveWarehouseTaskId);
        command.Parameters.AddWithValue("active_wcs_task_id", ActiveWcsTaskId);
        command.Parameters.AddWithValue("terminal_warehouse_task_id", TerminalWarehouseTaskId);
        command.Parameters.AddWithValue("terminal_wcs_task_id", TerminalWcsTaskId);
        command.Parameters.AddWithValue("inbound_order_id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("count_execution_id", Guid.CreateVersion7());
        command.Parameters.AddWithValue(
            "created_at_utc",
            new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero));
        command.Parameters.AddWithValue("active_dispatched_at_utc", ActiveDispatchedAtUtc);
        command.Parameters.AddWithValue("terminal_dispatched_at_utc", TerminalDispatchedAtUtc);
        command.Parameters.AddWithValue(
            "terminal_completed_at_utc",
            new DateTime(2026, 7, 20, 2, 10, 0, DateTimeKind.Utc));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedAmbiguousLegacyWcsRowsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO wms.warehouse_tasks (
                id, task_type, organization_id, environment_id, task_no, source_order_no,
                source_order_line_no, sku_code, uom_code, site_code, from_location_code,
                to_location_code, planned_quantity, executed_quantity, status, created_at_utc)
            VALUES (
                @warehouse_task_id, 'Putaway', 'org-conflict', 'env-conflict',
                'TASK-WCS-CONFLICT-001', 'IN-WCS-CONFLICT-001', 'LINE-001', 'SKU-001',
                'PCS', 'SITE-001', 'STAGE-001', 'BIN-001', 10, 0, 'Open',
                @created_at_utc);

            INSERT INTO wms.wcs_tasks (
                id, warehouse_task_id, adapter_type, device_id, external_task_id, payload_json,
                status, attempt_count, dispatched_at_utc, completed_at_utc, organization_id,
                environment_id, is_terminal_failure)
            VALUES (
                @active_wcs_task_id, @warehouse_task_id, 'agv', 'AGV-01',
                'WCS-CONFLICT-ACTIVE', '{}', 'Dispatched', 1, @created_at_utc, NULL,
                'org-conflict', 'env-conflict', FALSE),
                (
                @terminal_wcs_task_id, @warehouse_task_id, 'conveyor', 'CV-01',
                'WCS-CONFLICT-TERMINAL', '{"result":"done"}', 'Completed', 1,
                @created_at_utc, @completed_at_utc, 'org-conflict', 'env-conflict', FALSE);
            """,
            connection);
        command.Parameters.AddWithValue(
            "warehouse_task_id",
            Guid.Parse("01982f4d-3f80-7000-8000-000000000101"));
        command.Parameters.AddWithValue(
            "active_wcs_task_id",
            Guid.Parse("01982f4d-3f80-7000-8000-000000000102"));
        command.Parameters.AddWithValue(
            "terminal_wcs_task_id",
            Guid.Parse("01982f4d-3f80-7000-8000-000000000103"));
        command.Parameters.AddWithValue(
            "created_at_utc",
            new DateTime(2026, 7, 20, 3, 0, 0, DateTimeKind.Utc));
        command.Parameters.AddWithValue(
            "completed_at_utc",
            new DateTime(2026, 7, 20, 3, 10, 0, DateTimeKind.Utc));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<LegacyAssignmentBoundary> ReadLegacyBoundaryAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT
                task.execution_channel,
                task.version,
                task.assigned_pool_code,
                task.assigned_operator_user_id,
                inbound.version,
                inbound.assigned_pool_code,
                inbound.assigned_operator_user_id,
                count.version,
                count.assigned_pool_code,
                count.assigned_operator_user_id
            FROM wms.warehouse_tasks AS task
            CROSS JOIN wms.inbound_orders AS inbound
            CROSS JOIN wms.count_executions AS count
            WHERE task.task_no = 'TASK-LEGACY-001'
              AND inbound.inbound_order_no = 'IN-LEGACY-001'
              AND count.count_no = 'COUNT-LEGACY-001'
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new LegacyAssignmentBoundary(
            reader.GetString(0),
            reader.GetInt64(1),
            await reader.IsDBNullAsync(2) ? null : reader.GetString(2),
            await reader.IsDBNullAsync(3) ? null : reader.GetString(3),
            reader.GetInt64(4),
            await reader.IsDBNullAsync(5) ? null : reader.GetString(5),
            await reader.IsDBNullAsync(6) ? null : reader.GetString(6),
            reader.GetInt64(7),
            await reader.IsDBNullAsync(8) ? null : reader.GetString(8),
            await reader.IsDBNullAsync(9) ? null : reader.GetString(9));
    }

    private sealed record LegacyAssignmentBoundary(
        string ExecutionChannel,
        long WarehouseTaskVersion,
        string? WarehouseTaskPoolCode,
        string? WarehouseTaskOperatorId,
        long InboundOrderVersion,
        string? InboundOrderPoolCode,
        string? InboundOrderOperatorId,
        long CountExecutionVersion,
        string? CountExecutionPoolCode,
        string? CountExecutionOperatorId);

    private static async Task<AmbiguousLegacyState> ReadAmbiguousLegacyStateAsync(
        string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT
                (SELECT COUNT(*) FROM wms.wcs_tasks
                 WHERE warehouse_task_id = @warehouse_task_id),
                EXISTS (
                    SELECT 1
                    FROM pg_indexes
                    WHERE schemaname = 'wms'
                      AND indexname = 'IX_wcs_tasks_warehouse_task_id_adapter_type'),
                EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'wms'
                      AND table_name = 'warehouse_tasks'
                      AND column_name = 'execution_channel'),
                EXISTS (
                    SELECT 1
                    FROM wms."__EFMigrationsHistory"
                    WHERE "MigrationId" = @migration_id);
            """,
            connection);
        command.Parameters.AddWithValue(
            "warehouse_task_id",
            Guid.Parse("01982f4d-3f80-7000-8000-000000000101"));
        command.Parameters.AddWithValue("migration_id", WorkAssignmentMigration);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new AmbiguousLegacyState(
            reader.GetInt64(0),
            reader.GetBoolean(1),
            reader.GetBoolean(2),
            reader.GetBoolean(3));
    }

    private sealed record AmbiguousLegacyState(
        long WcsTaskCount,
        bool LegacyCompositeIndexExists,
        bool ExecutionChannelColumnExists,
        bool WorkAssignmentMigrationApplied);
}

public sealed class WmsWorkAssignmentPostgresFactAttribute : FactAttribute
{
    public WmsWorkAssignmentPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run WMS work-assignment PostgreSQL upgrade tests.";
        }
    }
}
