using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

public sealed class StockMovementReplayPersistenceTests
{
    [Fact]
    public async Task Identical_outbound_payload_replay_returns_the_existing_posting()
    {
        await using var connection = await OpenDatabaseAsync();
        var seed = NewCommand("seed-inbound", 10m) with
        {
            MovementType = "inbound",
            SourceDocumentId = "RECEIPT-001",
            UnitCost = 12.34m,
        };
        var replay = NewCommand("issue-outbound", -2m);
        StockMovementId firstMovementId;

        await using (var db = CreateContext(connection))
        {
            await new PostStockMovementCommandHandler(db).Handle(seed, CancellationToken.None);
            await db.SaveChangesAsync(CancellationToken.None);
            var first = await new PostStockMovementCommandHandler(db).Handle(replay, CancellationToken.None);
            firstMovementId = first.MovementId;
            await db.SaveChangesAsync(CancellationToken.None);

            Assert.Null(replay.UnitCost);
            Assert.Equal(12.34m, (await db.StockMovements.SingleAsync(x => x.Id == firstMovementId)).UnitCost);
        }

        await using (var db = CreateContext(connection))
        {
            var duplicate = await new PostStockMovementCommandHandler(db).Handle(replay, CancellationToken.None);

            Assert.Equal(firstMovementId, duplicate.MovementId);
            Assert.Equal(8m, duplicate.OnHandQuantity);
            Assert.Equal(2, await db.StockMovements.CountAsync());
        }
    }

    [Fact]
    public async Task Outbound_replay_with_a_different_quantity_remains_an_idempotency_conflict()
    {
        await using var connection = await OpenDatabaseAsync();
        await using (var db = CreateContext(connection))
        {
            await new PostStockMovementCommandHandler(db).Handle(
                NewCommand("seed-inbound", 10m) with
                {
                    MovementType = "inbound",
                    SourceDocumentId = "RECEIPT-001",
                    UnitCost = 12.34m,
                },
                CancellationToken.None);
            await db.SaveChangesAsync(CancellationToken.None);
            await new PostStockMovementCommandHandler(db).Handle(NewCommand("issue-outbound", -2m), CancellationToken.None);
            await db.SaveChangesAsync(CancellationToken.None);
        }

        await using (var db = CreateContext(connection))
        {
            var exception = await Assert.ThrowsAsync<InventoryPostingRejectedException>(() =>
                new PostStockMovementCommandHandler(db).Handle(NewCommand("issue-outbound", -3m), CancellationToken.None));

            Assert.Equal(InventoryPostingFailureCodes.IdempotencyConflict, exception.FailureCode);
        }
    }

    [Fact]
    public async Task Rejected_movement_does_not_persist_empty_ledgers_or_make_the_next_lookup_ambiguous()
    {
        await using var connection = await OpenDatabaseAsync();
        var rejected = NewCommand("missing-stock", -1m);

        await using (var db = CreateContext(connection))
        {
            var handler = new PostStockMovementCommandHandler(db);
            await Assert.ThrowsAsync<InventoryPostingRejectedException>(() => handler.Handle(rejected, CancellationToken.None));
            await Assert.ThrowsAsync<InventoryPostingRejectedException>(() => handler.Handle(rejected, CancellationToken.None));
            await db.SaveChangesAsync(CancellationToken.None);
        }

        await using (var db = CreateContext(connection))
        {
            Assert.Empty(await db.StockLedgers.ToListAsync());
            var exception = await Assert.ThrowsAsync<InventoryPostingRejectedException>(() =>
                new PostStockMovementCommandHandler(db).Handle(rejected, CancellationToken.None));
            Assert.Equal(InventoryPostingFailureCodes.NegativeOnHand, exception.FailureCode);
        }
    }

    private static async Task<SqliteConnection> OpenDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE stock_ledgers (
                id TEXT NOT NULL PRIMARY KEY,
                organization_id TEXT NOT NULL,
                environment_id TEXT NOT NULL,
                sku_code TEXT NOT NULL,
                uom_code TEXT NOT NULL,
                site_code TEXT NOT NULL,
                location_code TEXT NOT NULL,
                lot_no TEXT NULL,
                serial_no TEXT NULL,
                quality_status TEXT NOT NULL,
                owner_type TEXT NOT NULL,
                owner_id TEXT NULL,
                production_date TEXT NULL,
                expiry_date TEXT NULL,
                shelf_life_days INTEGER NULL,
                expiry_date_source TEXT NULL,
                on_hand_quantity TEXT NOT NULL,
                reserved_quantity TEXT NOT NULL,
                moving_average_unit_cost TEXT NOT NULL,
                inventory_value TEXT NOT NULL,
                is_frozen_for_count INTEGER NOT NULL,
                frozen_count_task_code TEXT NULL,
                ledger_version INTEGER NOT NULL,
                row_version INTEGER NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            CREATE UNIQUE INDEX ux_stock_ledgers_dimensions ON stock_ledgers (
                organization_id, environment_id, sku_code, uom_code, site_code, location_code,
                lot_no, serial_no, production_date, expiry_date, quality_status, owner_type, owner_id
            );
            CREATE TABLE stock_movements (
                id TEXT NOT NULL PRIMARY KEY,
                organization_id TEXT NOT NULL,
                environment_id TEXT NOT NULL,
                movement_type TEXT NOT NULL,
                source_service TEXT NOT NULL,
                source_document_id TEXT NOT NULL,
                source_document_line_id TEXT NULL,
                idempotency_key TEXT NOT NULL,
                sku_code TEXT NOT NULL,
                uom_code TEXT NOT NULL,
                site_code TEXT NOT NULL,
                location_code TEXT NOT NULL,
                lot_no TEXT NULL,
                serial_no TEXT NULL,
                quality_status TEXT NOT NULL,
                owner_type TEXT NOT NULL,
                owner_id TEXT NULL,
                production_date TEXT NULL,
                expiry_date TEXT NULL,
                quantity TEXT NOT NULL,
                unit_cost TEXT NULL,
                movement_amount TEXT NULL,
                posted_at_utc TEXT NOT NULL
            );
            CREATE UNIQUE INDEX ux_stock_movements_idempotency ON stock_movements (
                organization_id, environment_id, source_service, source_document_id, idempotency_key
            );
            """);
        return connection;
    }

    private static ApplicationDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static PostStockMovementCommand NewCommand(string idempotencyKey, decimal quantity)
    {
        return new PostStockMovementCommand(
            "org-001",
            "env-dev",
            "outbound",
            "mes",
            "MIR-001",
            "LINE-001",
            idempotencyKey,
            "MAT-OIL",
            "L",
            "SITE-01",
            "LOC-A-01",
            "LOT-OIL-A",
            null,
            "unrestricted",
            "company",
            null,
            quantity);
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
