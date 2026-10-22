using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Inventory.Domain;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockStatusTransfers;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.DomainEvents;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;
using InventoryDbContext = Nerv.IIP.Business.Inventory.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

/// <summary>
/// #2976：Quality 发布 → Inventory 消费的来源环节门。
///
/// 为什么必须跑真实 PostgreSQL：本用例要钉住的失败之一是**列宽溢出**
/// （<c>stock_movements.idempotency_key</c> 是 <c>varchar(128)</c>，而消费者把校验器放行的幂等键再追加
/// <c>:out</c>/<c>:in</c> 才落库）。EF InMemory 不带列长约束，在它上面这条永远是绿的，不承重。
///
/// 为什么走完整管道：命令上界由 FluentValidation 校验器承担，而校验器只在 MediatR 管道
/// （<c>AddKnownExceptionValidationBehavior</c>）里生效。直接 new CommandHandler 会绕过它。
/// </summary>
[Collection(AcceptancePostgresLaneDatabase.CollectionName)]
public sealed class QualityInspectionInventoryStockGateAcceptanceTests
{
    private const string Org = "org-001";
    private const string Env = "env-dev";
    private const string Sku = "SKU-FG-1000";

    /// <summary>标准编码规则产出的工单号：<c>StandardCodeRules</c> 的 <c>work-order</c> 规则 = <c>WO-yyyyMMdd-nnnnnn</c>。</summary>
    private const string GeneratedWorkOrderId = "WO-20260906-000001";

    /// <summary>工序任务号在 <c>ConvertPlanToWorkOrderCommandHandler</c> 里派生自工单号：<c>{workOrderId}-OP-10</c>。</summary>
    private const string GeneratedOperationTaskId = GeneratedWorkOrderId + "-OP-10";

    /// <summary>
    /// 命令校验器允许的工单号/工序号上界都是 100（<c>MesProductionCommands.cs:121-122</c>），
    /// 且 <c>CodeAllocator</c> 对外部指定的编码不做规则校验，所以 100 + ':' + 100 = **201** 是可构造的真上界。
    /// </summary>
    private static readonly string LongWorkOrderId = "WO-" + new string('9', 97);

    private static readonly string LongOperationTaskId = "OP-" + new string('9', 97);

    // 两条身份写成两个独立 Fact，不是一个 Theory、也不是一个循环，有两条硬理由：
    // ① 门被移除时它们**炸在不同的地方**（标准编码炸在 idempotency_key 列宽，上界身份炸在命令校验器），
    //    合成一条会让先失败的那次把后一条的鉴别力吞掉，变异矩阵里只剩一个红格；
    // ② PostgreSQL lane 的冻结身份契约（scripts/lib/PostgresTestLane.ps1 的 Get-NervPostgresTrxResult）
    //    用 TRX 的 <c>TestMethod/@className + "." + @name</c> 作身份，而 xUnit Theory 的每个 InlineData
    //    在 TRX 里都是**同一个** name —— 实测同名两条，identitiesMatch 直接判假、整个 lane 成员报错。
    [RealPostgresFact]
    public async Task Postgres_first_article_inspection_result_with_generated_codes_never_reaches_inventory_stock_commands() =>
        await AssertFirstArticleIsGatedAsync(GeneratedWorkOrderId, GeneratedOperationTaskId, "standard-generated", expectsOverlongSourceDocumentId: false);

    [RealPostgresFact]
    public async Task Postgres_first_article_inspection_result_at_validator_upper_bound_never_reaches_inventory_stock_commands() =>
        await AssertFirstArticleIsGatedAsync(LongWorkOrderId, LongOperationTaskId, "validator-upper-bound", expectsOverlongSourceDocumentId: true);

