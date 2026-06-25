using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.DemandSourceAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;

public sealed record CancelDemandSourceCommand(
    string OrganizationId,
    string EnvironmentId,
    DemandSourceId DemandSourceId) : ICommand;

public sealed class CancelDemandSourceCommandValidator : AbstractValidator<CancelDemandSourceCommand>
{
    public CancelDemandSourceCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.DemandSourceId).NotEmpty();
    }
}

public sealed class CancelDemandSourceCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CancelDemandSourceCommand>
{
    public async Task Handle(CancelDemandSourceCommand request, CancellationToken cancellationToken)
    {
        var demand = await dbContext.DemandSources.SingleOrDefaultAsync(x =>
            x.Id == request.DemandSourceId
            && x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId,
            cancellationToken)
            ?? throw new KnownException($"Demand source '{request.DemandSourceId}' was not found.");

        dbContext.DemandSources.Remove(demand);
    }
}
