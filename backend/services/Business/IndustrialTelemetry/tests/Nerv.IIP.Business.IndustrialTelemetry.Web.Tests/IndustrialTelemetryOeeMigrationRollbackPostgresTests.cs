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

        await dbContext.GetService<IMigrator>().MigrateAsync(
            "20260718040416_HardenConnectorTagBindingConstraints");

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
