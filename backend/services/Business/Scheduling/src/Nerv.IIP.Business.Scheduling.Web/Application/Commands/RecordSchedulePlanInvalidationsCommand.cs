using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Commands;

public enum SchedulePlanInvalidationScope
{
    Resource = 0,
    WorkOrderOrOperation = 1,
    AllInvalidatablePlans = 2,
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
    string? AffectedSkuCode) : ICommand<RecordSchedulePlanInvalidationsResponse>;

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
            .When(x => x.Scope is SchedulePlanInvalidationScope.Resource or SchedulePlanInvalidationScope.WorkOrderOrOperation);
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
        var plans = await QueryPlans(request).ToArrayAsync(cancellationToken);
        if (plans.Length == 0)
        {
            return new RecordSchedulePlanInvalidationsResponse(0, 0);
        }

        var existingPlanIds = await dbContext.SchedulePlanInvalidations
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.SourceEventType == request.SourceEventType &&
                x.SourceEventId == request.SourceEventId)
            .Select(x => x.PlanId)
            .ToArrayAsync(cancellationToken);
        var existing = existingPlanIds.ToHashSet(StringComparer.Ordinal);
        var recordedAtUtc = timeProvider.GetUtcNow();
        var recordedCount = 0;

        foreach (var plan in plans
                     .Where(x => !existing.Contains(x.PlanId))
                     .OrderBy(x => x.PlanId, StringComparer.Ordinal))
        {
            var affectedResourceId = request.Scope == SchedulePlanInvalidationScope.Resource
                ? Normalize(request.ScopeValue)
                : null;
            var (affectedWorkOrderId, affectedOperationId) = ResolveWorkOrderOrOperation(request, plans);
            var affectedOperations = SelectAffectedOperations(
                plan,
                affectedResourceId,
                affectedWorkOrderId,
                affectedOperationId);
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
                request.AffectedSkuCode,
                request.OccurredAtUtc,
                recordedAtUtc,
                snapshot);
            dbContext.SchedulePlanInvalidations.Add(invalidation);
            recordedCount++;
        }

        return new RecordSchedulePlanInvalidationsResponse(plans.Length, recordedCount);
    }

    private IQueryable<SchedulePlan> QueryPlans(RecordSchedulePlanInvalidationsCommand request)
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
            SchedulePlanInvalidationScope.WorkOrderOrOperation => query.Where(x => x.Assignments.Any(assignment =>
                assignment.WorkOrderId == normalizedScopeValue ||
                assignment.OperationId == normalizedScopeValue)),
            SchedulePlanInvalidationScope.AllInvalidatablePlans => query,
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Scope, "Unsupported schedule invalidation scope.")
        };
    }

    private static (string? WorkOrderId, string? OperationId) ResolveWorkOrderOrOperation(
        RecordSchedulePlanInvalidationsCommand request,
        IReadOnlyCollection<SchedulePlan> plans)
    {
        if (request.Scope != SchedulePlanInvalidationScope.WorkOrderOrOperation)
        {
            return (request.AffectedWorkOrderId, null);
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

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
