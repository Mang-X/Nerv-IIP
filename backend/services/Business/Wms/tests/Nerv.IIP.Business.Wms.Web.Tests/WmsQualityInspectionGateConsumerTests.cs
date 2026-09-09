using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Wms.Domain;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.SupplierReturnAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing.PostgreSql;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsQualityInspectionGateConsumerTests
{
    private const string PostgresConnectionStringEnvironmentVariable = "NERV_IIP_TEST_POSTGRES";

    private sealed record PostgresInspectionScenario(
        string EventType,
        string InboundOrderNo,
        string ExpectedGateStatus,
        bool AllowsPutaway,
        string TargetLocationCode);

    [Fact]
    public async Task Quality_passed_event_releases_wms_putaway_gate_for_received_stock()
    {
        var databaseName = nameof(Quality_passed_event_releases_wms_putaway_gate_for_received_stock);
        var databaseRoot = new InMemoryDatabaseRoot();
        await using (var dbContext = CreateContext(databaseName, databaseRoot))
        {
            var createdInbound = QualityRequiredInboundOrder("IN-QA-PASS-001");
            dbContext.InboundOrders.Add(createdInbound);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await new CompleteInboundOrderCommandHandler(dbContext, new WmsReceiptRouteFixture()).Handle(
                new CompleteInboundOrderCommand(createdInbound.Id, "idem-in-pass-001")
                    .TrustedFor(dbContext, createdInbound),
                CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            var handler = new QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(CreateInspectionEvent(QualityIntegrationEventTypes.InspectionPassed, "IN-QA-PASS-001"), CancellationToken.None);
        }

        await using var assertionContext = CreateContext(databaseName, databaseRoot);
        var persistedInbound = await assertionContext.InboundOrders.SingleAsync(x => x.InboundOrderNo == "IN-QA-PASS-001");
        Assert.Equal(InboundOrderStatus.Completed, persistedInbound.Status);
        var task = await new CreatePutawayTaskCommandHandler(assertionContext).Handle(
            new CreatePutawayTaskCommand(persistedInbound.Id, "PUT-QA-PASS-001", "LINE-001", "LOC-STAGE", "LOC-A-01", 5m),
            CancellationToken.None);
        await assertionContext.SaveChangesAsync(CancellationToken.None);
        Assert.True(await assertionContext.WarehouseTasks.AnyAsync(x => x.Id == task));
    }

    [Fact]
    public async Task Quality_rejected_event_keeps_putaway_blocked_and_records_supplier_return_fact()
    {
        var databaseName = nameof(Quality_rejected_event_keeps_putaway_blocked_and_records_supplier_return_fact);
        var databaseRoot = new InMemoryDatabaseRoot();
        await using (var dbContext = CreateContext(databaseName, databaseRoot))
        {
            var createdInbound = QualityRequiredInboundOrder("IN-QA-REJ-001");
            dbContext.InboundOrders.Add(createdInbound);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await new CompleteInboundOrderCommandHandler(dbContext, new WmsReceiptRouteFixture()).Handle(
                new CompleteInboundOrderCommand(createdInbound.Id, "idem-in-rej-001")
                    .TrustedFor(dbContext, createdInbound),
                CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            var handler = new QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(CreateInspectionEvent(QualityIntegrationEventTypes.InspectionRejected, "IN-QA-REJ-001"), CancellationToken.None);
        }

        await using var assertionContext = CreateContext(databaseName, databaseRoot);
        var persistedInbound = await assertionContext.InboundOrders.SingleAsync(x => x.InboundOrderNo == "IN-QA-REJ-001");
        var supplierReturn = await assertionContext.Set<SupplierReturnRequest>().SingleAsync();
        Assert.Equal("IN-QA-REJ-001", supplierReturn.InboundOrderNo);
        Assert.Equal("QI-001", supplierReturn.InspectionRecordId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CreatePutawayTaskCommandHandler(assertionContext).Handle(
            new CreatePutawayTaskCommand(persistedInbound.Id, "PUT-QA-REJ-001", "LINE-001", "LOC-STAGE", "LOC-A-01", 5m),
            CancellationToken.None));
    }

    [Fact]
    public async Task Quality_rejected_event_creates_supplier_return_outbound_with_immutable_receipt_reference()
    {
        var databaseName = nameof(Quality_rejected_event_creates_supplier_return_outbound_with_immutable_receipt_reference);
        var databaseRoot = new InMemoryDatabaseRoot();
        await using (var dbContext = CreateContext(databaseName, databaseRoot))
        {
            var createdInbound = QualityRequiredInboundOrder("IN-QA-RETURN-001");
            dbContext.InboundOrders.Add(createdInbound);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await new CompleteInboundOrderCommandHandler(dbContext, new WmsReceiptRouteFixture()).Handle(
                new CompleteInboundOrderCommand(createdInbound.Id, "idem-in-return-001")
                    .TrustedFor(dbContext, createdInbound),
                CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            var handler = new QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(
                CreateInspectionEvent(QualityIntegrationEventTypes.InspectionRejected, "IN-QA-RETURN-001"),
                CancellationToken.None);
        }

        await using var assertionContext = CreateContext(databaseName, databaseRoot);
        var outbound = Assert.Single(await assertionContext.OutboundOrders.Include(x => x.Lines).ToListAsync());
        Assert.Equal("RTS-IN-QA-RETURN-001-LINE-001-QI-001", outbound.OutboundOrderNo);
        Assert.Equal("purchase-receipt-return", outbound.SourceDocumentType);
        Assert.Equal("PO-001", outbound.SourceDocumentId);
        var line = Assert.Single(outbound.Lines);
        Assert.Equal("LINE-001", line.LineNo);
        Assert.Equal(5m, line.RequestedQuantity);
    }

    [Fact]
    public async Task Quality_conditional_release_event_allows_restricted_putaway_gate()
    {
        var databaseName = nameof(Quality_conditional_release_event_allows_restricted_putaway_gate);
        var databaseRoot = new InMemoryDatabaseRoot();
        await using (var dbContext = CreateContext(databaseName, databaseRoot))
        {
            var createdInbound = QualityRequiredInboundOrder("IN-QA-COND-001");
            dbContext.InboundOrders.Add(createdInbound);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await new CompleteInboundOrderCommandHandler(dbContext, new WmsReceiptRouteFixture()).Handle(
                new CompleteInboundOrderCommand(createdInbound.Id, "idem-in-cond-001")
                    .TrustedFor(dbContext, createdInbound),
                CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            var handler = new QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(CreateInspectionEvent(QualityIntegrationEventTypes.InspectionConditionalReleased, "IN-QA-COND-001"), CancellationToken.None);
        }

        await using var assertionContext = CreateContext(databaseName, databaseRoot);
        var persistedInbound = await assertionContext.InboundOrders.SingleAsync(x => x.InboundOrderNo == "IN-QA-COND-001");
        var task = await new CreatePutawayTaskCommandHandler(assertionContext).Handle(
            new CreatePutawayTaskCommand(persistedInbound.Id, "PUT-QA-COND-001", "LINE-001", "LOC-STAGE", "LOC-RESTRICTED-01", 5m),
            CancellationToken.None);
        await assertionContext.SaveChangesAsync(CancellationToken.None);
        Assert.True(await assertionContext.WarehouseTasks.AnyAsync(x => x.Id == task));
    }

    [Fact]
    public async Task Quality_divergence_event_is_dead_lettered_without_retry_exception()
    {
        var databaseName = nameof(Quality_divergence_event_is_dead_lettered_without_retry_exception);
        var databaseRoot = new InMemoryDatabaseRoot();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        await using (var dbContext = CreateContext(databaseName, databaseRoot))
        {
            var createdInbound = QualityRequiredInboundOrder("IN-QA-DIV-001");
            dbContext.InboundOrders.Add(createdInbound);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await new CompleteInboundOrderCommandHandler(dbContext, new WmsReceiptRouteFixture()).Handle(
                new CompleteInboundOrderCommand(createdInbound.Id, "idem-in-div-001")
                    .TrustedFor(dbContext, createdInbound),
                CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            var handler = new QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate(dbContext, deadLetters);
            await handler.HandleAsync(
                CreateInspectionEvent(QualityIntegrationEventTypes.InspectionPassed, "IN-QA-DIV-001", inspectedQuantity: 6m),
                CancellationToken.None);
        }

        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("quality-inspection-result-divergence", deadLetter.FailureCode);

        await using var assertionContext = CreateContext(databaseName, databaseRoot);
        var persistedInbound = await assertionContext.InboundOrders.SingleAsync(x => x.InboundOrderNo == "IN-QA-DIV-001");
        Assert.Equal(InboundOrderStatus.PendingQualityCheck, persistedInbound.Status);
    }

    [WmsRealPostgresFact]
    public async Task Quality_events_persist_gate_and_putaway_outcome_on_postgres()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)!;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(postgresConnectionString, "nerv_wms_quality_gate");

        await using (var dbContext = CreatePostgresContext(database.ConnectionString))
        {
            await dbContext.Database.MigrateAsync();
            var handler = new QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());

            foreach (var scenario in PostgresInspectionScenarios())
            {
                var createdInbound = QualityRequiredInboundOrder(scenario.InboundOrderNo);
                dbContext.InboundOrders.Add(createdInbound);
                await dbContext.SaveChangesAsync(CancellationToken.None);
                await new CompleteInboundOrderCommandHandler(dbContext, new WmsReceiptRouteFixture()).Handle(
                    new CompleteInboundOrderCommand(createdInbound.Id, $"idem-{scenario.InboundOrderNo.ToLowerInvariant()}")
                        .TrustedFor(dbContext, createdInbound),
                    CancellationToken.None);
                await dbContext.SaveChangesAsync(CancellationToken.None);

                await handler.HandleAsync(
                    CreateInspectionEvent(scenario.EventType, scenario.InboundOrderNo),
                    CancellationToken.None);
            }
        }

        await using var assertionContext = CreatePostgresContext(database.ConnectionString);
        foreach (var scenario in PostgresInspectionScenarios())
        {
            var persistedInbound = await assertionContext.InboundOrders
                .Include(x => x.Lines)
                .SingleAsync(x => x.InboundOrderNo == scenario.InboundOrderNo);
            var persistedLine = Assert.Single(persistedInbound.Lines);
            Assert.Equal(InboundOrderStatus.Completed, persistedInbound.Status);
            Assert.Equal(scenario.ExpectedGateStatus, persistedLine.QualityGateStatus);
            Assert.Equal("QI-001", persistedLine.InspectionRecordId);

            if (scenario.AllowsPutaway)
            {
                var task = await new CreatePutawayTaskCommandHandler(assertionContext).Handle(
                    new CreatePutawayTaskCommand(persistedInbound.Id, $"PUT-{scenario.InboundOrderNo}", "LINE-001", "LOC-STAGE", scenario.TargetLocationCode, 5m),
                    CancellationToken.None);
                await assertionContext.SaveChangesAsync(CancellationToken.None);
                Assert.True(await assertionContext.WarehouseTasks.AnyAsync(x => x.Id == task));
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => new CreatePutawayTaskCommandHandler(assertionContext).Handle(
                    new CreatePutawayTaskCommand(persistedInbound.Id, $"PUT-{scenario.InboundOrderNo}", "LINE-001", "LOC-STAGE", scenario.TargetLocationCode, 5m),
                    CancellationToken.None));
            }
        }

        var supplierReturn = await assertionContext.SupplierReturnRequests.SingleAsync();
        Assert.Equal("IN-QA-PG-REJ-001", supplierReturn.InboundOrderNo);
        Assert.Equal("QI-001", supplierReturn.InspectionRecordId);
    }

    /// <summary>
    /// 退供单号必须塞得进 <c>outbound_order_no</c>（#3228）。**只能在真 Postgres 上跑**——
    /// EF InMemory 看不见列宽，同一条用例在 InMemory 下恒绿。
    /// </summary>
    /// <remarks>
    /// 两个夹具走的是两条**互不等价**的输入区间：
    /// ① 朴素拼法恰好等于 outbound 列宽 —— 可读形态必须原样保留（挡「一律走摘要」）；
    /// ② 朴素拼法只超出 outbound 列宽 1 个字符 —— 这是**唯一**能区分「上界取两列最小值」与
    ///    「上界取退供自己那列的 300」的区间，落在 (100, 300] 里。
    ///
    /// **这里没有「各组件顶格」那一格**（朴素拼法 356）：实测它在 100 与 300 两种上界下都回落到
    /// 摘要形态，是一组等价输入，跨两轮变异从未单独承重，却要在真库上多付一次建单往返。
    /// 它作为零成本的回归护栏留在 <c>WmsOperationalCodeKindTests</c> 的 theory 里。
    /// </remarks>
    [WmsRealPostgresFact]
    public async Task Supplier_return_numbers_stay_within_the_outbound_order_no_column_on_postgres()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)!;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(postgresConnectionString, "nerv_wms_return_no_bound");

        IReadOnlyList<SupplierReturnBoundaryCase> cases;
        int outboundOrderNoColumnMaxLength;

        await using (var dbContext = CreatePostgresContext(database.ConnectionString))
        {
            await dbContext.Database.MigrateAsync();
            outboundOrderNoColumnMaxLength = ColumnMaxLength(dbContext, typeof(OutboundOrder), nameof(OutboundOrder.OutboundOrderNo));
            cases = SupplierReturnBoundaryCases(dbContext, outboundOrderNoColumnMaxLength);

            foreach (var boundaryCase in cases)
            {
                var createdInbound = QualityRequiredInboundOrder(boundaryCase.InboundOrderNo, boundaryCase.LineNo);
                dbContext.InboundOrders.Add(createdInbound);
                await dbContext.SaveChangesAsync(CancellationToken.None);
                await new CompleteInboundOrderCommandHandler(dbContext, new WmsReceiptRouteFixture()).Handle(
                    new CompleteInboundOrderCommand(createdInbound.Id, $"idem-in-bound-{boundaryCase.Name}")
                        .TrustedFor(dbContext, createdInbound),
                    CancellationToken.None);
                await dbContext.SaveChangesAsync(CancellationToken.None);

                var handler = new QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate(
                    dbContext,
                    new InMemoryIntegrationEventDeadLetterStore());
                await handler.HandleAsync(
                    CreateInspectionEvent(
                        QualityIntegrationEventTypes.InspectionRejected,
                        boundaryCase.InboundOrderNo,
                        inspectionRecordId: boundaryCase.InspectionRecordId),
                    CancellationToken.None);
                dbContext.ChangeTracker.Clear();
            }
        }

        await using var assertionContext = CreatePostgresContext(database.ConnectionString);
        Assert.Equal(cases.Count, await assertionContext.SupplierReturnRequests.CountAsync());

        foreach (var boundaryCase in cases)
        {
            var expectedNo = SupplierReturnRequest.ComposeSupplierReturnNo(
                boundaryCase.InboundOrderNo,
                boundaryCase.LineNo,
                boundaryCase.InspectionRecordId);
            var supplierReturn = await assertionContext.SupplierReturnRequests
                .SingleAsync(x => x.InboundOrderNo == boundaryCase.InboundOrderNo);
            var outbound = await assertionContext.OutboundOrders
                .SingleAsync(x => x.OutboundOrderNo == supplierReturn.SupplierReturnNo);

            // 有界构造必须仍可复算：重放同一份检验结论要落到同一张退供单/出库单上。
            Assert.Equal(expectedNo, supplierReturn.SupplierReturnNo);
            Assert.True(
                outbound.OutboundOrderNo.Length <= outboundOrderNoColumnMaxLength,
                $"[{boundaryCase.Name}] 退供派生出库单号长度 {outbound.OutboundOrderNo.Length} 超出承载列宽 {outboundOrderNoColumnMaxLength}。");
            Assert.StartsWith($"{WmsOperationalCodeKind.SupplierReturn.Prefix}-", outbound.OutboundOrderNo, StringComparison.Ordinal);

            if (boundaryCase.KeepsReadableForm)
            {
                Assert.Equal(boundaryCase.NaiveComposition, supplierReturn.SupplierReturnNo);
            }
            else
            {
                Assert.NotEqual(boundaryCase.NaiveComposition, supplierReturn.SupplierReturnNo);
            }
        }
    }

    private sealed record SupplierReturnBoundaryCase(
        string Name,
        string InboundOrderNo,
        string LineNo,
        string InspectionRecordId,
        bool KeepsReadableForm)
    {
        public string NaiveComposition => $"RTS-{InboundOrderNo}-{LineNo}-{InspectionRecordId}";
    }

    private static IReadOnlyList<SupplierReturnBoundaryCase> SupplierReturnBoundaryCases(
        ApplicationDbContext dbContext,
        int outboundOrderNoColumnMaxLength)
    {
        var inboundOrderNoMax = ColumnMaxLength(dbContext, typeof(InboundOrder), nameof(InboundOrder.InboundOrderNo));

        // "RTS-" + 入库单号 + "-" + 行号 + "-" + 检验记录号：固定开销 6 个字符。
        const int FixedOverhead = 6;
        const string LineNo = "LINE-001";
        // 两条短用例的检验记录号必须彼此不同（消费者按检验记录号构成幂等键，同号第二条会被收件箱吞掉），
        // 但长度须相等，否则朴素拼法的长度算术就对不上。
        const string ExactInspectionRecordId = "QI-001";
        const string OverByOneInspectionRecordId = "QI-002";

        var exactInboundLength = outboundOrderNoColumnMaxLength - FixedOverhead - LineNo.Length - ExactInspectionRecordId.Length;
        Assert.InRange(exactInboundLength, 3, inboundOrderNoMax);

        var cases = new[]
        {
            new SupplierReturnBoundaryCase(
                "exact",
                "EX-" + new string('E', exactInboundLength - 3),
                LineNo,
                ExactInspectionRecordId,
                KeepsReadableForm: true),
            new SupplierReturnBoundaryCase(
                "over-by-one",
                "OV-" + new string('O', exactInboundLength - 2),
                LineNo,
                OverByOneInspectionRecordId,
                KeepsReadableForm: false),
        };

        Assert.Equal(outboundOrderNoColumnMaxLength, cases[0].NaiveComposition.Length);
        Assert.Equal(outboundOrderNoColumnMaxLength + 1, cases[1].NaiveComposition.Length);
        // ② 必须落在 (outbound 列宽, supplier_return 列宽] 里，否则它退化成与「顶格」等价的输入。
        Assert.InRange(
            cases[1].NaiveComposition.Length,
            outboundOrderNoColumnMaxLength + 1,
            WmsOperationalCodeKind.SupplierReturnNoColumnMaxLength);
        return cases;
    }

    private static int ColumnMaxLength(ApplicationDbContext dbContext, Type entityType, string propertyName)
    {
        var maxLength = dbContext.Model.FindEntityType(entityType)!.FindProperty(propertyName)!.GetMaxLength();
        Assert.True(maxLength.HasValue, $"{entityType.Name}.{propertyName} 没有配置列宽，夹具无法顶格。");
        return maxLength!.Value;
    }

    private static IReadOnlyCollection<PostgresInspectionScenario> PostgresInspectionScenarios()
    {
        return
        [
            new PostgresInspectionScenario(
                QualityIntegrationEventTypes.InspectionPassed,
                "IN-QA-PG-PASS-001",
                InboundQualityGateStatuses.Passed,
                true,
                "LOC-A-01"),
            new PostgresInspectionScenario(
                QualityIntegrationEventTypes.InspectionConditionalReleased,
                "IN-QA-PG-COND-001",
                InboundQualityGateStatuses.ConditionalReleased,
                true,
                "LOC-RESTRICTED-01"),
            new PostgresInspectionScenario(
                QualityIntegrationEventTypes.InspectionRejected,
                "IN-QA-PG-REJ-001",
                InboundQualityGateStatuses.Rejected,
                false,
                "LOC-A-01"),
        ];
    }

    private static InboundOrder QualityRequiredInboundOrder(string inboundOrderNo, string lineNo = "LINE-001")
    {
        return InboundOrder.Create(
            "org-001",
            "env-dev",
            inboundOrderNo,
            "purchase-receipt",
            "PO-001",
            "SITE-01",
            [new InboundOrderLineDraft(lineNo, "SKU-FG-1000", "kg", 5m, "LOC-STAGE", "LOT-001", null, "quality", "company", "owner-001")]);
    }

    private static InspectionResultIntegrationEvent CreateInspectionEvent(
        string eventType,
        string inboundOrderNo,
        decimal inspectedQuantity = 5m,
        string inspectionRecordId = "QI-001")
    {
        var result = eventType == QualityIntegrationEventTypes.InspectionPassed
            ? "passed"
            : eventType == QualityIntegrationEventTypes.InspectionConditionalReleased
                ? "conditional-release"
                : "rejected";
        return new InspectionResultIntegrationEvent(
            "quality-event-001",
            eventType,
            QualityIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            QualityIntegrationEventSources.BusinessQuality,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "system:quality",
            $"quality:inspection-result:org-001:env-dev:{inspectionRecordId}:{eventType}",
            new InspectionResultPayload(
                inspectionRecordId,
                "PLAN-001",
                "receiving",
                "wms",
                inboundOrderNo,
                "SKU-FG-1000",
                inspectedQuantity,
                result,
                eventType == QualityIntegrationEventTypes.InspectionRejected ? "critical-defect" : null,
                [],
                DateTimeOffset.UtcNow,
                new StockReleaseDimensionPayload("kg", "SITE-01", "LOC-STAGE", "LOT-001", null, "quality", "company", "owner-001")));
    }

    private static ApplicationDbContext CreateContext(string databaseName, InMemoryDatabaseRoot databaseRoot)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static ApplicationDbContext CreatePostgresContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "wms"))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class WmsRealPostgresFactAttribute : FactAttribute
    {
        public WmsRealPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)))
            {
                Skip = $"Set {PostgresConnectionStringEnvironmentVariable} to run this real PostgreSQL WMS quality gate consumer test.";
            }
        }
    }
}
