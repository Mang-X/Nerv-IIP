using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure.Repositories;

public interface IProductionVersionRepository : IRepository<ProductionVersion, ProductionVersionId>
{
    Task<ProductionVersion?> GetByIdAsync(string productionVersionId, CancellationToken cancellationToken = default);

    Task<ProductionVersion?> GetByIdAsync(
        string organizationId,
        string environmentId,
        string productionVersionId,
        CancellationToken cancellationToken = default);

    Task<bool> HasOverlappingDefaultAsync(
        string organizationId,
        string environmentId,
        string skuCode,
        DateOnly validFrom,
        DateOnly? validTo,
        string? excludingProductionVersionId = null,
        CancellationToken cancellationToken = default);
}

public sealed class ProductionVersionRepository(ApplicationDbContext context)
    : RepositoryBase<ProductionVersion, ProductionVersionId, ApplicationDbContext>(context), IProductionVersionRepository
{
    public async Task<ProductionVersion?> GetByIdAsync(string productionVersionId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(productionVersionId, out var id))
        {
            return null;
        }

        var typedId = new ProductionVersionId(id);
        return await DbContext.ProductionVersions.SingleOrDefaultAsync(x => x.Id == typedId, cancellationToken);
    }

    public async Task<ProductionVersion?> GetByIdAsync(
        string organizationId,
        string environmentId,
        string productionVersionId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(productionVersionId, out var id))
        {
            return null;
        }

        var typedId = new ProductionVersionId(id);
        return await DbContext.ProductionVersions.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.Id == typedId, cancellationToken);
    }

    public async Task<bool> HasOverlappingDefaultAsync(
        string organizationId,
        string environmentId,
        string skuCode,
        DateOnly validFrom,
        DateOnly? validTo,
        string? excludingProductionVersionId = null,
        CancellationToken cancellationToken = default)
    {
        var requestedEnd = validTo ?? DateOnly.MaxValue;
        var query = DbContext.ProductionVersions.Where(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.SkuCode == skuCode &&
            x.IsDefault &&
            x.Status != ProductionVersionStatus.Archived &&
            x.ValidFrom <= requestedEnd &&
            validFrom <= (x.ValidTo ?? DateOnly.MaxValue));

        if (Guid.TryParse(excludingProductionVersionId, out var excludingId))
        {
            var typedExcludingId = new ProductionVersionId(excludingId);
            query = query.Where(x => x.Id != typedExcludingId);
        }

        return await query.AnyAsync(cancellationToken);
    }
}
