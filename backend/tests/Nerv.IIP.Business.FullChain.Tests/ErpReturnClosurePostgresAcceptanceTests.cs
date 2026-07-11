using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
        await using var database = await TemporaryPostgresDatabase.CreateAsync(baseConnectionString, "erp_return_closure");
        await using var erpDb = CreateErpContext(database.ConnectionString);
        await using var wmsDb = CreateWmsContext(database.ConnectionString);
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
        purchaseInbound.Complete("wms-complete:return:purchase:001");
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
        supplierReturnOutbound.CompletePackReview("PACK-RETURN-PG-001", true, "wms-complete:return:purchase:outbound:001");
        await wmsDb.SaveChangesAsync(CancellationToken.None);

        var purchaseReturnEvent = new OutboundOrderCompletedIntegrationEventConverter()
            .Convert(new WmsOutboundOrderCompletedDomainEvent(supplierReturnOutbound));
        var purchaseReturnHandler = new WmsOutboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReturn(erpDb, erpDeadLetters, new ErpCodingService());
        await purchaseReturnHandler.HandleAsync(purchaseReturnEvent, CancellationToken.None);
        await erpDb.SaveChangesAsync(CancellationToken.None);
        await purchaseReturnHandler.HandleAsync(purchaseReturnEvent, CancellationToken.None);
        await erpDb.SaveChangesAsync(CancellationToken.None);

        var purchaseReturn = Assert.Single(await erpDb.PurchaseReturns.Include(x => x.Lines).ToListAsync());
        var purchaseVoucher = Assert.Single(await erpDb.JournalVouchers.Where(x => x.VoucherNo == $"JV-PRTN-{purchaseReturn.PurchaseReturnNo}").Include(x => x.Lines).ToListAsync());
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
        rmaInbound.Complete("wms-complete:return:sales:001");
        await wmsDb.SaveChangesAsync(CancellationToken.None);
        var inboundEvent = new InboundOrderCompletedIntegrationEventConverter()
            .Convert(new WmsInboundOrderCompletedDomainEvent(rmaInbound));
        var inboundHandler = new WmsInboundOrderCompletedIntegrationEventHandlerForRecordSalesReturnReceipt(erpDb, erpDeadLetters);
        await inboundHandler.HandleAsync(inboundEvent, CancellationToken.None);
        await erpDb.SaveChangesAsync(CancellationToken.None);
        await inboundHandler.HandleAsync(inboundEvent, CancellationToken.None);
        await erpDb.SaveChangesAsync(CancellationToken.None);

        var rmaQualityHandler = new QualityInspectionResultIntegrationEventHandlerForSettleSalesReturnCredit(erpDb, erpDeadLetters, new ErpCodingService());
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
        var creditVoucher = Assert.Single(await erpDb.JournalVouchers.Where(x => x.VoucherNo == $"JV-CN-{creditNote.CreditNoteNo}").Include(x => x.Lines).ToListAsync());
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
            var databaseName = $"{prefix}_{Guid.CreateVersion7():N}";
            var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = string.IsNullOrWhiteSpace(baseBuilder.Database) ? "postgres" : baseBuilder.Database,
            };
            var databaseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = databaseName,
            };
            await using var connection = new NpgsqlConnection(adminBuilder.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\";", connection);
            await command.ExecuteNonQueryAsync();
            return new TemporaryPostgresDatabase(adminBuilder.ConnectionString, databaseBuilder.ConnectionString, databaseName);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);", connection);
            await command.ExecuteNonQueryAsync();
        }
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
