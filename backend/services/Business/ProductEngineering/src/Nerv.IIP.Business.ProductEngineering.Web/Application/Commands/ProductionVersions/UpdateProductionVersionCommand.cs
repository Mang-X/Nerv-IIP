using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure.Repositories;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Commands.ProductionVersions;

public sealed record UpdateProductionVersionCommand(
    string ProductionVersionId,
    string MbomVersionId,
    string RoutingVersionId,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    decimal? LotSizeMin,
    decimal? LotSizeMax,
    int Priority,
    bool IsDefault) : ICommand<ProductionVersionCommandResult>;

public sealed class UpdateProductionVersionCommandValidator : AbstractValidator<UpdateProductionVersionCommand>
{
    public UpdateProductionVersionCommandValidator()
    {
        RuleFor(x => x.ProductionVersionId).NotEmpty();
        RuleFor(x => x.MbomVersionId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RoutingVersionId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Priority).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateProductionVersionCommandHandler(IProductionVersionRepository repository)
    : ICommandHandler<UpdateProductionVersionCommand, ProductionVersionCommandResult>
{
    public async Task<ProductionVersionCommandResult> Handle(UpdateProductionVersionCommand request, CancellationToken cancellationToken)
    {
        var version = await repository.GetByIdAsync(request.ProductionVersionId, cancellationToken)
            ?? throw new KnownException($"Production version '{request.ProductionVersionId}' was not found.");

        if (request.IsDefault && await repository.HasOverlappingDefaultAsync(
            version.OrganizationId,
            version.EnvironmentId,
            version.SkuCode,
            request.ValidFrom,
            request.ValidTo,
            request.ProductionVersionId,
            cancellationToken))
        {
            throw new KnownException($"Production version default already exists for SKU '{version.SkuCode}' in the requested effective window.");
        }

        version.UpdateBinding(
            request.MbomVersionId,
            request.RoutingVersionId,
            request.ValidFrom,
            request.ValidTo,
            request.LotSizeMin,
            request.LotSizeMax,
            request.Priority,
            request.IsDefault,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published);
        return new ProductionVersionCommandResult(version.Id.Id.ToString("D"));
    }
}
