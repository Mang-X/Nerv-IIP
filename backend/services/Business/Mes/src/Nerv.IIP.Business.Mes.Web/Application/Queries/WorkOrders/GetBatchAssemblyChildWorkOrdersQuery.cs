using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Contracts.DemandPlanning;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;

public sealed record GetBatchAssemblyChildWorkOrdersQuery(
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<string> WorkOrderIds) : IQuery<BatchAssemblyChildWorkOrdersResponse>;

public sealed class GetBatchAssemblyChildWorkOrdersQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetBatchAssemblyChildWorkOrdersQuery, BatchAssemblyChildWorkOrdersResponse>
{
    public async Task<BatchAssemblyChildWorkOrdersResponse> Handle(
        GetBatchAssemblyChildWorkOrdersQuery request, CancellationToken cancellationToken)
    {
        var tenant = TenantScope.From(request.OrganizationId, request.EnvironmentId);
        var ids = request.WorkOrderIds.Distinct(StringComparer.Ordinal).ToArray();
        var plannedWorkOrders = dbContext.WorkOrders.AsNoTracking().Where(x =>
            x.OrganizationId == tenant.OrganizationId &&
            x.EnvironmentId == tenant.EnvironmentId &&
            x.SourcePlanReference != null &&
            x.SourcePlanReference.SourceSystem == DemandPlanningSourceReferences.DemandPlanning &&
            x.SourcePlanReference.SourceDocumentType == DemandPlanningSourceReferences.PlanningSuggestion);
        var parents = await plannedWorkOrders
            .Where(x => ids.Contains(x.WorkOrderIdValue))
            .Select(x => new { WorkOrderId = x.WorkOrderIdValue, SuggestionId = x.SourcePlanReference!.SourceDocumentId })
            .ToArrayAsync(cancellationToken);
        var parentById = parents.ToDictionary(x => x.WorkOrderId, x => x.SuggestionId, StringComparer.Ordinal);
        var suggestionIds = parents.Select(x => x.SuggestionId).Distinct(StringComparer.Ordinal).ToArray();
        if (suggestionIds.Length == 0)
        {
            return new BatchAssemblyChildWorkOrdersResponse(ids.Select(id =>
                new AssemblyChildWorkOrdersItem(id, [])).ToArray());
        }

        // Combine the existing single-parent Contains predicate into one database query.
        var order = Expression.Parameter(typeof(WorkOrder), "order");
        var parentIds = Expression.Property(
            Expression.Property(order, nameof(WorkOrder.SourcePlanReference)),
            nameof(SourcePlanReference.AssemblyParentSuggestionIds));
        Expression related = Expression.Constant(false);
        foreach (var suggestionId in suggestionIds)
        {
            related = Expression.OrElse(related, Expression.Call(
                typeof(Enumerable), nameof(Enumerable.Contains), [typeof(string)],
                parentIds, Expression.Constant(suggestionId)));
        }
        var relatedOrders = plannedWorkOrders
            .Where(x => x.SourcePlanReference!.AssemblyParentSuggestionIds != null)
            .Where(Expression.Lambda<Func<WorkOrder, bool>>(related, order));
        var children = await relatedOrders
            .OrderBy(x => x.WorkOrderIdValue)
            .Select(x => new { WorkOrderId = x.WorkOrderIdValue, ParentIds = x.SourcePlanReference!.AssemblyParentSuggestionIds! })
            .ToArrayAsync(cancellationToken);
        var childrenBySuggestion = children.SelectMany(child => child.ParentIds
                .Where(id => suggestionIds.Contains(id, StringComparer.Ordinal))
                .Select(id => new { SuggestionId = id, child.WorkOrderId }))
            .ToLookup(x => x.SuggestionId, x => x.WorkOrderId, StringComparer.Ordinal);
        return new BatchAssemblyChildWorkOrdersResponse(ids.Select(id =>
            new AssemblyChildWorkOrdersItem(id, parentById.TryGetValue(id, out var suggestionId)
                ? childrenBySuggestion[suggestionId].ToArray() : [])).ToArray());
    }
}
