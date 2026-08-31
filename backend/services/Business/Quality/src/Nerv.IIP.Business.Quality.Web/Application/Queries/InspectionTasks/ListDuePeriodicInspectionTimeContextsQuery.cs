using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.PeriodicInspectionOperationAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;

namespace Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionTasks;

public sealed record DuePeriodicInspectionTimeContext(
    string WorkOrderId,
    string OperationId,
    PeriodicInspectionRuntimeContextId RuntimeContextId);

public sealed record ListDuePeriodicInspectionTimeContextsQuery(
    string OrganizationId,
    string EnvironmentId,
    DateTime NowUtc,
    int ContextBatchSize) : IQuery<IReadOnlyList<DuePeriodicInspectionTimeContext>>;

public sealed class ListDuePeriodicInspectionTimeContextsQueryValidator
    : AbstractValidator<ListDuePeriodicInspectionTimeContextsQuery>
{
    public ListDuePeriodicInspectionTimeContextsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.NowUtc.Kind).Equal(DateTimeKind.Utc);
        RuleFor(x => x.ContextBatchSize).InclusiveBetween(1, 1000);
    }
}

public sealed class ListDuePeriodicInspectionTimeContextsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListDuePeriodicInspectionTimeContextsQuery, IReadOnlyList<DuePeriodicInspectionTimeContext>>
{
    public async Task<IReadOnlyList<DuePeriodicInspectionTimeContext>> Handle(
        ListDuePeriodicInspectionTimeContextsQuery request,
        CancellationToken cancellationToken)
        => await dbContext.PeriodicInspectionRuntimeContexts
            .AsNoTracking()
            .Where(context =>
                context.OrganizationId == request.OrganizationId
                && context.EnvironmentId == request.EnvironmentId
                && context.Status == "active"
                && context.TimeIntervalHours != null
                && context.NextTimeWindowAtUtc != null
                && context.NextTimeWindowAtUtc <= request.NowUtc
                && context.UomCode != null
                && context.QuantityHighWater > 0m)
            .OrderBy(context => context.NextTimeWindowAtUtc)
            .ThenBy(context => context.WorkOrderId)
            .ThenBy(context => context.OperationId)
            .ThenBy(context => context.Id)
            .Select(context => new DuePeriodicInspectionTimeContext(
                context.WorkOrderId,
                context.OperationId,
                context.Id))
            .Take(request.ContextBatchSize)
            .ToArrayAsync(cancellationToken);
}
