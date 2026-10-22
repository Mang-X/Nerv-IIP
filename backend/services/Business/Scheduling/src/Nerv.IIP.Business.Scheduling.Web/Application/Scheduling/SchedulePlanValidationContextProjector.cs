using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

internal static class SchedulePlanValidationContextProjector
{
    public static SchedulePlanContract Attach(
        SchedulePlanContract plan,
        SchedulingProblemContract? problem,
        IReadOnlyCollection<FixedWorkCenterReservation> fixedReservations)
    {
        if (problem is null)
        {
            return plan;
        }

        var fixedKeys = fixedReservations.Select(x => (x.OrderId, x.OperationId)).ToHashSet();
        return plan with
        {
            ValidationContext = new SchedulePlanValidationContextContract(
                problem.HorizonStartUtc,
                problem.HorizonEndUtc,
                problem.Resources.Select(x => new SchedulePlanResourceContextContract(
                    x.ResourceId, x.WorkCenterId, x.CalendarId, x.CapacityUnits, x.UtilizationRate)).ToArray(),
                problem.Orders.SelectMany(order => order.Operations.Select(operation =>
                    new SchedulePlanOperationContextContract(
                        order.OrderId, operation.OperationId, operation.PredecessorOperationIds,
                        operation.DueUtc, operation.DurationMinutes, operation.SetupMinutes,
                        fixedKeys.Contains((order.OrderId, operation.OperationId))))).ToArray(),
                fixedReservations.OrderBy(x => x.OrderId, StringComparer.Ordinal)
                    .ThenBy(x => x.OperationId, StringComparer.Ordinal)
                    .Select(x => new SchedulePlanFixedReservationContract(
                        x.OrderId, x.OperationId, x.WorkCenterId, x.StartUtc, x.EndUtc, x.ResourceId)).ToArray())
        };
    }
}
