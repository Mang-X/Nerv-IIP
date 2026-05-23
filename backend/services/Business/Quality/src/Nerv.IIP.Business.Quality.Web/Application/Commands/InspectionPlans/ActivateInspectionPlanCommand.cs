using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionPlans;

public sealed record ActivateInspectionPlanCommand(InspectionPlanId InspectionPlanId) : ICommand;

public sealed class ActivateInspectionPlanCommandValidator : AbstractValidator<ActivateInspectionPlanCommand>
{
    public ActivateInspectionPlanCommandValidator()
    {
        RuleFor(x => x.InspectionPlanId).NotEmpty();
    }
}

public sealed class ActivateInspectionPlanCommandHandler(IInspectionPlanRepository repository)
    : ICommandHandler<ActivateInspectionPlanCommand>
{
    public async Task Handle(ActivateInspectionPlanCommand request, CancellationToken cancellationToken)
    {
        var plan = await repository.GetAsync(request.InspectionPlanId, cancellationToken)
            ?? throw new KnownException($"Inspection plan '{request.InspectionPlanId}' was not found.");
        plan.Activate();
    }
}
