using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Commands;

public sealed record PreviewSchedulePlanCommand(SchedulingProblemContract Problem) : ICommand<SchedulePlanContract>;

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

public sealed class PreviewSchedulePlanCommandHandler(FiniteCapacityScheduler scheduler, TimeProvider timeProvider)
    : ICommandHandler<PreviewSchedulePlanCommand, SchedulePlanContract>
{
    public Task<SchedulePlanContract> Handle(PreviewSchedulePlanCommand request, CancellationToken cancellationToken)
    {
        var plan = scheduler.Schedule(request.Problem, $"preview-{request.Problem.ProblemId}", timeProvider.GetUtcNow());
        return Task.FromResult(SchedulePlanContractMapper.WithStatus(plan, SchedulePlanStatusContract.Preview));
    }
}
