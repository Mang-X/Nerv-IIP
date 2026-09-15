using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountReceivableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesReturnAuthorizationAggregate;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;
using Nerv.IIP.Business.Erp.Domain;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;
using Nerv.IIP.Business.Wms.Domain;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing.PostgreSql;
using ErpDbContext = Nerv.IIP.Business.Erp.Infrastructure.ApplicationDbContext;
using WmsDbContext = Nerv.IIP.Business.Wms.Infrastructure.ApplicationDbContext;
using ErpSalesReturnAuthorizedDomainEvent = Nerv.IIP.Business.Erp.Domain.DomainEvents.SalesReturnAuthorizedDomainEvent;
using WmsInboundOrderCompletedDomainEvent = Nerv.IIP.Business.Wms.Domain.DomainEvents.InboundOrderCompletedDomainEvent;
using WmsOutboundOrderCompletedDomainEvent = Nerv.IIP.Business.Wms.Domain.DomainEvents.OutboundOrderCompletedDomainEvent;

namespace Nerv.IIP.Business.FullChain.Tests;

public sealed class ErpReturnClosurePostgresAcceptanceTests
{
    [RealPostgresFact]
    public async Task Purchase_return_and_sales_rma_close_through_real_postgres_contexts_with_replay_safety()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            baseConnectionString,
            "nerv_fullchain_erp_return_closure");
        try
        {
            await using var erpDb = CreateErpContext(database.ConnectionString);
            await using var wmsDb = CreateWmsContext(database.ConnectionString);
            database.AssertOwns(erpDb.Database.GetConnectionString());
            database.AssertOwns(wmsDb.Database.GetConnectionString());
            await erpDb.Database.MigrateAsync();
            await wmsDb.Database.MigrateAsync();

            var erpDeadLetters = new InMemoryIntegrationEventDeadLetterStore();
            var wmsDeadLetters = new InMemoryIntegrationEventDeadLetterStore();

            await SeedUninvoicedPurchaseReceiptAsync(erpDb);
            var purchaseInbound = InboundOrder.Create(
            "org-001",
            "env-dev",
            "WMS-IN-RETURN-PG-001",
            WmsSourceDocumentTypes.PurchaseReceipt,
            "GR-RETURN-PG-001",
            "SITE-01",
            [new InboundOrderLineDraft("LINE-001", "SKU-RETURN-PG-001", "EA", 1m, "LOC-QA", null, null, "quality", "company", null)]);
            wmsDb.InboundOrders.Add(purchaseInbound);
            await wmsDb.SaveChangesAsync(CancellationToken.None);
            purchaseInbound.Complete(
            "wms-complete:return:purchase:001",
            purchaseInbound.Version);
            await wmsDb.SaveChangesAsync(CancellationToken.None);

            var rejectedQualityEvent = QualityEvent(
            QualityIntegrationEventTypes.InspectionRejected,
            purchaseInbound.InboundOrderNo,
            "quality-rejected:return:purchase:001",
            "SKU-RETURN-PG-001");
            var qualityGateHandler = new QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate(wmsDb, wmsDeadLetters);
            await qualityGateHandler.HandleAsync(rejectedQualityEvent, CancellationToken.None);
            await wmsDb.SaveChangesAsync(CancellationToken.None);
            var supplierReturnOutbound = await wmsDb.OutboundOrders.SingleAsync(x => x.SourceDocumentType == WmsSourceDocumentTypes.PurchaseReceiptReturn);
            supplierReturnOutbound.CompletePackReview(
            "PACK-RETURN-PG-001",
            true,
            "wms-complete:return:purchase:outbound:001",
            supplierReturnOutbound.Version);
            await wmsDb.SaveChangesAsync(CancellationToken.None);

            var purchaseReturnEvent = new OutboundOrderCompletedIntegrationEventConverter()
                .Convert(new WmsOutboundOrderCompletedDomainEvent(supplierReturnOutbound));
            // ⭐ #3278 / S7：两个消费者必须共用**同一个**分配器。
            // 无参 new ErpCodingService() 的计数器挂在实例上，各给一个新实例时两边都从
            // JV-yyyyMMdd-000001 起号，在真库上直接撞
            // IX_journal_vouchers_organization_id_environment_id_voucher_no（23505）。
            // 改短号前两边的凭证号分别派生自退货单号与红字号，天然不撞。
            //
            // ⚠️ 这条约束**只对夹具成立，不是生产约束**：生产装配里 <c>ErpCodingService</c> 由 DI 解析，
            // 走的是 (ApplicationDbContext, IServiceScopeFactory) 那个构造 = **落库**分配器，
            // 计数器在 code_counters 表里，多少个实例共用同一库都不会重号。
            // 只有用例里无参 new ErpCodingService() 那个**进程内**分配器才按实例分桶。
            var erpCoding = new ErpCodingService();
            var purchaseReturnHandler = new WmsOutboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReturn(erpDb, erpDeadLetters, erpCoding);
            await purchaseReturnHandler.HandleAsync(purchaseReturnEvent, CancellationToken.None);
            await erpDb.SaveChangesAsync(CancellationToken.None);
            await purchaseReturnHandler.HandleAsync(purchaseReturnEvent, CancellationToken.None);
            await erpDb.SaveChangesAsync(CancellationToken.None);

            var purchaseReturn = Assert.Single(await erpDb.PurchaseReturns.Include(x => x.Lines).ToListAsync());
            // #3278 / S7：凭证号改分配器短号，不再从退货单号派生，定位改走 S5 的来源两列。
            // ⭐ 本用例连投两次（上面 :86 / :88）且跑在真 Postgres 上，
            // 所以 Assert.Single 同时就是 CAP 重投的重放证明：只记一张。
            var purchaseVoucher = Assert.Single(await erpDb.JournalVouchers
                .Where(x => x.SourceType == JournalVoucherSourceType.PurchaseReturn.Code && x.SourceNo == purchaseReturn.PurchaseReturnNo)
                .Include(x => x.Lines)
                .ToListAsync());
            Assert.Matches(@"^JV-\d{8}-\d{6}$", purchaseVoucher.VoucherNo);
            Assert.NotEqual($"JV-PRTN-{purchaseReturn.PurchaseReturnNo}", purchaseVoucher.VoucherNo);
            Assert.Equal(100m, purchaseReturn.GrIrReversalAmount);
            Assert.Equal(0m, purchaseReturn.DebitNoteAmount);
            Assert.Contains(purchaseVoucher.Lines, x => x.AccountCode == "GR-IR" && x.DebitAmount == 100m);
            Assert.Contains(purchaseVoucher.Lines, x => x.AccountCode == "1401" && x.CreditAmount == 100m);

            var receivable = AccountReceivable.Create(
            "org-001", "env-dev", "AR-RETURN-PG-001", "DO-RETURN-PG-001", "CUST-RETURN-PG-001", 100m, "CNY");
            var rma = SalesReturnAuthorization.Authorize(
            "org-001",
            "env-dev",
            "RMA-RETURN-PG-001",
            "SO-RETURN-PG-001",
            "AR-RETURN-PG-001",
            "CUST-RETURN-PG-001",
            "SITE-01",
            "CNY",
            1m,
            [new SalesReturnAuthorizationLineDraft("LINE-001", "SKU-RETURN-PG-001", "EA", 1m, 100m, "LOC-RETURN", null)]);
            erpDb.AccountReceivables.Add(receivable);
            erpDb.SalesReturnAuthorizations.Add(rma);
            await erpDb.SaveChangesAsync(CancellationToken.None);

            var authorizationEvent = new SalesReturnAuthorizedIntegrationEventConverter()
                .Convert(new ErpSalesReturnAuthorizedDomainEvent(rma));
            var authorizationHandler = new ErpSalesReturnAuthorizedIntegrationEventHandler(wmsDb, wmsDeadLetters);
            await authorizationHandler.HandleAsync(authorizationEvent, CancellationToken.None);
            await wmsDb.SaveChangesAsync(CancellationToken.None);
            await authorizationHandler.HandleAsync(authorizationEvent, CancellationToken.None);
            await wmsDb.SaveChangesAsync(CancellationToken.None);

            var rmaInbound = await wmsDb.InboundOrders.SingleAsync(x => x.SourceDocumentType == WmsSourceDocumentTypes.SalesReturnRma);
            rmaInbound.Complete(
            "wms-complete:return:sales:001",
            rmaInbound.Version);
            await wmsDb.SaveChangesAsync(CancellationToken.None);
            var inboundEvent = new InboundOrderCompletedIntegrationEventConverter()
                .Convert(new WmsInboundOrderCompletedDomainEvent(rmaInbound));
            var inboundHandler = new WmsInboundOrderCompletedIntegrationEventHandlerForRecordSalesReturnReceipt(erpDb, erpDeadLetters);
            await inboundHandler.HandleAsync(inboundEvent, CancellationToken.None);
            await erpDb.SaveChangesAsync(CancellationToken.None);
            await inboundHandler.HandleAsync(inboundEvent, CancellationToken.None);
            await erpDb.SaveChangesAsync(CancellationToken.None);

            var rmaQualityHandler = new QualityInspectionResultIntegrationEventHandlerForSettleSalesReturnCredit(erpDb, erpDeadLetters, erpCoding);
            var rmaQualityEvent = QualityEvent(
            QualityIntegrationEventTypes.InspectionPassed,
            rmaInbound.InboundOrderNo,
            "quality-passed:return:sales:001",
            "SKU-RETURN-PG-001");
            await rmaQualityHandler.HandleAsync(rmaQualityEvent, CancellationToken.None);
            await erpDb.SaveChangesAsync(CancellationToken.None);
            await rmaQualityHandler.HandleAsync(rmaQualityEvent, CancellationToken.None);
            await erpDb.SaveChangesAsync(CancellationToken.None);

            var persistedRma = await erpDb.SalesReturnAuthorizations.SingleAsync(x => x.RmaNo == "RMA-RETURN-PG-001");
            var creditNote = Assert.Single(await erpDb.CreditNotes.ToListAsync());
            var persistedReceivable = await erpDb.AccountReceivables.SingleAsync(x => x.ReceivableNo == "AR-RETURN-PG-001");
            // #3278 / S7：同上。本用例也连投两次（:142 / :144）。
            var creditVoucher = Assert.Single(await erpDb.JournalVouchers
                .Where(x => x.SourceType == JournalVoucherSourceType.CreditNote.Code && x.SourceNo == creditNote.CreditNoteNo)
                .Include(x => x.Lines)
                .ToListAsync());
            Assert.Matches(@"^JV-\d{8}-\d{6}$", creditVoucher.VoucherNo);
            Assert.NotEqual($"JV-CN-{creditNote.CreditNoteNo}", creditVoucher.VoucherNo);
            // 两张凭证来自两个不同消费者、各自分配：号必须互异。
            // （真库上 (organization_id, environment_id, voucher_no) 仍是唯一索引，撞号会在写入时就炸 23505，
            // 但这条断言把它写成用例事实，不依赖索引仍在。）
            Assert.NotEqual(purchaseVoucher.VoucherNo, creditVoucher.VoucherNo);
            Assert.Equal(SalesReturnAuthorizationStatus.CreditIssued, persistedRma.Status);
            Assert.Equal(rmaInbound.InboundOrderNo, persistedRma.WmsInboundOrderNo);
            Assert.Equal(100m, creditNote.Amount);
            Assert.Equal(100m, persistedReceivable.CreditNoteAmount);
            Assert.Equal(0m, persistedReceivable.OpenAmount);
            Assert.Contains(creditVoucher.Lines, x => x.AccountCode == "6001" && x.DebitAmount == 100m);
            Assert.Contains(creditVoucher.Lines, x => x.AccountCode == "1122" && x.CreditAmount == 100m);
            Assert.Empty(await erpDeadLetters.ListAsync(null, IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
            Assert.Empty(await wmsDeadLetters.ListAsync(null, IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
        }
        finally
        {
            await database.DropAsync();
        }
    }

    private static async Task SeedUninvoicedPurchaseReceiptAsync(ErpDbContext dbContext)
    {
        var order = PurchaseOrder.Create(
            "org-001",
            "env-dev",
            "PO-RETURN-PG-001",
            "SUP-RETURN-PG-001",
            "SITE-01",
            "CNY",
            [new PurchaseOrderLineDraft("LINE-001", "SKU-RETURN-PG-001", "EA", 1m, 100m, new DateOnly(2026, 7, 1))]);
        order.MarkApprovalRequested("approval:return:purchase:pg:001");
        order.ReleaseAfterApproval("approval:return:purchase:pg:001");
        var receipt = PurchaseReceipt.Record(
            order,
            "GR-RETURN-PG-001",
            [new PurchaseReceiptLineDraft("LINE-001", 1m, "quality", "LOC-QA", null)],
            1m);
        dbContext.PurchaseOrders.Add(order);
        dbContext.PurchaseReceipts.Add(receipt);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static InspectionResultIntegrationEvent QualityEvent(
        string eventType,
        string inboundOrderNo,
        string idempotencyKey,
        string skuCode)
    {
        return new InspectionResultIntegrationEvent(
            $"evt-{idempotencyKey}",
            eventType,
            QualityIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            QualityIntegrationEventSources.BusinessQuality,
            "corr-return-pg",
            "cause-return-pg",
            "org-001",
            "env-dev",
            "system:quality",
            idempotencyKey,
            new InspectionResultPayload(
                $"QI-{idempotencyKey}",
                "PLAN-RETURN-PG-001",
                "receiving",
                "wms",
                inboundOrderNo,
                skuCode,
                1m,
                eventType == QualityIntegrationEventTypes.InspectionRejected ? "rejected" : "passed",
                null,
                [],
                DateTimeOffset.UtcNow));
    }

    private static ErpDbContext CreateErpContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ErpFacts.Schema))
            .Options;
        return new ErpDbContext(options, new NoopMediator());
    }

    private static WmsDbContext CreateWmsContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", WmsFacts.Schema))
            .Options;
        return new WmsDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("No mediator requests are expected in this acceptance test.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => throw new NotSupportedException("No mediator requests are expected in this acceptance test.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("No mediator requests are expected in this acceptance test.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("No mediator streams are expected in this acceptance test.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("No mediator streams are expected in this acceptance test.");
    }
}

internal sealed class RealPostgresFactAttribute : FactAttribute
{
    public RealPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run the real PostgreSQL ERP return closure acceptance test.";
        }
    }
}
