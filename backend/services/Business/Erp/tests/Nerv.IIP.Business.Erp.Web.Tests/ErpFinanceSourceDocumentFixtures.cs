using Nerv.IIP.Business.Erp.Domain.AggregatesModel.DeliveryOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;

namespace Nerv.IIP.Business.Erp.Web.Tests;

/// <summary>
/// 应收登记要求来源单据真实存在（<see cref="Web.Application.Commands.Finance.AccountReceivableSourceDocumentGuard"/>），
/// 财务用例因此需要一张真实发货单做来源。这里按报价→销售订单→发货单的领域路径造一张全额发货的单据：
/// 全额发货后销售订单不再留有未交敞口，不会污染信用额度类断言。
/// </summary>
internal static class ErpFinanceSourceDocumentFixtures
{
    public static async Task SeedPurchaseOrderAsync(
        Infrastructure.ApplicationDbContext dbContext,
        string purchaseOrderNo,
        string supplierCode,
        string organizationId = "org-001",
        string environmentId = "env-dev")
    {
        var purchaseOrder = PurchaseOrder.Create(
            organizationId,
            environmentId,
            purchaseOrderNo,
            supplierCode,
            "SITE-001",
            [new PurchaseOrderLineDraft("L1", "SKU-RM-SRC", "EA", 1m, 1m, new DateOnly(2026, 8, 1))]);
        dbContext.PurchaseOrders.Add(purchaseOrder);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    public static async Task SeedPurchaseReceiptAsync(
        Infrastructure.ApplicationDbContext dbContext,
        string purchaseReceiptNo,
        string supplierCode,
        string organizationId = "org-001",
        string environmentId = "env-dev")
    {
        var purchaseOrder = PurchaseOrder.Create(
            organizationId,
            environmentId,
            $"PO-SRC-{purchaseReceiptNo}",
            supplierCode,
            "SITE-001",
            [new PurchaseOrderLineDraft("L1", "SKU-RM-SRC", "EA", 1m, 1m, new DateOnly(2026, 8, 1))]);
        purchaseOrder.MarkApprovalRequested($"chain-{purchaseReceiptNo}");
        purchaseOrder.ReleaseAfterApproval($"chain-{purchaseReceiptNo}");
        dbContext.PurchaseOrders.Add(purchaseOrder);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new RecordPurchaseReceiptCommandHandler(dbContext).Handle(
            new RecordPurchaseReceiptCommand(
                organizationId,
                environmentId,
                purchaseReceiptNo,
                purchaseOrder.PurchaseOrderNo,
                [new PurchaseReceiptCommandLine("L1", 1m, "accepted")]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    public static async Task SeedDeliveryOrderAsync(
        Infrastructure.ApplicationDbContext dbContext,
        string deliveryOrderNo,
        string customerCode,
        string organizationId = "org-001",
        string environmentId = "env-dev")
    {
        var requiredDate = new DateOnly(2026, 8, 1);
        var quotation = Quotation.Create(
            organizationId,
            environmentId,
            $"QT-SRC-{deliveryOrderNo}",
            customerCode,
            requiredDate,
            [new QuotationLineDraft("L1", "SKU-FG-SRC", "EA", 1m, 1m, requiredDate)]);
        quotation.Approve();
        var salesOrder = SalesOrder.CreateFromQuotation($"SO-SRC-{deliveryOrderNo}", "SITE-001", quotation);
        var delivery = DeliveryOrder.Release(salesOrder, deliveryOrderNo, [new DeliveryOrderLineDraft("L1", 1m)]);
        dbContext.Quotations.Add(quotation);
        dbContext.SalesOrders.Add(salesOrder);
        dbContext.DeliveryOrders.Add(delivery);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
