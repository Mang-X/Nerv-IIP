using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.DemandSourceAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;

public sealed record CreateOrUpdateDemandSourceCommand(
    string OrganizationId,
    string EnvironmentId,
    string DemandType,
    string? SourceReference,
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal Quantity,
    DateOnly DueDate,
    string? IdempotencyKey = null) : ICommand<DemandSourceId>;

public sealed class CreateOrUpdateDemandSourceCommandValidator : AbstractValidator<CreateOrUpdateDemandSourceCommand>
{
    public CreateOrUpdateDemandSourceCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.DemandType)
            .NotEmpty()
            .MaximumLength(32)
            .Must(value => !string.Equals(value, "sales-order", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Demand type 'sales-order' is integration-owned and cannot be created manually.");
        RuleFor(x => x.SourceReference).MaximumLength(128);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.UomCode).NotEmpty().MaximumLength(32);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class CreateOrUpdateDemandSourceCommandHandler(ApplicationDbContext dbContext, DemandPlanningCodingService? codingService = null)
    : ICommandHandler<CreateOrUpdateDemandSourceCommand, DemandSourceId>
{
    private readonly DemandPlanningCodingService _codingService = codingService ?? new DemandPlanningCodingService();

    public async Task<DemandSourceId> Handle(CreateOrUpdateDemandSourceCommand request, CancellationToken cancellationToken)
    {
        if (string.Equals(request.DemandType, "sales-order", StringComparison.OrdinalIgnoreCase))
        {
            throw new KnownException("Demand type 'sales-order' is integration-owned and cannot be created manually.");
        }

        var allocation = await _codingService.AllocateDemandReferenceAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.SourceReference,
            request.IdempotencyKey,
            string.Join('|', request.DemandType, request.SkuCode, request.UomCode, request.SiteCode, request.Quantity, request.DueDate),
            cancellationToken);
        var demand = await dbContext.DemandSources.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.DemandType == request.DemandType.ToLower()
            && x.SourceReference == allocation.Code,
            cancellationToken);
        if (demand is null)
        {
            demand = DemandSource.Create(
                request.OrganizationId,
                request.EnvironmentId,
                request.DemandType,
                allocation.Code,
                request.SkuCode,
                request.UomCode,
                request.SiteCode,
                request.Quantity,
                request.DueDate);
            dbContext.DemandSources.Add(demand);
        }
        else
        {
            demand.Update(request.Quantity, request.DueDate);
        }

        return demand.Id;
    }
}
