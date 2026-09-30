using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Commands;

public enum SchedulePlanInvalidationScope
{
    Resource = 0,
    WorkOrderOrOperation = 1,
    GeneratedWorkCenter = 3,
    GeneratedCalendar = 4,
    GeneratedSku = 5,
    ExactWorkOrderOperation = 6,
    SnapshotWorkOrderOrSku = 7,
    SnapshotMaterial = 8,
}

public enum SchedulePlanExecutionMilestone
{
    Started = 0,
    Completed = 1,
}

public sealed record RecordSchedulePlanInvalidationsCommand(
    string OrganizationId,
    string EnvironmentId,
    string SourceEventId,
    string SourceEventType,
    string SourceService,
    DateTimeOffset OccurredAtUtc,
    string ReasonCode,
    SchedulePlanInvalidationScope Scope,
    string? ScopeValue,
    string? AffectedWorkOrderId,
    string? AffectedSkuCode,
    IReadOnlyCollection<string>? AffectedSkuCodes = null,
    string? AffectedOperationId = null,
    SchedulePlanExecutionMilestone? ExecutionMilestone = null,
    DateTimeOffset? ActualExecutionAtUtc = null,
    int? DeviationToleranceMinutes = null) : ICommand<RecordSchedulePlanInvalidationsResponse>;

public sealed record RecordSchedulePlanInvalidationsResponse(int MatchedPlanCount, int RecordedInvalidationCount);

public sealed class RecordSchedulePlanInvalidationsCommandValidator
    : AbstractValidator<RecordSchedulePlanInvalidationsCommand>
{
    public RecordSchedulePlanInvalidationsCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.SourceEventId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.SourceEventType).NotEmpty().MaximumLength(128);
        RuleFor(x => x.SourceService).NotEmpty().MaximumLength(64);
        RuleFor(x => x.ReasonCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.ScopeValue)
            .NotEmpty()
            .When(x => x.Scope is SchedulePlanInvalidationScope.Resource
                or SchedulePlanInvalidationScope.WorkOrderOrOperation
                or SchedulePlanInvalidationScope.GeneratedWorkCenter
                or SchedulePlanInvalidationScope.GeneratedCalendar);
        RuleFor(x => x.AffectedSkuCodes)
            .NotEmpty()
            .When(x => x.Scope == SchedulePlanInvalidationScope.GeneratedSku);
        RuleFor(x => x.AffectedWorkOrderId)
            .NotEmpty()
            .When(x => x.Scope == SchedulePlanInvalidationScope.ExactWorkOrderOperation);
        RuleFor(x => x.AffectedOperationId)
            .NotEmpty()
            .When(x => x.Scope == SchedulePlanInvalidationScope.ExactWorkOrderOperation);
        RuleFor(x => x.ExecutionMilestone)
            .NotNull()
            .When(x => x.Scope == SchedulePlanInvalidationScope.ExactWorkOrderOperation);
        RuleFor(x => x.ActualExecutionAtUtc)
            .NotNull()
            .When(x => x.Scope == SchedulePlanInvalidationScope.ExactWorkOrderOperation);
        RuleFor(x => x.DeviationToleranceMinutes)
            .NotNull()
            .GreaterThanOrEqualTo(0)
            .When(x => x.Scope == SchedulePlanInvalidationScope.ExactWorkOrderOperation);
    }
}

