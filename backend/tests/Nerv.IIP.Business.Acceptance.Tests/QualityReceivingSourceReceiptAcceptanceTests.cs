using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Procurement;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionRecords;
using Nerv.IIP.Business.Quality.Web.Application.InspectionRecords;
using NetCorePal.Extensions.Primitives;
using ErpDbContext = Nerv.IIP.Business.Erp.Infrastructure.ApplicationDbContext;
using QualityDbContext = Nerv.IIP.Business.Quality.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

public sealed class QualityReceivingSourceReceiptAcceptanceTests
{
    [Fact]
    public async Task Quality_rejects_missing_erp_source_receipt_before_creating_receiving_inspection()
    {
        await using var erpDb = CreateErpContext();
        await using var qualityDb = CreateQualityContext();
        var handler = CreateQualityHandler(qualityDb, erpDb);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            NewInspectionCommand("RCV-MISSING", "SKU-RM-1000", 1m),
            CancellationToken.None));

        Assert.Contains("source", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(qualityDb.InspectionRecords);
    }

    [Fact]
    public async Task Quality_rejects_sku_mismatch_against_erp_purchase_receipt_lines()
    {
        await using var erpDb = CreateErpContext();
        await using var qualityDb = CreateQualityContext();
        await SeedPurchaseReceiptAsync(erpDb, "RCV-SKU-MISMATCH");
        var handler = CreateQualityHandler(qualityDb, erpDb);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            NewInspectionCommand("RCV-SKU-MISMATCH", "SKU-RM-404", 1m),
            CancellationToken.None));

        Assert.Contains("SKU", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(qualityDb.InspectionRecords);
    }

    [Fact]
    public async Task Quality_rejects_inspected_quantity_greater_than_erp_receipt_line_quantity()
    {
        await using var erpDb = CreateErpContext();
        await using var qualityDb = CreateQualityContext();
        await SeedPurchaseReceiptAsync(erpDb, "RCV-OVER-QTY");
        var handler = CreateQualityHandler(qualityDb, erpDb);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            NewInspectionCommand("RCV-OVER-QTY", "SKU-RM-1000", 3m),
            CancellationToken.None));

        Assert.Contains("quantity", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(qualityDb.InspectionRecords);
    }

    [Fact]
    public async Task Quality_accepts_distinct_skus_from_the_same_erp_purchase_receipt()
    {
        await using var erpDb = CreateErpContext();
        await using var qualityDb = CreateQualityContext();
        await SeedPurchaseReceiptAsync(erpDb, "RCV-MULTI-SKU");
        var handler = CreateQualityHandler(qualityDb, erpDb);

        var firstId = await handler.Handle(
            NewInspectionCommand("RCV-MULTI-SKU", "SKU-RM-1000", 2m, "LOT-001"),
            CancellationToken.None);
        var secondId = await handler.Handle(
            NewInspectionCommand("RCV-MULTI-SKU", "SKU-RM-2000", 3m, "LOT-002"),
            CancellationToken.None);
        await qualityDb.SaveChangesAsync(CancellationToken.None);

        Assert.NotEqual(firstId, secondId);
        Assert.Equal(2, qualityDb.InspectionRecords.Count());
    }

    private static CreateInspectionRecordCommandHandler CreateQualityHandler(QualityDbContext qualityDb, ErpDbContext erpDb)
    {
        return new CreateInspectionRecordCommandHandler(
            new InspectionRecordRepository(qualityDb),
            new InspectionPlanRepository(qualityDb),
            sourceDocumentVerifier: new ErpPurchaseReceiptInspectionSourceDocumentVerifier(
                new DbBackedErpPurchaseReceiptFactClient(erpDb)));
    }

    private static CreateInspectionRecordCommand NewInspectionCommand(
        string receiptNo,
        string skuCode,
        decimal inspectedQuantity,
        string? lotNo = null)
    {
        return new CreateInspectionRecordCommand(
            "org-001",
            "env-dev",
            null,
            "receiving",
            "purchase-receipt",
            receiptNo,
            skuCode,
            inspectedQuantity,
            lotNo,
            null,
            [new InspectionResultLineCommandInput("appearance", "ok", null, InspectionLineResults.Passed, null, null, [])],
            null,
            []);
    }

    private static async Task SeedPurchaseReceiptAsync(ErpDbContext erpDb, string receiptNo)
    {
        var order = PurchaseOrder.Create(
            "org-001",
            "env-dev",
            $"PO-{receiptNo}",
            "SUP-001",
            "SITE-01",
            [
                new PurchaseOrderLineDraft("LINE-001", "SKU-RM-1000", "kg", 5m, 12.5m, new DateOnly(2026, 6, 5)),
                new PurchaseOrderLineDraft("LINE-002", "SKU-RM-2000", "kg", 7m, 8m, new DateOnly(2026, 6, 5)),
            ]);
        order.MarkApprovalRequested($"approval-{receiptNo}");
        order.ReleaseAfterApproval($"approval-{receiptNo}");
        erpDb.PurchaseOrders.Add(order);
        await erpDb.SaveChangesAsync(CancellationToken.None);

        await new RecordPurchaseReceiptCommandHandler(erpDb).Handle(
            new RecordPurchaseReceiptCommand(
                "org-001",
                "env-dev",
                receiptNo,
                order.PurchaseOrderNo,
                [
                    new PurchaseReceiptCommandLine("LINE-001", 2m, "inspection", "IQC-STAGE", "LOT-001"),
                    new PurchaseReceiptCommandLine("LINE-002", 3m, "inspection", "IQC-STAGE", "LOT-002"),
                ]),
            CancellationToken.None);
        await erpDb.SaveChangesAsync(CancellationToken.None);
    }

    private static ErpDbContext CreateErpContext()
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseInMemoryDatabase($"quality-receipt-erp-{Guid.NewGuid():N}")
            .Options;
        return new ErpDbContext(options, new NoopMediator());
    }

    private static QualityDbContext CreateQualityContext()
    {
        var options = new DbContextOptionsBuilder<QualityDbContext>()
            .UseInMemoryDatabase($"quality-receipt-quality-{Guid.NewGuid():N}")
            .Options;
        return new QualityDbContext(options, new NoopMediator());
    }

    private sealed class DbBackedErpPurchaseReceiptFactClient(ErpDbContext dbContext) : IErpPurchaseReceiptFactClient
    {
        public async Task<ErpPurchaseReceiptFact?> GetPurchaseReceiptAsync(
            string organizationId,
            string environmentId,
            string purchaseReceiptNo,
            CancellationToken cancellationToken)
        {
            var response = await new GetPurchaseReceiptSourceDocumentQueryHandler(dbContext).Handle(
                new GetPurchaseReceiptSourceDocumentQuery(organizationId, environmentId, purchaseReceiptNo),
                cancellationToken);

            return response is null
                ? null
                : new ErpPurchaseReceiptFact(
                    response.PurchaseReceiptNo,
                    response.Status,
                    response.Lines
                        .Select(line => new ErpPurchaseReceiptLineFact(
                            line.LineNo,
                            line.SkuCode,
                            line.UomCode,
                            line.ReceivedQuantity,
                            line.LotNo,
                            line.Status))
                        .ToArray());
        }
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
