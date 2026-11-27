using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;

public sealed record ListEngineeringChangeWorkOrderImpactsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? ChangeNumber,
    string? WorkOrderId,
    int Skip,
    int Take,
    string? AuthorizedAssignedUserIds,
    string? AuthorizedTeamIds,
    string? AuthorizedWorkCenterIds) : IQuery<ListEngineeringChangeWorkOrderImpactsResponse>;

public sealed record ListEngineeringChangeWorkOrderImpactsResponse(
    IReadOnlyCollection<EngineeringChangeWorkOrderImpactReadback> Items,
    int Total);

public sealed record EngineeringChangeWorkOrderImpactReadback(
    string ChangeNumber,
    string WorkOrderId,
    string Disposition,
    string? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionReason);

public sealed class ListEngineeringChangeWorkOrderImpactsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListEngineeringChangeWorkOrderImpactsQuery, ListEngineeringChangeWorkOrderImpactsResponse>
{
    public async Task<ListEngineeringChangeWorkOrderImpactsResponse> Handle(
        ListEngineeringChangeWorkOrderImpactsQuery request,
        CancellationToken cancellationToken)
    {
        var tenant = TenantScope.From(request.OrganizationId, request.EnvironmentId);
        var changeNumber = request.ChangeNumber?.Trim();
        var workOrderId = request.WorkOrderId?.Trim();
        var page = OffsetPage.From(request.Skip, request.Take);
        var impacts = dbContext.EngineeringChangeWorkOrderImpacts.AsNoTracking()
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId &&
                x.Status != MesEngineeringChangeImpactStatuses.ArchivedProductionVersion &&
                (string.IsNullOrEmpty(changeNumber) || x.ChangeNumber == changeNumber) &&
                (string.IsNullOrEmpty(workOrderId) || x.WorkOrderId == workOrderId));

        var readScope = new WorkOrderTaskReadScope(
            request.AuthorizedAssignedUserIds, request.AuthorizedTeamIds, request.AuthorizedWorkCenterIds);
        if (readScope.IsRestricted)
        {
            var visibleTasks = readScope.ApplyTo(dbContext.OperationTasks.AsNoTracking().Where(task =>
                task.OrganizationId == tenant.OrganizationId && task.EnvironmentId == tenant.EnvironmentId));
            impacts = impacts.Where(x => visibleTasks.Any(task => task.WorkOrderId == x.WorkOrderId));
        }

        var total = await impacts.CountAsync(cancellationToken);
        var items = await impacts.OrderBy(x => x.ChangeNumber).ThenBy(x => x.WorkOrderId)
            .Skip(page.Skip).Take(page.Take)
            .Select(x => new EngineeringChangeWorkOrderImpactReadback(
                x.ChangeNumber, x.WorkOrderId,
                x.Status == MesEngineeringChangeImpactStatuses.Decided ? x.Decision! : x.Status,
                x.DecidedBy, x.DecidedAtUtc, x.DecisionReason))
            .ToArrayAsync(cancellationToken);
        return new ListEngineeringChangeWorkOrderImpactsResponse(items, total);
    }
}
