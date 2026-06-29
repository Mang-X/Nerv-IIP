using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.Scans;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class BarcodeLabelRecordScanCommandTests
{
    [Fact]
    public async Task Record_scan_idempotency_replay_does_not_create_duplicate_scan_or_epcis_fact()
    {
        await using var dbContext = CreateDbContext();
        var handler = new RecordScanCommandHandler(dbContext);
        var command = NewInventoryScanCommand("idem-scan-gs1-001");

        var firstId = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var secondId = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(firstId, secondId);
        Assert.Equal(1, await dbContext.ScanRecords.CountAsync());
        Assert.Equal(1, await dbContext.EpcisEvents.CountAsync());
    }

    [Fact]
    public async Task Record_scan_rejects_duplicate_serialized_epc_across_idempotency_keys()
    {
        await using var dbContext = CreateDbContext();
        var handler = new RecordScanCommandHandler(dbContext);

        await handler.Handle(NewInventoryScanCommand("idem-scan-gs1-001"), CancellationToken.None);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            handler.Handle(NewInventoryScanCommand("idem-scan-gs1-002"), CancellationToken.None));

        Assert.Contains("serialized barcode", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Scan_record_epc_uri_unique_index_is_enforced_by_relational_store()
    {
        await using var database = await BarcodeLabelSqliteDatabase.CreateAsync();

        await using (var dbContext = database.CreateDbContext())
        {
            dbContext.ScanRecords.Add(NewInventoryScan("idem-scan-gs1-001"));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = database.CreateDbContext())
        {
            dbContext.ScanRecords.Add(NewInventoryScan("idem-scan-gs1-002"));
            await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        }
    }

    private static RecordScanCommand NewInventoryScanCommand(string idempotencyKey)
    {
        return new RecordScanCommand(
            "org-001",
            "env-dev",
            "PDA-01",
            "(01)09506000134352(10)LOT-A(21)SN-0001(30)2",
            "inventory.receipt",
            "ASN-001",
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

    private static ScanRecord NewInventoryScan(string idempotencyKey)
    {
        return ScanRecord.Record(
            "org-001",
            "env-dev",
            "PDA-01",
            "(01)09506000134352(10)LOT-A\u001D(21)SN-0001",
            "inventory.receipt",
            "ASN-001",
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

    private sealed class BarcodeLabelSqliteDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection keepAliveConnection;

        private BarcodeLabelSqliteDatabase()
        {
            keepAliveConnection = new SqliteConnection("Filename=:memory:");
        }

        public static async Task<BarcodeLabelSqliteDatabase> CreateAsync()
        {
            var database = new BarcodeLabelSqliteDatabase();
            await database.keepAliveConnection.OpenAsync();
            await using var context = database.CreateDbContext();
            await context.Database.EnsureCreatedAsync();
            return database;
        }

        public ApplicationDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(keepAliveConnection)
                .Options;
            return new ApplicationDbContext(options, new NoopMediator());
        }

        public async ValueTask DisposeAsync()
        {
            await keepAliveConnection.DisposeAsync();
        }
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
