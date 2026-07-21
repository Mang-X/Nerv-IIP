using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using Nerv.IIP.Business.ProductEngineering.Infrastructure.Repositories;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Queries.ProductionVersions;

public sealed record GetProductionVersionRoutingSnapshotQuery(
    string OrganizationId,
    string EnvironmentId,
    string ProductionVersionId) : IQuery<ProductionVersionRoutingSnapshotResponse>;

public sealed class GetProductionVersionRoutingSnapshotQueryValidator
    : AbstractValidator<GetProductionVersionRoutingSnapshotQuery>
{
    public GetProductionVersionRoutingSnapshotQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ProductionVersionId).NotEmpty().MaximumLength(100);
    }
}

public sealed record ProductionVersionRoutingSnapshotResponse(
    string ProductionVersionId,
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string ProductionVersionStatus,
    string RoutingVersionId,
    string RoutingCode,
    string RoutingRevision,
    string RoutingStatus,
    IReadOnlyCollection<ProductionVersionRoutingOperationResponse> Operations);

public sealed record ProductionVersionRoutingOperationResponse(
    int Sequence,
    string WorkCenterCode,
    string OperationCode,
    string OperationName,
    int StandardMinutes,
    int SetupMinutes,
    int RunMinutes,
    int TeardownMinutes,
    string ControlKey,
    bool RequiresReporting,
    bool RequiresQualityInspection,
    bool IsOutsourced);

public sealed class GetProductionVersionRoutingSnapshotQueryHandler(
    ApplicationDbContext dbContext,
    IRoutingRepository routingRepository)
    : IQueryHandler<GetProductionVersionRoutingSnapshotQuery, ProductionVersionRoutingSnapshotResponse>
{
    public async Task<ProductionVersionRoutingSnapshotResponse> Handle(
        GetProductionVersionRoutingSnapshotQuery request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.ProductionVersionId, out var productionVersionId))
        {
            throw new KnownException($"Production version '{request.ProductionVersionId}' was not found.");
        }

        var typedId = new ProductionVersionId(productionVersionId);
        var version = await dbContext.ProductionVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.Id == typedId,
                cancellationToken)
            ?? throw new KnownException($"Production version '{request.ProductionVersionId}' was not found.");
        if (version.Status != ProductionVersionStatus.Active)
        {
            throw new KnownException($"Production version '{request.ProductionVersionId}' is not active.");
        }

        var routing = await routingRepository.GetByVersionIdAsync(
            request.OrganizationId,
            request.EnvironmentId,
            version.RoutingVersionId,
            cancellationToken)
            ?? throw new KnownException($"Routing version '{version.RoutingVersionId}' was not found.");
        if (routing.Status != EngineeringVersionStatus.Published ||
            !string.Equals(routing.SkuCode, version.SkuCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new KnownException($"Routing version '{version.RoutingVersionId}' is not a published routing for SKU '{version.SkuCode}'.");
        }

        await dbContext.Entry(routing).Collection(x => x.Operations).LoadAsync(cancellationToken);
        return new ProductionVersionRoutingSnapshotResponse(
            version.Id.Id.ToString("D"),
            version.OrganizationId,
            version.EnvironmentId,
            version.SkuCode,
            version.Status.ToLowerInvariant(),
            version.RoutingVersionId,
            routing.RoutingCode,
            routing.Revision,
            routing.Status.ToString().ToLowerInvariant(),
            routing.Operations
                .OrderBy(x => x.Sequence)
                .Select(x => new ProductionVersionRoutingOperationResponse(
                    x.Sequence,
                    x.WorkCenterCode,
                    x.OperationCode,
                    x.OperationName,
                    x.StandardMinutes,
                    x.SetupMinutes,
                    x.RunMinutes,
                    x.TeardownMinutes,
                    x.ControlKey,
                    x.RequiresReporting,
                    x.RequiresQualityInspection,
                    x.IsOutsourced))
                .ToArray());
    }
}
