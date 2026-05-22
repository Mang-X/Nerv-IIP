using MediatR;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;

public enum RescheduleTrigger
{
    Manual,
    RushOrder,
    AssetUnavailable,
    AssetRestored,
}

public sealed record RescheduleCommand(
    string OrganizationId,
    string EnvironmentId,
    RescheduleTrigger Trigger,
    DateTimeOffset RequestedAtUtc) : IRequest<MesScheduleResult>;

public sealed class RescheduleCommandHandler(IMesPlanningStore store, RuleScheduler scheduler)
    : IRequestHandler<RescheduleCommand, MesScheduleResult>
{
    public Task<MesScheduleResult> Handle(RescheduleCommand request, CancellationToken cancellationToken)
    {
        var plan = scheduler.Schedule(
            store.GetScheduleOperations(request.OrganizationId, request.EnvironmentId),
            store.Unavailabilities);
        var result = store.AddScheduleResult(request.Trigger, request.RequestedAtUtc, plan);
        return Task.FromResult(result);
    }
}
