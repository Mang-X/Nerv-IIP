using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
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
    ISchedulingWorkbenchSourceProvider sourceProvider,
    ISchedulingProblemProducer problemProducer,
    ISender sender) : ICommandHandler<CreateSchedulingWorkbenchPlanCommand, SchedulePlanContract>
{
    public async Task<SchedulePlanContract> Handle(
        CreateSchedulingWorkbenchPlanCommand request,
        CancellationToken cancellationToken)
    {
        var orders = await sourceProvider.ResolveOrdersAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.HorizonStartUtc,
            request.Orders,
            cancellationToken);
        var problem = await problemProducer.AssembleAsync(
            new AssembleSchedulingProblemRequest(
                $"workbench-{Guid.CreateVersion7():N}",
                request.OrganizationId,
                request.EnvironmentId,
                request.HorizonStartUtc,
                request.HorizonEndUtc,
                orders),
            cancellationToken);
        return await sender.Send(new CreateSchedulePlanCommand(problem), cancellationToken);
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
    ISender sender) : ICommandHandler<CreateSchedulePlanRevisionCommand, SchedulePlanRevisionContract>
{
    public async Task<SchedulePlanRevisionContract> Handle(
        CreateSchedulePlanRevisionCommand request,
        CancellationToken cancellationToken)
    {
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
            ?? throw new KnownException($"Schedule plan was not found, PlanId = {request.PlanId}");
        var snapshot = await dbContext.ScheduleProblems.AsNoTracking()
            .SingleAsync(x =>
                x.ProblemId == basePlanEntity.ProblemId &&
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId,
                cancellationToken);
        var baseProblem = JsonSerializer.Deserialize<SchedulingProblemContract>(snapshot.ProblemJson, SchedulingJson.Options)
            ?? throw new KnownException($"Schedule problem snapshot is invalid, ProblemId = {snapshot.ProblemId}");
        var included = request.IncludedOrderIds.ToHashSet(StringComparer.Ordinal);
        var orders = baseProblem.Orders.Where(x => included.Contains(x.OrderId)).ToArray();
        var missingOrders = included.Except(orders.Select(x => x.OrderId), StringComparer.Ordinal).ToArray();
        if (missingOrders.Length > 0)
        {
            throw new KnownException($"Included work orders are not part of the base plan: {string.Join(", ", missingOrders)}");
        }

        var normalizedLocks = ValidateLocks(baseProblem, orders, request.LockedAssignments);
        var revisionProblem = baseProblem with
        {
            ProblemId = $"revision-{Guid.CreateVersion7():N}",
            Orders = orders,
            LockedAssignments = normalizedLocks,
        };
        var basePlan = SchedulePlanContractMapper.ToContract(basePlanEntity);
        var impact = await LoadLatestImpactAsync(request, baseProblem, basePlan, cancellationToken);
        var candidate = await sender.Send(new CreateSchedulePlanCommand(revisionProblem), cancellationToken);
        return new SchedulePlanRevisionContract(candidate, impact, Compare(basePlan, candidate));
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
                throw new KnownException($"Locked operation '{assignment.OperationId}' is not part of the revision.");
            }

            if (!resources.TryGetValue(assignment.ResourceId, out var resource) ||
                !source.Operation.EligibleResourceIds.Contains(assignment.ResourceId, StringComparer.Ordinal))
            {
                throw new KnownException($"Resource '{assignment.ResourceId}' is not eligible for operation '{assignment.OperationId}'.");
            }

            if (assignment.StartUtc < problem.HorizonStartUtc || assignment.EndUtc > problem.HorizonEndUtc ||
                assignment.EndUtc <= assignment.StartUtc)
            {
                throw new KnownException($"Locked operation '{assignment.OperationId}' is outside the scheduling horizon.");
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

    private static SchedulePlanComparisonContract Compare(SchedulePlanContract basePlan, SchedulePlanContract candidate)
    {
        var baseAssignments = basePlan.Assignments.ToDictionary(x => (x.OrderId, x.OperationId));
        var moved = candidate.Assignments.Count(x =>
            baseAssignments.TryGetValue((x.OrderId, x.OperationId), out var previous) &&
            (previous.ResourceId != x.ResourceId || previous.StartUtc != x.StartUtc || previous.EndUtc != x.EndUtc));
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
