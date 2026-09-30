using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Contracts.DemandPlanning;

namespace Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;

public sealed record GetAssemblyChildWorkOrdersQuery(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId) : IQuery<AssemblyChildWorkOrdersResponse>;

public sealed record AssemblyChildWorkOrdersResponse(IReadOnlyCollection<string> AssemblyChildWorkOrderIds);

public sealed class GetAssemblyChildWorkOrdersQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetAssemblyChildWorkOrdersQuery, AssemblyChildWorkOrdersResponse>
{
    public async Task<AssemblyChildWorkOrdersResponse> Handle(
        GetAssemblyChildWorkOrdersQuery request, CancellationToken cancellationToken)
    {
        var tenant = TenantScope.From(request.OrganizationId, request.EnvironmentId);
        var plannedWorkOrders = dbContext.WorkOrders.AsNoTracking().Where(x =>
            x.OrganizationId == tenant.OrganizationId &&
            x.EnvironmentId == tenant.EnvironmentId &&
            x.SourcePlanReference != null &&
            x.SourcePlanReference.SourceSystem == DemandPlanningSourceReferences.DemandPlanning &&
            x.SourcePlanReference.SourceDocumentType == DemandPlanningSourceReferences.PlanningSuggestion);
        var parentSuggestionId = await plannedWorkOrders
            .Where(x => x.WorkOrderIdValue == request.WorkOrderId)
            .Select(x => x.SourcePlanReference!.SourceDocumentId)
            .SingleOrDefaultAsync(cancellationToken);
        if (parentSuggestionId is null)
        {
            return new AssemblyChildWorkOrdersResponse([]);
        }

        var childIds = await plannedWorkOrders
            .Where(x => x.SourcePlanReference!.AssemblyParentSuggestionIds != null &&
                x.SourcePlanReference.AssemblyParentSuggestionIds.Contains(parentSuggestionId))
            .OrderBy(x => x.WorkOrderIdValue)
            .Select(x => x.WorkOrderIdValue)
            .ToArrayAsync(cancellationToken);
        return new AssemblyChildWorkOrdersResponse(childIds);
    }
}
