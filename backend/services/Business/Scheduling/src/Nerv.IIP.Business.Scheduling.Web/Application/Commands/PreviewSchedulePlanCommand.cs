using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Commands;

public sealed record PreviewSchedulePlanCommand(
    SchedulingProblemContract Problem,
    IReadOnlyCollection<FixedWorkCenterReservation>? FixedReservations = null) : ICommand<SchedulePlanContract>;

public sealed class PreviewSchedulePlanCommandValidator : AbstractValidator<PreviewSchedulePlanCommand>
{
    public PreviewSchedulePlanCommandValidator()
    {
        RuleFor(x => x.Problem).NotNull();
        RuleFor(x => x.Problem.OrganizationId).NotEmpty().MaximumLength(64).When(x => x.Problem is not null);
        RuleFor(x => x.Problem.EnvironmentId).NotEmpty().MaximumLength(64).When(x => x.Problem is not null);
        RuleFor(x => x.Problem.HorizonEndUtc).GreaterThan(x => x.Problem.HorizonStartUtc).When(x => x.Problem is not null);
        RuleFor(x => x.Problem).Custom((problem, context) =>
        {
            foreach (var error in SchedulingProblemNormalizer.ValidateForErrors(problem))
            {
                context.AddFailure(error);
            }
        });
    }
}

public sealed class PreviewSchedulePlanCommandHandler(
    FiniteCapacityScheduler scheduler,
    TimeProvider timeProvider,
    ISchedulingEquipmentAvailabilityProvider equipmentAvailabilityProvider,
    ISchedulingMaterialReadinessProvider materialReadinessProvider,
    ISchedulingOperationOverrideOverlay overrideOverlay,
    SchedulingEquipmentUnknownModeOption equipmentUnknownMode)
    : ICommandHandler<PreviewSchedulePlanCommand, SchedulePlanContract>
{
    public async Task<SchedulePlanContract> Handle(PreviewSchedulePlanCommand request, CancellationToken cancellationToken)
    {
        var overlaidProblem = await overrideOverlay.ApplyAsync(request.Problem, cancellationToken);
        var availability = await equipmentAvailabilityProvider.QueryAsync(overlaidProblem, cancellationToken);
        var materialReadiness = await materialReadinessProvider.QueryAsync(overlaidProblem, cancellationToken);
        var schedulingProblem = SchedulingProblemNormalizer.Normalize(MaterialReadinessSchedulingAdapter.Apply(
            EquipmentAvailabilitySchedulingAdapter.Apply(overlaidProblem, availability, equipmentUnknownMode.Mode),
            materialReadiness));
        var fixedReservations = request.FixedReservations ?? [];
        var operationKeys = schedulingProblem.Orders
            .SelectMany(order => order.Operations.Select(operation => (order.OrderId, operation.OperationId)))
            .ToHashSet();
        var plan = scheduler.ScheduleNormalized(schedulingProblem, $"preview-{request.Problem.ProblemId}", timeProvider.GetUtcNow(),
            fixedReservations.Where(x => operationKeys.Contains((x.OrderId, x.OperationId))).ToArray(),
            fixedReservations.Where(x => !operationKeys.Contains((x.OrderId, x.OperationId))).ToArray());
        return SchedulePlanContractMapper.WithStatus(plan, SchedulePlanStatusContract.Preview);
    }
}
