using MediatR;
using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.BarcodeLabel.Domain;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TraceabilityAggregate;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure;
using Npgsql;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class BarcodeLabelPostgresProfileTests
{
    private const string PostgresConnectionStringEnvironmentVariable = "NERV_IIP_TEST_POSTGRES";

    [RealPostgresFact]
    public async Task Postgres_unique_conflicts_are_mapped_for_scan_natural_key_and_epcis_event()
    {
        await ResetBarcodeLabelSchemaAsync();

        await using (var dbContext = CreatePostgresDbContext(LaneConnectionString))
        {
            AssertUsesGovernedDatabase(dbContext);
            await dbContext.Database.MigrateAsync();
        }

        await using (var dbContext = CreatePostgresDbContext(LaneConnectionString))
        {
            dbContext.ScanRecords.Add(NewPlainInventoryScan("idem-postgres-natural-001"));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreatePostgresDbContext(LaneConnectionString))
        {
            dbContext.ScanRecords.Add(NewPlainInventoryScan("idem-postgres-natural-002"));

            var exception = await Assert.ThrowsAsync<KnownException>(() => dbContext.SaveChangesAsync());

            Assert.Contains("accepted barcode scan natural key", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        await using (var dbContext = CreatePostgresDbContext(LaneConnectionString))
        {
            var epcisEvent = NewEpcisObjectEvent("idem-postgres-epcis-001");
            dbContext.EpcisEvents.Add(epcisEvent);
            dbContext.Entry(epcisEvent).Property(nameof(EpcisEvent.ScanRecordId)).CurrentValue = null;
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreatePostgresDbContext(LaneConnectionString))
        {
            var epcisEvent = NewEpcisObjectEvent("idem-postgres-epcis-002");
            dbContext.EpcisEvents.Add(epcisEvent);
            dbContext.Entry(epcisEvent).Property(nameof(EpcisEvent.ScanRecordId)).CurrentValue = null;

            var exception = await Assert.ThrowsAsync<KnownException>(() => dbContext.SaveChangesAsync());

            Assert.Contains("Duplicate BarcodeLabel EPCIS event", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static ApplicationDbContext CreatePostgresDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", BarcodeLabelFacts.Schema))
            .Options;

        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static ScanRecord NewPlainInventoryScan(string idempotencyKey)
    {
        return ScanRecord.Record(
            "org-001",
            "env-dev",
            "PDA-01",
            "PLAIN-POSTGRES-NATURAL-001",
            "inventory.receipt",
            "ASN-POSTGRES-NATURAL",
            idempotencyKey,
            "accepted",
            null,
            "SKU-FG-1000",
            "EA",
            "SITE-01",
            "STAGE-01",
            "qualified",
            "owned",
            null,
            2);
    }

    private static EpcisEvent NewEpcisObjectEvent(string idempotencyKey)
    {
        return EpcisEvent.ObjectEvent(
            "org-001",
            "env-dev",
            ScanRecord.Record(
                "org-001",
                "env-dev",
                "PDA-01",
                "(01)09506000134352(10)LOT-PG\u001D(21)SN-PG-0001",
                "inventory.receipt",
                "ASN-POSTGRES-EPCIS",
                idempotencyKey,
                "accepted",
                null,
                "SKU-FG-1000",
                "EA",
                "SITE-01",
                "STAGE-01",
                "qualified",
                "owned",
                null,
                2));
    }

    // NERV-688 拆解③：BarcodeLabel 的 PostgreSQL 用例使用 lane runner 注入的成员数据库
    // （NERV_IIP_TEST_POSTGRES），不再自建内层数据库——内层数据库外层既读不到失败诊断，也证明不了清理。
    private static string LaneConnectionString =>
        Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)
        ?? throw new InvalidOperationException(
            $"{PostgresConnectionStringEnvironmentVariable} must be set for BarcodeLabel PostgreSQL profile tests.");

    private static async Task ResetBarcodeLabelSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(LaneConnectionString);
        await connection.OpenAsync();
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(BarcodeLabelFacts.Schema);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS {quotedSchema} CASCADE";
        await command.ExecuteNonQueryAsync();
    }

    private static void AssertUsesGovernedDatabase(ApplicationDbContext dbContext)
    {
        var governed = new NpgsqlConnectionStringBuilder(LaneConnectionString);
        Assert.Equal(governed.Database, dbContext.Database.GetDbConnection().Database);
    }

    private sealed class RealPostgresFactAttribute : FactAttribute
    {
        public RealPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)))
            {
                Skip = $"Set {PostgresConnectionStringEnvironmentVariable} to run this real PostgreSQL BarcodeLabel profile test.";
            }
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            _ = notification;
            _ = cancellationToken;
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            _ = notification;
            _ = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("PostgreSQL profile mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("PostgreSQL profile mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("PostgreSQL profile mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("PostgreSQL profile mediator cannot stream requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("PostgreSQL profile mediator cannot stream requests.");
        }
    }
}
