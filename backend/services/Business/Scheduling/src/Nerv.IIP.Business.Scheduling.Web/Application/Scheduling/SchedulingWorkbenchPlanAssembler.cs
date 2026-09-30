using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Business.Scheduling.Web.Application.Urgency;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public sealed class SchedulingWorkbenchPlanAssembler(
    ApplicationDbContext dbContext,
    ISchedulingWorkbenchSourceProvider sourceProvider,
    ISchedulingProblemProducer problemProducer,
    OrderUrgencyService urgencyService)
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
        var problemTask = problemProducer.AssembleWorkbenchAsync(
            new AssembleSchedulingWorkbenchProblemRequest(
                $"workbench-{Guid.CreateVersion7():N}",
                organizationId,
                environmentId,
                horizonStartUtc,
                horizonEndUtc,
                orders),
            cancellationToken);
        var orderIds = orders.Select(x => x.Order.OrderId).ToArray();
        var urgenciesTask = urgencyService.ListAsync(
            organizationId, environmentId, orderIds, cancellationToken);
        await Task.WhenAll(problemTask, urgenciesTask);
        var problem = await problemTask;
        var urgencies = await urgenciesTask;
        var urgencyByOrder = urgencies.ToDictionary(x => x.OrderId, StringComparer.Ordinal);
        var priorityKeys = problem.Orders.ToDictionary(
            x => x.OrderId,
            x => (Urgency: OrderUrgencyService.UrgencyRank(urgencyByOrder[x.OrderId].Level), BusinessPriority: x.Priority),
            StringComparer.Ordinal);
        // ADR 0022 §D.13: compress the ordered pairs into the existing operation Priority slot.
        // Dense ranks cover the full MES int range without changing business Priority or adding a sort key.
        var ranks = priorityKeys.Values.Distinct()
            .OrderBy(x => x.Urgency).ThenBy(x => x.BusinessPriority)
            .Select((key, rank) => (key, rank))
            .ToDictionary(x => x.key, x => x.rank);
        problem = problem with
        {
            Orders = problem.Orders.Select(order => order with
            {
                Operations = order.Operations.Select(operation => operation with
                {
                    Priority = ranks[priorityKeys[order.OrderId]],
                }).ToArray(),
            }).ToArray(),
        };
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
