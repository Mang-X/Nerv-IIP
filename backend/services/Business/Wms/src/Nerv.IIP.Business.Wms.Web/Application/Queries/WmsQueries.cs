using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;

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

public sealed record ListWcsTasksQuery(string OrganizationId, string EnvironmentId, string? ExternalTaskId, WarehouseTaskId? WarehouseTaskId = null) : IQuery<ListWcsTasksResponse>;

public sealed record ListWcsTasksResponse(IReadOnlyCollection<WcsTaskFact> Items);

public sealed record WcsTaskFact(
    WcsTaskId WcsTaskId,
    string OrganizationId,
    string EnvironmentId,
    WarehouseTaskId WarehouseTaskId,
    string AdapterType,
    string ExternalTaskId,
    string Status,
    int AttemptCount,
    string? FailureCode,
    string? FailureMessage,
    DateTime DispatchedAtUtc,
    DateTime? FailedAtUtc,
    DateTime? CompletedAtUtc);

public sealed class ListWcsTasksQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListWcsTasksQuery, ListWcsTasksResponse>
{
    public async Task<ListWcsTasksResponse> Handle(ListWcsTasksQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.WcsTasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.ExternalTaskId))
        {
            query = query.Where(x => x.ExternalTaskId == request.ExternalTaskId);
        }

        if (request.WarehouseTaskId is not null)
        {
            query = query.Where(x => x.WarehouseTaskId == request.WarehouseTaskId);
        }

        var items = await query
            .OrderByDescending(x => x.DispatchedAtUtc)
            .Take(100)
            .Select(x => new WcsTaskFact(
                x.Id,
                x.OrganizationId,
                x.EnvironmentId,
                x.WarehouseTaskId,
                x.AdapterType,
                x.ExternalTaskId,
                x.Status.ToString(),
                x.AttemptCount,
                x.FailureCode,
                x.FailureMessage,
                x.DispatchedAtUtc,
                x.FailedAtUtc,
                x.CompletedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new ListWcsTasksResponse(items);
    }
}
