using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountAdjustmentAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockReservationAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockReservations;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockStatusTransfers;
using Nerv.IIP.Business.Inventory.Web.Application.Validation;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

/// <summary>
/// GitHub #3176：幂等键的有效上界是「列宽 − handler 落库前追加的最长后缀」，不是列宽本身。
///
/// 这个类把**三者关系**钉住，而不是只钉住某一个数字：
/// - <see cref="Idempotency_key_column_width_matches_the_policy_constant"/>：列宽（读 EF 模型元数据，
///   不读代码里的常量）必须等于策略常量 → 单边改 <c>HasMaxLength</c> 即红；
/// - <see cref="Every_suffixing_write_face_rejects_a_base_key_that_would_overflow_the_column"/>：
///   逐个写面跑**真实校验器**，上界那一位放行、+1 位拒绝 → 校验器上界改回列宽即红；
/// - <see cref="Handler_composed_keys_of_a_max_length_base_key_still_fit_the_column"/>：拿真实 handler
///   跑「恰好顶到校验器上界」的基础键，把它**实际落库的键长**与**从 EF 模型读到的列宽**直接对撞
///   → 这条不经过任何策略常量，handler 换个更长的后缀而没有同步上界就红。
/// </summary>
public sealed class InventoryIdempotencyKeyLengthContractTests
{
    /// <summary>所有承载幂等键的实体：新增一张带 idempotency_key 的表就要登记进来。</summary>
    private static readonly (Type EntityType, string PropertyName)[] IdempotencyKeyProperties =
    [
        (typeof(StockMovement), nameof(StockMovement.IdempotencyKey)),
        (typeof(StockReservation), nameof(StockReservation.IdempotencyKey)),
        (typeof(StockCountTask), nameof(StockCountTask.IdempotencyKey)),
        (typeof(StockCountAdjustment), nameof(StockCountAdjustment.IdempotencyKey)),
        (typeof(InventoryAuthorityResolutionPendingAudit), nameof(InventoryAuthorityResolutionPendingAudit.IdempotencyKey)),
    ];

    [Fact]
    public void Idempotency_key_column_width_matches_the_policy_constant()
    {
        using var fixture = CreateModelFixture();
        var model = fixture.DbContext.GetService<IDesignTimeModel>().Model;

        foreach (var (entityType, propertyName) in IdempotencyKeyProperties)
        {
            var property = model.FindEntityType(entityType)?.FindProperty(propertyName);
            Assert.NotNull(property);
            Assert.Equal("idempotency_key", property.GetColumnName());
            Assert.Equal(InventoryIdempotencyKeyPolicy.ColumnMaxLength, property.GetMaxLength());
        }
    }

    [Fact]
    public async Task Every_suffixing_write_face_rejects_a_base_key_that_would_overflow_the_column()
    {
        // 状态调拨：拒绝点在校验器上（:out / :in 是这个写面唯一的行为）。
        AssertBoundary(
            PostStockStatusTransferCommandHandler.BaseIdempotencyKeyMaxLength,
            length => new PostStockStatusTransferCommandValidator()
                .Validate(StatusTransferCommand(new string('k', length)))
                .IsValid);

        // FEFO 预留分配腿：拒绝点也在校验器上。
        AssertBoundary(
            ReserveFefoStockCommandHandler.BaseIdempotencyKeyMaxLength,
            length => new ReserveFefoStockCommandValidator()
                .Validate(FefoCommand(new string('k', length)))
                .IsValid);

        // 库存移动的拒绝点**不在**校验器上：非调拨移动不追加后缀，合法上界仍是整列宽 128，
        // 因此这个写面的收紧只对 movementType=transfer 生效，落在 handler 的 ValidateTransferLegsOrReject 里。
        Assert.True(new PostStockMovementCommandValidator()
            .Validate(InboundMovementCommand(new string('k', InventoryIdempotencyKeyPolicy.ColumnMaxLength), 1m))
            .IsValid);
        Assert.False(new PostStockMovementCommandValidator()
            .Validate(InboundMovementCommand(new string('k', InventoryIdempotencyKeyPolicy.ColumnMaxLength + 1), 1m))
            .IsValid);
        Assert.True(await TransferMovementHandlerAcceptsAsync(
            PostStockMovementCommandHandler.TransferBaseIdempotencyKeyMaxLength));
        Assert.False(await TransferMovementHandlerAcceptsAsync(
            PostStockMovementCommandHandler.TransferBaseIdempotencyKeyMaxLength + 1));
    }

