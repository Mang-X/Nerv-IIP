using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleInsertionPreviewJobAggregate;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Commands;

public sealed record CreateSchedulingWorkbenchPlanCommand(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset HorizonStartUtc,
    DateTimeOffset HorizonEndUtc,
    IReadOnlyCollection<SchedulingWorkbenchOrderSelection> Orders) : ICommand<SchedulePlanContract>;

public sealed class CreateSchedulingWorkbenchPlanCommandValidator
    : AbstractValidator<CreateSchedulingWorkbenchPlanCommand>
{
    public CreateSchedulingWorkbenchPlanCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.HorizonEndUtc).GreaterThan(x => x.HorizonStartUtc);
        RuleFor(x => x.Orders).NotEmpty().Must(x => x.Count <= SchedulingWorkbenchLimits.MaxOrderCount);
        RuleForEach(x => x.Orders).ChildRules(order =>
        {
            order.RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(128);
            order.RuleFor(x => x.Priority).InclusiveBetween(0, 9999);
        });
        RuleFor(x => x.Orders).Must(x => x.Select(y => y.WorkOrderId).Distinct(StringComparer.Ordinal).Count() == x.Count)
            .WithMessage("Work-order selections must be distinct.");
    }
}

public sealed class CreateSchedulingWorkbenchPlanCommandHandler(
    SchedulingWorkbenchPlanAssembler assembler,
    ISender sender) : ICommandHandler<CreateSchedulingWorkbenchPlanCommand, SchedulePlanContract>
{
    public async Task<SchedulePlanContract> Handle(
        CreateSchedulingWorkbenchPlanCommand request,
        CancellationToken cancellationToken)
    {
        var input = await assembler.AssembleAsync(request.OrganizationId, request.EnvironmentId,
            request.HorizonStartUtc, request.HorizonEndUtc, request.Orders, cancellationToken);
        return await sender.Send(new CreateSchedulePlanCommand(input.Problem, input.FixedReservations), cancellationToken);
    }
}

public sealed record PreviewSchedulingWorkbenchPlanCommand(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset HorizonStartUtc,
    DateTimeOffset HorizonEndUtc,
    IReadOnlyCollection<SchedulingWorkbenchOrderSelection> Orders) : ICommand<SchedulePlanContract>;

public sealed class PreviewSchedulingWorkbenchPlanCommandValidator
    : AbstractValidator<PreviewSchedulingWorkbenchPlanCommand>
{
    public PreviewSchedulingWorkbenchPlanCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.HorizonEndUtc).GreaterThan(x => x.HorizonStartUtc);
        RuleFor(x => x.Orders).NotEmpty().Must(x => x.Count <= SchedulingWorkbenchLimits.MaxOrderCount);
        RuleForEach(x => x.Orders).ChildRules(order =>
        {
            order.RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(128);
            order.RuleFor(x => x.Priority).InclusiveBetween(0, 9999);
        });
        RuleFor(x => x.Orders).Must(x => x.Select(y => y.WorkOrderId).Distinct(StringComparer.Ordinal).Count() == x.Count)
            .WithMessage("Work-order selections must be distinct.");
    }
}

public sealed class PreviewSchedulingWorkbenchPlanCommandHandler(
    SchedulingWorkbenchPlanAssembler assembler,
    ISender sender) : ICommandHandler<PreviewSchedulingWorkbenchPlanCommand, SchedulePlanContract>
{
    public async Task<SchedulePlanContract> Handle(
        PreviewSchedulingWorkbenchPlanCommand request,
        CancellationToken cancellationToken)
    {
        var input = await assembler.AssembleAsync(request.OrganizationId, request.EnvironmentId,
            request.HorizonStartUtc, request.HorizonEndUtc, request.Orders, cancellationToken);
        return await sender.Send(new PreviewSchedulePlanCommand(input.Problem, input.FixedReservations), cancellationToken);
    }
}

public sealed record CreateSchedulePlanRevisionCommand(
    string PlanId,
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<string> IncludedOrderIds,
    IReadOnlyCollection<SchedulingLockedAssignmentContract> LockedAssignments)
    : ICommand<SchedulePlanRevisionContract>;

