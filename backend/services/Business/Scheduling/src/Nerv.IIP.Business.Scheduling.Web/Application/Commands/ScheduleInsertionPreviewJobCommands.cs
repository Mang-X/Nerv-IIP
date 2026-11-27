using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleInsertionPreviewJobAggregate;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Commands;

public sealed record AcceptScheduleInsertionPreviewJobCommand(SchedulingInsertionPreviewRequestContract Input) : ICommand<SchedulingInsertionPreviewJobDetailContract>;
public sealed class AcceptScheduleInsertionPreviewJobCommandHandler(ApplicationDbContext db, TimeProvider clock, ISender sender)
    : ICommandHandler<AcceptScheduleInsertionPreviewJobCommand, SchedulingInsertionPreviewJobDetailContract>
{
    public async Task<SchedulingInsertionPreviewJobDetailContract> Handle(AcceptScheduleInsertionPreviewJobCommand request, CancellationToken ct)
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
        var accepted = new SchedulingInsertionAcceptedInput(acceptedInput, new(
            await sender.Send(new GetSchedulePlanDetailQuery(input.PlanId, input.OrganizationId, input.EnvironmentId), ct), problem));
        var job = new ScheduleInsertionPreviewJob(input.OrganizationId, input.EnvironmentId,
            JsonSerializer.Serialize(accepted, SchedulingJson.Options), clock.GetUtcNow());
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
    ApplicationDbContext db, ISender sender, TimeProvider clock, SchedulingFreezeSettings freezeSettings,
    FiniteCapacityScheduler scheduler, SchedulingInsertionInputs inputs)
    : ICommandHandler<ExecuteScheduleInsertionPreviewJobCommand>
{
    public async Task Handle(ExecuteScheduleInsertionPreviewJobCommand request, CancellationToken ct)
    {
        var job = await db.ScheduleInsertionPreviewJobs.SingleAsync(x => x.Id == request.JobId, ct);
        var accepted = ScheduleInsertionPreviewJobMapper.ReadInput(job.InputJson);
        var input = accepted.Input;
        var policy = freezeSettings.At(clock.GetUtcNow());
        var baseline = accepted.BaselineSnapshot?.Baseline ?? await sender.Send(new GetSchedulePlanDetailQuery(input.PlanId, input.OrganizationId, input.EnvironmentId), ct);
        var baselineProblem = accepted.BaselineSnapshot?.Problem;
        if (baselineProblem is null)
        {
            // Created jobs accepted before this contract still have their original persisted problem.
            var snapshot = await db.ScheduleProblems.AsNoTracking().SingleAsync(x => x.ProblemId == baseline.ProblemId &&
                x.OrganizationId == input.OrganizationId && x.EnvironmentId == input.EnvironmentId, ct);
            baselineProblem = JsonSerializer.Deserialize<SchedulingProblemContract>(snapshot.ProblemJson, SchedulingJson.Options)!;
        }
        var calculationSnapshot = await inputs.LoadAsync(input, baseline, baselineProblem, policy, ct);
        var problem = calculationSnapshot.Problem;
        var reservations = SchedulingInsertionInputs.FixedReservations(calculationSnapshot);
        var freeze = calculationSnapshot.Freeze;
        var result = SchedulingInsertionCalculator.Calculate(scheduler, problem, calculationSnapshot.CalculationBaseline,
            input.WorkOrderId, $"insertion-{job.Id.Id:N}", calculationSnapshot.Execution.Select(x =>
                new SchedulingFreezeExecutionFact(x.OrderId, x.OperationId, x.ActualStartedAtUtc, x.ActualCompletedAtUtc)).ToArray(),
            SchedulingInsertionInputs.ManualLocks(baseline, baselineProblem), policy, reservations,
            SchedulingFrozenOccupancy.ExternalFrozenAssignments(SchedulingInsertionInputs.FreezeSnapshot(freeze), problem, reservations));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            ReschedulingImpactAnalyzer.CanonicalJson(new { Snapshot = calculationSnapshot, result.InputFingerprint })))).ToLowerInvariant();
        result = result with { InputFingerprint = fingerprint,
            Candidate = result.Candidate with { ProblemFingerprint = fingerprint, Status = SchedulePlanStatusContract.Preview, FreezeContext = freeze } };
        var projected = SchedulingInsertionResultProjector.Project(result, calculationSnapshot, input.WorkOrderId, scheduler);
        job.Complete(JsonSerializer.Serialize(projected, SchedulingJson.Options), clock.GetUtcNow());
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
internal sealed record SchedulingInsertionAcceptedInput(SchedulingInsertionPreviewInputContract Input,
    SchedulingInsertionAcceptedBaselineContract? BaselineSnapshot);

internal static class ScheduleInsertionPreviewJobMapper
{
    public static SchedulingInsertionAcceptedInput ReadInput(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("input", out _)
            ? JsonSerializer.Deserialize<SchedulingInsertionAcceptedInput>(json, SchedulingJson.Options)!
            : new(JsonSerializer.Deserialize<SchedulingInsertionPreviewInputContract>(json, SchedulingJson.Options)!, null);
    }

    public static SchedulingInsertionPreviewJobDetailContract ToContract(ScheduleInsertionPreviewJob job)
    {
        var accepted = ReadInput(job.InputJson);
        SchedulingInsertionPreviewResultContract? result = null;
        SchedulePlanContract? preview = null;
        if (job.PreviewJson is not null)
        {
            using var document = JsonDocument.Parse(job.PreviewJson);
            // Completed jobs from the prior contract retain their Preview; no derived CTP is invented for them.
            if (document.RootElement.TryGetProperty("candidate", out _))
            {
                result = JsonSerializer.Deserialize<SchedulingInsertionPreviewResultContract>(job.PreviewJson, SchedulingJson.Options)!;
                preview = result.Candidate;
            }
            else preview = JsonSerializer.Deserialize<SchedulePlanContract>(job.PreviewJson, SchedulingJson.Options);
        }
        return new(job.Id.Id, (SchedulingInsertionPreviewJobStatusContract)job.Status,
            accepted.Input,
            job.CreatedAtUtc, job.StartedAtUtc, job.FinishedAtUtc, preview, job.FailureReason, result, accepted.BaselineSnapshot);
    }
}
