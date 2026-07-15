using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public interface ISchedulingOperationOverrideOverlay
{
    Task<SchedulingProblemContract> ApplyAsync(SchedulingProblemContract problem, CancellationToken cancellationToken);
}

public sealed class SchedulingOperationOverrideOverlay(ApplicationDbContext dbContext)
    : ISchedulingOperationOverrideOverlay
{
    public async Task<SchedulingProblemContract> ApplyAsync(
        SchedulingProblemContract problem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(problem);
        var operationKeys = problem.Orders
            .SelectMany(order => order.Operations.Select(operation => (order.OrderId, operation.OperationId)))
            .ToArray();
        var operationIds = operationKeys.Select(x => x.OperationId).Distinct(StringComparer.Ordinal).ToArray();
        var overrides = await dbContext.ScheduleOperationOverrides.AsNoTracking()
            .Where(x => x.OrganizationId == problem.OrganizationId &&
                x.EnvironmentId == problem.EnvironmentId &&
                x.IsActive &&
                operationIds.Contains(x.OperationId))
            .ToArrayAsync(cancellationToken);

        var validKeys = operationKeys.ToHashSet();
        var merged = new Dictionary<(string OrderId, string OperationId), SchedulingLockedAssignmentContract>();
        foreach (var locked in problem.LockedAssignments.Where(x => validKeys.Contains((x.OrderId, x.OperationId))))
        {
            merged[(locked.OrderId, locked.OperationId)] = locked;
        }
        foreach (var item in overrides.Where(x => validKeys.Contains((x.WorkOrderId, x.OperationId))))
        {
            merged[(item.WorkOrderId, item.OperationId)] = new SchedulingLockedAssignmentContract(
                $"override-{item.OperationId}", item.WorkOrderId, item.OperationId,
                item.OperationSequence, item.ResourceId, item.WorkCenterId,
                item.StartUtc, item.EndUtc, item.LockReasonCode);
        }

        return problem with
        {
            LockedAssignments = merged.Values
                .OrderBy(x => x.OrderId, StringComparer.Ordinal)
                .ThenBy(x => x.OperationSequence)
                .ThenBy(x => x.OperationId, StringComparer.Ordinal)
                .ToArray()
        };
    }
}
