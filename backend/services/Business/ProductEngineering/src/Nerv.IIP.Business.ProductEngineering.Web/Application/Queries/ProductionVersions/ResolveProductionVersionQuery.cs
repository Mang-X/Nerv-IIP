using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Contracts.ProductEngineering;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Queries.ProductionVersions;

public sealed record ResolveProductionVersionQuery(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    DateOnly EffectiveDate,
    decimal LotSize) : IQuery<ResolveProductionVersionResponse>;

public sealed class ResolveProductionVersionQueryValidator : AbstractValidator<ResolveProductionVersionQuery>
{
    public ResolveProductionVersionQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LotSize).GreaterThanOrEqualTo(0);
    }
}

public sealed class ResolveProductionVersionQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ResolveProductionVersionQuery, ResolveProductionVersionResponse>
{
    public async Task<ResolveProductionVersionResponse> Handle(ResolveProductionVersionQuery request, CancellationToken cancellationToken)
    {
        var candidates = await dbContext.ProductionVersions
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.SkuCode == request.SkuCode &&
                x.Status == ProductionVersionStatus.Active &&
                x.ValidFrom <= request.EffectiveDate &&
                (x.ValidTo == null || request.EffectiveDate <= x.ValidTo) &&
                (x.LotSizeMin == null || x.LotSizeMin <= request.LotSize) &&
                (x.LotSizeMax == null || request.LotSize <= x.LotSizeMax))
            .Select(x => new
            {
                ProductionVersionId = x.Id.Id.ToString("D"),
                x.OrganizationId,
                x.EnvironmentId,
                x.SkuCode,
                x.MbomVersionId,
                x.RoutingVersionId,
                x.Status,
                x.Priority,
                x.IsDefault,
                HasLotWindow = x.LotSizeMin != null || x.LotSizeMax != null
            })
            .ToListAsync(cancellationToken);

        var selected = candidates
            .OrderByDescending(x => x.HasLotWindow)
            .ThenBy(x => x.Priority)
            .ThenByDescending(x => x.IsDefault)
            .FirstOrDefault()
            ?? throw new KnownException($"No active production version can resolve SKU '{request.SkuCode}' for {request.EffectiveDate:yyyy-MM-dd} and lot size {request.LotSize}.");

        return new ResolveProductionVersionResponse(
            selected.ProductionVersionId,
            selected.OrganizationId,
            selected.EnvironmentId,
            selected.SkuCode,
            selected.MbomVersionId,
            selected.RoutingVersionId,
            request.EffectiveDate,
            request.LotSize,
            selected.Status);
    }
}