public sealed class RecordSchedulePlanInvalidationsCommandHandler(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<RecordSchedulePlanInvalidationsCommand, RecordSchedulePlanInvalidationsResponse>
{
    public async Task<RecordSchedulePlanInvalidationsResponse> Handle(
        RecordSchedulePlanInvalidationsCommand request,
        CancellationToken cancellationToken)
    {
        var calendarResourceIdsByProblem = request.Scope == SchedulePlanInvalidationScope.GeneratedCalendar
            ? await FindCalendarResourceIdsByProblemAsync(request, cancellationToken)
            : [];
        var skuByProblem = request.Scope == SchedulePlanInvalidationScope.GeneratedSku
            ? await FindSkuByProblemAsync(request, cancellationToken)
            : [];
        var inputMatchesByProblem = request.Scope is SchedulePlanInvalidationScope.SnapshotWorkOrderOrSku or SchedulePlanInvalidationScope.SnapshotMaterial
            ? await FindInputMatchesByProblemAsync(request, cancellationToken)
            : [];
        var plans = await QueryPlans(request, calendarResourceIdsByProblem.Keys, skuByProblem.Keys, inputMatchesByProblem.Keys).ToArrayAsync(cancellationToken);
        if (request.Scope == SchedulePlanInvalidationScope.GeneratedCalendar)
        {
            plans = plans
                .Where(plan =>
                    calendarResourceIdsByProblem.TryGetValue(plan.ProblemId, out var calendarResourceIds) &&
                    plan.Assignments.Any(assignment => calendarResourceIds.Contains(assignment.ResourceId)))
                .ToArray();
        }

        if (plans.Length == 0)
        {
            return new RecordSchedulePlanInvalidationsResponse(0, 0);
        }

        var existingInvalidations = dbContext.SchedulePlanInvalidations.Where(x =>
            x.OrganizationId == request.OrganizationId &&
            x.EnvironmentId == request.EnvironmentId);
        existingInvalidations = request.Scope == SchedulePlanInvalidationScope.ExactWorkOrderOperation
            ? existingInvalidations.Where(x =>
                (x.SourceEventType == request.SourceEventType && x.SourceEventId == request.SourceEventId) ||
                (x.ReasonCode == request.ReasonCode &&
                 x.AffectedWorkOrderId == request.AffectedWorkOrderId &&
                 x.AffectedOperationId == request.AffectedOperationId))
            : existingInvalidations.Where(x =>
                x.SourceEventType == request.SourceEventType &&
                x.SourceEventId == request.SourceEventId);
        var existingPlanIds = await existingInvalidations
            .Select(x => x.PlanId)
            .ToArrayAsync(cancellationToken);
        var existing = existingPlanIds.ToHashSet(StringComparer.Ordinal);
        var recordedAtUtc = timeProvider.GetUtcNow();
        var recordedCount = 0;

        foreach (var plan in plans
                     .Where(x => !existing.Contains(x.PlanId))
                     .OrderBy(x => x.PlanId, StringComparer.Ordinal))
        {
            var affectedResourceId = request.Scope is SchedulePlanInvalidationScope.Resource or SchedulePlanInvalidationScope.GeneratedWorkCenter
                ? Normalize(request.ScopeValue)
                : null;
            var (affectedWorkOrderId, affectedOperationId) = ResolveWorkOrderOrOperation(request, plans);
            if (request.Scope == SchedulePlanInvalidationScope.SnapshotWorkOrderOrSku &&
                !inputMatchesByProblem[plan.ProblemId].OrderIds.Contains(Normalize(request.AffectedWorkOrderId)))
            {
                affectedWorkOrderId = null;
            }
            var affectedOperations = request.Scope switch
            {
                SchedulePlanInvalidationScope.GeneratedCalendar =>
                    SelectAffectedOperationsForCalendar(plan, calendarResourceIdsByProblem[plan.ProblemId]),
                SchedulePlanInvalidationScope.GeneratedWorkCenter =>
                    SelectAffectedOperationsForWorkCenter(plan, Normalize(request.ScopeValue)),
                SchedulePlanInvalidationScope.SnapshotWorkOrderOrSku or SchedulePlanInvalidationScope.SnapshotMaterial =>
                    plan.Assignments.Where(x =>
                        inputMatchesByProblem[plan.ProblemId].OrderIds.Contains(x.WorkOrderId) ||
                        inputMatchesByProblem[plan.ProblemId].Operations.Contains((x.WorkOrderId, x.OperationId))).ToArray(),
                _ => SelectAffectedOperations(
                    plan,
                    affectedResourceId,
                    affectedWorkOrderId,
                    affectedOperationId),
            };
            var snapshot = SchedulePlanInvalidatedSnapshot.FromPlan(plan, affectedOperations);
            var invalidation = SchedulePlanInvalidation.Create(
                request.OrganizationId,
                request.EnvironmentId,
                plan.PlanId,
                request.SourceEventId,
                request.SourceEventType,
                request.SourceService,
                request.ReasonCode,
                affectedResourceId,
                affectedWorkOrderId,
                affectedOperationId,
                request.Scope == SchedulePlanInvalidationScope.GeneratedSku
                    ? skuByProblem[plan.ProblemId]
                    : request.AffectedSkuCode,
                request.OccurredAtUtc,
                recordedAtUtc,
                snapshot);
            dbContext.SchedulePlanInvalidations.Add(invalidation);
            recordedCount++;
        }

        return new RecordSchedulePlanInvalidationsResponse(plans.Length, recordedCount);
    }

    private IQueryable<SchedulePlan> QueryPlans(
        RecordSchedulePlanInvalidationsCommand request,
        IReadOnlyCollection<string> calendarProblemIds,
        IReadOnlyCollection<string> skuProblemIds,
        IReadOnlyCollection<string> inputProblemIds)
    {
        var normalizedScopeValue = Normalize(request.ScopeValue);
        // Inline the invalidatable-status predicate: a custom method call (IsInvalidatableStatus) inside a
        // Where cannot be translated to SQL and throws on relational providers (EF InMemory client-evaluates
        // it, which is why unit tests missed this). Keep it as a translatable boolean expression.
        var query = dbContext.SchedulePlans
            .Include(x => x.Assignments)
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                (x.Status == SchedulePlanLifecycleStatus.Generated ||
                    x.Status == SchedulePlanLifecycleStatus.Released));

        return request.Scope switch
        {
            SchedulePlanInvalidationScope.Resource => query.Where(x => x.Assignments.Any(assignment =>
                assignment.ResourceId == normalizedScopeValue ||
                assignment.WorkCenterId == normalizedScopeValue)),
            SchedulePlanInvalidationScope.GeneratedWorkCenter => query.Where(x =>
                x.Status == SchedulePlanLifecycleStatus.Generated &&
                x.Assignments.Any(assignment => assignment.WorkCenterId == normalizedScopeValue)),
            SchedulePlanInvalidationScope.GeneratedCalendar => query.Where(x =>
                x.Status == SchedulePlanLifecycleStatus.Generated &&
                calendarProblemIds.Contains(x.ProblemId)),
            SchedulePlanInvalidationScope.GeneratedSku => query.Where(x =>
                x.Status == SchedulePlanLifecycleStatus.Generated &&
                skuProblemIds.Contains(x.ProblemId)),
            SchedulePlanInvalidationScope.SnapshotWorkOrderOrSku or SchedulePlanInvalidationScope.SnapshotMaterial =>
                query.Where(x => inputProblemIds.Contains(x.ProblemId)),
            SchedulePlanInvalidationScope.WorkOrderOrOperation => query.Where(x => x.Assignments.Any(assignment =>
                assignment.WorkOrderId == normalizedScopeValue ||
                assignment.OperationId == normalizedScopeValue)),
            SchedulePlanInvalidationScope.ExactWorkOrderOperation => QueryExecutionDeviationPlans(query, request),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Scope, "Unsupported schedule invalidation scope.")
        };
    }

    private static IQueryable<SchedulePlan> QueryExecutionDeviationPlans(
        IQueryable<SchedulePlan> query,
        RecordSchedulePlanInvalidationsCommand request)
    {
        var affectedWorkOrderId = Normalize(request.AffectedWorkOrderId);
        var affectedOperationId = Normalize(request.AffectedOperationId);
        var cutoffUtc = request.ActualExecutionAtUtc!.Value.AddMinutes(-request.DeviationToleranceMinutes!.Value);

        return request.ExecutionMilestone switch
        {
            SchedulePlanExecutionMilestone.Started => query.Where(x => x.Assignments.Any(assignment =>
                assignment.WorkOrderId == affectedWorkOrderId &&
                assignment.OperationId == affectedOperationId &&
                assignment.StartUtc < cutoffUtc)),
            SchedulePlanExecutionMilestone.Completed => query.Where(x => x.Assignments.Any(assignment =>
                assignment.WorkOrderId == affectedWorkOrderId &&
                assignment.OperationId == affectedOperationId &&
                assignment.EndUtc < cutoffUtc)),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.ExecutionMilestone, "Unsupported execution milestone."),
        };
    }

    private async Task<Dictionary<string, IReadOnlySet<string>>> FindCalendarResourceIdsByProblemAsync(
        RecordSchedulePlanInvalidationsCommand request,
        CancellationToken cancellationToken)
    {
        var calendarId = Normalize(request.ScopeValue);
        var generatedProblemIds = dbContext.SchedulePlans.AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.Status == SchedulePlanLifecycleStatus.Generated)
            .Select(x => x.ProblemId);
        var snapshots = await dbContext.ScheduleProblems.AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                generatedProblemIds.Contains(x.ProblemId))
            .Select(x => new { x.ProblemId, x.ProblemJson })
            .ToArrayAsync(cancellationToken);
        var matched = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

        foreach (var snapshot in snapshots)
        {
            SchedulingProblemContract? problem;
            try
            {
                problem = System.Text.Json.JsonSerializer.Deserialize<SchedulingProblemContract>(
                    snapshot.ProblemJson,
                    SchedulingJson.Options);
            }
            catch (System.Text.Json.JsonException)
            {
                continue;
            }

            if (problem is null || !problem.Calendars.Any(x => string.Equals(x.CalendarId, calendarId, StringComparison.Ordinal)))
            {
                continue;
            }

            var resourceIds = problem.Resources
                .Where(x => string.Equals(x.CalendarId, calendarId, StringComparison.Ordinal))
                .Select(x => x.ResourceId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.Ordinal);
            if (resourceIds.Count > 0)
            {
                matched[snapshot.ProblemId] = resourceIds;
            }
        }

        return matched;
    }

    private async Task<Dictionary<string, string>> FindSkuByProblemAsync(
        RecordSchedulePlanInvalidationsCommand request,
        CancellationToken cancellationToken)
    {
        var skuCodes = request.AffectedSkuCodes!
            .Select(Normalize)
            .ToHashSet(StringComparer.Ordinal);
        var generatedProblemIds = dbContext.SchedulePlans.AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.Status == SchedulePlanLifecycleStatus.Generated)
            .Select(x => x.ProblemId);
        var snapshots = await dbContext.ScheduleProblems.AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                generatedProblemIds.Contains(x.ProblemId))
            .Select(x => new { x.ProblemId, x.ProblemJson })
            .ToArrayAsync(cancellationToken);

        return snapshots
            .Select(snapshot => new
            {
                snapshot.ProblemId,
                SkuCode = FindFirstMatchingSku(snapshot.ProblemJson, skuCodes),
            })
            .Where(x => x.SkuCode is not null)
            .ToDictionary(x => x.ProblemId, x => x.SkuCode!, StringComparer.Ordinal);
    }

    private static string? FindFirstMatchingSku(string problemJson, IReadOnlySet<string> skuCodes)
    {
        try
        {
            var problem = System.Text.Json.JsonSerializer.Deserialize<SchedulingProblemContract>(
                problemJson,
                SchedulingJson.Options);
            return problem?.MaterialReadiness
                .SelectMany(readiness => readiness.Shortages ?? [])
                .Select(shortage => shortage.MaterialId)
                .Where(skuCodes.Contains)
                .Order(StringComparer.Ordinal)
                .FirstOrDefault();
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private sealed record InputMatch(
        IReadOnlySet<string> OrderIds,
        IReadOnlySet<(string OrderId, string OperationId)> Operations);

    private async Task<Dictionary<string, InputMatch>> FindInputMatchesByProblemAsync(
        RecordSchedulePlanInvalidationsCommand request,
        CancellationToken cancellationToken)
    {
        var problemIds = dbContext.SchedulePlans.AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                (x.Status == SchedulePlanLifecycleStatus.Generated || x.Status == SchedulePlanLifecycleStatus.Released))
            .Select(x => x.ProblemId);
        var snapshots = await dbContext.ScheduleProblems.AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId && problemIds.Contains(x.ProblemId))
            .Select(x => new { x.ProblemId, x.ProblemJson })
            .ToArrayAsync(cancellationToken);
        var matches = new Dictionary<string, InputMatch>(StringComparer.Ordinal);
        var workOrderId = Normalize(request.AffectedWorkOrderId);
        var skuCode = Normalize(request.AffectedSkuCode);

        foreach (var snapshot in snapshots)
        {
            SchedulingProblemContract? problem;
            try
            {
                problem = System.Text.Json.JsonSerializer.Deserialize<SchedulingProblemContract>(
                    snapshot.ProblemJson, SchedulingJson.Options);
            }
            catch (System.Text.Json.JsonException)
            {
                continue;
            }

            if (problem is null)
            {
                continue;
            }

            var orderIds = new HashSet<string>(StringComparer.Ordinal);
            var operations = new HashSet<(string OrderId, string OperationId)>();
            if (request.Scope == SchedulePlanInvalidationScope.SnapshotWorkOrderOrSku)
            {
                foreach (var order in problem.Orders.Where(x =>
                    string.Equals(x.OrderId, workOrderId, StringComparison.Ordinal) ||
                    string.Equals(x.SkuCode, skuCode, StringComparison.Ordinal)))
                {
                    orderIds.Add(order.OrderId);
                }
            }
            else
            {
                foreach (var readiness in problem.MaterialReadiness.Where(x =>
                    x.Shortages?.Any(shortage => string.Equals(shortage.MaterialId, skuCode, StringComparison.Ordinal)) == true))
                {
                    switch (readiness.ScopeType.ToLowerInvariant())
                    {
                        case "order":
                            orderIds.Add(readiness.ScopeId);
                            break;
                        case "sku":
                            foreach (var order in problem.Orders.Where(x => string.Equals(x.SkuCode, readiness.ScopeId, StringComparison.Ordinal)))
                            {
                                orderIds.Add(order.OrderId);
                            }
                            break;
                        case "operation":
                        case "resource":
                            foreach (var order in problem.Orders)
                            {
                                foreach (var operation in order.Operations.Where(x =>
                                    string.Equals(readiness.ScopeType, "operation", StringComparison.OrdinalIgnoreCase)
                                        ? string.Equals(x.OperationId, readiness.ScopeId, StringComparison.Ordinal)
                                        : x.EligibleResourceIds.Contains(readiness.ScopeId, StringComparer.Ordinal) ||
                                          string.Equals(x.PrimaryResourceId, readiness.ScopeId, StringComparison.Ordinal)))
                                {
                                    operations.Add((order.OrderId, operation.OperationId));
                                }
                            }
                            break;
                    }
                }
            }

            if (orderIds.Count > 0 || operations.Count > 0)
            {
                matches[snapshot.ProblemId] = new InputMatch(orderIds, operations);
            }
        }

        return matches;
    }

    private static (string? WorkOrderId, string? OperationId) ResolveWorkOrderOrOperation(
        RecordSchedulePlanInvalidationsCommand request,
        IReadOnlyCollection<SchedulePlan> plans)
    {
        if (request.Scope != SchedulePlanInvalidationScope.WorkOrderOrOperation)
        {
            return request.Scope == SchedulePlanInvalidationScope.ExactWorkOrderOperation
                ? (Normalize(request.AffectedWorkOrderId), Normalize(request.AffectedOperationId))
                : (request.AffectedWorkOrderId, null);
        }

        var normalizedSource = Normalize(request.ScopeValue);
        var matchesWorkOrder = plans
            .SelectMany(x => x.Assignments)
            .Any(x => string.Equals(x.WorkOrderId, normalizedSource, StringComparison.Ordinal));
        var matchesOperation = plans
            .SelectMany(x => x.Assignments)
            .Any(x => string.Equals(x.OperationId, normalizedSource, StringComparison.Ordinal));
        return (
            matchesWorkOrder ? normalizedSource : null,
            matchesOperation && !matchesWorkOrder ? normalizedSource : null);
    }

    private static IReadOnlyCollection<SchedulePlanAssignment> SelectAffectedOperations(
        SchedulePlan plan,
        string? affectedResourceId,
        string? affectedWorkOrderId,
        string? affectedOperationId)
    {
        var assignments = plan.Assignments.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(affectedResourceId))
        {
            var normalized = affectedResourceId.Trim();
            assignments = assignments.Where(x =>
                string.Equals(x.ResourceId, normalized, StringComparison.Ordinal) ||
                string.Equals(x.WorkCenterId, normalized, StringComparison.Ordinal));
        }
        else if (!string.IsNullOrWhiteSpace(affectedWorkOrderId) &&
                 !string.IsNullOrWhiteSpace(affectedOperationId))
        {
            var normalizedWorkOrderId = affectedWorkOrderId.Trim();
            var normalizedOperationId = affectedOperationId.Trim();
            assignments = assignments.Where(x =>
                string.Equals(x.WorkOrderId, normalizedWorkOrderId, StringComparison.Ordinal) &&
                string.Equals(x.OperationId, normalizedOperationId, StringComparison.Ordinal));
        }
        else if (!string.IsNullOrWhiteSpace(affectedOperationId))
        {
            var normalized = affectedOperationId.Trim();
            assignments = assignments.Where(x => string.Equals(x.OperationId, normalized, StringComparison.Ordinal));
        }
        else if (!string.IsNullOrWhiteSpace(affectedWorkOrderId))
        {
            var normalized = affectedWorkOrderId.Trim();
            assignments = assignments.Where(x => string.Equals(x.WorkOrderId, normalized, StringComparison.Ordinal));
        }

        var selected = assignments.ToArray();
        return selected.Length == 0 ? plan.Assignments.ToArray() : selected;
    }

    private static IReadOnlyCollection<SchedulePlanAssignment> SelectAffectedOperationsForCalendar(
        SchedulePlan plan,
        IReadOnlySet<string> resourceIds)
    {
        return plan.Assignments
            .Where(x => resourceIds.Contains(x.ResourceId))
            .ToArray();
    }

    private static IReadOnlyCollection<SchedulePlanAssignment> SelectAffectedOperationsForWorkCenter(
        SchedulePlan plan,
        string workCenterId)
    {
        return plan.Assignments
            .Where(x => string.Equals(x.WorkCenterId, workCenterId, StringComparison.Ordinal))
            .ToArray();
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
