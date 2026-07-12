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
        var postgresConnectionString = Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)!;
        await using var database = await TemporaryPostgresDatabase.CreateAsync(postgresConnectionString, "barcode_unique");

        await using (var dbContext = CreatePostgresDbContext(database.ConnectionString))
        {
            await dbContext.Database.MigrateAsync();
        }

        await using (var dbContext = CreatePostgresDbContext(database.ConnectionString))
        {
            dbContext.ScanRecords.Add(NewPlainInventoryScan("idem-postgres-natural-001"));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreatePostgresDbContext(database.ConnectionString))
        {
            dbContext.ScanRecords.Add(NewPlainInventoryScan("idem-postgres-natural-002"));

            var exception = await Assert.ThrowsAsync<KnownException>(() => dbContext.SaveChangesAsync());

            Assert.Contains("accepted barcode scan natural key", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        await using (var dbContext = CreatePostgresDbContext(database.ConnectionString))
        {
            var epcisEvent = NewEpcisObjectEvent("idem-postgres-epcis-001");
            dbContext.EpcisEvents.Add(epcisEvent);
            dbContext.Entry(epcisEvent).Property(nameof(EpcisEvent.ScanRecordId)).CurrentValue = null;
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreatePostgresDbContext(database.ConnectionString))
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

    private sealed class TemporaryPostgresDatabase : IAsyncDisposable
    {
        private TemporaryPostgresDatabase(string adminConnectionString, string connectionString, string databaseName)
        {
            AdminConnectionString = adminConnectionString;
            ConnectionString = connectionString;
            DatabaseName = databaseName;
        }

        public string ConnectionString { get; }

        private string AdminConnectionString { get; }

        private string DatabaseName { get; }

        public static async Task<TemporaryPostgresDatabase> CreateAsync(string baseConnectionString, string prefix)
        {
            var baseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString);
            var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = string.IsNullOrWhiteSpace(baseBuilder.Database) ? "postgres" : baseBuilder.Database
            };
            var databaseName = $"{prefix}_{Guid.NewGuid():N}";
            var databaseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = databaseName
            };

            await using var connection = new NpgsqlConnection(adminBuilder.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"""CREATE DATABASE "{databaseName}";""", connection);
            await command.ExecuteNonQueryAsync();

            return new TemporaryPostgresDatabase(adminBuilder.ConnectionString, databaseBuilder.ConnectionString, databaseName);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(AdminConnectionString);
            await connection.OpenAsync();
            await using (var terminate = new NpgsqlCommand(
                """
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = @databaseName AND pid <> pg_backend_pid();
                """,
                connection))
            {
                terminate.Parameters.AddWithValue("databaseName", DatabaseName);
                await terminate.ExecuteNonQueryAsync();
            }

            await using var drop = new NpgsqlCommand($"""DROP DATABASE IF EXISTS "{DatabaseName}";""", connection);
            await drop.ExecuteNonQueryAsync();
        }
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