    private static async Task<bool> TransferMovementHandlerAcceptsAsync(int idempotencyKeyLength)
    {
        await using var dbContext = CreateContext($"movement-boundary-{idempotencyKeyLength}");
        var handler = new PostStockMovementCommandHandler(dbContext);
        await handler.Handle(InboundMovementCommand("seed-inbound", 10m), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        try
        {
            await handler.Handle(
                TransferMovementCommand(new string('k', idempotencyKeyLength)),
                CancellationToken.None);
            return true;
        }
        catch (InventoryPostingRejectedException exception)
            when (exception.Message.Contains("幂等键过长", StringComparison.Ordinal))
        {
            return false;
        }
    }

    [Fact]
    public void Every_suffixing_write_face_bound_leaves_room_for_its_own_longest_suffix()
    {
        // 后缀字面量取自 handler 自己，不是测试里另抄一份。
        Assert.Equal(
            InventoryIdempotencyKeyPolicy.ColumnMaxLength,
            PostStockStatusTransferCommandHandler.BaseIdempotencyKeyMaxLength
                + Math.Max(
                    PostStockStatusTransferCommandHandler.OutboundLegSuffix.Length,
                    PostStockStatusTransferCommandHandler.InboundLegSuffix.Length));

        Assert.Equal(
            InventoryIdempotencyKeyPolicy.ColumnMaxLength,
            PostStockMovementCommandHandler.TransferBaseIdempotencyKeyMaxLength
                + Math.Max(
                    PostStockMovementCommandHandler.TransferOutLegSuffix.Length,
                    PostStockMovementCommandHandler.TransferInLegSuffix.Length));

        // FEFO 后缀是变长的：上界由候选台账数上限决定，逐个求值取最大，不靠「序号越大后缀越长」的推断。
        var longestPartSuffixLength = Enumerable
            .Range(2, ReserveFefoStockCommandHandler.MaxFefoCandidateLedgers - 1)
            .Max(index => ReserveFefoStockCommandHandler.PartSuffix(index).Length);
        Assert.Equal(
            InventoryIdempotencyKeyPolicy.ColumnMaxLength,
            ReserveFefoStockCommandHandler.BaseIdempotencyKeyMaxLength + longestPartSuffixLength);
    }

    [Fact]
    public async Task Handler_composed_keys_of_a_max_length_base_key_still_fit_the_column()
    {
        using var fixture = CreateModelFixture();
        var model = fixture.DbContext.GetService<IDesignTimeModel>().Model;
        var movementColumnWidth = model.FindEntityType(typeof(StockMovement))!
            .FindProperty(nameof(StockMovement.IdempotencyKey))!.GetMaxLength()!.Value;
        var reservationColumnWidth = model.FindEntityType(typeof(StockReservation))!
            .FindProperty(nameof(StockReservation.IdempotencyKey))!.GetMaxLength()!.Value;

        // 1) 状态调拨：基础键恰好顶到校验器上界，handler 拼出的两腿键都必须还塞得进列。
        await using (var dbContext = CreateContext("status-transfer"))
        {
            var statusKey = new string('k', PostStockStatusTransferCommandHandler.BaseIdempotencyKeyMaxLength);
            await SeedLedgerAsync(dbContext, "unrestricted", 10m);
            await new PostStockStatusTransferCommandHandler(dbContext)
                .Handle(StatusTransferCommand(statusKey), CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            var keys = dbContext.StockMovements
                .Where(x => x.IdempotencyKey.StartsWith(statusKey))
                .Select(x => x.IdempotencyKey)
                .ToList();
            Assert.Equal(2, keys.Count);
            Assert.All(keys, key => Assert.True(
                key.Length <= movementColumnWidth,
                $"状态调拨腿键 {key.Length} 位超出 stock_movements.idempotency_key 列宽 {movementColumnWidth}。"));
            // 顶格键必须真的顶到列宽：否则说明上界被放松成了「远小于列宽」，这条读数就不再有鉴别力。
            Assert.Equal(movementColumnWidth, keys.Max(key => key.Length));
        }

        // 2) 库存移动调拨腿：同样的对撞。
        await using (var dbContext = CreateContext("movement-transfer"))
        {
            var transferKey = new string('k', PostStockMovementCommandHandler.TransferBaseIdempotencyKeyMaxLength);
            var handler = new PostStockMovementCommandHandler(dbContext);
            await handler.Handle(InboundMovementCommand("seed-inbound", 10m), CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await handler.Handle(TransferMovementCommand(transferKey), CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            var keys = dbContext.StockMovements
                .Where(x => x.MovementType == "transfer")
                .Select(x => x.IdempotencyKey)
                .ToList();
            Assert.Equal(2, keys.Count);
            Assert.All(keys, key => Assert.True(
                key.Length <= movementColumnWidth,
                $"调拨腿键 {key.Length} 位超出 stock_movements.idempotency_key 列宽 {movementColumnWidth}。"));
            Assert.Equal(movementColumnWidth, keys.Max(key => key.Length));
        }

        // 3) FEFO 预留：两条台账 → 第二腿带 :part-2 后缀。
        await using (var dbContext = CreateContext("fefo-reservation"))
        {
            var fefoKey = new string('k', ReserveFefoStockCommandHandler.BaseIdempotencyKeyMaxLength);
            // 真实 UtcNow 参与 FEFO 的过期过滤，锚点必须取「相对今天的未来」而不是写死年份。
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            await SeedLedgerAsync(dbContext, "unrestricted", 4m, "LOC-A-01", today.AddYears(1));
            await SeedLedgerAsync(dbContext, "unrestricted", 6m, "LOC-A-02", today.AddYears(2));
            await new ReserveFefoStockCommandHandler(dbContext)
                .Handle(FefoCommand(fefoKey) with { Quantity = 10m }, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            var keys = dbContext.StockReservations.Select(x => x.IdempotencyKey).ToList();
            Assert.Equal(2, keys.Count);
            Assert.Contains(keys, key => key == fefoKey + ReserveFefoStockCommandHandler.PartSuffix(2));
            Assert.All(keys, key => Assert.True(
                key.Length <= reservationColumnWidth,
                $"FEFO 分配腿键 {key.Length} 位超出 stock_reservations.idempotency_key 列宽 {reservationColumnWidth}。"));
        }
    }

    [Fact]
    public void Compose_refuses_to_overflow_the_column_instead_of_letting_the_database_throw()
    {
        var atColumnWidth = new string('k', InventoryIdempotencyKeyPolicy.ColumnMaxLength);

        // 落库前拼接的唯一入口必须就地拒绝（KnownException → 400），而不是把越界值送进库换一个
        // DbUpdateException(22001)——那个类型不被 KnownException 拦截器覆盖，会逃逸出 CAP 消费者。
        var exception = Assert.Throws<KnownException>(
            () => InventoryIdempotencyKeyPolicy.Compose(atColumnWidth, ":out"));
        Assert.Contains("超出长度上限", exception.Message, StringComparison.Ordinal);

        // 恰好塞满不拒绝，也绝不截断。
        var exact = InventoryIdempotencyKeyPolicy.Compose(
            new string('k', InventoryIdempotencyKeyPolicy.ColumnMaxLength - 4),
            ":out");
        Assert.Equal(InventoryIdempotencyKeyPolicy.ColumnMaxLength, exact.Length);
        Assert.EndsWith(":out", exact, StringComparison.Ordinal);
    }

    private static void AssertBoundary(int baseMaxLength, Func<int, bool> validate)
    {
        Assert.True(validate(baseMaxLength), $"长度 {baseMaxLength} 的基础幂等键应当通过校验。");
        Assert.False(validate(baseMaxLength + 1), $"长度 {baseMaxLength + 1} 的基础幂等键应当被校验器拒绝。");
    }

    private static PostStockStatusTransferCommand StatusTransferCommand(string idempotencyKey)
    {
        return new PostStockStatusTransferCommand(
            "org-001",
            "env-dev",
            "unrestricted",
            "blocked",
            "quality",
            "DOC-IDEM-001",
            "LINE-001",
            idempotencyKey,
            "SKU-IDEM",
            "EA",
            "SITE-001",
            "LOC-A-01",
            "LOT-001",
            null,
            "owned",
            null,
            1m);
    }

    private static PostStockMovementCommand InboundMovementCommand(string idempotencyKey, decimal quantity)
    {
        return new PostStockMovementCommand(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            "DOC-IDEM-001",
            "LINE-001",
            idempotencyKey,
            "SKU-IDEM",
            "EA",
            "SITE-001",
            "LOC-A-01",
            "LOT-001",
            null,
            "unrestricted",
            "owned",
            null,
            quantity,
            UnitCost: 5m);
    }

    private static PostStockMovementCommand TransferMovementCommand(string idempotencyKey)
    {
        return InboundMovementCommand(idempotencyKey, -1m) with
        {
            MovementType = "transfer",
            TransferInLocationCode = "LOC-B-01",
            TransferInQuantity = 1m,
        };
    }

    private static ReserveFefoStockCommand FefoCommand(string idempotencyKey)
    {
        return new ReserveFefoStockCommand(
            "org-001",
            "env-dev",
            "wms",
            "DOC-IDEM-001",
            "LINE-001",
            idempotencyKey,
            "SKU-IDEM",
            "EA",
            "SITE-001",
            "unrestricted",
            "owned",
            null,
            1m,
            LocationCode: null);
    }

    private static async Task SeedLedgerAsync(
        ApplicationDbContext dbContext,
        string qualityStatus,
        decimal quantity,
        string locationCode = "LOC-A-01",
        DateOnly? expiryDate = null)
    {
        var ledger = StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-IDEM",
            "EA",
            "SITE-001",
            locationCode,
            "LOT-001",
            null,
            qualityStatus,
            "owned",
            null,
            null,
            expiryDate);
        var movement = StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            "DOC-IDEM-SEED",
            "LINE-001",
            $"seed-{locationCode}",
            "SKU-IDEM",
            "EA",
            "SITE-001",
            locationCode,
            "LOT-001",
            null,
            qualityStatus,
            "owned",
            null,
            quantity,
            5m,
            null,
            expiryDate);
        ledger.ApplyMovement(movement);
        dbContext.StockLedgers.Add(ledger);
        dbContext.StockMovements.Add(movement);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static ApplicationDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"inventory-idempotency-key-{name}-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static ModelFixture CreateModelFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddInventoryPostgreSqlPersistence("Host=localhost;Database=nerv_iip_idempotency_key_contract;Username=nerv;Password=nerv");
        return new ModelFixture(services.BuildServiceProvider());
    }

    private sealed class ModelFixture : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly IServiceScope scope;

        public ModelFixture(ServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
            scope = serviceProvider.CreateScope();
            DbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        }

        public ApplicationDbContext DbContext { get; }

        public void Dispose()
        {
            DbContext.Dispose();
            scope.Dispose();
            serviceProvider.Dispose();
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException("This test mediator only supports publish.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");
    }
}
