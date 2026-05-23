using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;

namespace Nerv.IIP.Business.Wms.Web.Application.Queries;

public sealed record ListInboundOrdersQuery(string? OrganizationId, string? EnvironmentId) : IQuery<IReadOnlyCollection<InboundOrderListItem>>;

public sealed record InboundOrderListItem(InboundOrderId InboundOrderId, string InboundOrderNo, string Status, DateTime CreatedAtUtc);

public sealed class ListInboundOrdersQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListInboundOrdersQuery, IReadOnlyCollection<InboundOrderListItem>>
{
    public async Task<IReadOnlyCollection<InboundOrderListItem>> Handle(ListInboundOrdersQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.InboundOrders
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new InboundOrderListItem(x.Id, x.InboundOrderNo, x.Status.ToString(), x.CreatedAtUtc))
            .Take(100)
            .ToArrayAsync(cancellationToken);
    }
}

public sealed record ListOutboundOrdersQuery(string? OrganizationId, string? EnvironmentId) : IQuery<IReadOnlyCollection<OutboundOrderListItem>>;

public sealed record OutboundOrderListItem(OutboundOrderId OutboundOrderId, string OutboundOrderNo, string Status, DateTime CreatedAtUtc);

public sealed class ListOutboundOrdersQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListOutboundOrdersQuery, IReadOnlyCollection<OutboundOrderListItem>>
{
    public async Task<IReadOnlyCollection<OutboundOrderListItem>> Handle(ListOutboundOrdersQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.OutboundOrders
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new OutboundOrderListItem(x.Id, x.OutboundOrderNo, x.Status.ToString(), x.CreatedAtUtc))
            .Take(100)
            .ToArrayAsync(cancellationToken);
    }
}
