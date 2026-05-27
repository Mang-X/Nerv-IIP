using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;

public sealed record CreateRushWorkOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    decimal Quantity,
    DateTimeOffset DueUtc,
    string WorkCenterId,
    string? OperationTaskId,
    int OperationSequence,
    TimeSpan Duration,
    DateTimeOffset RequestedAtUtc,
    string? IdempotencyKey = null) : ICommand<CreateRushWorkOrderResponse>;

public sealed record CreateRushWorkOrderResponse(
    string WorkOrderId,
    MesScheduleResult Schedule,
    IReadOnlyCollection<string> AffectedWorkOrderIds);

public sealed class CreateRushWorkOrderCommandHandler(IMesPlanningStore store, RuleScheduler scheduler, MesNumberingService? numberingService = null)
    : ICommandHandler<CreateRushWorkOrderCommand, CreateRushWorkOrderResponse>
{
    private const int RushPriority = 1000;
    private readonly MesNumberingService _numberingService = numberingService ?? new MesNumberingService();

    public async Task<CreateRushWorkOrderResponse> Handle(CreateRushWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var allocation = _numberingService.AllocateWorkOrderId(
            request.OrganizationId,
            request.EnvironmentId,
            request.WorkOrderId,
            request.IdempotencyKey,
            WorkOrderPayloadFingerprint(request));
        if (allocation.IsIdempotentReplay)
        {
            return new CreateRushWorkOrderResponse(
                allocation.Number,
                new MesScheduleResult(0, RescheduleTrigger.RushOrder, request.RequestedAtUtc, [], []),
                []);
        }

        var operationTaskId = string.IsNullOrWhiteSpace(request.OperationTaskId)
            ? $"{allocation.Number}-OP-{request.OperationSequence}"
            : request.OperationTaskId.Trim();
        var baselinePlan = scheduler.Schedule(
            await store.GetScheduleOperationsAsync(request.OrganizationId, request.EnvironmentId, cancellationToken),
            await store.GetUnavailabilitiesAsync(request.OrganizationId, request.EnvironmentId, cancellationToken));

        store.AddWorkOrder(new PlannedWorkOrder(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Number,
            request.SkuId,
            request.ProductionVersionId,
            request.Quantity,
            RushPriority,
            request.DueUtc));
        store.AddOperationTask(new PlannedOperationTask(
            allocation.Number,
            operationTaskId,
            OperationTaskStatus.Queued,
            request.OperationSequence,
            request.WorkCenterId,
            [],
            request.RequestedAtUtc,
            request.Duration,
            null,
            null,
            request.OrganizationId,
            request.EnvironmentId));

        var plan = scheduler.Schedule(
            await store.GetScheduleOperationsAsync(request.OrganizationId, request.EnvironmentId, cancellationToken),
            await store.GetUnavailabilitiesAsync(request.OrganizationId, request.EnvironmentId, cancellationToken));
        var schedule = await store.AddScheduleResultAsync(
            RescheduleTrigger.RushOrder,
            request.RequestedAtUtc,
            plan,
            baselinePlan.Assignments,
            cancellationToken);
        return new CreateRushWorkOrderResponse(
            allocation.Number,
            schedule,
            schedule.AffectedWorkOrderIds);
    }

    private static string WorkOrderPayloadFingerprint(CreateRushWorkOrderCommand request)
    {
        return string.Join('|',
            request.OrganizationId,
            request.EnvironmentId,
            request.SkuId,
            request.ProductionVersionId,
            request.Quantity,
            request.DueUtc.ToUnixTimeMilliseconds(),
            request.WorkCenterId,
            request.OperationSequence,
            request.Duration.Ticks);
    }
}