public sealed class CreateSchedulePlanRevisionCommandValidator
    : AbstractValidator<CreateSchedulePlanRevisionCommand>
{
    public CreateSchedulePlanRevisionCommandValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.IncludedOrderIds).NotEmpty().Must(x => x.Count <= SchedulingWorkbenchLimits.MaxOrderCount);
        RuleFor(x => x.IncludedOrderIds).Must(x => x.Distinct(StringComparer.Ordinal).Count() == x.Count)
            .WithMessage("Included work-order ids must be distinct.");
        RuleForEach(x => x.LockedAssignments).ChildRules(assignment =>
        {
            assignment.RuleFor(x => x.OperationId).NotEmpty().MaximumLength(128);
            assignment.RuleFor(x => x.ResourceId).NotEmpty().MaximumLength(128);
            assignment.RuleFor(x => x.EndUtc).GreaterThan(x => x.StartUtc);
        });
        RuleFor(x => x.LockedAssignments)
            .Must(x => x.Select(y => (y.OrderId, y.OperationId)).Distinct().Count() == x.Count)
            .WithMessage("Locked order-operation ids must be distinct.");
    }
}

public sealed class CreateSchedulePlanRevisionCommandHandler(
    ApplicationDbContext dbContext,
    ISender sender,
    TimeProvider timeProvider,
    SchedulingFreezeSettings freezeSettings) : ICommandHandler<CreateSchedulePlanRevisionCommand, SchedulePlanRevisionContract>
{
    public async Task<SchedulePlanRevisionContract> Handle(
        CreateSchedulePlanRevisionCommand request,
        CancellationToken cancellationToken)
    {
        if (request.PlanId.StartsWith("insertion-", StringComparison.Ordinal) &&
            Guid.TryParseExact(request.PlanId["insertion-".Length..], "N", out var jobId))
        {
            return await SaveInsertionCandidateAsync(request, jobId, cancellationToken);
        }

        var basePlanEntity = await dbContext.SchedulePlans.AsNoTracking()
            .Include(x => x.Assignments)
            .Include(x => x.ResourceLoads)
            .Include(x => x.Conflicts)
            .Include(x => x.UnscheduledOperations)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x =>
                x.PlanId == request.PlanId &&
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId,
                cancellationToken)
            ?? throw new KnownException($"未找到排程方案，请刷新后重试，方案 ID = {request.PlanId}");
        var snapshot = await dbContext.ScheduleProblems.AsNoTracking()
            .SingleAsync(x =>
                x.ProblemId == basePlanEntity.ProblemId &&
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId,
                cancellationToken);
        var baseProblem = JsonSerializer.Deserialize<SchedulingProblemContract>(snapshot.ProblemJson, SchedulingJson.Options)
            ?? throw new KnownException($"排程问题快照无效，请重新生成方案，问题 ID = {snapshot.ProblemId}");
        var snapshotReservations = SchedulingFrozenOccupancy.ReadSnapshot(snapshot.ProblemJson);
        var included = request.IncludedOrderIds.ToHashSet(StringComparer.Ordinal);
        var orders = baseProblem.Orders.Where(x => included.Contains(x.OrderId)).ToArray();
        var missingOrders = included.Except(orders.Select(x => x.OrderId), StringComparer.Ordinal).ToArray();
        if (missingOrders.Length > 0)
        {
            throw new KnownException($"所选工单不在基础方案中，请刷新后重新选择：{string.Join(", ", missingOrders)}");
        }

        var priorFreeze = SchedulingFrozenOccupancy.ReadFreezeSnapshot(snapshot.ProblemJson);
        var baseline = SchedulePlanContractMapper.ToContract(basePlanEntity, baseProblem, snapshotReservations, priorFreeze);
        var baselineKeys = baseline.Assignments.Select(x => (x.OrderId, x.OperationId)).ToHashSet();
        var baselineAssignments = baseline.Assignments
            .Concat((priorFreeze?.Assignments.Select(x => x.Assignment) ?? [])
                .Where(x => !baselineKeys.Contains((x.OrderId, x.OperationId))))
            .ToArray();
        var baselineOrderIds = baseProblem.Orders.Select(x => x.OrderId)
            .Concat(priorFreeze?.Assignments.Select(x => x.Assignment.OrderId) ?? [])
            .Distinct(StringComparer.Ordinal).ToArray();
        var execution = await dbContext.OperationExecutionProjections.AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                baselineOrderIds.Contains(x.WorkOrderId))
            .ToArrayAsync(cancellationToken);
        var operations = baseProblem.Orders.SelectMany(order => order.Operations.Select(operation =>
            (Key: (order.OrderId, operation.OperationId), Operation: operation)))
            .ToDictionary(x => x.Key, x => x.Operation);
        var priorFrozenAssignments = priorFreeze?.Assignments.ToDictionary(
            x => (x.Assignment.OrderId, x.Assignment.OperationId), x => x.Assignment) ?? [];
        var reservationsByOperation = snapshotReservations
            .ToDictionary(x => (x.OrderId, x.OperationId));
        foreach (var projection in execution.Where(x => x.ActualStartedAtUtc.HasValue))
        {
            var key = (projection.WorkOrderId, projection.OperationId);
            var hasOperation = operations.TryGetValue(key, out var operation);
            var hasPriorAssignment = priorFrozenAssignments.TryGetValue(key, out var priorAssignment);
            if (!hasOperation && !hasPriorAssignment)
            {
                continue;
            }
            reservationsByOperation[(projection.WorkOrderId, projection.OperationId)] = new FixedWorkCenterReservation(
                projection.WorkOrderId, projection.OperationId,
                operation?.OperationSequence ?? priorAssignment!.OperationSequence,
                projection.WorkCenterId ?? (operation is null
                    ? priorAssignment!.WorkCenterId
                    : baseProblem.Resources.First(resource => operation.EligibleResourceIds.Contains(
                        resource.ResourceId, StringComparer.Ordinal)).WorkCenterId),
                projection.ActualStartedAtUtc!.Value,
                projection.ActualCompletedAtUtc ?? baseProblem.HorizonEndUtc,
                null);
        }
        var fixedReservations = reservationsByOperation.Values.ToArray();
        var policy = freezeSettings.At(timeProvider.GetUtcNow());
        var priorManualLocks = priorFreeze?
            .Assignments.Where(x => ((SchedulingFreezeReason)x.Reasons).HasFlag(SchedulingFreezeReason.ManualLock))
            .Select(x => (x.Assignment.OrderId, x.Assignment.OperationId)) ?? [];
        var manualLocks = baseProblem.LockedAssignments
            .Where(x => x.LockReasonCode != SchedulingFrozenOccupancy.BaselineLockReasonCode)
            .Select(x => (x.OrderId, x.OperationId))
            .Concat(priorManualLocks)
            .Distinct()
            .ToArray();
        var frozen = SchedulingFreezeCalculator.Calculate(
            baselineAssignments,
            execution.Select(x => new SchedulingFreezeExecutionFact(
                x.WorkOrderId, x.OperationId, x.ActualStartedAtUtc, x.ActualCompletedAtUtc)).ToArray(),
            manualLocks,
            policy);
        var fixedKeys = fixedReservations.Select(x => (x.OrderId, x.OperationId)).ToHashSet();
        var frozenLocks = frozen
            .Where(x => included.Contains(x.Assignment.OrderId) &&
                !fixedKeys.Contains((x.Assignment.OrderId, x.Assignment.OperationId)))
            .Select(x => new SchedulingLockedAssignmentContract(
                x.Assignment.AssignmentId, x.Assignment.OrderId, x.Assignment.OperationId,
                x.Assignment.OperationSequence, x.Assignment.ResourceId, x.Assignment.WorkCenterId,
                x.Assignment.StartUtc, x.Assignment.EndUtc,
                SchedulingFrozenOccupancy.BaselineLockReasonCode, x.Assignment.Segments))
            .ToArray();
        var frozenKeys = frozenLocks.Select(x => (x.OrderId, x.OperationId)).ToHashSet();
        var normalizedLocks = ValidateLocks(baseProblem, orders,
            request.LockedAssignments.Where(x =>
                !fixedKeys.Contains((x.OrderId, x.OperationId)) &&
                !frozenKeys.Contains((x.OrderId, x.OperationId))).ToArray());
        var revisionProblem = baseProblem with
        {
            ProblemId = $"revision-{Guid.CreateVersion7():N}",
            Orders = orders,
            LockedAssignments = frozenLocks.Concat(normalizedLocks).ToArray(),
        };
        var impact = await LoadLatestImpactAsync(request, baseProblem, baseline, cancellationToken);
        var candidate = await sender.Send(new CreateSchedulePlanCommand(revisionProblem, fixedReservations,
            SchedulingFreezeSnapshot.From(policy, frozen)), cancellationToken);
        return new SchedulePlanRevisionContract(candidate, impact, Compare(baseline, candidate));
    }

    private async Task<SchedulePlanRevisionContract> SaveInsertionCandidateAsync(
        CreateSchedulePlanRevisionCommand request, Guid jobId, CancellationToken cancellationToken)
    {
        var id = new ScheduleInsertionPreviewJobId(jobId);
        var job = await dbContext.ScheduleInsertionPreviewJobs.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == id && x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId &&
            x.Status == ScheduleInsertionPreviewJobStatus.Completed, cancellationToken)
            ?? throw new KnownException("未找到已完成的插单候选，请等待计算完成后重试。");
        var detail = ScheduleInsertionPreviewJobMapper.ToContract(job);
        var result = detail.Result;
        if (result is null || result.CandidatePlanId != request.PlanId ||
            result.Candidate.PlanId != request.PlanId || result.BaselinePlanId != detail.Input.PlanId ||
            detail.AcceptedBaseline?.Baseline.PlanId != result.BaselinePlanId)
        {
            throw new KnownException("插单候选与受理基线不匹配，请重新计算后重试。");
        }
        if (request.LockedAssignments.Count != 0 ||
            !request.IncludedOrderIds.ToHashSet(StringComparer.Ordinal).SetEquals(
                result.Snapshot.Problem.Orders.Select(x => x.OrderId)))
        {
            throw new KnownException("保存插单候选必须保留完整工单集合，不能添加人工锁，请重新选择候选。");
        }

        var problem = result.Snapshot.Problem with { ProblemId = request.PlanId };
        var reservations = result.Snapshot.FixedReservations.Select(x => new FixedWorkCenterReservation(
            x.OrderId, x.OperationId,
            problem.Orders.Single(order => order.OrderId == x.OrderId).Operations
                .Single(operation => operation.OperationId == x.OperationId).OperationSequence,
            x.WorkCenterId, x.StartUtc, x.EndUtc, x.ResourceId)).ToArray();
        var freezeContext = result.Snapshot.Freeze;
        var freeze = new SchedulingFreezeSnapshot(freezeContext.AsOfUtc,
            freezeContext.DefaultWindowEndUtc - freezeContext.AsOfUtc,
            freezeContext.WorkCenterWindows.ToDictionary(x => x.WorkCenterId, x => x.EndUtc - freezeContext.AsOfUtc,
                StringComparer.Ordinal),
            freezeContext.Assignments.Select(x => new SchedulingFrozenAssignmentSnapshot(x.Assignment,
                x.Reasons.Aggregate(0, (reasons, reason) => reasons | (int)reason))).ToArray());
        var existing = await dbContext.SchedulePlans.AsNoTracking()
            .Include(x => x.Assignments).Include(x => x.ResourceLoads).Include(x => x.Conflicts).Include(x => x.UnscheduledOperations)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.PlanId == request.PlanId &&
                x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId, cancellationToken);
        SchedulePlanContract candidate;
        if (existing is not null)
        {
            candidate = SchedulePlanContractMapper.ToContract(existing, problem, reservations, freeze);
        }
        else
        {
            var generated = SchedulePlanContractMapper.WithStatus(result.Candidate with { ProblemId = problem.ProblemId },
                SchedulePlanStatusContract.Generated);
            var entity = SchedulePlan.FromGeneratedPlan(request.OrganizationId, request.EnvironmentId,
                SchedulePlanContractMapper.ToDomainSnapshot(generated));
            dbContext.ScheduleProblems.Add(new ScheduleProblemSnapshot(problem.ProblemId, problem.ContractVersion,
                request.OrganizationId, request.EnvironmentId, result.Candidate.ProblemFingerprint,
                SchedulingFrozenOccupancy.SerializeSnapshot(problem, reservations, freeze, result.Snapshot.EquipmentAvailability),
                problem.HorizonStartUtc, problem.HorizonEndUtc, result.Candidate.GeneratedAtUtc));
            dbContext.SchedulePlans.Add(entity);
            candidate = SchedulePlanContractMapper.ToContract(entity, problem, reservations, freeze);
        }
        var baseline = detail.AcceptedBaseline.Baseline;
        var impact = await LoadLatestImpactAsync(request with { PlanId = result.BaselinePlanId }, problem, baseline, cancellationToken);
        return new(candidate, impact, Compare(baseline, candidate));
    }

    private static IReadOnlyCollection<SchedulingLockedAssignmentContract> ValidateLocks(
        SchedulingProblemContract problem,
        IReadOnlyCollection<SchedulingOrderContract> includedOrders,
        IReadOnlyCollection<SchedulingLockedAssignmentContract> locks)
    {
        var operations = includedOrders
            .SelectMany(x => x.Operations.Select(operation => (x.OrderId, Operation: operation)))
            .ToDictionary(x => (x.OrderId, x.Operation.OperationId));
        var resources = problem.Resources.ToDictionary(x => x.ResourceId, StringComparer.Ordinal);
        return locks.Select(assignment =>
        {
            if (!operations.TryGetValue((assignment.OrderId, assignment.OperationId), out var source))
            {
                throw new KnownException($"锁定工序不在修订方案中，请刷新后重新选择：'{assignment.OperationId}'");
            }

            if (!resources.TryGetValue(assignment.ResourceId, out var resource) ||
                !source.Operation.EligibleResourceIds.Contains(assignment.ResourceId, StringComparer.Ordinal))
            {
                throw new KnownException($"资源不可用于工序，请选择可用资源：资源 '{assignment.ResourceId}'，工序 '{assignment.OperationId}'");
            }

            if (assignment.StartUtc < problem.HorizonStartUtc || assignment.EndUtc > problem.HorizonEndUtc ||
                assignment.EndUtc <= assignment.StartUtc)
            {
                throw new KnownException($"锁定工序超出排程时间范围，请调整时间：'{assignment.OperationId}'");
            }

            return assignment with
            {
                AssignmentId = string.IsNullOrWhiteSpace(assignment.AssignmentId)
                    ? CreateLockAssignmentId(assignment.OrderId, assignment.OperationId)
                    : assignment.AssignmentId.Trim(),
                OperationSequence = source.Operation.OperationSequence,
                WorkCenterId = resource.WorkCenterId,
                LockReasonCode = "planner-draft-lock",
            };
        }).ToArray();
    }

    private static string CreateLockAssignmentId(string orderId, string operationId)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{orderId}\n{operationId}"));
        return $"lock-{Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant()}";
    }

    private async Task<SchedulePlanImpactContract> LoadLatestImpactAsync(
        CreateSchedulePlanRevisionCommand request,
        SchedulingProblemContract problem,
        SchedulePlanContract basePlan,
        CancellationToken cancellationToken)
    {
        var latest = await dbContext.SchedulePlanInvalidations.AsNoTracking()
            .Where(x => x.PlanId == request.PlanId &&
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId)
            .OrderByDescending(x => x.RecordedAtUtc)
            .ThenByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.SourceEventType)
            .ThenByDescending(x => x.SourceEventId)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is null)
        {
            return new(false, null, null, null, null, [], [], []);
        }

        var rows = await dbContext.SchedulePlanInvalidations.AsNoTracking()
            .Where(x => x.PlanId == request.PlanId &&
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.SourceEventType == latest.SourceEventType &&
                x.SourceEventId == latest.SourceEventId)
            .ToArrayAsync(cancellationToken);
        var affectedAssignments = basePlan.Assignments
            .Where(assignment => rows.Any(row => IsAffected(row, assignment, problem)))
            .ToArray();
        return new(
            true,
            latest.ReasonCode,
            latest.SourceEventType,
            latest.SourceEventId,
            latest.OccurredAtUtc,
            rows.Select(x => x.AffectedResourceId)
                .Where(x => x is not null)
                .Cast<string>()
                .Concat(affectedAssignments.Select(x => x.ResourceId))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            rows.Select(x => x.AffectedWorkOrderId)
                .Where(x => x is not null)
                .Cast<string>()
                .Concat(affectedAssignments.Select(x => x.OrderId))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            rows.Select(x => x.AffectedOperationId)
                .Where(x => x is not null)
                .Cast<string>()
                .Concat(affectedAssignments.Select(x => x.OperationId))
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    private static bool IsAffected(
        SchedulePlanInvalidation invalidation,
        ScheduleAssignmentContract assignment,
        SchedulingProblemContract problem)
    {
        if (!string.IsNullOrWhiteSpace(invalidation.AffectedOperationId))
        {
            return string.Equals(invalidation.AffectedOperationId, assignment.OperationId, StringComparison.Ordinal) &&
                   (string.IsNullOrWhiteSpace(invalidation.AffectedWorkOrderId) ||
                    string.Equals(invalidation.AffectedWorkOrderId, assignment.OrderId, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(invalidation.AffectedWorkOrderId))
        {
            return string.Equals(invalidation.AffectedWorkOrderId, assignment.OrderId, StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(invalidation.AffectedResourceId))
        {
            return string.Equals(invalidation.AffectedResourceId, assignment.ResourceId, StringComparison.Ordinal) ||
                   string.Equals(invalidation.AffectedResourceId, assignment.WorkCenterId, StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(invalidation.AffectedSkuCode) &&
            problem.Orders.Any(order =>
                string.Equals(order.OrderId, assignment.OrderId, StringComparison.Ordinal) &&
                string.Equals(order.SkuCode, invalidation.AffectedSkuCode, StringComparison.Ordinal)))
        {
            return true;
        }

        return string.IsNullOrWhiteSpace(invalidation.AffectedResourceId) &&
               string.IsNullOrWhiteSpace(invalidation.AffectedWorkOrderId) &&
               string.IsNullOrWhiteSpace(invalidation.AffectedOperationId) &&
               string.IsNullOrWhiteSpace(invalidation.AffectedSkuCode);
    }

    internal static SchedulePlanComparisonContract Compare(SchedulePlanContract basePlan, SchedulePlanContract candidate)
    {
        var baseAssignments = basePlan.Assignments.ToDictionary(x => (x.OrderId, x.OperationId));
        var moved = candidate.Assignments.Count(x =>
            !string.Equals(x.ExplanationCode, "in-progress", StringComparison.Ordinal) &&
            !string.Equals(x.ExplanationCode, SchedulingFrozenOccupancy.BaselineLockReasonCode, StringComparison.Ordinal) &&
            baseAssignments.TryGetValue((x.OrderId, x.OperationId), out var previous) &&
            (previous.ResourceId != x.ResourceId || previous.StartUtc != x.StartUtc || previous.EndUtc != x.EndUtc
                || !(previous.Segments ?? []).SequenceEqual(x.Segments ?? [])));
        return new(
            basePlan.PlanId,
            candidate.PlanId,
            basePlan.Metrics,
            candidate.Metrics,
            moved,
            candidate.Assignments.Count(x => x.IsLocked),
            candidate.UnscheduledOperations.Count);
    }
}
