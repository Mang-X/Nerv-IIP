using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleInsertionPreviewJobAggregate;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Commands;

public sealed record AcceptScheduleInsertionPreviewJobCommand(SchedulingInsertionPreviewRequestContract Input) : ICommand<SchedulingInsertionPreviewJobContract>;
public sealed class AcceptScheduleInsertionPreviewJobCommandHandler(ApplicationDbContext db, TimeProvider clock)
    : ICommandHandler<AcceptScheduleInsertionPreviewJobCommand, SchedulingInsertionPreviewJobContract>
{
    public async Task<SchedulingInsertionPreviewJobContract> Handle(AcceptScheduleInsertionPreviewJobCommand request, CancellationToken ct)
    {
        var input = request.Input;
        var plan = await db.SchedulePlans.AsNoTracking().SingleOrDefaultAsync(x =>
            x.PlanId == input.PlanId && x.OrganizationId == input.OrganizationId && x.EnvironmentId == input.EnvironmentId, ct)
            ?? throw new KnownException("未找到排程方案，请刷新后重试。");
        var snapshot = await db.ScheduleProblems.AsNoTracking().SingleOrDefaultAsync(x =>
            x.ProblemId == plan.ProblemId && x.OrganizationId == input.OrganizationId && x.EnvironmentId == input.EnvironmentId, ct);
        SchedulingProblemContract? problem;
        try { problem = snapshot is null ? null : JsonSerializer.Deserialize<SchedulingProblemContract>(snapshot.ProblemJson, SchedulingJson.Options); }
        catch (JsonException) { throw new KnownException("原方案排程问题快照无效，请重新生成方案后重试。"); }
        if (problem is null || problem.Orders is null || problem.Orders.Count == 0 ||
            problem.HorizonEndUtc <= problem.HorizonStartUtc || problem.Orders.Any(x => string.IsNullOrWhiteSpace(x.OrderId)))
            throw new KnownException("原方案缺少完整排程问题快照，请重新生成方案后重试。");
        var orderIds = problem.Orders.Select(x => x.OrderId).Append(input.WorkOrderId.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        if (orderIds.Length > SchedulingFirstPlanJobLimits.MaxOrderCount)
            throw new KnownException("合并后的工单数量不得超过 500，请调整选择后重试。");
        var acceptedInput = new SchedulingInsertionPreviewInputContract(input.OrganizationId, input.EnvironmentId,
            input.PlanId, input.WorkOrderId.Trim(), problem.HorizonStartUtc, problem.HorizonEndUtc, orderIds);
        var job = new ScheduleInsertionPreviewJob(input.OrganizationId, input.EnvironmentId,
            JsonSerializer.Serialize(acceptedInput, SchedulingJson.Options), clock.GetUtcNow());
        await db.ScheduleInsertionPreviewJobs.AddAsync(job, ct);
        return ScheduleInsertionPreviewJobMapper.ToContract(job);
    }
}
public sealed record StartScheduleInsertionPreviewJobCommand(ScheduleInsertionPreviewJobId JobId) : ICommand<bool>;
public sealed class StartScheduleInsertionPreviewJobCommandHandler(ApplicationDbContext db, TimeProvider clock)
    : ICommandHandler<StartScheduleInsertionPreviewJobCommand, bool>
{
    public async Task<bool> Handle(StartScheduleInsertionPreviewJobCommand request, CancellationToken ct) =>
        (await db.ScheduleInsertionPreviewJobs.SingleAsync(x => x.Id == request.JobId, ct)).Start(clock.GetUtcNow());
}
public sealed record ExecuteScheduleInsertionPreviewJobCommand(ScheduleInsertionPreviewJobId JobId) : ICommand;
public sealed class ExecuteScheduleInsertionPreviewJobCommandHandler(
    ApplicationDbContext db, SchedulingWorkbenchPlanAssembler assembler, ISender sender, TimeProvider clock,
    SchedulingFreezeSettings freezeSettings)
    : ICommandHandler<ExecuteScheduleInsertionPreviewJobCommand>
{
    public async Task Handle(ExecuteScheduleInsertionPreviewJobCommand request, CancellationToken ct)
    {
        var job = await db.ScheduleInsertionPreviewJobs.SingleAsync(x => x.Id == request.JobId, ct);
        var input = JsonSerializer.Deserialize<SchedulingInsertionPreviewInputContract>(job.InputJson, SchedulingJson.Options)!;
        var policy = freezeSettings.At(clock.GetUtcNow());
        var baseline = await sender.Send(new GetSchedulePlanDetailQuery(input.PlanId, input.OrganizationId, input.EnvironmentId), ct);
        var snapshot = await db.ScheduleProblems.AsNoTracking().SingleAsync(x =>
            x.ProblemId == baseline.ProblemId && x.OrganizationId == input.OrganizationId && x.EnvironmentId == input.EnvironmentId, ct);
        var baselineProblem = JsonSerializer.Deserialize<SchedulingProblemContract>(snapshot.ProblemJson, SchedulingJson.Options)!;
        var priorFreeze = SchedulingFrozenOccupancy.ReadFreezeSnapshot(snapshot.ProblemJson);
        var baselineKeys = baseline.Assignments.Select(x => (x.OrderId, x.OperationId)).ToHashSet();
        var baselineAssignments = baseline.Assignments.Concat((priorFreeze?.Assignments.Select(x => x.Assignment) ?? [])
            .Where(x => !baselineKeys.Contains((x.OrderId, x.OperationId)))).ToArray();
        var assembled = await assembler.AssembleAsync(input.OrganizationId, input.EnvironmentId,
            input.HorizonStartUtc, input.HorizonEndUtc,
            input.WorkOrderIds.Select(x => new SchedulingWorkbenchOrderSelection(x, 0, false)).ToArray(), ct);
        var baselineOrderIds = baselineAssignments.Select(x => x.OrderId).Distinct(StringComparer.Ordinal).ToArray();
        var execution = await db.OperationExecutionProjections.AsNoTracking().Where(x =>
            x.OrganizationId == input.OrganizationId && x.EnvironmentId == input.EnvironmentId &&
            baselineOrderIds.Contains(x.WorkOrderId)).ToArrayAsync(ct);
        var manualLocks = baselineProblem.LockedAssignments
            .Where(x => x.LockReasonCode != SchedulingFrozenOccupancy.BaselineLockReasonCode)
            .Select(x => (x.OrderId, x.OperationId))
            .Concat(priorFreeze?.Assignments.Where(x =>
                ((SchedulingFreezeReason)x.Reasons).HasFlag(SchedulingFreezeReason.ManualLock))
                .Select(x => (x.Assignment.OrderId, x.Assignment.OperationId)) ?? [])
            .Distinct().ToArray();
        var frozen = SchedulingFreezeCalculator.Calculate(baselineAssignments,
            execution.Select(x => new SchedulingFreezeExecutionFact(x.WorkOrderId, x.OperationId,
                x.ActualStartedAtUtc, x.ActualCompletedAtUtc)).ToArray(), manualLocks, policy);
        var fixedKeys = assembled.FixedReservations.Select(x => (x.OrderId, x.OperationId)).ToHashSet();
        var operationKeys = assembled.Problem.Orders.SelectMany(order =>
            order.Operations.Select(operation => (order.OrderId, operation.OperationId))).ToHashSet();
        var frozenLocks = frozen.Where(x => operationKeys.Contains((x.Assignment.OrderId, x.Assignment.OperationId)) &&
            !fixedKeys.Contains((x.Assignment.OrderId, x.Assignment.OperationId)))
            .Select(x => new SchedulingLockedAssignmentContract(x.Assignment.AssignmentId,
                x.Assignment.OrderId, x.Assignment.OperationId, x.Assignment.OperationSequence,
                x.Assignment.ResourceId, x.Assignment.WorkCenterId, x.Assignment.StartUtc, x.Assignment.EndUtc,
                SchedulingFrozenOccupancy.BaselineLockReasonCode, x.Assignment.Segments)).ToArray();
        var frozenKeys = frozenLocks.Select(x => (x.OrderId, x.OperationId)).ToHashSet();
        var problem = assembled.Problem with
        {
            LockedAssignments = frozenLocks.Concat(assembled.Problem.LockedAssignments.Where(x =>
                !frozenKeys.Contains((x.OrderId, x.OperationId)) && !fixedKeys.Contains((x.OrderId, x.OperationId)))).ToArray()
        };
        var plan = await sender.Send(new PreviewSchedulePlanCommand(problem, assembled.FixedReservations,
            SchedulingFreezeSnapshot.From(policy, frozen)), ct);
        job.Complete(JsonSerializer.Serialize(plan, SchedulingJson.Options), clock.GetUtcNow());
    }
}
public sealed record FailScheduleInsertionPreviewJobCommand(ScheduleInsertionPreviewJobId JobId, string Reason) : ICommand;
public sealed class FailScheduleInsertionPreviewJobCommandHandler(ApplicationDbContext db, TimeProvider clock)
    : ICommandHandler<FailScheduleInsertionPreviewJobCommand>
{
    public async Task Handle(FailScheduleInsertionPreviewJobCommand request, CancellationToken ct)
    {
        var job = await db.ScheduleInsertionPreviewJobs.SingleAsync(x => x.Id == request.JobId, ct);
        job.Fail(request.Reason, clock.GetUtcNow());
    }
}
internal static class ScheduleInsertionPreviewJobMapper
{
    public static SchedulingInsertionPreviewJobContract ToContract(ScheduleInsertionPreviewJob job) => new(
        job.Id.Id, (SchedulingInsertionPreviewJobStatusContract)job.Status,
        JsonSerializer.Deserialize<SchedulingInsertionPreviewInputContract>(job.InputJson, SchedulingJson.Options)!,
        job.CreatedAtUtc, job.StartedAtUtc, job.FinishedAtUtc, job.PreviewJson is null ? null : JsonSerializer.Deserialize<SchedulePlanContract>(job.PreviewJson, SchedulingJson.Options), job.FailureReason);
}
