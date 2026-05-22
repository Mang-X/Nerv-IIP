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

public sealed class CreateProductionVersionCommandHandler(IProductionVersionRepository repository)
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
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published);
        await repository.AddAsync(version, cancellationToken);
        return new ProductionVersionCommandResult(version.Id.Id.ToString("D"));
    }
}
