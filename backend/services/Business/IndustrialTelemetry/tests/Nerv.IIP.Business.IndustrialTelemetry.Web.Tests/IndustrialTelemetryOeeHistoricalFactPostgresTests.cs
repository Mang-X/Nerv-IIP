using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.OeeProductionFactAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing;
using Npgsql;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

[Collection(IndustrialTelemetryPostgresLaneDatabase.CollectionName)]
public sealed class IndustrialTelemetryOeeHistoricalFactPostgresTests
{
    private const string PreviousMigration = "20260718040416_HardenConnectorTagBindingConstraints";
    private const string TargetMigration = "20260827211850_AddOeeHistoricalProductionFacts";
    private static readonly string[] HistoricalColumns =
    [
        "aggregation_occurred_at_utc",
        "business_date",
        "historical_dimension_status",
        "line_code",
        "reversed_report_no",
        "shift_break_minutes",
        "shift_bucket_end_utc",
        "shift_bucket_start_utc",
        "shift_code",
        "shift_crosses_midnight",
        "shift_ends_at",
        "shift_paid_minutes",
        "shift_starts_at",
        "site_code",
        "site_timezone",
        "workshop_code",
    ];

    // Contract: ProviderBehavior + Regression. Authority: Issue #2604 migration acceptance.
    [RealPostgresFact]
    public async Task Prior_schema_fact_survives_up_down_up_without_fabricated_historical_dimensions_on_postgres()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        await using var context = CreateLaneDbContext();
        IndustrialTelemetryPostgresLaneDatabase.AssertUsesGovernedDatabase(context);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        var reportedAtUtc = DateTimeOffset.Parse("2026-08-14T17:30:45.123456Z");
        await using var connection = new NpgsqlConnection(IndustrialTelemetryPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await InsertPriorSchemaFactAsync(connection, reportedAtUtc);
        var priorRow = await ReadCoreFactJsonAsync(connection, "PRPT-OEE-LEGACY-001", subtractHistoricalColumns: false);

        await migrator.MigrateAsync(TargetMigration);
        Assert.Equal(priorRow, await ReadCoreFactJsonAsync(connection, "PRPT-OEE-LEGACY-001", subtractHistoricalColumns: true));
        await AssertHistoricalColumnContractAsync(connection);
        await AssertLegacyFactBackfillAsync(connection, "PRPT-OEE-LEGACY-001", reportedAtUtc);

        context.ChangeTracker.Clear();
        var resolvedAtUtc = DateTimeOffset.Parse("2026-08-15T01:00:00Z");
        var resolved = OeeProductionFact.Project(
            "org-001",
            "env-dev",
            "PRPT-OEE-RESOLVED-001",
            "WC-PACK-01",
            "DEV-PACK-01",
            10m,
            0m,
            0m,
            "PCS",
            100m,
            resolvedAtUtc,
            ResolvedSnapshot());
        context.OeeProductionFacts.AddRange(
            resolved,
            OeeProductionFact.Project(
                "org-001",
                "env-dev",
                "PRPT-OEE-DEGRADED-001",
                "WC-PACK-01",
                "DEV-PACK-01",
                5m,
                0m,
                0m,
                "PCS",
                100m,
                resolvedAtUtc,
                OeeHistoricalDimensionSnapshot.LegacyUnresolved with
                {
                    Status = OeeHistoricalDimensionStatus.MissingTimezone
                }),
            resolved.ProjectReversal(
                "PRPT-OEE-REVERSAL-001",
                -10m,
                0m,
                0m,
                resolvedAtUtc.AddDays(2)),
            resolved.ProjectReversal(
                "PRPT-OEE-REVERSAL-SAME-TIME-001",
                -10m,
                0m,
                0m,
                resolvedAtUtc));
        await context.SaveChangesAsync();

        await migrator.MigrateAsync(PreviousMigration);
        Assert.Equal(priorRow, await ReadCoreFactJsonAsync(connection, "PRPT-OEE-LEGACY-001", subtractHistoricalColumns: false));
        Assert.Equal(
            ["PRPT-OEE-LEGACY-001", "PRPT-OEE-RESOLVED-001"],
            await ReadReportNumbersAsync(connection));

        await migrator.MigrateAsync(TargetMigration);
        Assert.Equal(priorRow, await ReadCoreFactJsonAsync(connection, "PRPT-OEE-LEGACY-001", subtractHistoricalColumns: true));
        await AssertLegacyFactBackfillAsync(connection, "PRPT-OEE-LEGACY-001", reportedAtUtc);
        await AssertLegacyFactBackfillAsync(connection, "PRPT-OEE-RESOLVED-001", resolvedAtUtc);
    }

