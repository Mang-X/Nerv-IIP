using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleFirstPlanJobAggregate;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Commands;

public sealed record AcceptScheduleFirstPlanJobCommand(SchedulingFirstPlanInputContract Input) : ICommand<SchedulingFirstPlanJobContract>;
public sealed class AcceptScheduleFirstPlanJobCommandHandler(ApplicationDbContext db, TimeProvider clock)
    : ICommandHandler<AcceptScheduleFirstPlanJobCommand, SchedulingFirstPlanJobContract>
{
    public async Task<SchedulingFirstPlanJobContract> Handle(AcceptScheduleFirstPlanJobCommand request, CancellationToken ct)
    {
        var job = new ScheduleFirstPlanJob(request.Input.OrganizationId, request.Input.EnvironmentId,
            JsonSerializer.Serialize(request.Input, SchedulingJson.Options), clock.GetUtcNow());
        await db.ScheduleFirstPlanJobs.AddAsync(job, ct);
        return ScheduleFirstPlanJobMapper.ToContract(job);
    }
}
public sealed record StartScheduleFirstPlanJobCommand(ScheduleFirstPlanJobId JobId) : ICommand<bool>;
public sealed class StartScheduleFirstPlanJobCommandHandler(ApplicationDbContext db, TimeProvider clock)
    : ICommandHandler<StartScheduleFirstPlanJobCommand, bool>
{
    public async Task<bool> Handle(StartScheduleFirstPlanJobCommand request, CancellationToken ct) =>
        (await db.ScheduleFirstPlanJobs.SingleAsync(x => x.Id == request.JobId, ct)).Start(clock.GetUtcNow());
}
public sealed record ExecuteScheduleFirstPlanJobCommand(ScheduleFirstPlanJobId JobId) : ICommand;
public sealed class ExecuteScheduleFirstPlanJobCommandHandler(
    ApplicationDbContext db, SchedulingWorkbenchPlanAssembler assembler, ISender sender, TimeProvider clock)
    : ICommandHandler<ExecuteScheduleFirstPlanJobCommand>
{
    public async Task Handle(ExecuteScheduleFirstPlanJobCommand request, CancellationToken ct)
    {
        var job = await db.ScheduleFirstPlanJobs.SingleAsync(x => x.Id == request.JobId, ct);
        var input = JsonSerializer.Deserialize<SchedulingFirstPlanInputContract>(job.InputJson, SchedulingJson.Options)!;
        var assembled = await assembler.AssembleAsync(input.OrganizationId, input.EnvironmentId,
            input.HorizonStartUtc, input.HorizonEndUtc,
            input.Orders.Select(x => new SchedulingWorkbenchOrderSelection(x.WorkOrderId, x.Priority, x.IsRush)).ToArray(), ct);
        var plan = await sender.Send(new CreateSchedulePlanCommand(assembled.Problem, assembled.FixedReservations), ct);
        job.Complete(plan.PlanId, clock.GetUtcNow());
    }
}
public sealed record FailScheduleFirstPlanJobCommand(ScheduleFirstPlanJobId JobId, string Reason) : ICommand;
public sealed class FailScheduleFirstPlanJobCommandHandler(ApplicationDbContext db, TimeProvider clock)
    : ICommandHandler<FailScheduleFirstPlanJobCommand>
{
    public async Task Handle(FailScheduleFirstPlanJobCommand request, CancellationToken ct)
    {
        var job = await db.ScheduleFirstPlanJobs.SingleAsync(x => x.Id == request.JobId, ct);
        job.Fail(request.Reason, clock.GetUtcNow());
    }
}
internal static class ScheduleFirstPlanJobMapper
{
    public static SchedulingFirstPlanJobContract ToContract(ScheduleFirstPlanJob job) => new(
        job.Id.Id, (SchedulingFirstPlanJobStatusContract)job.Status,
        JsonSerializer.Deserialize<SchedulingFirstPlanInputContract>(job.InputJson, SchedulingJson.Options)!,
        job.CreatedAtUtc, job.StartedAtUtc, job.FinishedAtUtc, job.PlanId, job.FailureReason);
}
