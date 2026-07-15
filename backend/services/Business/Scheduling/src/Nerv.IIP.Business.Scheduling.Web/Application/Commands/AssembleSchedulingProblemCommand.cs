using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Commands;

public sealed record AssembleSchedulingProblemCommand(AssembleSchedulingProblemRequest Request) : ICommand<SchedulingProblemContract>;

public sealed class AssembleSchedulingProblemCommandValidator : AbstractValidator<AssembleSchedulingProblemCommand>
{
    public AssembleSchedulingProblemCommandValidator()
    {
        RuleFor(x => x.Request).NotNull();
        RuleFor(x => x.Request.ProblemId).NotEmpty().MaximumLength(128).When(x => x.Request is not null);
        RuleFor(x => x.Request.OrganizationId).NotEmpty().MaximumLength(64).When(x => x.Request is not null);
        RuleFor(x => x.Request.EnvironmentId).NotEmpty().MaximumLength(64).When(x => x.Request is not null);
        RuleFor(x => x.Request.HorizonEndUtc).GreaterThan(x => x.Request.HorizonStartUtc).When(x => x.Request is not null);
        RuleFor(x => x.Request.Orders).NotEmpty().When(x => x.Request is not null);
        RuleForEach(x => x.Request.Orders).ChildRules(order =>
        {
            order.RuleFor(x => x.OrderId).NotEmpty().MaximumLength(128);
            order.RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
            order.RuleFor(x => x.Quantity).GreaterThan(0);
            order.RuleFor(x => x.RoutingVersionId).NotEmpty().MaximumLength(150);
        });
    }
}

public sealed class AssembleSchedulingProblemCommandHandler(
    ISchedulingProblemProducer producer,
    ApplicationDbContext dbContext,
    ISchedulingOperationOverrideOverlay overrideOverlay)
    : ICommandHandler<AssembleSchedulingProblemCommand, SchedulingProblemContract>
{
    public async Task<SchedulingProblemContract> Handle(AssembleSchedulingProblemCommand request, CancellationToken cancellationToken)
    {
        var source = request.Request;
        if (source.LockedOperationIds is { Count: > 0 } && string.IsNullOrWhiteSpace(source.BasePlanId))
        {
            throw new KnownException("BasePlanId is required when LockedOperationIds are supplied.");
        }

        var resolvedLocks = new Dictionary<(string OrderId, string OperationId), SchedulingLockedAssignmentContract>();
        if (!string.IsNullOrWhiteSpace(source.BasePlanId))
        {
            var plan = await dbContext.SchedulePlans.AsNoTracking().Include(x => x.Assignments)
                .SingleOrDefaultAsync(x => x.OrganizationId == source.OrganizationId &&
                    x.EnvironmentId == source.EnvironmentId && x.PlanId == source.BasePlanId, cancellationToken)
                ?? throw new KnownException($"Schedule plan was not found, PlanId = {source.BasePlanId}");
            foreach (var operationId in (source.LockedOperationIds ?? []).Distinct(StringComparer.Ordinal))
            {
                var assignment = plan.Assignments.SingleOrDefault(x => x.OperationId == operationId)
                    ?? throw new KnownException($"Schedule operation was not found in base plan, OperationId = {operationId}");
                resolvedLocks[(assignment.WorkOrderId, assignment.OperationId)] = new SchedulingLockedAssignmentContract(
                    assignment.AssignmentId, assignment.WorkOrderId, assignment.OperationId,
                    assignment.OperationSequence, assignment.ResourceId, assignment.WorkCenterId,
                    assignment.StartUtc, assignment.EndUtc, "base-plan-lock");
            }
        }

        foreach (var explicitLock in source.LockedAssignments ?? [])
        {
            resolvedLocks[(explicitLock.OrderId, explicitLock.OperationId)] = explicitLock;
        }

        var problem = await producer.AssembleAsync(
            source with { LockedAssignments = resolvedLocks.Values.ToArray() }, cancellationToken);
        return await overrideOverlay.ApplyAsync(problem, cancellationToken);
    }
}
