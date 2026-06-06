using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Erp.Web.Application.Queries.Procurement;

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
    private const int DefaultTake = 100;
    private const int MaxTake = 500;

    public async Task<ListPurchaseOrdersResponse> Handle(ListPurchaseOrdersQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.PurchaseOrders
            .AsNoTracking()
            .Include(x => x.Lines)
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
        var take = Math.Min(request.Take <= 0 ? DefaultTake : request.Take, MaxTake);
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
