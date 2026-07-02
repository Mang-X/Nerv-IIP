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

public sealed class AssembleSchedulingProblemCommandHandler(ISchedulingProblemProducer producer)
    : ICommandHandler<AssembleSchedulingProblemCommand, SchedulingProblemContract>
{
    public async Task<SchedulingProblemContract> Handle(AssembleSchedulingProblemCommand request, CancellationToken cancellationToken)
    {
        return await producer.AssembleAsync(request.Request, cancellationToken);
    }
}
