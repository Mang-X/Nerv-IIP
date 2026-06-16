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

public sealed class UpdateProductionVersionCommandHandler(
    IProductionVersionRepository repository,
    IManufacturingBomRepository manufacturingBomRepository,
    IRoutingRepository routingRepository)
    : ICommandHandler<UpdateProductionVersionCommand, ProductionVersionCommandResult>
{
    public async Task<ProductionVersionCommandResult> Handle(UpdateProductionVersionCommand request, CancellationToken cancellationToken)
    {
        var version = await repository.GetByIdAsync(request.ProductionVersionId, cancellationToken)
            ?? throw new KnownException($"Production version '{request.ProductionVersionId}' was not found.");

        if (await repository.HasOverlappingActiveAsync(
            version.OrganizationId,
            version.EnvironmentId,
            version.SkuCode,
            request.ValidFrom,
            request.ValidTo,
            request.ProductionVersionId,
            cancellationToken))
        {
            var scope = request.IsDefault ? "default " : string.Empty;
            throw new KnownException($"Production version {scope}effective window already exists for SKU '{version.SkuCode}'. Archive or close the current version before creating an overlapping one.");
        }

        var binding = await ProductionVersionBindingValidator.ResolveAsync(
            manufacturingBomRepository,
            routingRepository,
            version.OrganizationId,
            version.EnvironmentId,
            version.SkuCode,
            request.MbomVersionId,
            request.RoutingVersionId,
            request.ValidFrom,
            request.ValidTo,
            cancellationToken);

        version.UpdateBinding(
            request.MbomVersionId,
            request.RoutingVersionId,
            request.ValidFrom,
            request.ValidTo,
            request.LotSizeMin,
            request.LotSizeMax,
            request.Priority,
            request.IsDefault,
            binding.MbomStatus,
            binding.RoutingStatus);
        return new ProductionVersionCommandResult(version.Id.Id.ToString("D"));
    }
}