    /// <summary>
    /// #3186 复审：上面两条首件用例的「零条 status-transfer 流水」断言**在它们自己身上不可达**——
    /// 两条身份的幂等键都必然超长，门一旦失效就先撞 <c>idempotency_key</c> 的 <c>varchar(128)</c>，
    /// 红在过账那一步，永远走不到流水断言。也就是说那条断言是**装饰**。
    ///
    /// 本条用短身份把它变成**真正可达**：幂等键不超列宽、来源单据号不超校验器上界，门失效时会
    /// **成功过账 2 条流水**，于是 <c>Assert.Empty</c> 成为真实的杀死点。
    /// 前置条件写成断言，防止将来编码规则变化后本用例悄悄退化回不可达。
    /// </summary>
    [RealPostgresFact]
    public async Task Postgres_first_article_inspection_result_with_short_identity_posts_no_stock_movement()
    {
        await AcceptancePostgresLaneDatabase.ResetSchemaAsync(InventoryFacts.Schema);
        await using var provider = CreateInventoryPostgresProvider();
        await MigrateAsync(provider);
        await SeedQualityStockAsync(provider, 5m);

        var integrationEvent = FirstArticleInspectionPassedEvent("WO-1", "OP-1");

        // 前置读数：本条**必须**落在列宽与校验器上界之内，否则它又变回不可达。
        Assert.True(
            integrationEvent.IdempotencyKey.Length + ":out".Length <= InventoryIdempotencyKeyColumnLength,
            $"幂等键 {integrationEvent.IdempotencyKey.Length} + 4 必须不超过 {InventoryIdempotencyKeyColumnLength} 列宽，否则本用例退化为不可达。");
        Assert.True(
            integrationEvent.Payload.SourceDocumentId.Length <= InventorySourceDocumentIdMaxLength,
            $"来源单据号 {integrationEvent.Payload.SourceDocumentId.Length} 必须不超过 {InventorySourceDocumentIdMaxLength}。");

        var logger = new RecordingLogger<QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer>();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        AcceptancePostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        var handler = new QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer(
            scope.ServiceProvider.GetRequiredService<ISender>(),
            db,
            new InMemoryIntegrationEventDeadLetterStore(),
            logger);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        await using var assertScope = provider.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        // 门失效时这里会读到 2 条——这才是本条的杀死点。
        Assert.Empty(await assertDb.StockMovements
            .Where(x => x.MovementType.StartsWith("status-transfer"))
            .ToListAsync());
        var ledger = await assertDb.StockLedgers.SingleAsync(x => x.QualityStatus == StockQualityStatus.Quality);
        Assert.Equal(5m, ledger.OnHandQuantity);
        Assert.Single(await assertDb.StockLedgers.ToListAsync());

        var skip = Assert.Single(logger.Entries);
        Assert.Contains(integrationEvent.EventId, skip.Message, StringComparison.Ordinal);
    }

