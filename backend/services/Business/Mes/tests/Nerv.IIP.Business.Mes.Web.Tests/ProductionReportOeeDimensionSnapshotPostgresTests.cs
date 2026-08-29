using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Npgsql;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class ProductionReportOeeDimensionSnapshotPostgresTests
{
    private const string PreviousMigration = "20260827093753_AddMesProductionReportOperator";
    private const string TargetMigration = "20260827160719_AddMesProductionReportOeeDimensionSnapshot";
    private const string LatestMigration = "20260828045610_AddMesBillableMachineTimeFacts";
    private static readonly string[] SnapshotColumns =
    [
        "oee_dimension_degraded_reason",
        "oee_dimension_resolution_status",
        "oee_line_code",
        "oee_shift_break_minutes",
        "oee_shift_code",
        "oee_shift_crosses_midnight",
        "oee_shift_ends_at",
        "oee_shift_paid_minutes",
        "oee_shift_starts_at",
        "oee_site_code",
        "oee_site_timezone",
        "oee_workshop_code",
    ];

    // Contract: ProviderBehavior + Regression. Authority: Issue #2602 acceptance and the target migration.
    // A fabricated default, wrong column, lost legacy value, or broken Down/second Up must fail this PostgreSQL proof.
    [MesRealPostgresFact]
    public async Task Prior_schema_report_survives_up_down_up_with_nullable_snapshot_columns_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        await using var context = new ApplicationDbContext(options, new NoopMediator());
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(context);
        var migrator = context.GetService<IMigrator>();
        // Seed through the current model before exercising the OEE migration's own down/up boundary.
        await migrator.MigrateAsync(LatestMigration);

        var reportedAtUtc = DateTimeOffset.Parse("2026-08-27T23:30:00Z");
        context.WorkOrders.Add(WorkOrder.Create(
            "org-001", "env-dev", "WO-OEE-PG-001", "SKU-001", "PV-001", 10m, 10,
            reportedAtUtc.AddHours(8)));
        context.OperationTasks.Add(OperationTask.Create(
            "org-001", "env-dev", "WO-OEE-PG-001", "OP-OEE-PG-10",
            OperationTaskLifecycleStatus.InProgress, 10, "WC-LEGACY", [],
            reportedAtUtc.AddHours(-1), TimeSpan.FromHours(1), reportedAtUtc.AddHours(-1), null));
        context.ProductionReports.Add(ProductionReport.Record(
            "org-001", "env-dev", "PR-TEMPLATE", "WO-OEE-PG-001", "OP-OEE-PG-10",
            2m, 1m, false, reportedAtUtc,
            reworkQuantity: 0.5m,
            scrapReasonCode: "SCRAP-01",
            defectRecordNo: "DEF-01",
            producedLotNo: "LOT-01",
            serialNo: "SN-01",
            source: ProductionReport.ManualSource,
            materialMovementCount: 2,
            reportedBy: "user:legacy"));
        await context.SaveChangesAsync();

        await migrator.MigrateAsync(PreviousMigration);
        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await CloneLegacyReportAtPriorSchemaAsync(connection);
        var priorRow = await ReadReportJsonAsync(connection, "PR-LEGACY");

        await migrator.MigrateAsync(TargetMigration);
        var firstUpRow = await ReadReportJsonWithoutSnapshotAsync(connection, "PR-LEGACY");
        Assert.Equal(priorRow, firstUpRow);
        await AssertSnapshotColumnsAsync(connection);
        await AssertLegacySnapshotIsNullAsync(connection);
        await AssertActualTimeSettlementSchemaStillExistsAsync(connection);

        await migrator.MigrateAsync(PreviousMigration);
        Assert.Equal(priorRow, await ReadReportJsonAsync(connection, "PR-LEGACY"));

        await migrator.MigrateAsync(TargetMigration);
        Assert.Equal(priorRow, await ReadReportJsonWithoutSnapshotAsync(connection, "PR-LEGACY"));
        await AssertLegacySnapshotIsNullAsync(connection);
        await AssertActualTimeSettlementSchemaStillExistsAsync(connection);
    }

    private static async Task CloneLegacyReportAtPriorSchemaAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mes.production_reports
            SELECT *
            FROM jsonb_populate_record(
                NULL::mes.production_reports,
                (
                    SELECT to_jsonb(template) || jsonb_build_object(
                        'id', @legacy_id,
                        'report_no', 'PR-LEGACY')
                    FROM mes.production_reports AS template
                    WHERE report_no = 'PR-TEMPLATE'
                ));
            DELETE FROM mes.production_reports WHERE report_no = 'PR-TEMPLATE';
            """;
        command.Parameters.AddWithValue("legacy_id", Guid.Parse("019c9c71-b054-7d0e-a804-9ab0bb4f8e32"));
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
    }

    private static async Task<string> ReadReportJsonAsync(NpgsqlConnection connection, string reportNo)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT to_jsonb(report)::text FROM mes.production_reports AS report WHERE report_no = @report_no";
        command.Parameters.AddWithValue("report_no", reportNo);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> ReadReportJsonWithoutSnapshotAsync(NpgsqlConnection connection, string reportNo)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT (to_jsonb(report) - @columns)::text FROM mes.production_reports AS report WHERE report_no = @report_no";
        command.Parameters.AddWithValue("columns", SnapshotColumns);
        command.Parameters.AddWithValue("report_no", reportNo);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task AssertSnapshotColumnsAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT columns.column_name, columns.is_nullable, columns.column_default,
                   col_description('mes.production_reports'::regclass, attributes.attnum)
            FROM information_schema.columns AS columns
            JOIN pg_attribute AS attributes
              ON attributes.attrelid = 'mes.production_reports'::regclass
             AND attributes.attname = columns.column_name
            WHERE columns.table_schema = 'mes'
              AND columns.table_name = 'production_reports'
              AND columns.column_name = ANY(@columns)
            ORDER BY columns.column_name
            """;
        command.Parameters.AddWithValue("columns", SnapshotColumns);
        await using var reader = await command.ExecuteReaderAsync();
        var observed = new List<(string Name, string Nullable, object Default, string Comment)>();
        while (await reader.ReadAsync())
        {
            observed.Add((reader.GetString(0), reader.GetString(1), reader.GetValue(2), reader.GetString(3)));
        }

        Assert.Equal(SnapshotColumns.Order(StringComparer.Ordinal), observed.Select(x => x.Name));
        Assert.All(observed, column =>
        {
            Assert.Equal("YES", column.Nullable);
            Assert.Equal(DBNull.Value, column.Default);
            Assert.False(string.IsNullOrWhiteSpace(column.Comment));
        });
    }

    private static async Task AssertLegacySnapshotIsNullAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT count(*)
            FROM mes.production_reports
            WHERE report_no = 'PR-LEGACY'
              AND oee_dimension_degraded_reason IS NULL
              AND oee_dimension_resolution_status IS NULL
              AND oee_line_code IS NULL
              AND oee_shift_break_minutes IS NULL
              AND oee_shift_code IS NULL
              AND oee_shift_crosses_midnight IS NULL
              AND oee_shift_ends_at IS NULL
              AND oee_shift_paid_minutes IS NULL
              AND oee_shift_starts_at IS NULL
              AND oee_site_code IS NULL
              AND oee_site_timezone IS NULL
              AND oee_workshop_code IS NULL
            """;
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    private static async Task AssertActualTimeSettlementSchemaStillExistsAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT count(*)
            FROM information_schema.columns
            WHERE table_schema = 'mes'
              AND ((table_name = 'operation_tasks' AND column_name = 'actual_time_settlement_revision')
                OR (table_name = 'operation_actual_time_settlements' AND column_name = 'revision')
                OR (table_name = 'operation_actual_time_settlement_reports' AND column_name = 'report_no'))
            """;
        Assert.Equal(3L, (long)(await command.ExecuteScalarAsync())!);
    }
}
