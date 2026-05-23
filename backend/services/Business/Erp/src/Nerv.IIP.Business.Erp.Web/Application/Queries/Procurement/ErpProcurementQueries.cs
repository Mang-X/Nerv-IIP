using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Erp.Web.Application.Queries.Procurement;

public sealed record ListPurchaseOrdersQuery(string OrganizationId, string EnvironmentId) : IQuery<ListPurchaseOrdersResponse>;

public sealed record ListPurchaseOrdersResponse(IReadOnlyCollection<PurchaseOrderResponse> Items);

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
        var orders = await dbContext.PurchaseOrders
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .OrderByDescending(x => x.CreatedAtUtc)
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

        return new ListPurchaseOrdersResponse(orders);
    }
}
