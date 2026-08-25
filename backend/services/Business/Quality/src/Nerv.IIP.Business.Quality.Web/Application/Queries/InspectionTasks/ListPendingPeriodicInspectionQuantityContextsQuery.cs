using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.PeriodicInspectionOperationAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;

namespace Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionTasks;

public sealed record PendingPeriodicInspectionQuantityContext(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string OperationId,
    PeriodicInspectionRuntimeContextId RuntimeContextId);

public sealed record ListPendingPeriodicInspectionQuantityContextsQuery(int ContextBatchSize)
    : IQuery<IReadOnlyList<PendingPeriodicInspectionQuantityContext>>;

public sealed class ListPendingPeriodicInspectionQuantityContextsQueryValidator
    : AbstractValidator<ListPendingPeriodicInspectionQuantityContextsQuery>
{
    public ListPendingPeriodicInspectionQuantityContextsQueryValidator()
    {
        RuleFor(x => x.ContextBatchSize).InclusiveBetween(1, 1000);
    }
}

public sealed class ListPendingPeriodicInspectionQuantityContextsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<
        ListPendingPeriodicInspectionQuantityContextsQuery,
        IReadOnlyList<PendingPeriodicInspectionQuantityContext>>
{
    public async Task<IReadOnlyList<PendingPeriodicInspectionQuantityContext>> Handle(
        ListPendingPeriodicInspectionQuantityContextsQuery request,
        CancellationToken cancellationToken)
        => await dbContext.PeriodicInspectionRuntimeContexts
            .AsNoTracking()
            .Where(context =>
                context.Status == "active"
                && context.QuantityGenerationAnchorAtUtc != null)
            .OrderBy(context => context.QuantityGenerationAnchorAtUtc)
            .ThenBy(context => context.OrganizationId)
            .ThenBy(context => context.EnvironmentId)
            .ThenBy(context => context.WorkOrderId)
            .ThenBy(context => context.OperationId)
            .ThenBy(context => context.Id)
            .Select(context => new PendingPeriodicInspectionQuantityContext(
                context.OrganizationId,
                context.EnvironmentId,
                context.WorkOrderId,
                context.OperationId,
                context.Id))
            .Take(request.ContextBatchSize)
            .ToArrayAsync(cancellationToken);
}
