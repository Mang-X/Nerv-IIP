using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.PeriodicInspectionOperationAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;

namespace Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionTasks;

public sealed record PendingPeriodicInspectionQuantityContext(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string OperationId,
    PeriodicInspectionRuntimeContextId RuntimeContextId,
    DateTime ObservedNextAttemptAtUtc);

public sealed record ListPendingPeriodicInspectionQuantityContextsQuery(DateTime NowUtc, int ContextBatchSize)
    : IQuery<IReadOnlyList<PendingPeriodicInspectionQuantityContext>>;

public sealed class ListPendingPeriodicInspectionQuantityContextsQueryValidator
    : AbstractValidator<ListPendingPeriodicInspectionQuantityContextsQuery>
{
    public ListPendingPeriodicInspectionQuantityContextsQueryValidator()
    {
        RuleFor(x => x.NowUtc.Kind).Equal(DateTimeKind.Utc);
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
                context.QuantityGenerationAnchorAtUtc != null
                && context.QuantityContinuationNextAttemptAtUtc != null
                && context.QuantityContinuationNextAttemptAtUtc <= request.NowUtc)
            .OrderBy(context => context.QuantityContinuationNextAttemptAtUtc)
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
                context.Id,
                context.QuantityContinuationNextAttemptAtUtc!.Value))
            .Take(request.ContextBatchSize)
            .ToArrayAsync(cancellationToken);
}