    // Contract: ProviderBehavior + DomainInvariant. Authority: Issue #2604 idempotency and scope acceptance.
    [RealPostgresFact]
    public async Task Concurrent_projection_keeps_one_source_report_per_scope_on_postgres()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        await using (var setup = CreateLaneDbContext())
        {
            IndustrialTelemetryPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();
        }

        var barrier = new TwoPartySaveBarrierInterceptor();
        await using var firstContext = CreateLaneDbContext(barrier);
        await using var secondContext = CreateLaneDbContext(barrier);
        var integrationEvent = CreateHistoricalEvent("env-dev");
        var firstHandler = new ProductionReportOeeProjectionHandler(firstContext, new InMemoryIntegrationEventDeadLetterStore());
        var secondHandler = new ProductionReportOeeProjectionHandler(secondContext, new InMemoryIntegrationEventDeadLetterStore());
        var first = firstHandler.HandleAsync(integrationEvent, CancellationToken.None);
        var second = secondHandler.HandleAsync(integrationEvent, CancellationToken.None);

        await TestTimeout.RunAsync(
            operation: "two concurrent OEE source-report projections reach one PostgreSQL unique key",
            action: async cancellationToken =>
            {
                await Task.WhenAll(first.WaitAsync(cancellationToken), second.WaitAsync(cancellationToken));
            },
            timeout: TimeSpan.FromSeconds(10),
            sensitiveValues: [IndustrialTelemetryPostgresLaneDatabase.ConnectionString]);

        await using var otherScopeContext = CreateLaneDbContext();
        await new ProductionReportOeeProjectionHandler(otherScopeContext, new InMemoryIntegrationEventDeadLetterStore())
            .HandleAsync(CreateHistoricalEvent("env-other"), CancellationToken.None);

