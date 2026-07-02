using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain;
using Nerv.IIP.Ops.Infrastructure;
using Nerv.IIP.Ops.Web.Application;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Ops.Web.Application.Queries;

public sealed record ListOperationTasksQuery(
    string OrganizationId,
    string EnvironmentId,
    int? Page,
    int? PageSize) : IQuery<PagedOperationTaskListResponse>;

public sealed class ListOperationTasksQueryHandler(IServiceProvider serviceProvider)
    : IQueryHandler<ListOperationTasksQuery, PagedOperationTaskListResponse>
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 200;

    public async Task<PagedOperationTaskListResponse> Handle(ListOperationTasksQuery request, CancellationToken cancellationToken)
    {
        var context = serviceProvider.GetService<ApplicationDbContext>();
        if (context is null)
        {
            return serviceProvider.GetRequiredService<IOpsStateStore>()
                .ListTasks(request.OrganizationId, request.EnvironmentId, request.Page, request.PageSize)
                .ToContract();
        }

        var page = request.Page is > 0 ? request.Page.Value : DefaultPage;
        var pageSize = request.PageSize is > 0 ? Math.Min(request.PageSize.Value, MaxPageSize) : DefaultPageSize;
        var skip = (page - 1) * pageSize;

        var query = context.OperationTasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.RequestedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(x => new OperationTaskListItem(
                x.Id.Id,
                x.OrganizationId,
                x.EnvironmentId,
                x.InstanceKey,
                x.OperationCode,
                x.Status,
                x.RequestedBy,
                x.RequestedAtUtc,
                x.Attempts
                    .OrderByDescending(a => a.StartedAtUtc)
                    .Select(a => a.Id.Id)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new PagedOperationTaskListResponse(page, pageSize, totalCount, items);
    }
}
