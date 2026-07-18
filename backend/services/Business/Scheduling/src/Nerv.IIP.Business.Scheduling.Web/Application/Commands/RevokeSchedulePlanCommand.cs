using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Commands;

public sealed record RevokeSchedulePlanCommand(
    string PlanId,
    string OrganizationId,
    string EnvironmentId) : ICommand<RevokeSchedulePlanResponse>;

public sealed record RevokeSchedulePlanResponse(
    string PlanId,
    SchedulePlanStatusContract Status,
    long ReleaseRevision,
    DateTimeOffset? RevokedAtUtc,
    string Reason,
    string? SupersededByPlanId);

public sealed class RevokeSchedulePlanCommandValidator : AbstractValidator<RevokeSchedulePlanCommand>
{
    public RevokeSchedulePlanCommandValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
    }
}

public sealed class RevokeSchedulePlanCommandHandler(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider,
    IScheduleReleaseScopeLock? releaseScopeLock = null)
    : ICommandHandler<RevokeSchedulePlanCommand, RevokeSchedulePlanResponse>
{
    public async Task<RevokeSchedulePlanResponse> Handle(
        RevokeSchedulePlanCommand request,
        CancellationToken cancellationToken)
    {
        await using var scopeLock = releaseScopeLock is null
            ? NoopAsyncDisposable.Instance
            : await releaseScopeLock.AcquireAsync(
                request.OrganizationId,
                request.EnvironmentId,
                cancellationToken);
        var plan = await dbContext.SchedulePlans.SingleOrDefaultAsync(
            x => x.PlanId == request.PlanId &&
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId,
            cancellationToken)
            ?? throw new KnownException($"Schedule plan was not found, PlanId = {request.PlanId}");

        if (plan.Status == SchedulePlanLifecycleStatus.Generated)
        {
            throw new KnownException("Generated schedule plan has not been released and cannot be revoked.");
        }

        if (plan.Status == SchedulePlanLifecycleStatus.Released)
        {
            plan.Revoke(timeProvider.GetUtcNow());
        }

        return new RevokeSchedulePlanResponse(
            plan.PlanId,
            Status(plan.Status),
            plan.ReleaseRevision ?? throw new InvalidOperationException("Revoked plan must retain its release revision."),
            plan.RevokedAtUtc,
            Reason(plan.RevocationReason),
            plan.SupersededByPlanId);
    }

    private static SchedulePlanStatusContract Status(SchedulePlanLifecycleStatus status) => status switch
    {
        SchedulePlanLifecycleStatus.Superseded => SchedulePlanStatusContract.Superseded,
        SchedulePlanLifecycleStatus.Revoked => SchedulePlanStatusContract.Revoked,
        _ => throw new InvalidOperationException($"Schedule plan status {status} is not revoked.")
    };

    private static string Reason(SchedulePlanRevocationReason? reason) => reason switch
    {
        SchedulePlanRevocationReason.Superseded => "superseded",
        SchedulePlanRevocationReason.Explicit => "explicit",
        _ => throw new InvalidOperationException("Revoked plan must retain its revocation reason.")
    };

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public static NoopAsyncDisposable Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
