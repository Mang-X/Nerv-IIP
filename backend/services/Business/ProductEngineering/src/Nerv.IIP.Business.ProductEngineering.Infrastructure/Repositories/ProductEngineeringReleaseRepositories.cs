using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringDocumentAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringItemAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.StandardOperationAggregate;

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

public interface IEngineeringItemRepository : IRepository<EngineeringItem, EngineeringItemId>
{
    Task<bool> ExistsAsync(string organizationId, string environmentId, string itemCode, string revision, CancellationToken cancellationToken = default);
}

public sealed class EngineeringItemRepository(ApplicationDbContext context)
    : RepositoryBase<EngineeringItem, EngineeringItemId, ApplicationDbContext>(context), IEngineeringItemRepository
{
    public async Task<bool> ExistsAsync(string organizationId, string environmentId, string itemCode, string revision, CancellationToken cancellationToken = default)
    {
        return await DbContext.EngineeringItems.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.ItemCode == itemCode &&
            x.Revision == revision,
            cancellationToken);
    }
}

public interface IEngineeringBomRepository : IRepository<EngineeringBom, EngineeringBomId>
{
    Task<EngineeringBom?> GetByBusinessKeyAsync(string organizationId, string environmentId, string bomCode, string revision, CancellationToken cancellationToken = default);

    Task<EngineeringBom?> GetByVersionIdAsync(string organizationId, string environmentId, string versionId, CancellationToken cancellationToken = default);

    Task<bool> HasPublishedRevisionAsync(string organizationId, string environmentId, string bomCode, string excludingRevision, CancellationToken cancellationToken = default);
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

    public async Task<EngineeringBom?> GetByVersionIdAsync(string organizationId, string environmentId, string versionId, CancellationToken cancellationToken = default)
    {
        return ProductEngineeringVersionReference.TryParse(versionId, out var code, out var revision)
            ? await GetByBusinessKeyAsync(organizationId, environmentId, code, revision, cancellationToken)
            : null;
    }

    public async Task<bool> HasPublishedRevisionAsync(string organizationId, string environmentId, string bomCode, string excludingRevision, CancellationToken cancellationToken = default)
    {
        return await DbContext.EngineeringBoms.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.BomCode == bomCode &&
            x.Revision != excludingRevision &&
            x.Status == EngineeringVersionStatus.Published,
            cancellationToken);
    }
}

public interface IManufacturingBomRepository : IRepository<ManufacturingBom, ManufacturingBomId>
{
    Task<bool> ExistsAsync(string organizationId, string environmentId, string bomCode, string revision, CancellationToken cancellationToken = default);

    Task<ManufacturingBom?> GetByBusinessKeyAsync(string organizationId, string environmentId, string bomCode, string revision, CancellationToken cancellationToken = default);

    Task<ManufacturingBom?> GetByVersionIdAsync(string organizationId, string environmentId, string versionId, CancellationToken cancellationToken = default);

    Task<bool> HasPublishedRevisionAsync(string organizationId, string environmentId, string bomCode, string excludingRevision, CancellationToken cancellationToken = default);
}

public sealed class ManufacturingBomRepository(ApplicationDbContext context)
    : RepositoryBase<ManufacturingBom, ManufacturingBomId, ApplicationDbContext>(context), IManufacturingBomRepository
{
    public async Task<bool> ExistsAsync(string organizationId, string environmentId, string bomCode, string revision, CancellationToken cancellationToken = default)
    {
        return await DbContext.ManufacturingBoms.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.BomCode == bomCode &&
            x.Revision == revision,
            cancellationToken);
    }

    public async Task<ManufacturingBom?> GetByBusinessKeyAsync(string organizationId, string environmentId, string bomCode, string revision, CancellationToken cancellationToken = default)
    {
        return await DbContext.ManufacturingBoms.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.BomCode == bomCode &&
            x.Revision == revision,
            cancellationToken);
    }

    public async Task<ManufacturingBom?> GetByVersionIdAsync(string organizationId, string environmentId, string versionId, CancellationToken cancellationToken = default)
    {
        return ProductEngineeringVersionReference.TryParse(versionId, out var code, out var revision)
            ? await GetByBusinessKeyAsync(organizationId, environmentId, code, revision, cancellationToken)
            : null;
    }

    public async Task<bool> HasPublishedRevisionAsync(string organizationId, string environmentId, string bomCode, string excludingRevision, CancellationToken cancellationToken = default)
    {
        return await DbContext.ManufacturingBoms.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.BomCode == bomCode &&
            x.Revision != excludingRevision &&
            x.Status == EngineeringVersionStatus.Published,
            cancellationToken);
    }
}

