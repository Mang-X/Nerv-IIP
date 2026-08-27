using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.OeeProductionFactAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Npgsql;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

[Collection(IndustrialTelemetryPostgresLaneDatabase.CollectionName)]
public sealed class IndustrialTelemetryOeeMigrationRollbackPostgresTests
{
    private const string PriorMigration = "20260718040416_HardenConnectorTagBindingConstraints";

    [RealPostgresFact]
    public async Task Oee_historical_dimension_migration_backfills_old_reported_time_and_round_trips_on_postgres()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                IndustrialTelemetryPostgresLaneDatabase.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "industrial_telemetry"))
            .Options;
        await using var dbContext = new ApplicationDbContext(options, new NoopMediator());
        IndustrialTelemetryPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(PriorMigration);

        var historicalRows = new[]
        {
            new LegacyFact(
                Guid.CreateVersion7(), "org-history-a", "env-history-a", "PR-HISTORY-ORDINARY",
                "WC-HISTORY-A", "DEV-HISTORY-A", DateTimeOffset.Parse("2024-02-29T12:34:56.789012Z")),
            new LegacyFact(
                Guid.CreateVersion7(), "org-history-b", "env-history-b", "PR-HISTORY-UTC-BOUNDARY",
                "WC-HISTORY-B", "DEV-HISTORY-B", DateTimeOffset.Parse("2025-01-01T00:00:00Z"))
        };

        await AssertColumnPresenceAsync("aggregation_occurred_at_utc", expected: false);
        await InsertLegacyFactsAsync(historicalRows);

        await migrator.MigrateAsync();
        await AssertUpgradedFactsAsync(historicalRows);

        await migrator.MigrateAsync(PriorMigration);
        await AssertColumnPresenceAsync("aggregation_occurred_at_utc", expected: false);
        await AssertLegacyFactsAsync(historicalRows);

        await migrator.MigrateAsync();
        await AssertUpgradedFactsAsync(historicalRows);
    }

    [RealPostgresFact]
    public async Task Oee_historical_dimension_migration_removes_unrepresentable_degraded_facts_before_rollback_on_postgres()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                IndustrialTelemetryPostgresLaneDatabase.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "industrial_telemetry"))
            .Options;
        await using var dbContext = new ApplicationDbContext(options, new NoopMediator());
        IndustrialTelemetryPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();

        dbContext.OeeProductionFacts.AddRange(
            OeeProductionFact.Project(
                "org-001", "env-dev", "PR-REPRESENTABLE", "WC-01", "DEV-01",
                10m, 0m, 0m, "PCS", 10m, DateTimeOffset.Parse("2026-07-10T08:00:00Z")),
            OeeProductionFact.Project(
                "org-001", "env-dev", "PR-DEGRADED", null, null,
                5m, 0m, 0m, "PCS", null, DateTimeOffset.Parse("2026-07-10T08:05:00Z")));
        await dbContext.SaveChangesAsync();

        await dbContext.GetService<IMigrator>().MigrateAsync(PriorMigration);

        await using var connection = new NpgsqlConnection(IndustrialTelemetryPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                COUNT(*) FILTER (WHERE source_report_no = 'PR-REPRESENTABLE'),
                COUNT(*) FILTER (WHERE source_report_no = 'PR-DEGRADED'),
                bool_and(work_center_id IS NOT NULL AND device_asset_id IS NOT NULL),
                (
                    SELECT is_nullable = 'NO'
                    FROM information_schema.columns
                    WHERE table_schema = 'industrial_telemetry'
                      AND table_name = 'oee_production_facts'
                      AND column_name = 'work_center_id'),
                (
                    SELECT is_nullable = 'NO'
                    FROM information_schema.columns
                    WHERE table_schema = 'industrial_telemetry'
                      AND table_name = 'oee_production_facts'
                      AND column_name = 'device_asset_id'),
                NOT EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'industrial_telemetry'
                      AND table_name = 'oee_production_facts'
                      AND column_name = 'site_code')
            FROM industrial_telemetry.oee_production_facts;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(0L, reader.GetInt64(1));
        Assert.True(reader.GetBoolean(2));
        Assert.True(reader.GetBoolean(3));
        Assert.True(reader.GetBoolean(4));
        Assert.True(reader.GetBoolean(5));
    }

    private static async Task InsertLegacyFactsAsync(IEnumerable<LegacyFact> facts)
    {
        await using var connection = new NpgsqlConnection(IndustrialTelemetryPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        foreach (var fact in facts)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO industrial_telemetry.oee_production_facts
                    (id, organization_id, environment_id, source_report_no, work_center_id, device_asset_id,
                     good_quantity, scrap_quantity, rework_quantity, uom_code, theoretical_rate_per_hour, reported_at_utc)
                VALUES
                    (@id, @organizationId, @environmentId, @sourceReportNo, @workCenterId, @deviceAssetId,
                     10.000000, 1.000000, 2.000000, 'PCS', 60.000000, @reportedAtUtc);
                """;
            command.Parameters.AddWithValue("id", fact.Id);
            command.Parameters.AddWithValue("organizationId", fact.OrganizationId);
            command.Parameters.AddWithValue("environmentId", fact.EnvironmentId);
            command.Parameters.AddWithValue("sourceReportNo", fact.SourceReportNo);
            command.Parameters.AddWithValue("workCenterId", fact.WorkCenterId);
            command.Parameters.AddWithValue("deviceAssetId", fact.DeviceAssetId);
            command.Parameters.AddWithValue("reportedAtUtc", fact.ReportedAtUtc);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
    }

    private static async Task AssertUpgradedFactsAsync(IReadOnlyCollection<LegacyFact> expectedFacts)
    {
        await AssertColumnPresenceAsync("aggregation_occurred_at_utc", expected: true);
        await using var connection = new NpgsqlConnection(IndustrialTelemetryPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, organization_id, environment_id, source_report_no, work_center_id, device_asset_id,
                   reported_at_utc, aggregation_occurred_at_utc,
                   site_code IS NULL AND workshop_code IS NULL AND line_code IS NULL AND shift_code IS NULL
                       AND site_timezone IS NULL AND business_date IS NULL
                       AND day_bucket_start_utc IS NULL AND day_bucket_end_utc IS NULL
                       AND shift_business_date IS NULL AND shift_bucket_start_utc IS NULL AND shift_bucket_end_utc IS NULL
            FROM industrial_telemetry.oee_production_facts
            ORDER BY source_report_no;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var actualFacts = new List<UpgradedFact>();
        while (await reader.ReadAsync())
        {
            actualFacts.Add(new UpgradedFact(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6),
                reader.GetFieldValue<DateTimeOffset>(7), reader.GetBoolean(8)));
        }

        Assert.Equal(expectedFacts.Count, actualFacts.Count);
        foreach (var expected in expectedFacts.OrderBy(fact => fact.SourceReportNo, StringComparer.Ordinal))
        {
            var actual = Assert.Single(actualFacts, fact => fact.SourceReportNo == expected.SourceReportNo);
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.OrganizationId, actual.OrganizationId);
            Assert.Equal(expected.EnvironmentId, actual.EnvironmentId);
            Assert.Equal(expected.WorkCenterId, actual.WorkCenterId);
            Assert.Equal(expected.DeviceAssetId, actual.DeviceAssetId);
            Assert.Equal(expected.ReportedAtUtc, actual.ReportedAtUtc);
            Assert.Equal(expected.ReportedAtUtc, actual.AggregationOccurredAtUtc);
            Assert.True(actual.HasNoInventedHistoricalDimensions);
        }
    }

    private static async Task AssertLegacyFactsAsync(IReadOnlyCollection<LegacyFact> expectedFacts)
    {
        await using var connection = new NpgsqlConnection(IndustrialTelemetryPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, organization_id, environment_id, source_report_no, work_center_id, device_asset_id, reported_at_utc
            FROM industrial_telemetry.oee_production_facts
            ORDER BY source_report_no;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var actualFacts = new List<LegacyFact>();
        while (await reader.ReadAsync())
        {
            actualFacts.Add(new LegacyFact(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6)));
        }

        Assert.Equal(
            expectedFacts.OrderBy(fact => fact.SourceReportNo, StringComparer.Ordinal),
            actualFacts);
    }

    private static async Task AssertColumnPresenceAsync(string columnName, bool expected)
    {
        await using var connection = new NpgsqlConnection(IndustrialTelemetryPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'industrial_telemetry'
                  AND table_name = 'oee_production_facts'
                  AND column_name = @columnName);
            """;
        command.Parameters.AddWithValue("columnName", columnName);
        Assert.Equal(expected, (bool)(await command.ExecuteScalarAsync())!);
    }

    private sealed record LegacyFact(
        Guid Id,
        string OrganizationId,
        string EnvironmentId,
        string SourceReportNo,
        string WorkCenterId,
        string DeviceAssetId,
        DateTimeOffset ReportedAtUtc);

    private sealed record UpgradedFact(
        Guid Id,
        string OrganizationId,
        string EnvironmentId,
        string SourceReportNo,
        string WorkCenterId,
        string DeviceAssetId,
        DateTimeOffset ReportedAtUtc,
        DateTimeOffset AggregationOccurredAtUtc,
        bool HasNoInventedHistoricalDimensions);

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
