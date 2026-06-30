using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.RequestForQuotationAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Erp.Web.Application.Queries.Procurement;

public sealed record ListRequestsForQuotationQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    int Skip = 0,
    int Take = 100) : IQuery<ListRequestsForQuotationResponse>;

public sealed record ListRequestsForQuotationResponse(IReadOnlyCollection<RequestForQuotationResponse> Items, int Total);

public sealed record RequestForQuotationResponse(
    string RfqNo,
    string Status,
    IReadOnlyCollection<string> SupplierCodes,
    IReadOnlyCollection<RequestForQuotationLineResponse> Lines,
    DateTime CreatedAtUtc);

public sealed record RequestForQuotationLineResponse(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal Quantity,
    string SiteCode,
    DateOnly RequiredDate);

internal static class ErpProcurementListPaging
{
    public const int DefaultTake = 100;
    public const int MaxTake = 500;

    public static int NormalizeTake(int take)
    {
        return Math.Min(take <= 0 ? DefaultTake : take, MaxTake);
    }
}

public sealed class ListRequestsForQuotationQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListRequestsForQuotationQuery, ListRequestsForQuotationResponse>
{
    public async Task<ListRequestsForQuotationResponse> Handle(ListRequestsForQuotationQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.RequestForQuotations
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.Status)
            && Enum.TryParse<RequestForQuotationStatus>(request.Status.Trim(), ignoreCase: true, out var status))
        {
            query = query.Where(x => x.Status == status);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            query = query.Where(x => false);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x =>
                x.RfqNo.Contains(keyword)
                || x.Suppliers.Any(supplier => supplier.SupplierCode.Contains(keyword))
                || x.Lines.Any(line =>
                    line.SkuCode.Contains(keyword)
                    || line.SiteCode.Contains(keyword)));
        }

        var total = await query.CountAsync(cancellationToken);
        var skip = Math.Max(request.Skip, 0);
        var take = ErpProcurementListPaging.NormalizeTake(request.Take);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(x => new RequestForQuotationResponse(
                x.RfqNo,
                x.Status.ToString(),
                x.Suppliers
                    .OrderBy(supplier => supplier.SupplierCode)
                    .Select(supplier => supplier.SupplierCode)
                    .ToArray(),
                x.Lines
                    .OrderBy(line => line.LineNo)
                    .Select(line => new RequestForQuotationLineResponse(
                        line.LineNo,
                        line.SkuCode,
                        line.UomCode,
                        line.Quantity,
                        line.SiteCode,
                        line.RequiredDate))
                    .ToArray(),
                x.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);

        return new ListRequestsForQuotationResponse(items, total);
    }
}

public sealed record ListPurchaseOrdersQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    int Skip = 0,
    int Take = 100) : IQuery<ListPurchaseOrdersResponse>;

public sealed record ListPurchaseOrdersResponse(IReadOnlyCollection<PurchaseOrderResponse> Items, int Total);

public sealed record PurchaseOrderResponse(
    string PurchaseOrderNo,
    string SupplierCode,
    string SiteCode,
    string Status,
    decimal TotalAmount,
    IReadOnlyCollection<PurchaseOrderLineResponse> Lines);

public sealed record PurchaseOrderLineResponse(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal OrderedQuantity,
    decimal ReceivedQuantity,
    decimal UnitPrice,
    DateOnly PromisedDate);

public sealed class ListPurchaseOrdersQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListPurchaseOrdersQuery, ListPurchaseOrdersResponse>
{
    public async Task<ListPurchaseOrdersResponse> Handle(ListPurchaseOrdersQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.PurchaseOrders
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.Status)
            && Enum.TryParse<Domain.AggregatesModel.PurchaseOrderAggregate.PurchaseOrderStatus>(request.Status.Trim(), ignoreCase: true, out var status))
        {
            query = query.Where(x => x.Status == status);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            query = query.Where(x => false);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x =>
                x.PurchaseOrderNo.Contains(keyword)
                || x.SupplierCode.Contains(keyword)
                || x.SiteCode.Contains(keyword)
                || x.Lines.Any(line => line.SkuCode.Contains(keyword)));
        }

        var total = await query.CountAsync(cancellationToken);
        var skip = Math.Max(request.Skip, 0);
        var take = ErpProcurementListPaging.NormalizeTake(request.Take);
        var orders = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(x => new PurchaseOrderResponse(
                x.PurchaseOrderNo,
                x.SupplierCode,
                x.SiteCode,
                x.Status.ToString(),
                x.TotalAmount,
                x.Lines
                    .OrderBy(line => line.LineNo)
                    .Select(line => new PurchaseOrderLineResponse(
                        line.LineNo,
                        line.SkuCode,
                        line.UomCode,
                        line.OrderedQuantity,
                        line.ReceivedQuantity,
                        line.UnitPrice,
                        line.PromisedDate))
                    .ToArray()))
            .ToArrayAsync(cancellationToken);

        return new ListPurchaseOrdersResponse(orders, total);
    }
}

public sealed record GetPurchaseReceiptSourceDocumentQuery(
    string OrganizationId,
    string EnvironmentId,
    string PurchaseReceiptNo) : IQuery<PurchaseReceiptSourceDocumentResponse?>;

public sealed record PurchaseReceiptSourceDocumentResponse(
    string PurchaseReceiptNo,
    string Status,
    IReadOnlyCollection<PurchaseReceiptSourceDocumentLineResponse> Lines);

public sealed record PurchaseReceiptSourceDocumentLineResponse(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal ReceivedQuantity,
    string? LotNo,
    string Status);

public sealed class GetPurchaseReceiptSourceDocumentQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetPurchaseReceiptSourceDocumentQuery, PurchaseReceiptSourceDocumentResponse?>
{
    public async Task<PurchaseReceiptSourceDocumentResponse?> Handle(
        GetPurchaseReceiptSourceDocumentQuery request,
        CancellationToken cancellationToken)
    {
        return await dbContext.PurchaseReceipts
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PurchaseReceiptNo == request.PurchaseReceiptNo)
            .Select(x => new PurchaseReceiptSourceDocumentResponse(
                x.PurchaseReceiptNo,
                "recorded",
                x.Lines
                    .OrderBy(line => line.PurchaseOrderLineNo)
                    .Select(line => new PurchaseReceiptSourceDocumentLineResponse(
                        line.PurchaseOrderLineNo,
                        line.SkuCode,
                        line.UomCode,
                        line.ReceivedQuantity,
                        line.LotNo,
                        line.QualityStatus))
                    .ToArray()))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
