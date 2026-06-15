using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure.Repositories;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Commands.ProductionVersions;

public sealed record CreateProductionVersionCommand(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string MbomVersionId,
    string RoutingVersionId,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    decimal? LotSizeMin,
    decimal? LotSizeMax,
    int Priority,
    bool IsDefault) : ICommand<ProductionVersionCommandResult>;

public sealed record ProductionVersionCommandResult(string ProductionVersionId);

public sealed class CreateProductionVersionCommandValidator : AbstractValidator<CreateProductionVersionCommand>
{
    public CreateProductionVersionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.MbomVersionId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RoutingVersionId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Priority).GreaterThanOrEqualTo(0);
        RuleFor(x => x.LotSizeMin).GreaterThanOrEqualTo(0).When(x => x.LotSizeMin.HasValue);
        RuleFor(x => x.LotSizeMax).GreaterThanOrEqualTo(0).When(x => x.LotSizeMax.HasValue);
    }
}

public sealed class CreateProductionVersionCommandHandler(
    IProductionVersionRepository repository,
    IManufacturingBomRepository manufacturingBomRepository,
    IRoutingRepository routingRepository)
    : ICommandHandler<CreateProductionVersionCommand, ProductionVersionCommandResult>
{
    public async Task<ProductionVersionCommandResult> Handle(CreateProductionVersionCommand request, CancellationToken cancellationToken)
    {
        if (request.IsDefault && await repository.HasOverlappingDefaultAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.SkuCode,
            request.ValidFrom,
            request.ValidTo,
            cancellationToken: cancellationToken))
        {
            throw new KnownException($"Production version default already exists for SKU '{request.SkuCode}' in the requested effective window.");
        }

        var binding = await ProductionVersionBindingValidator.ResolveAsync(
            manufacturingBomRepository,
            routingRepository,
            request.OrganizationId,
            request.EnvironmentId,
            request.SkuCode,
            request.MbomVersionId,
            request.RoutingVersionId,
            request.ValidFrom,
            request.ValidTo,
            cancellationToken);

        var version = ProductionVersion.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.SkuCode,
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
        await repository.AddAsync(version, cancellationToken);
        return new ProductionVersionCommandResult(version.Id.Id.ToString("D"));
    }
}

internal sealed record ProductionVersionBinding(EngineeringVersionStatus MbomStatus, EngineeringVersionStatus RoutingStatus);

internal static class ProductionVersionBindingValidator
{
    public static async Task<ProductionVersionBinding> ResolveAsync(
        IManufacturingBomRepository manufacturingBomRepository,
        IRoutingRepository routingRepository,
        string organizationId,
        string environmentId,
        string skuCode,
        string mbomVersionId,
        string routingVersionId,
        DateOnly validFrom,
        DateOnly? validTo,
        CancellationToken cancellationToken)
    {
        var mbom = await manufacturingBomRepository.GetByVersionIdAsync(organizationId, environmentId, mbomVersionId, cancellationToken)
            ?? throw new KnownException($"MBOM version '{mbomVersionId}' was not found.");
        var routing = await routingRepository.GetByVersionIdAsync(organizationId, environmentId, routingVersionId, cancellationToken)
            ?? throw new KnownException($"Routing version '{routingVersionId}' was not found.");

        if (!string.Equals(mbom.SkuCode, skuCode, StringComparison.Ordinal))
        {
            throw new KnownException($"MBOM version '{mbomVersionId}' belongs to SKU '{mbom.SkuCode}', not requested SKU '{skuCode}'.");
        }

        if (!string.Equals(routing.SkuCode, skuCode, StringComparison.Ordinal))
        {
            throw new KnownException($"Routing version '{routingVersionId}' belongs to SKU '{routing.SkuCode}', not requested SKU '{skuCode}'.");
        }

        EnsurePublishedAndEffective("MBOM", mbomVersionId, mbom.Status, mbom.EffectiveDate, validFrom, validTo);
        EnsurePublishedAndEffective("Routing", routingVersionId, routing.Status, routing.EffectiveDate, validFrom, validTo);
        return new ProductionVersionBinding(mbom.Status, routing.Status);
    }

    private static void EnsurePublishedAndEffective(
        string kind,
        string versionId,
        EngineeringVersionStatus status,
        DateOnly? effectiveDate,
        DateOnly validFrom,
        DateOnly? validTo)
    {
        if (status != EngineeringVersionStatus.Published)
        {
            throw new KnownException($"{kind} version '{versionId}' must be published before it can be bound to a production version.");
        }

        if (effectiveDate is not null && effectiveDate.Value > validFrom)
        {
            throw new KnownException($"{kind} version '{versionId}' is not effective for the requested production version window.");
        }
    }
}
