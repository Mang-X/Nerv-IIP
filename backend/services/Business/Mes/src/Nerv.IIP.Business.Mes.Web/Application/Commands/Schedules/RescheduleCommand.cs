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
    DateTimeOffset RequestedAtUtc) : ICommand<MesScheduleResult>;

public sealed class RescheduleCommandHandler(IMesPlanningStore store, RuleScheduler scheduler)
    : ICommandHandler<RescheduleCommand, MesScheduleResult>
{
    public async Task<MesScheduleResult> Handle(RescheduleCommand request, CancellationToken cancellationToken)
    {
        var plan = scheduler.Schedule(
            await store.GetScheduleOperationsAsync(request.OrganizationId, request.EnvironmentId, cancellationToken),
            await store.GetUnavailabilitiesAsync(request.OrganizationId, request.EnvironmentId, cancellationToken));
        return await store.AddScheduleResultAsync(request.Trigger, request.RequestedAtUtc, plan, cancellationToken: cancellationToken);
    }
}
