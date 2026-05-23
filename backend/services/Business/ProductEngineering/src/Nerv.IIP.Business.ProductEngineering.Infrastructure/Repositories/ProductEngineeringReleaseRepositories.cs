using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringDocumentAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure.Repositories;

public interface IEngineeringDocumentRepository : IRepository<EngineeringDocument, EngineeringDocumentId>
{
    Task<bool> ExistsAsync(string organizationId, string environmentId, string documentNumber, string revision, CancellationToken cancellationToken = default);
}

public sealed class EngineeringDocumentRepository(ApplicationDbContext context)
    : RepositoryBase<EngineeringDocument, EngineeringDocumentId, ApplicationDbContext>(context), IEngineeringDocumentRepository
{
    public async Task<bool> ExistsAsync(string organizationId, string environmentId, string documentNumber, string revision, CancellationToken cancellationToken = default)
    {
        return await DbContext.EngineeringDocuments.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.DocumentNumber == documentNumber &&
            x.Revision == revision,
            cancellationToken);
    }
}

public interface IEngineeringBomRepository : IRepository<EngineeringBom, EngineeringBomId>
{
    Task<EngineeringBom?> GetByBusinessKeyAsync(string organizationId, string environmentId, string bomCode, string revision, CancellationToken cancellationToken = default);
}

public sealed class EngineeringBomRepository(ApplicationDbContext context)
    : RepositoryBase<EngineeringBom, EngineeringBomId, ApplicationDbContext>(context), IEngineeringBomRepository
{
    public async Task<EngineeringBom?> GetByBusinessKeyAsync(string organizationId, string environmentId, string bomCode, string revision, CancellationToken cancellationToken = default)
    {
        return await DbContext.EngineeringBoms.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.BomCode == bomCode &&
            x.Revision == revision,
            cancellationToken);
    }
}

public interface IManufacturingBomRepository : IRepository<ManufacturingBom, ManufacturingBomId>
{
    Task<EngineeringVersionStatus> GetStatusByBusinessKeyAsync(string organizationId, string environmentId, string bomCode, string revision, CancellationToken cancellationToken = default);
}

public sealed class ManufacturingBomRepository(ApplicationDbContext context)
    : RepositoryBase<ManufacturingBom, ManufacturingBomId, ApplicationDbContext>(context), IManufacturingBomRepository
{
    public async Task<EngineeringVersionStatus> GetStatusByBusinessKeyAsync(string organizationId, string environmentId, string bomCode, string revision, CancellationToken cancellationToken = default)
    {
        var status = await DbContext.ManufacturingBoms
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.BomCode == bomCode && x.Revision == revision)
            .Select(x => (EngineeringVersionStatus?)x.Status)
            .SingleOrDefaultAsync(cancellationToken);
        return status ?? EngineeringVersionStatus.Draft;
    }
}

public interface IRoutingRepository : IRepository<Routing, RoutingId>
{
    Task<EngineeringVersionStatus> GetStatusByBusinessKeyAsync(string organizationId, string environmentId, string routingCode, string revision, CancellationToken cancellationToken = default);
}

public sealed class RoutingRepository(ApplicationDbContext context)
    : RepositoryBase<Routing, RoutingId, ApplicationDbContext>(context), IRoutingRepository
{
    public async Task<EngineeringVersionStatus> GetStatusByBusinessKeyAsync(string organizationId, string environmentId, string routingCode, string revision, CancellationToken cancellationToken = default)
    {
        var status = await DbContext.Routings
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.RoutingCode == routingCode && x.Revision == revision)
            .Select(x => (EngineeringVersionStatus?)x.Status)
            .SingleOrDefaultAsync(cancellationToken);
        return status ?? EngineeringVersionStatus.Draft;
    }
}

public interface IEngineeringChangeRepository : IRepository<EngineeringChange, EngineeringChangeId>
{
}

public sealed class EngineeringChangeRepository(ApplicationDbContext context)
    : RepositoryBase<EngineeringChange, EngineeringChangeId, ApplicationDbContext>(context), IEngineeringChangeRepository;
