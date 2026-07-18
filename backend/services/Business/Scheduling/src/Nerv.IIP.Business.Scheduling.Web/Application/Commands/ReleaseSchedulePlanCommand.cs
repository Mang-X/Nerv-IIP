using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Commands;

public sealed record ReleaseSchedulePlanCommand(string PlanId, string OrganizationId, string EnvironmentId) : ICommand<ReleaseSchedulePlanResponse>;

public sealed record ReleaseSchedulePlanResponse(
    string PlanId,
    SchedulePlanStatusContract Status,
    DateTimeOffset? ReleasedAtUtc,
    long ReleaseRevision);

public sealed class ReleaseSchedulePlanCommandValidator : AbstractValidator<ReleaseSchedulePlanCommand>
{
    public ReleaseSchedulePlanCommandValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
    }
}

public sealed class ReleaseSchedulePlanUniqueConflictBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            request is ReleaseSchedulePlanCommand &&
            ScheduleReleaseUniqueConflictClassifier.IsReleaseGovernanceConflict(exception))
        {
            throw new KnownException("Schedule release conflicted with another release in the same scope; refresh and retry.");
        }
    }
}

public sealed class ReleaseSchedulePlanCommandHandler(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider,
    IScheduleReleaseScopeLock? releaseScopeLock = null)
    : ICommandHandler<ReleaseSchedulePlanCommand, ReleaseSchedulePlanResponse>
{
    public async Task<ReleaseSchedulePlanResponse> Handle(ReleaseSchedulePlanCommand request, CancellationToken cancellationToken)
    {
        await using var scopeLock = await AcquireScopeLockAsync(request, cancellationToken);
        var plan = await dbContext.SchedulePlans
            .SingleOrDefaultAsync(
                x => x.PlanId == request.PlanId &&
                    x.OrganizationId == request.OrganizationId &&
                    x.EnvironmentId == request.EnvironmentId,
                cancellationToken)
            ?? throw new KnownException($"Schedule plan was not found, PlanId = {request.PlanId}");

        if (plan.Status == SchedulePlanLifecycleStatus.Released)
        {
            return Response(plan);
        }

        if (plan.Status is SchedulePlanLifecycleStatus.Superseded or SchedulePlanLifecycleStatus.Revoked)
        {
            throw new KnownException("Superseded or revoked schedule plan cannot be released again.");
        }

        var hasErrorConflict = await dbContext.Set<SchedulePlanConflict>()
            .AnyAsync(
                x => x.SchedulePlanId == plan.Id &&
                    x.Severity == ScheduleConflictSeverity.Error,
                cancellationToken);
        var hasUnscheduledOperation = await dbContext.Set<SchedulePlanUnscheduledOperation>()
            .AnyAsync(x => x.SchedulePlanId == plan.Id, cancellationToken);
        if (hasErrorConflict || hasUnscheduledOperation)
        {
            throw new KnownException("Schedule plan cannot be released because it contains error conflicts or unscheduled operations.");
        }

        // A plan whose scheduling inputs have since changed (recorded as an invalidation) is stale and
        // must be regenerated before release — releasing it would dispatch an out-of-date schedule.
        var isInvalidated = await dbContext.SchedulePlanInvalidations
            .AnyAsync(
                x => x.OrganizationId == request.OrganizationId &&
                    x.EnvironmentId == request.EnvironmentId &&
                    x.PlanId == request.PlanId,
                cancellationToken);
        if (isInvalidated)
        {
            throw new KnownException("Schedule plan cannot be released because it has been invalidated by a scheduling input change; regenerate the plan first.");
        }

        var activePlan = await dbContext.SchedulePlans.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.Status == SchedulePlanLifecycleStatus.Released &&
                x.PlanId != request.PlanId,
            cancellationToken);
        var maxReleaseRevision = await dbContext.SchedulePlans
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.ReleaseRevision != null)
            .MaxAsync(x => (long?)x.ReleaseRevision, cancellationToken) ?? 0;
        var occurredAtUtc = timeProvider.GetUtcNow();
        activePlan?.Supersede(plan.PlanId, occurredAtUtc);
        plan.Release(occurredAtUtc, checked(maxReleaseRevision + 1));
        return Response(plan);
    }

    private async Task<IAsyncDisposable> AcquireScopeLockAsync(
        ReleaseSchedulePlanCommand request,
        CancellationToken cancellationToken)
    {
        return releaseScopeLock is null
            ? NoopAsyncDisposable.Instance
            : await releaseScopeLock.AcquireAsync(
                request.OrganizationId,
                request.EnvironmentId,
                cancellationToken);
    }

    private static ReleaseSchedulePlanResponse Response(SchedulePlan plan)
    {
        return new ReleaseSchedulePlanResponse(
            plan.PlanId,
            SchedulePlanStatusContract.Released,
            plan.ReleasedAtUtc,
            plan.ReleaseRevision ?? throw new InvalidOperationException("Released plan must have a release revision."));
    }

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public static NoopAsyncDisposable Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
