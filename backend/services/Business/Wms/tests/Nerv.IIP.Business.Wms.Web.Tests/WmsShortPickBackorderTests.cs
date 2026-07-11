using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.Queries;
using Npgsql;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsShortPickBackorderTests
{
    private const string PostgresConnectionStringEnvironmentVariable = "NERV_IIP_TEST_POSTGRES";

    [Fact]
    public async Task Completing_short_pick_persists_one_backorder_and_one_replenishment_recommendation_on_retry()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var outbound = OutboundOrder.Create(
            "org-001", "env-dev", "OUT-001", "sales-delivery", "SO-001", "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-001", "pcs", 10m, "PICK-01", null, null, "qualified", "company", null)]);
        var picking = outbound.CreatePickingTask("PICK-OUT-001-001", "LINE-001", "PICK-01", "PACK-01", 10m);
        picking.RecordProgress(7m);
        dbContext.AddRange(outbound, picking);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new CompleteOutboundOrderCommandHandler(dbContext);
        var command = new CompleteOutboundOrderCommand(outbound.Id, "PACK-001", true, "complete-out-001");

        var first = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var replay = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var backorder = Assert.Single(await dbContext.BackorderOrders.AsNoTracking().ToListAsync());
        Assert.Equal("OUT-001", backorder.OutboundOrderNo);
        Assert.Equal("LINE-001", backorder.OutboundOrderLineNo);
        Assert.Equal(3m, backorder.BackorderQuantity);
        var recommendation = Assert.Single(await dbContext.WarehouseTasks.AsNoTracking()
            .Where(x => x.TaskType == WarehouseTaskType.Replenishment)
            .ToListAsync());
        Assert.Equal(backorder.BackorderOrderNo, recommendation.SourceOrderNo);
        Assert.Equal("PICK-01", recommendation.ToLocationCode);
        Assert.Equal(first, replay);
    }

    [Fact]
    public async Task Backorder_query_is_tenant_scoped_and_close_is_idempotent()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var backorder = Domain.AggregatesModel.BackorderOrderAggregate.BackorderOrder.Create(
            "org-001", "env-dev", "BO-001", "OUT-001", "LINE-001", "SKU-001", "pcs", "SITE-01", "PICK-01", 3m);
        dbContext.BackorderOrders.Add(backorder);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var query = await new ListBackorderOrdersQueryHandler(dbContext).Handle(
            new ListBackorderOrdersQuery("org-001", "env-dev"), CancellationToken.None);
        await new CloseBackorderOrderCommandHandler(dbContext).Handle(
            new CloseBackorderOrderCommand(backorder.Id, "stock-restored"), CancellationToken.None);
        await new CloseBackorderOrderCommandHandler(dbContext).Handle(
            new CloseBackorderOrderCommand(backorder.Id, "stock-restored"), CancellationToken.None);

        Assert.Single(query.Items);
        Assert.Equal("BO-001", query.Items.Single().BackorderOrderNo);
        Assert.Equal(Domain.AggregatesModel.BackorderOrderAggregate.BackorderOrderStatus.Closed, backorder.Status);
    }

    [Fact]
    public void Close_backorder_validator_rejects_blank_and_oversized_reasons()
    {
        var validator = new CloseBackorderOrderCommandValidator();
        var id = new Domain.AggregatesModel.BackorderOrderAggregate.BackorderOrderId(Guid.CreateVersion7());

        Assert.False(validator.Validate(new CloseBackorderOrderCommand(id, " ")).IsValid);
        Assert.False(validator.Validate(new CloseBackorderOrderCommand(id, new string('x', 1001))).IsValid);
        Assert.True(validator.Validate(new CloseBackorderOrderCommand(id, "stock-restored")).IsValid);
    }

    [Fact]
    public async Task Closing_backorder_with_a_conflicting_reason_returns_known_error()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var backorder = Domain.AggregatesModel.BackorderOrderAggregate.BackorderOrder.Create(
            "org-001", "env-dev", "BO-001", "OUT-001", "LINE-001", "SKU-001", "pcs", "SITE-01", "PICK-01", 3m);
        backorder.Close("stock-restored");
        dbContext.BackorderOrders.Add(backorder);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CloseBackorderOrderCommandHandler(dbContext);

        await Assert.ThrowsAsync<NetCorePal.Extensions.Primitives.KnownException>(() => handler.Handle(
            new CloseBackorderOrderCommand(backorder.Id, "customer-cancelled"), CancellationToken.None));
    }

    [WmsShortPickRealPostgresFact]
    public async Task Short_pick_chain_is_durable_and_idempotent_on_postgres()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)!;
        await using var database = await TemporaryPostgresDatabase.CreateAsync(postgresConnectionString, "wms_short_pick");
        Domain.AggregatesModel.BackorderOrderAggregate.BackorderOrderId backorderId;

        await using (var dbContext = CreatePostgresContext(database.ConnectionString))
        {
            await dbContext.Database.MigrateAsync();
            var outbound = OutboundOrder.Create(
                "org-postgres", "env-acceptance", "OUT-PG-001", "sales-delivery", "SO-PG-001", "SITE-01",
                [new OutboundOrderLineDraft("LINE-001", "SKU-001", "pcs", 10m, "PICK-01", null, null, "qualified", "company", null)]);
            var picking = outbound.CreatePickingTask("PICK-PG-001", "LINE-001", "PICK-01", "PACK-01", 10m);
            picking.RecordProgress(7m);
            dbContext.AddRange(outbound, picking);
            await dbContext.SaveChangesAsync();
            var handler = new CompleteOutboundOrderCommandHandler(dbContext);
            var command = new CompleteOutboundOrderCommand(outbound.Id, "PACK-PG-001", true, "complete-pg-001");

            await handler.Handle(command, CancellationToken.None);
            await dbContext.SaveChangesAsync();
            await handler.Handle(command, CancellationToken.None);
            await dbContext.SaveChangesAsync();
        }

        await using (var assertionContext = CreatePostgresContext(database.ConnectionString))
        {
            var backorder = Assert.Single(await assertionContext.BackorderOrders.AsNoTracking().ToListAsync());
            backorderId = backorder.Id;
            Assert.Equal(3m, backorder.BackorderQuantity);
            var replenishment = Assert.Single(await assertionContext.WarehouseTasks.AsNoTracking()
                .Where(x => x.TaskType == WarehouseTaskType.Replenishment).ToListAsync());
            Assert.Equal(backorder.BackorderOrderNo, replenishment.SourceOrderNo);
            await new CloseBackorderOrderCommandHandler(assertionContext).Handle(
                new CloseBackorderOrderCommand(backorderId, "stock-restored"), CancellationToken.None);
            await assertionContext.SaveChangesAsync();
        }

        await using (var finalContext = CreatePostgresContext(database.ConnectionString))
        {
            var closed = await finalContext.BackorderOrders.AsNoTracking().SingleAsync(x => x.Id == backorderId);
            Assert.Equal(Domain.AggregatesModel.BackorderOrderAggregate.BackorderOrderStatus.Closed, closed.Status);
            Assert.Equal("stock-restored", closed.ClosureReason);
            Assert.NotNull(closed.ClosedAtUtc);
        }
    }

    private static ApplicationDbContext CreatePostgresContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "wms"))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class TemporaryPostgresDatabase : IAsyncDisposable
    {
        private readonly string adminConnectionString;
        private readonly string databaseName;

        private TemporaryPostgresDatabase(string adminConnectionString, string connectionString, string databaseName)
        {
            this.adminConnectionString = adminConnectionString;
            ConnectionString = connectionString;
            this.databaseName = databaseName;
        }

        public string ConnectionString { get; }

        public static async Task<TemporaryPostgresDatabase> CreateAsync(string baseConnectionString, string prefix)
        {
            var baseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString);
            var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = string.IsNullOrWhiteSpace(baseBuilder.Database) ? "postgres" : baseBuilder.Database,
            };
            var databaseName = $"nerv_iip_{prefix}_{Guid.CreateVersion7():N}";
            var databaseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = databaseName };
            await using var connection = new NpgsqlConnection(adminBuilder.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"""CREATE DATABASE "{databaseName}";""", connection);
            await command.ExecuteNonQueryAsync();
            return new TemporaryPostgresDatabase(adminBuilder.ConnectionString, databaseBuilder.ConnectionString, databaseName);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using (var terminate = new NpgsqlCommand(
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @databaseName AND pid <> pg_backend_pid();", connection))
            {
                terminate.Parameters.AddWithValue("databaseName", databaseName);
                await terminate.ExecuteNonQueryAsync();
            }

            await using var drop = new NpgsqlCommand($"""DROP DATABASE IF EXISTS "{databaseName}";""", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class WmsShortPickRealPostgresFactAttribute : FactAttribute
    {
        public WmsShortPickRealPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)))
            {
                Skip = $"Set {PostgresConnectionStringEnvironmentVariable} to run this real PostgreSQL WMS short-pick acceptance test.";
            }
        }
    }
}
