using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Nerv.IIP.Business.Inventory.Domain;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockCounts;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockReservations;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Wms.Domain;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.Inventory;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedLocks;
using NetCorePal.Extensions.Primitives;
using InventoryDbContext = Nerv.IIP.Business.Inventory.Infrastructure.ApplicationDbContext;
using WmsDbContext = Nerv.IIP.Business.Wms.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

public sealed class WmsInventoryRpcIdempotencyAcceptanceTests
{
    [Fact]
    public async Task Picking_task_retry_after_inventory_reservation_timeout_recovers_existing_reservation()
    {
        await using var wmsDb = CreateWmsContext();
        await using var inventoryDb = CreateInventoryContext();
        await SeedInventoryAsync(inventoryDb, "SKU-FG-1000", "LOC-A-01", "LOT-001", 10m, "seed-pick-retry-001");
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-RPC-RETRY-001",
            "sales-delivery",
            "SO-RPC-001",
            "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 4m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001")]);
        wmsDb.OutboundOrders.Add(outbound);
        await wmsDb.SaveChangesAsync(CancellationToken.None);
        var inventoryClient = new TimeoutAfterInventoryCommitClient(inventoryDb)
        {
            ThrowOnNextReservation = true,
        };
        var command = new CreatePickingTaskCommand(outbound.Id, "TASK-RPC-RETRY-001", "LINE-001", "LOC-A-01", "PACK-01", 4m);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            new CreatePickingTaskCommandHandler(wmsDb, inventoryClient).Handle(command, CancellationToken.None));

        Assert.Empty(wmsDb.WarehouseTasks);
        var reservation = Assert.Single(inventoryDb.StockReservations);
        var recoveredTaskId = await new CreatePickingTaskCommandHandler(wmsDb, inventoryClient).Handle(command, CancellationToken.None);
        await wmsDb.SaveChangesAsync(CancellationToken.None);

        Assert.Single(inventoryDb.StockReservations);
        var task = Assert.Single(wmsDb.WarehouseTasks);
        Assert.Equal(recoveredTaskId, task.Id);
        Assert.Equal(reservation.Id.ToString(), wmsDb.OutboundOrders.Include(x => x.Lines).Single().Lines.Single().InventoryReservationId);
    }

    [Fact]
    public async Task Count_execution_retry_after_inventory_freeze_timeout_recovers_existing_count_task()
    {
        await using var wmsDb = CreateWmsContext();
        await using var inventoryDb = CreateInventoryContext();
        await SeedInventoryAsync(inventoryDb, "SKU-FG-1000", "LOC-A-01", null, 10m, "seed-count-retry-001", ownerId: null);
        var inventoryClient = new TimeoutAfterInventoryCommitClient(inventoryDb)
        {
            ThrowOnNextCountTask = true,
        };
        var command = new CreateCountExecutionCommand("org-001", "env-dev", "COUNT-RPC-RETRY-001", "SKU-FG-1000", "kg", "SITE-01", "LOC-A-01", 10m);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            new CreateCountExecutionCommandHandler(wmsDb, inventoryClient).Handle(command, CancellationToken.None));

        Assert.Empty(wmsDb.CountExecutions);
        var inventoryTask = Assert.Single(inventoryDb.StockCountTasks);
        var recoveredCountId = await new CreateCountExecutionCommandHandler(wmsDb, inventoryClient).Handle(command, CancellationToken.None);
        await wmsDb.SaveChangesAsync(CancellationToken.None);

        Assert.Single(inventoryDb.StockCountTasks);
        var count = Assert.Single(wmsDb.CountExecutions);
        Assert.Equal(recoveredCountId, count.Id);
        Assert.Equal(inventoryTask.Id.ToString(), count.InventoryCountTaskId);
        Assert.True(inventoryDb.StockLedgers.Single().IsFrozenForCount);
    }

    [RealPostgresFact]
    public async Task Postgres_count_execution_timeout_and_concurrent_retry_converges_inventory_and_wms()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        await using var wmsDatabase = await TemporaryPostgresDatabase.CreateAsync(postgresConnectionString, "wms_rpc");
        await using var inventoryDatabase = await TemporaryPostgresDatabase.CreateAsync(postgresConnectionString, "inv_rpc");
        await using var inventoryProvider = CreateInventoryPostgresProvider(inventoryDatabase.ConnectionString);
        await using var wmsDb = CreatePostgresWmsContext(wmsDatabase.ConnectionString);
        await wmsDb.Database.MigrateAsync();
        await using (var scope = inventoryProvider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await db.Database.MigrateAsync();
        }

        await SeedInventoryPostgresAsync(inventoryProvider, "SKU-FG-1000", "LOC-A-01", null, 10m, "seed-count-postgres-001", ownerId: null);
        var client = new InventoryMediatRTimeoutClient(inventoryProvider)
        {
            ThrowOnNextCountTask = true,
        };
        var command = new CreateCountExecutionCommand("org-001", "env-dev", "COUNT-RPC-PG-001", "SKU-FG-1000", "kg", "SITE-01", "LOC-A-01", 10m);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            new CreateCountExecutionCommandHandler(wmsDb, client).Handle(command, CancellationToken.None));

        Assert.Empty(wmsDb.CountExecutions);
        var inventoryRequest = new WmsInventoryCountTaskRequest(
            "org-001",
            "env-dev",
            "COUNT-RPC-PG-001",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            null,
            null,
            "qualified",
            "company",
            null,
            "wms-count-freeze:postgres-concurrent");
        var concurrentResults = await Task.WhenAll(
            client.CreateCountTaskAsync(inventoryRequest, CancellationToken.None),
            client.CreateCountTaskAsync(inventoryRequest, CancellationToken.None));

        Assert.Equal(concurrentResults[0].CountTaskId, concurrentResults[1].CountTaskId);
        var recoveredCountId = await new CreateCountExecutionCommandHandler(wmsDb, client).Handle(command, CancellationToken.None);
        await wmsDb.SaveChangesAsync(CancellationToken.None);

        await using var inventoryAssertScope = inventoryProvider.CreateAsyncScope();
        var inventoryDb = inventoryAssertScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Single(await inventoryDb.StockCountTasks.ToListAsync());
        Assert.True((await inventoryDb.StockLedgers.SingleAsync()).IsFrozenForCount);
        var count = Assert.Single(wmsDb.CountExecutions);
        Assert.Equal(recoveredCountId, count.Id);
        Assert.Equal(concurrentResults[0].CountTaskId, count.InventoryCountTaskId);
    }

    [RealPostgresFact]
    public async Task Postgres_count_task_same_code_different_key_unique_conflict_reruns_as_domain_conflict()
    {
        const string countTaskCode = "COUNT-RPC-PG-CONFLICT";
        var postgresConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        await using var inventoryDatabase = await TemporaryPostgresDatabase.CreateAsync(postgresConnectionString, "inv_rpc_conflict");
        var saveRace = new StockCountTaskSaveRaceInterceptor(countTaskCode, 2);
        await using var inventoryProvider = CreateInventoryPostgresProvider(inventoryDatabase.ConnectionString, saveRace);
        await using (var scope = inventoryProvider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await db.Database.MigrateAsync();
        }

        await SeedInventoryPostgresAsync(
            inventoryProvider,
            "SKU-FG-1000",
            "LOC-A-01",
            null,
            10m,
            "seed-count-postgres-conflict",
            ownerId: null);
        var client = new InventoryMediatRTimeoutClient(inventoryProvider);
        var firstRequest = new WmsInventoryCountTaskRequest(
            "org-001",
            "env-dev",
            countTaskCode,
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            null,
            null,
            "qualified",
            "company",
            null,
            "wms-count-freeze:postgres-conflict-a");
        var secondRequest = firstRequest with
        {
            IdempotencyKey = "wms-count-freeze:postgres-conflict-b",
        };

        var first = client.CreateCountTaskAsync(firstRequest, CancellationToken.None);
        var second = client.CreateCountTaskAsync(secondRequest, CancellationToken.None);
        var exception = await Record.ExceptionAsync(() => Task.WhenAll(first, second));

        Assert.NotNull(exception);
        var successful = Assert.Single(new[] { first, second }, task => task.Status == TaskStatus.RanToCompletion);
        var failed = Assert.Single(new[] { first, second }, task => task.IsFaulted);
        var knownException = Assert.IsType<KnownException>(failed.Exception!.GetBaseException());
        Assert.Contains("Stock count task code conflicts", knownException.Message, StringComparison.Ordinal);
        await using var assertScope = inventoryProvider.CreateAsyncScope();
        var inventoryDb = assertScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var task = Assert.Single(await inventoryDb.StockCountTasks.ToListAsync());
        Assert.Equal(successful.Result.CountTaskId, task.Id.ToString());
        Assert.True((await inventoryDb.StockLedgers.SingleAsync()).IsFrozenForCount);
    }

    private static async Task SeedInventoryAsync(
        InventoryDbContext inventoryDb,
        string skuCode,
        string locationCode,
        string? lotNo,
        decimal quantity,
        string idempotencyKey,
        string? ownerId = "owner-001")
    {
        await new PostStockMovementCommandHandler(inventoryDb).Handle(
            new PostStockMovementCommand(
                "org-001",
                "env-dev",
                "inbound",
                "wms",
                "SEED",
                idempotencyKey,
                idempotencyKey,
                skuCode,
                "kg",
                "SITE-01",
                locationCode,
                lotNo,
                null,
                "qualified",
                "company",
                ownerId,
                quantity),
            CancellationToken.None);
        await inventoryDb.SaveChangesAsync(CancellationToken.None);
    }

    private static WmsDbContext CreateWmsContext()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase($"wms-rpc-idempotency-{Guid.NewGuid():N}")
            .Options;
        return new WmsDbContext(options, new NoopMediator());
    }

    private static InventoryDbContext CreateInventoryContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase($"inventory-rpc-idempotency-{Guid.NewGuid():N}")
            .Options;
        return new InventoryDbContext(options, new NoopMediator());
    }

    private static WmsDbContext CreatePostgresWmsContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", WmsFacts.Schema))
            .Options;
        return new WmsDbContext(options, new NoopMediator());
    }

    private static ServiceProvider CreateInventoryPostgresProvider(string connectionString, params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration => configuration
            .RegisterServicesFromAssembly(typeof(CreateStockCountTaskCommand).Assembly)
            .AddCommandLockBehavior()
            .AddKnownExceptionValidationBehavior()
            .AddOpenBehavior(typeof(CreateStockCountTaskUniqueConflictBehavior<,>))
            .AddUnitOfWorkBehaviors());
        services.AddInventoryPostgreSqlPersistence(connectionString, interceptors: interceptors);
        services.AddInMemoryDistributedLock();
        services.AddScoped<ICommandLock<CreateStockCountTaskCommand>, CreateStockCountTaskCommandLock>();
        return services.BuildServiceProvider();
    }

    private static async Task SeedInventoryPostgresAsync(
        IServiceProvider inventoryProvider,
        string skuCode,
        string locationCode,
        string? lotNo,
        decimal quantity,
        string idempotencyKey,
        string? ownerId = "owner-001")
    {
        await using var scope = inventoryProvider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        await sender.Send(
            new PostStockMovementCommand(
                "org-001",
                "env-dev",
                "inbound",
                "wms",
                "SEED",
                idempotencyKey,
                idempotencyKey,
                skuCode,
                "kg",
                "SITE-01",
                locationCode,
                lotNo,
                null,
                "qualified",
                "company",
                ownerId,
                quantity),
            CancellationToken.None);
    }

    private sealed class TimeoutAfterInventoryCommitClient(InventoryDbContext inventoryDb) : IWmsInventoryReservationClient
    {
        public bool ThrowOnNextReservation { get; set; }
        public bool ThrowOnNextCountTask { get; set; }

        public async Task<WmsInventoryReservationResult> ReserveAsync(
            WmsInventoryReservationRequest request,
            CancellationToken cancellationToken)
        {
            var result = await new ReserveStockCommandHandler(inventoryDb).Handle(
                new ReserveStockCommand(
                    request.OrganizationId,
                    request.EnvironmentId,
                    request.SourceService,
                    request.SourceDocumentId,
                    request.SourceDocumentLineId,
                    request.IdempotencyKey,
                    request.SkuCode,
                    request.UomCode,
                    request.SiteCode,
                    request.LocationCode,
                    request.LotNo,
                    request.SerialNo,
                    request.QualityStatus,
                    request.OwnerType,
                    request.OwnerId,
                    request.Quantity,
                    request.ProductionDate,
                    request.ExpiryDate),
                cancellationToken);
            await inventoryDb.SaveChangesAsync(cancellationToken);
            if (ThrowOnNextReservation)
            {
                ThrowOnNextReservation = false;
                throw new TimeoutException("Simulated timeout after Inventory committed the reservation.");
            }

            return new WmsInventoryReservationResult(
                result.ReservationId.ToString(),
                result.ReservedQuantity,
                result.AvailableQuantity,
                result.LotNo,
                result.ProductionDate,
                result.ExpiryDate);
        }

        public Task<WmsInventoryFefoReservationResult> ReserveFefoAsync(
            WmsInventoryFefoReservationRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("This test uses explicit lot reservations.");
        }

        public Task<WmsInventoryReservationReleaseResult> ReleaseAsync(
            WmsInventoryReservationReleaseRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("This test does not release reservations.");
        }

        public Task<WmsInventoryReservationRenewalResult> RenewAsync(
            WmsInventoryReservationRenewalRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("This test does not renew reservations.");
        }

        public async Task<WmsInventoryCountTaskResult> CreateCountTaskAsync(
            WmsInventoryCountTaskRequest request,
            CancellationToken cancellationToken)
        {
            var result = await new CreateStockCountTaskCommandHandler(inventoryDb).Handle(
                new CreateStockCountTaskCommand(
                    request.OrganizationId,
                    request.EnvironmentId,
                    request.CountTaskCode,
                    request.SkuCode,
                    request.UomCode,
                    request.SiteCode,
                    request.LocationCode,
                    request.LotNo,
                    request.SerialNo,
                    request.QualityStatus,
                    request.OwnerType,
                    request.OwnerId,
                    request.IdempotencyKey),
                cancellationToken);
            await inventoryDb.SaveChangesAsync(cancellationToken);
            if (ThrowOnNextCountTask)
            {
                ThrowOnNextCountTask = false;
                throw new TimeoutException("Simulated timeout after Inventory committed the count freeze.");
            }

            return new WmsInventoryCountTaskResult(result.CountTaskId.ToString(), result.ExpectedLedgerVersion);
        }

        public Task<WmsInventoryCountAdjustmentResult> ConfirmCountAdjustmentAsync(
            WmsInventoryCountAdjustmentRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("This test does not confirm count adjustments.");
        }
    }

    private sealed class InventoryMediatRTimeoutClient(IServiceProvider inventoryProvider) : IWmsInventoryReservationClient
    {
        private int throwOnNextReservation;
        private int throwOnNextCountTask;

        public bool ThrowOnNextReservation
        {
            get => Volatile.Read(ref throwOnNextReservation) == 1;
            set => Volatile.Write(ref throwOnNextReservation, value ? 1 : 0);
        }

        public bool ThrowOnNextCountTask
        {
            get => Volatile.Read(ref throwOnNextCountTask) == 1;
            set => Volatile.Write(ref throwOnNextCountTask, value ? 1 : 0);
        }

        public async Task<WmsInventoryReservationResult> ReserveAsync(
            WmsInventoryReservationRequest request,
            CancellationToken cancellationToken)
        {
            await using var scope = inventoryProvider.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var result = await sender.Send(
                new ReserveStockCommand(
                    request.OrganizationId,
                    request.EnvironmentId,
                    request.SourceService,
                    request.SourceDocumentId,
                    request.SourceDocumentLineId,
                    request.IdempotencyKey,
                    request.SkuCode,
                    request.UomCode,
                    request.SiteCode,
                    request.LocationCode,
                    request.LotNo,
                    request.SerialNo,
                    request.QualityStatus,
                    request.OwnerType,
                    request.OwnerId,
                    request.Quantity,
                    request.ProductionDate,
                    request.ExpiryDate),
                cancellationToken);
            if (Interlocked.Exchange(ref throwOnNextReservation, 0) == 1)
            {
                throw new TimeoutException("Simulated timeout after Inventory committed the reservation.");
            }

            return new WmsInventoryReservationResult(
                result.ReservationId.ToString(),
                result.ReservedQuantity,
                result.AvailableQuantity,
                result.LotNo,
                result.ProductionDate,
                result.ExpiryDate);
        }

        public Task<WmsInventoryFefoReservationResult> ReserveFefoAsync(
            WmsInventoryFefoReservationRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("This test uses explicit lot reservations.");
        }

        public Task<WmsInventoryReservationReleaseResult> ReleaseAsync(
            WmsInventoryReservationReleaseRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("This test does not release reservations.");
        }

        public Task<WmsInventoryReservationRenewalResult> RenewAsync(
            WmsInventoryReservationRenewalRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("This test does not renew reservations.");
        }

        public async Task<WmsInventoryCountTaskResult> CreateCountTaskAsync(
            WmsInventoryCountTaskRequest request,
            CancellationToken cancellationToken)
        {
            await using var scope = inventoryProvider.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var result = await sender.Send(
                new CreateStockCountTaskCommand(
                    request.OrganizationId,
                    request.EnvironmentId,
                    request.CountTaskCode,
                    request.SkuCode,
                    request.UomCode,
                    request.SiteCode,
                    request.LocationCode,
                    request.LotNo,
                    request.SerialNo,
                    request.QualityStatus,
                    request.OwnerType,
                    request.OwnerId,
                    request.IdempotencyKey),
                cancellationToken);
            if (Interlocked.Exchange(ref throwOnNextCountTask, 0) == 1)
            {
                throw new TimeoutException("Simulated timeout after Inventory committed the count freeze.");
            }

            return new WmsInventoryCountTaskResult(result.CountTaskId.ToString(), result.ExpectedLedgerVersion);
        }

        public Task<WmsInventoryCountAdjustmentResult> ConfirmCountAdjustmentAsync(
            WmsInventoryCountAdjustmentRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("This test does not confirm count adjustments.");
        }
    }

    private sealed class StockCountTaskSaveRaceInterceptor(string countTaskCode, int participantCount) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource allParticipantsArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (ShouldWait(eventData.Context))
            {
                if (Interlocked.Increment(ref arrivals) == participantCount)
                {
                    allParticipantsArrived.TrySetResult();
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await allParticipantsArrived.Task.WaitAsync(timeout.Token);
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private bool ShouldWait(DbContext? context)
        {
            return context?.ChangeTracker.Entries<StockCountTask>().Any(entry =>
                entry.State == EntityState.Added &&
                string.Equals(entry.Entity.CountTaskCode, countTaskCode, StringComparison.Ordinal)) is true;
        }
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
            var databaseName = $"nerv_iip_{prefix}_{Guid.NewGuid():N}";
            var databaseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = databaseName,
            };

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
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @databaseName AND pid <> pg_backend_pid();",
                connection))
            {
                terminate.Parameters.AddWithValue("databaseName", databaseName);
                await terminate.ExecuteNonQueryAsync();
            }

            await using var drop = new NpgsqlCommand($"""DROP DATABASE IF EXISTS "{databaseName}";""", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Test mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException("Test mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Test mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Test mediator cannot stream requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Test mediator cannot stream requests.");
        }
    }
}
