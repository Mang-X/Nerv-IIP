using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public sealed class SchedulingWorkbenchPlanAssembler(
    ApplicationDbContext dbContext,
    ISchedulingWorkbenchSourceProvider sourceProvider,
    ISchedulingProblemProducer problemProducer)
{
    public async Task<(SchedulingProblemContract Problem, IReadOnlyCollection<FixedWorkCenterReservation> FixedReservations)> AssembleAsync(
        string organizationId,
        string environmentId,
        DateTimeOffset horizonStartUtc,
        DateTimeOffset horizonEndUtc,
        IReadOnlyCollection<SchedulingWorkbenchOrderSelection> selections,
        CancellationToken cancellationToken)
    {
        var orders = await sourceProvider.ResolveOrdersAsync(
            organizationId,
            environmentId,
            horizonStartUtc,
            selections,
            cancellationToken);
        var problem = await problemProducer.AssembleWorkbenchAsync(
            new AssembleSchedulingWorkbenchProblemRequest(
                $"workbench-{Guid.CreateVersion7():N}",
                organizationId,
                environmentId,
                horizonStartUtc,
                horizonEndUtc,
                orders),
            cancellationToken);
        var orderIds = orders.Select(x => x.Order.OrderId).ToArray();
        var projections = await dbContext.OperationExecutionProjections.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                orderIds.Contains(x.WorkOrderId) &&
                x.ActualStartedAtUtc != null)
            .ToArrayAsync(cancellationToken);
        var operations = problem.Orders
            .SelectMany(order => order.Operations.Select(operation => new { order.OrderId, Operation = operation }))
            .ToDictionary(x => (x.OrderId, x.Operation.OperationId), x => x.Operation);
        var fixedReservations = projections
            .Where(x => operations.ContainsKey((x.WorkOrderId, x.OperationId)))
            .Select(x => new FixedWorkCenterReservation(
                x.WorkOrderId,
                x.OperationId,
                operations[(x.WorkOrderId, x.OperationId)].OperationSequence,
                x.WorkCenterId ?? problem.Resources.First(resource =>
                    operations[(x.WorkOrderId, x.OperationId)].EligibleResourceIds.Contains(
                        resource.ResourceId, StringComparer.Ordinal)).WorkCenterId,
                x.ActualStartedAtUtc!.Value,
                x.ActualCompletedAtUtc ?? horizonEndUtc,
                null))
            .ToArray();
        return (problem, fixedReservations);
    }
}