        await using var assertionContext = CreateLaneDbContext();
        var facts = await assertionContext.OeeProductionFacts
            .OrderBy(x => x.EnvironmentId)
            .ToArrayAsync();
        Assert.Equal(2, facts.Length);
        Assert.Equal(["env-dev", "env-other"], facts.Select(x => x.EnvironmentId));
        Assert.All(facts, fact => Assert.Equal("PRPT-OEE-CONCURRENT-001", fact.SourceReportNo));
    }

    private static ApplicationDbContext CreateLaneDbContext(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                IndustrialTelemetryPostgresLaneDatabase.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "industrial_telemetry"))
            .AddInterceptors(interceptors)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static OeeHistoricalDimensionSnapshot ResolvedSnapshot() => new(
        "SITE-SH",
        "WS-ASSEMBLY",
        "LINE-A",
        "NIGHT",
        "Asia/Shanghai",
        new TimeOnly(22, 0),
        new TimeOnly(6, 0),
        true,
        480,
        30,
        new DateOnly(2026, 8, 14),
        DateTimeOffset.Parse("2026-08-14T14:00:00Z"),
        DateTimeOffset.Parse("2026-08-14T22:00:00Z"),
        OeeHistoricalDimensionStatus.Resolved);

    private static ProductionReportRecordedIntegrationEvent CreateHistoricalEvent(string environmentId)
    {
        var reportedAtUtc = DateTimeOffset.Parse("2026-08-14T17:30:00Z");
        return new ProductionReportRecordedIntegrationEvent(
            $"evt-oee-concurrent-{environmentId}",
            MesIntegrationEventTypes.ProductionReportRecorded,
            MesIntegrationEventVersions.V1,
            reportedAtUtc,
            MesIntegrationEventSources.BusinessMes,
            "PRPT-OEE-CONCURRENT-001",
            "PRPT-OEE-CONCURRENT-001",
            "org-001",
            environmentId,
            "system:mes",
            $"production-report-recorded:org-001:{environmentId}:PRPT-OEE-CONCURRENT-001",
            new ProductionReportRecordedPayload(
                "PRPT-OEE-CONCURRENT-001",
                "WO-001",
                "OP-10",
                "WC-PACK-01",
                "DEV-PACK-01",
                10m,
                0m,
                0m,
                "PCS",
                100m,
                reportedAtUtc,
                false,
                SiteCode: "SITE-SH",
                WorkshopCode: "WS-ASSEMBLY",
                LineCode: "LINE-A",
                ShiftCode: "NIGHT",
                SiteTimezone: "Asia/Shanghai",
                ShiftStartsAt: new TimeOnly(22, 0),
                ShiftEndsAt: new TimeOnly(6, 0),
                ShiftCrossesMidnight: true,
                ShiftPaidMinutes: 480,
                ShiftBreakMinutes: 30));
    }

    private static async Task InsertPriorSchemaFactAsync(NpgsqlConnection connection, DateTimeOffset reportedAtUtc)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO industrial_telemetry.oee_production_facts
                (id, organization_id, environment_id, source_report_no, work_center_id, device_asset_id,
                 good_quantity, scrap_quantity, rework_quantity, uom_code, theoretical_rate_per_hour, reported_at_utc)
            VALUES
                (@id, 'org-001', 'env-dev', 'PRPT-OEE-LEGACY-001', 'WC-LEGACY-01', 'DEV-LEGACY-01',
                 12.345678, 1.250000, 0.500000, 'PCS', 42.125000, @reported_at_utc);
            """;
        command.Parameters.AddWithValue("id", Guid.Parse("019c9cf4-45a2-7e2b-a102-0123456789ab"));
        command.Parameters.AddWithValue("reported_at_utc", reportedAtUtc);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<string> ReadCoreFactJsonAsync(
        NpgsqlConnection connection,
        string reportNo,
        bool subtractHistoricalColumns)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = subtractHistoricalColumns
            ? "SELECT (to_jsonb(fact) - @columns)::text FROM industrial_telemetry.oee_production_facts AS fact WHERE source_report_no = @report_no"
            : "SELECT to_jsonb(fact)::text FROM industrial_telemetry.oee_production_facts AS fact WHERE source_report_no = @report_no";
        if (subtractHistoricalColumns)
        {
            command.Parameters.AddWithValue("columns", HistoricalColumns);
        }

        command.Parameters.AddWithValue("report_no", reportNo);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task AssertHistoricalColumnContractAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT columns.column_name, columns.is_nullable, columns.column_default,
                   col_description('industrial_telemetry.oee_production_facts'::regclass, attributes.attnum)
            FROM information_schema.columns AS columns
            JOIN pg_attribute AS attributes
              ON attributes.attrelid = 'industrial_telemetry.oee_production_facts'::regclass
             AND attributes.attname = columns.column_name
            WHERE columns.table_schema = 'industrial_telemetry'
              AND columns.table_name = 'oee_production_facts'
              AND columns.column_name = ANY(@columns)
            ORDER BY columns.column_name
            """;
        command.Parameters.AddWithValue("columns", HistoricalColumns);
        await using var reader = await command.ExecuteReaderAsync();
        var observed = new List<(string Name, string Nullable, object Default, string Comment)>();
        while (await reader.ReadAsync())
        {
            observed.Add((reader.GetString(0), reader.GetString(1), reader.GetValue(2), reader.GetString(3)));
        }

        Assert.Equal(HistoricalColumns.Order(StringComparer.Ordinal), observed.Select(x => x.Name));
        Assert.All(observed, column =>
        {
            var expectedNullability = column.Name is "aggregation_occurred_at_utc" or "historical_dimension_status"
                ? "NO"
                : "YES";
            Assert.Equal(expectedNullability, column.Nullable);
            Assert.Equal(DBNull.Value, column.Default);
            Assert.False(string.IsNullOrWhiteSpace(column.Comment));
        });
    }

    private static async Task AssertLegacyFactBackfillAsync(
        NpgsqlConnection connection,
        string reportNo,
        DateTimeOffset reportedAtUtc)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT aggregation_occurred_at_utc, historical_dimension_status,
                   business_date, line_code, reversed_report_no, shift_break_minutes,
                   shift_bucket_end_utc, shift_bucket_start_utc, shift_code, shift_crosses_midnight,
                   shift_ends_at, shift_paid_minutes, shift_starts_at, site_code, site_timezone, workshop_code
            FROM industrial_telemetry.oee_production_facts
            WHERE source_report_no = @report_no
            """;
        command.Parameters.AddWithValue("report_no", reportNo);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(reportedAtUtc, reader.GetFieldValue<DateTimeOffset>(0));
        Assert.Equal(nameof(OeeHistoricalDimensionStatus.LegacyUnresolved), reader.GetString(1));
        for (var index = 2; index < reader.FieldCount; index++)
        {
            Assert.True(reader.IsDBNull(index), $"Expected {reader.GetName(index)} to remain null for a prior-schema fact.");
        }
    }

    private static async Task<string[]> ReadReportNumbersAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT source_report_no FROM industrial_telemetry.oee_production_facts ORDER BY source_report_no";
        await using var reader = await command.ExecuteReaderAsync();
        var reportNumbers = new List<string>();
        while (await reader.ReadAsync())
        {
            reportNumbers.Add(reader.GetString(0));
        }

        return [.. reportNumbers];
    }

    private sealed class TwoPartySaveBarrierInterceptor : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref arrivals) == 2)
            {
                release.TrySetResult();
            }

            await release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

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

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
