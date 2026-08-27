using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed partial class MesOeeDimensionSnapshotProviderTests
{
    private const string HistoricalDimensionSnapshotMigration =
        "20260825180417_AddProductionReportOeeHistoricalDimensionSnapshot";
    private const string PriorHistoricalDimensionSnapshotMigration =
        "20260825164053_AddMesOperationTaskRequiredSkillSnapshot";

    private static readonly string[] HistoricalDimensionSnapshotColumns =
    [
        "oee_line_code",
        "oee_shift_break_minutes",
        "oee_shift_code",
        "oee_shift_crosses_midnight",
        "oee_shift_ends_at",
        "oee_shift_paid_minutes",
        "oee_shift_starts_at",
        "oee_site_code",
        "oee_site_timezone",
        "oee_workshop_code"
    ];

    private static async Task ExecuteHistoricalDimensionSnapshotMigrationContractAsync()
    {
        await using var dbContext = new Infrastructure.ApplicationDbContext(
            MesPostgresLaneDatabase.CreateOptions(),
            new NoopMediator());
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        var migrator = dbContext.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(PriorHistoricalDimensionSnapshotMigration);
        await AssertHistoricalDimensionColumnsAsync(expected: false);
        var expected = new PriorProductionReport(
            Guid.Parse("aef861a1-b6e9-42d9-aae1-f135cd74f84b"),
            "org-oee-prior",
            "env-oee-prior",
            "PR-OEE-PRIOR-001",
            "WO-OEE-PRIOR-001",
            "OP-OEE-PRIOR-010",
            17.25m,
            1.5m,
            0.75m,
            true,
            DateTimeOffset.Parse("2026-08-25T17:59:59.999999Z"),
            "telemetry",
            3,
            "WC-PRIOR",
            "DEVICE-PRIOR",
            "EA",
            42.5m);
        await SeedPriorProductionReportAsync(expected);

        await migrator.MigrateAsync(HistoricalDimensionSnapshotMigration);
        await AssertUpgradedPriorProductionReportAsync(expected);

        await migrator.MigrateAsync(PriorHistoricalDimensionSnapshotMigration);
        await AssertHistoricalDimensionColumnsAsync(expected: false);
        await AssertPriorProductionReportAsync(expected);

        await migrator.MigrateAsync(HistoricalDimensionSnapshotMigration);
        await AssertUpgradedPriorProductionReportAsync(expected);
    }

    private static async Task SeedPriorProductionReportAsync(PriorProductionReport expected)
    {
        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var disableConstraints = connection.CreateCommand();
        disableConstraints.CommandText = "SET session_replication_role = replica;";
        await disableConstraints.ExecuteNonQueryAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO mes.production_reports
                    (id, organization_id, environment_id, report_no, work_order_id, operation_task_id,
                     good_quantity, scrap_quantity, rework_quantity, completes_operation, reported_at_utc,
                     source, material_movement_count, oee_work_center_id, oee_device_asset_id,
                     oee_uom_code, oee_theoretical_rate_per_hour)
                VALUES
                    (@id, @organizationId, @environmentId, @reportNo, @workOrderId, @operationTaskId,
                     @goodQuantity, @scrapQuantity, @reworkQuantity, @completesOperation, @reportedAtUtc,
                     @source, @materialMovementCount, @oeeWorkCenterId, @oeeDeviceAssetId,
                     @oeeUomCode, @oeeTheoreticalRatePerHour);
                """;
            command.Parameters.AddWithValue("id", expected.Id);
            command.Parameters.AddWithValue("organizationId", expected.OrganizationId);
            command.Parameters.AddWithValue("environmentId", expected.EnvironmentId);
            command.Parameters.AddWithValue("reportNo", expected.ReportNo);
            command.Parameters.AddWithValue("workOrderId", expected.WorkOrderId);
            command.Parameters.AddWithValue("operationTaskId", expected.OperationTaskId);
            command.Parameters.AddWithValue("goodQuantity", expected.GoodQuantity);
            command.Parameters.AddWithValue("scrapQuantity", expected.ScrapQuantity);
            command.Parameters.AddWithValue("reworkQuantity", expected.ReworkQuantity);
            command.Parameters.AddWithValue("completesOperation", expected.CompletesOperation);
            command.Parameters.AddWithValue("reportedAtUtc", expected.ReportedAtUtc);
            command.Parameters.AddWithValue("source", expected.Source);
            command.Parameters.AddWithValue("materialMovementCount", expected.MaterialMovementCount);
            command.Parameters.AddWithValue("oeeWorkCenterId", expected.OeeWorkCenterId);
            command.Parameters.AddWithValue("oeeDeviceAssetId", expected.OeeDeviceAssetId);
            command.Parameters.AddWithValue("oeeUomCode", expected.OeeUomCode);
            command.Parameters.AddWithValue("oeeTheoreticalRatePerHour", expected.OeeTheoreticalRatePerHour);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            await using var enableConstraints = connection.CreateCommand();
            enableConstraints.CommandText = "SET session_replication_role = origin;";
            await enableConstraints.ExecuteNonQueryAsync();
        }
    }

    private static async Task AssertUpgradedPriorProductionReportAsync(PriorProductionReport expected)
    {
        await AssertHistoricalDimensionColumnsAsync(expected: true);
        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, organization_id, environment_id, report_no, work_order_id, operation_task_id,
                   good_quantity, scrap_quantity, rework_quantity, completes_operation, reported_at_utc,
                   source, material_movement_count, oee_work_center_id, oee_device_asset_id,
                   oee_uom_code, oee_theoretical_rate_per_hour,
                   oee_line_code, oee_shift_break_minutes, oee_shift_code, oee_shift_crosses_midnight,
                   oee_shift_ends_at, oee_shift_paid_minutes, oee_shift_starts_at,
                   oee_site_code, oee_site_timezone, oee_workshop_code
            FROM mes.production_reports
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("id", expected.Id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        AssertPriorProductionReport(expected, reader);
        for (var ordinal = 17; ordinal < 27; ordinal++)
        {
            Assert.True(reader.IsDBNull(ordinal), $"Expected upgraded legacy column ordinal {ordinal} to remain NULL.");
        }
        Assert.False(await reader.ReadAsync());
    }

    private static async Task AssertPriorProductionReportAsync(PriorProductionReport expected)
    {
        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, organization_id, environment_id, report_no, work_order_id, operation_task_id,
                   good_quantity, scrap_quantity, rework_quantity, completes_operation, reported_at_utc,
                   source, material_movement_count, oee_work_center_id, oee_device_asset_id,
                   oee_uom_code, oee_theoretical_rate_per_hour
            FROM mes.production_reports
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("id", expected.Id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        AssertPriorProductionReport(expected, reader);
        Assert.False(await reader.ReadAsync());
    }

    private static void AssertPriorProductionReport(PriorProductionReport expected, NpgsqlDataReader reader)
    {
        Assert.Equal(expected.Id, reader.GetGuid(0));
        Assert.Equal(expected.OrganizationId, reader.GetString(1));
        Assert.Equal(expected.EnvironmentId, reader.GetString(2));
        Assert.Equal(expected.ReportNo, reader.GetString(3));
        Assert.Equal(expected.WorkOrderId, reader.GetString(4));
        Assert.Equal(expected.OperationTaskId, reader.GetString(5));
        Assert.Equal(expected.GoodQuantity, reader.GetDecimal(6));
        Assert.Equal(expected.ScrapQuantity, reader.GetDecimal(7));
        Assert.Equal(expected.ReworkQuantity, reader.GetDecimal(8));
        Assert.Equal(expected.CompletesOperation, reader.GetBoolean(9));
        Assert.Equal(expected.ReportedAtUtc, reader.GetFieldValue<DateTimeOffset>(10));
        Assert.Equal(expected.Source, reader.GetString(11));
        Assert.Equal(expected.MaterialMovementCount, reader.GetInt32(12));
        Assert.Equal(expected.OeeWorkCenterId, reader.GetString(13));
        Assert.Equal(expected.OeeDeviceAssetId, reader.GetString(14));
        Assert.Equal(expected.OeeUomCode, reader.GetString(15));
        Assert.Equal(expected.OeeTheoreticalRatePerHour, reader.GetDecimal(16));
    }

    private static async Task AssertHistoricalDimensionColumnsAsync(bool expected)
    {
        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT column_name, is_nullable, column_default
            FROM information_schema.columns
            WHERE table_schema = 'mes'
              AND table_name = 'production_reports'
              AND column_name = ANY (@columnNames)
            ORDER BY column_name;
            """;
        command.Parameters.AddWithValue("columnNames", HistoricalDimensionSnapshotColumns);
        await using var reader = await command.ExecuteReaderAsync();
        var actual = new Dictionary<string, (string IsNullable, string? DefaultValue)>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            actual.Add(reader.GetString(0), (reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        if (!expected)
        {
            Assert.Empty(actual);
            return;
        }

        Assert.Equal(
            HistoricalDimensionSnapshotColumns.Order(StringComparer.Ordinal),
            actual.Keys.Order(StringComparer.Ordinal));
        foreach (var columnName in HistoricalDimensionSnapshotColumns)
        {
            Assert.Equal(("YES", (string?)null), actual[columnName]);
        }
    }

    private sealed record PriorProductionReport(
        Guid Id,
        string OrganizationId,
        string EnvironmentId,
        string ReportNo,
        string WorkOrderId,
        string OperationTaskId,
        decimal GoodQuantity,
        decimal ScrapQuantity,
        decimal ReworkQuantity,
        bool CompletesOperation,
        DateTimeOffset ReportedAtUtc,
        string Source,
        int MaterialMovementCount,
        string OeeWorkCenterId,
        string OeeDeviceAssetId,
        string OeeUomCode,
        decimal OeeTheoreticalRatePerHour);
}
