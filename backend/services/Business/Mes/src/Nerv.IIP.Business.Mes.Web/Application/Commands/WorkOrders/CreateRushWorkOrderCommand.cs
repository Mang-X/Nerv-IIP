using MediatR;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;

public sealed record CreateRushWorkOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    decimal Quantity,
    DateTimeOffset DueUtc,
    string WorkCenterId,
    string OperationTaskId,
    int OperationSequence,
    TimeSpan Duration,
    DateTimeOffset RequestedAtUtc) : IRequest<CreateRushWorkOrderResponse>;

public sealed record CreateRushWorkOrderResponse(
    string WorkOrderId,
    MesScheduleResult Schedule,
    IReadOnlyCollection<string> AffectedWorkOrderIds);

public sealed class CreateRushWorkOrderCommandHandler(IMesPlanningStore store, RuleScheduler scheduler)
    : IRequestHandler<CreateRushWorkOrderCommand, CreateRushWorkOrderResponse>
{
    private const int RushPriority = 1000;

    public Task<CreateRushWorkOrderResponse> Handle(CreateRushWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var baselinePlan = scheduler.Schedule(
            store.GetScheduleOperations(request.OrganizationId, request.EnvironmentId),
            store.Unavailabilities);

        store.AddWorkOrder(new PlannedWorkOrder(
            request.OrganizationId,
            request.EnvironmentId,
            request.WorkOrderId,
            request.SkuId,
            request.ProductionVersionId,
            request.Quantity,
            RushPriority,
            request.DueUtc));
        store.AddOperationTask(new PlannedOperationTask(
            request.WorkOrderId,
            request.OperationTaskId,
            OperationTaskStatus.Queued,
            request.OperationSequence,
            request.WorkCenterId,
            [],
            request.RequestedAtUtc,
            request.Duration));

        var plan = scheduler.Schedule(
            store.GetScheduleOperations(request.OrganizationId, request.EnvironmentId),
            store.Unavailabilities);
        var schedule = store.AddScheduleResult(
            RescheduleTrigger.RushOrder,
            request.RequestedAtUtc,
            plan,
            baselinePlan.Assignments);
        return Task.FromResult(new CreateRushWorkOrderResponse(
            request.WorkOrderId,
            schedule,
            schedule.AffectedWorkOrderIds));
    }
}