    private async Task AssertFirstArticleIsGatedAsync(
        string workOrderId,
        string operationTaskId,
        string label,
        bool expectsOverlongSourceDocumentId)
    {
        {
            await AcceptancePostgresLaneDatabase.ResetSchemaAsync(InventoryFacts.Schema);
            await using var provider = CreateInventoryPostgresProvider();
            await MigrateAsync(provider);
            await SeedQualityStockAsync(provider, 5m);

            var integrationEvent = FirstArticleInspectionPassedEvent(workOrderId, operationTaskId);
            // 前置读数：这两个长度就是缺陷的成因，写进断言以免将来编码规则变化后本用例悄悄失去针对性。
            Assert.Equal($"{workOrderId}:{operationTaskId}", integrationEvent.Payload.SourceDocumentId);
            Assert.True(
                integrationEvent.IdempotencyKey.Length + ":out".Length > InventoryIdempotencyKeyColumnLength,
                $"[{label}] 幂等键 {integrationEvent.IdempotencyKey.Length} + 4 应超过 {InventoryIdempotencyKeyColumnLength} 列宽，否则本用例不再覆盖 #2976 的成因。");
            Assert.Equal(
                expectsOverlongSourceDocumentId,
                integrationEvent.Payload.SourceDocumentId.Length > InventorySourceDocumentIdMaxLength);
            if (expectsOverlongSourceDocumentId)
            {
                // 验收标准写死 201：两段各取校验器上界 100，加一个分隔符。
                Assert.Equal(201, integrationEvent.Payload.SourceDocumentId.Length);
            }

            var logger = new RecordingLogger<QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer>();
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            AcceptancePostgresLaneDatabase.AssertUsesGovernedDatabase(db);
            var handler = new QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer(
                scope.ServiceProvider.GetRequiredService<ISender>(),
                db,
                new InMemoryIntegrationEventDeadLetterStore(),
                logger);

            // 不产生 poison message：消费者正常返回，不抛出。
            await handler.HandleAsync(integrationEvent, CancellationToken.None);

            await using var assertScope = provider.CreateAsyncScope();
            var assertDb = assertScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            Assert.Empty(await assertDb.StockMovements
                .Where(x => x.MovementType.StartsWith("status-transfer"))
                .ToListAsync());
            var ledger = await assertDb.StockLedgers.SingleAsync(x => x.QualityStatus == StockQualityStatus.Quality);
            Assert.Equal(5m, ledger.OnHandQuantity);
            Assert.Single(await assertDb.StockLedgers.ToListAsync());

            // gate-and-skip 不是静默丢弃：被挡的事件在流水/ledger/DLQ 三处都不留任何记录，
            // 这条日志是它**唯一**的可追踪痕迹，且必须点出是哪一条事件（EventId）、
            // 哪个来源环节被挡的。删掉它等于把一次丢弃变成零记录。
            var skip = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Information, skip.Level);
            Assert.Contains(QualityInspectionInventoryStockGateFacts.FirstArticleSourceType, skip.Message, StringComparison.Ordinal);
            Assert.Contains(QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer.ConsumerName, skip.Message, StringComparison.Ordinal);
            Assert.Contains(integrationEvent.EventId, skip.Message, StringComparison.Ordinal);
        }
    }

    [RealPostgresFact]
    public async Task Postgres_receiving_inspection_result_still_transfers_quality_stock()
    {
        // 反向对照：门只挡不承载库存的来源环节。若把门写成把所有来源都挡掉，这条会红。
        await AcceptancePostgresLaneDatabase.ResetSchemaAsync(InventoryFacts.Schema);
        await using var provider = CreateInventoryPostgresProvider();
        await MigrateAsync(provider);
        await SeedQualityStockAsync(provider, 5m);

        var integrationEvent = ReceivingInspectionPassedEvent("GR-20260906-000001");
        var logger = new RecordingLogger<QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer>();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var handler = new QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer(
            scope.ServiceProvider.GetRequiredService<ISender>(),
            db,
            new InMemoryIntegrationEventDeadLetterStore(),
            logger);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        await using var assertScope = provider.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Equal(2, await assertDb.StockMovements.CountAsync(x => x.MovementType.StartsWith("status-transfer")));
        Assert.Equal(
            4m,
            (await assertDb.StockLedgers.SingleAsync(x => x.QualityStatus == StockQualityStatus.Unrestricted)).OnHandQuantity);
        Assert.Equal(
            1m,
            (await assertDb.StockLedgers.SingleAsync(x => x.QualityStatus == StockQualityStatus.Quality)).OnHandQuantity);
        // 放行侧不得留下「被挡」的痕迹。流水断言接不住这一维：把 gate 条件放宽却忘了 return 时，
        // 事件**照样过账 2 条流水**（流水断言全绿），只是留下一条撒谎的痕迹——只有这条能看见。
        Assert.Empty(logger.Entries);
    }

    /// <summary>
    /// 放行侧的完整覆盖：<c>QualityInspectionSourceTypes.All</c> 里除首件外的**每一个**取值都必须
    /// 真的走到状态转移。少了这条，把门写宽到吃掉 <c>operation</c> / <c>final</c> /
    /// <c>maintenance</c> / <c>customer-return</c> 任意一个，整套用例都不会红——
    /// 「门没写太宽」这个命题此前只对 <c>receiving</c> 一个取值成立。
    /// </summary>
    [RealPostgresFact]
    public async Task Postgres_every_stock_bearing_inspection_source_type_still_transfers_quality_stock()
    {
        var stockBearing = QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer
            .StockBearingSourceTypes
            .Order(StringComparer.Ordinal)
            .ToArray();
        // 覆盖面不写死在本用例里：它由生产代码的分类决定，分类扩了这里自动跟着扩。
        Assert.Equal(
            QualityInspectionSourceTypes.All.Count - 1,
            stockBearing.Length);

        foreach (var sourceType in stockBearing)
        {
            await AcceptancePostgresLaneDatabase.ResetSchemaAsync(InventoryFacts.Schema);
            await using var provider = CreateInventoryPostgresProvider();
            await MigrateAsync(provider);
            await SeedQualityStockAsync(provider, 5m);

            var integrationEvent = InspectionPassedEvent(sourceType, SourceServiceFor(sourceType), $"DOC-{sourceType}-001");
            var logger = new RecordingLogger<QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer>();
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var handler = new QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer(
                scope.ServiceProvider.GetRequiredService<ISender>(),
                db,
                new InMemoryIntegrationEventDeadLetterStore(),
                logger);

            await handler.HandleAsync(integrationEvent, CancellationToken.None);

            await using var assertScope = provider.CreateAsyncScope();
            var assertDb = assertScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            Assert.True(
                2 == await assertDb.StockMovements.CountAsync(x => x.MovementType.StartsWith("status-transfer")),
                $"来源环节 '{sourceType}' 被挡住了：本次没有产生状态转移流水。");
            Assert.True(logger.Entries.Count == 0, $"来源环节 '{sourceType}' 不该被 gate-and-skip 留痕。");
        }
    }

    /// <summary>来源服务取值域见 <c>InspectionRecord.SourceServices</c>；这里只挑一个该来源环节说得通的。</summary>
    private static string SourceServiceFor(string sourceType) => sourceType switch
    {
        QualityInspectionSourceTypes.Receiving => QualityInspectionSourceTypes.Wms,
        QualityInspectionSourceTypes.Maintenance => "maintenance",
        QualityInspectionSourceTypes.CustomerReturn => "customer-return",
        _ => "mes",
    };

    /// <summary><c>stock_movements.idempotency_key</c> 的列宽，见 StockMovementEntityTypeConfiguration。</summary>
    private const int InventoryIdempotencyKeyColumnLength = 128;

    /// <summary><c>PostStockStatusTransferCommandValidator</c> 对来源单据身份的上界。</summary>
    private const int InventorySourceDocumentIdMaxLength = 150;

    private static InspectionResultIntegrationEvent FirstArticleInspectionPassedEvent(string workOrderId, string operationTaskId)
    {
        // 来源单据身份由生产代码拼（#2779），不在测试里手抄，否则 Quality 改了拼法本用例不会红。
        var record = InspectionRecord.Create(
            Org,
            Env,
            null,
            FirstArticleInspection.SourceType,
            FirstArticleInspection.SourceService,
            FirstArticleInspection.SourceDocumentId(workOrderId, operationTaskId),
            sourceDocumentLineId: null,
            Sku,
            1m,
            null,
            null,
            [new InspectionResultLineInput("appearance", "ok", null, InspectionLineResults.Passed, null, null, [])],
            null,
            [],
            // 带上库存维度，让消费者走「生产者已经指明库存位置」那条分支：门被移除时会真的去发状态转移命令，
            // 从而在校验器/列宽上炸出来。若不带维度，失败会落到「解析不出唯一台账」那条既有 KnownException 上，
            // 变异就分不清红的是本票的门还是那条既有分支。
            StockReleaseDimension.Create("kg", "SITE-01", "LOC-A-01", StockQualityStatus.Quality, "company", "owner-001"));
        return Convert(record);
    }

    private static InspectionResultIntegrationEvent ReceivingInspectionPassedEvent(string receiptNo) =>
        InspectionPassedEvent(QualityInspectionSourceTypes.Receiving, QualityInspectionSourceTypes.Wms, receiptNo);

    private static InspectionResultIntegrationEvent InspectionPassedEvent(string sourceType, string sourceService, string sourceDocumentId)
    {
        var record = InspectionRecord.Create(
            Org,
            Env,
            null,
            sourceType,
            sourceService,
            sourceDocumentId,
            sourceDocumentLineId: null,
            Sku,
            4m,
            null,
            null,
            [new InspectionResultLineInput("appearance", "ok", null, InspectionLineResults.Passed, null, null, [])],
            null,
            [],
            StockReleaseDimension.Create("kg", "SITE-01", "LOC-A-01", StockQualityStatus.Quality, "company", "owner-001"));
        return Convert(record);
    }

    private static InspectionResultIntegrationEvent Convert(InspectionRecord record) =>
        new InspectionPassedIntegrationEventConverter(new FixedQualityIntegrationEventContextAccessor())
            .Convert(new InspectionPassedDomainEvent(record));

    private static ServiceProvider CreateInventoryPostgresProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(configuration => configuration
            .RegisterServicesFromAssembly(typeof(PostStockStatusTransferCommand).Assembly)
            .AddKnownExceptionValidationBehavior()
            .AddUnitOfWorkBehaviors());
        // AddKnownExceptionValidationBehavior 只装管道，校验器实例要靠 AddValidatorsFromAssembly 注册
        // （生产侧在 Inventory.Web/Program.cs:65）。少这一行，管道拿到空校验器集合直接放行，
        // 「走了 FluentValidation」这句就是假的——本用例此前正是这样，190 字符的身份没被校验器拒，
        // 一路走到 source_document_id(150) 才炸。
        services.AddValidatorsFromAssembly(typeof(PostStockStatusTransferCommand).Assembly);
        services.AddIntegrationEvents(typeof(StockAvailabilityChangedIntegrationEventConverter));
        services.AddInventoryPostgreSqlPersistence(AcceptancePostgresLaneDatabase.ConnectionString);
        services.AddSingleton<IIntegrationEventPublisher, NoopStockGateIntegrationEventPublisher>();
        services.AddSingleton<IInventoryIntegrationEventContextAccessor, FixedStockGateInventoryIntegrationEventContextAccessor>();
        return services.BuildServiceProvider();
    }

    private static async Task MigrateAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.MigrateAsync();
    }

    private static async Task SeedQualityStockAsync(ServiceProvider provider, decimal quantity)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var ledger = StockLedger.Create(
            Org, Env, Sku, "kg", "SITE-01", "LOC-A-01", null, null, StockQualityStatus.Quality, "company", "owner-001");
        ledger.ApplyMovement(StockMovement.Post(
            Org, Env, "inbound", "wms", "IN-2976-001", "LINE-001", "idem-2976-seed-in",
            Sku, "kg", "SITE-01", "LOC-A-01", null, null, StockQualityStatus.Quality, "company", "owner-001",
            quantity, 2m));
        db.StockLedgers.Add(ledger);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private sealed class FixedQualityIntegrationEventContextAccessor : IQualityIntegrationEventContextAccessor
    {
        public QualityIntegrationEventContext GetContext() => new("corr-2976", "cause-2976", "system:quality");
    }

    private sealed class FixedStockGateInventoryIntegrationEventContextAccessor : IInventoryIntegrationEventContextAccessor
    {
        public InventoryIntegrationEventContext GetContext() => new("corr-2976", "cause-2976", "system:test");
    }

    private sealed class NoopStockGateIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public Task PublishAsync<TIntegrationEvent>(TIntegrationEvent eventData, CancellationToken cancellationToken = default)
            where TIntegrationEvent : notnull => Task.CompletedTask;
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }
}

internal static class QualityInspectionInventoryStockGateFacts
{
    internal const string FirstArticleSourceType = "first-article";
}
