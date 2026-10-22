using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;

public sealed record GetAssemblyChildWorkOrdersQuery(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId) : IQuery<AssemblyChildWorkOrdersResponse>;

public sealed class GetAssemblyChildWorkOrdersQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetAssemblyChildWorkOrdersQuery, AssemblyChildWorkOrdersResponse>
{
    public async Task<AssemblyChildWorkOrdersResponse> Handle(
        GetAssemblyChildWorkOrdersQuery request, CancellationToken cancellationToken)
    {
        var response = await new GetBatchAssemblyChildWorkOrdersQueryHandler(dbContext).Handle(
            new GetBatchAssemblyChildWorkOrdersQuery(
                request.OrganizationId, request.EnvironmentId, [request.WorkOrderId]), cancellationToken);
        return new AssemblyChildWorkOrdersResponse(response.Items[0].AssemblyChildWorkOrderIds);
    }
}