public interface IRoutingRepository : IRepository<Routing, RoutingId>
{
    Task<bool> ExistsAsync(string organizationId, string environmentId, string routingCode, string revision, CancellationToken cancellationToken = default);

    Task<Routing?> GetByBusinessKeyAsync(string organizationId, string environmentId, string routingCode, string revision, CancellationToken cancellationToken = default);

    Task<Routing?> GetByVersionIdAsync(string organizationId, string environmentId, string versionId, CancellationToken cancellationToken = default);

    Task<bool> HasPublishedRevisionAsync(string organizationId, string environmentId, string routingCode, string excludingRevision, CancellationToken cancellationToken = default);
}

public sealed class RoutingRepository(ApplicationDbContext context)
    : RepositoryBase<Routing, RoutingId, ApplicationDbContext>(context), IRoutingRepository
{
    public async Task<bool> ExistsAsync(string organizationId, string environmentId, string routingCode, string revision, CancellationToken cancellationToken = default)
    {
        return await DbContext.Routings.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.RoutingCode == routingCode &&
            x.Revision == revision,
            cancellationToken);
    }

    public async Task<Routing?> GetByBusinessKeyAsync(string organizationId, string environmentId, string routingCode, string revision, CancellationToken cancellationToken = default)
    {
        return await DbContext.Routings.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.RoutingCode == routingCode &&
            x.Revision == revision,
            cancellationToken);
    }

    public async Task<Routing?> GetByVersionIdAsync(string organizationId, string environmentId, string versionId, CancellationToken cancellationToken = default)
    {
        return ProductEngineeringVersionReference.TryParse(versionId, out var code, out var revision)
            ? await GetByBusinessKeyAsync(organizationId, environmentId, code, revision, cancellationToken)
            : null;
    }

    public async Task<bool> HasPublishedRevisionAsync(string organizationId, string environmentId, string routingCode, string excludingRevision, CancellationToken cancellationToken = default)
    {
        return await DbContext.Routings.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.RoutingCode == routingCode &&
            x.Revision != excludingRevision &&
            x.Status == EngineeringVersionStatus.Published,
            cancellationToken);
    }
}

public interface IStandardOperationRepository : IRepository<StandardOperation, StandardOperationId>
{
    Task<bool> ExistsAsync(string organizationId, string environmentId, string operationCode, CancellationToken cancellationToken = default);

    Task<StandardOperation?> GetByCodeAsync(string organizationId, string environmentId, string operationCode, CancellationToken cancellationToken = default);
}

public sealed class StandardOperationRepository(ApplicationDbContext context)
    : RepositoryBase<StandardOperation, StandardOperationId, ApplicationDbContext>(context), IStandardOperationRepository
{
    public async Task<bool> ExistsAsync(string organizationId, string environmentId, string operationCode, CancellationToken cancellationToken = default)
    {
        return await DbContext.StandardOperations.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.OperationCode == operationCode,
            cancellationToken);
    }

    public async Task<StandardOperation?> GetByCodeAsync(string organizationId, string environmentId, string operationCode, CancellationToken cancellationToken = default)
    {
        return await DbContext.StandardOperations.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.OperationCode == operationCode,
            cancellationToken);
    }
}

public interface IEngineeringChangeRepository : IRepository<EngineeringChange, EngineeringChangeId>
{
}

public sealed class EngineeringChangeRepository(ApplicationDbContext context)
    : RepositoryBase<EngineeringChange, EngineeringChangeId, ApplicationDbContext>(context), IEngineeringChangeRepository;

internal static class ProductEngineeringVersionReference
{
    public static bool TryParse(string versionId, out string code, out string revision)
    {
        code = string.Empty;
        revision = string.Empty;
        var parts = versionId.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            return false;
        }

        code = parts[0];
        revision = parts[1];
        return true;
    }
}
