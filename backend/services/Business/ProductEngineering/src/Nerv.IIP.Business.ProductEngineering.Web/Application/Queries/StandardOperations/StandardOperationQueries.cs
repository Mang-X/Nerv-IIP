using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Queries.StandardOperations;

public sealed record StandardOperationItem(
    string OperationCode,
    string OperationName,
    string DefaultWorkCenterCode,
    int StandardSetupMinutes,
    int StandardRunMinutes,
    int StandardMinutes,
    string ControlKey,
    bool RequiresReporting,
    bool RequiresQualityInspection,
    bool IsOutsourced,
    string? Description,
    bool Enabled,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record ListStandardOperationsResponse(IReadOnlyCollection<StandardOperationItem> Items, int Total);

public sealed record ListStandardOperationsQuery(
    string OrganizationId,
    string EnvironmentId,
    bool? Enabled,
    string? Search,
    int Skip = 0,
    int Take = 100) : IQuery<ListStandardOperationsResponse>;

public sealed class ListStandardOperationsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListStandardOperationsQuery, ListStandardOperationsResponse>
{
    public async Task<ListStandardOperationsResponse> Handle(ListStandardOperationsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.StandardOperations
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (request.Enabled is not null)
        {
            query = query.Where(x => x.Enabled == request.Enabled.Value);
        }

        var search = EngineeringQueryParameters.NormalizeOptionalText(request.Search);
        if (search is not null)
        {
            var normalizedSearch = search.ToUpperInvariant();
            query = query.Where(x =>
                x.OperationCode.ToUpper().Contains(normalizedSearch) ||
                x.OperationName.ToUpper().Contains(normalizedSearch));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.OperationCode)
            .Skip(EngineeringQueryParameters.NormalizeSkip(request.Skip))
            .Take(EngineeringQueryParameters.NormalizeTake(request.Take))
            .Select(x => new StandardOperationItem(
                x.OperationCode,
                x.OperationName,
                x.DefaultWorkCenterCode,
                x.StandardSetupMinutes,
                x.StandardRunMinutes,
                x.StandardSetupMinutes + x.StandardRunMinutes,
                x.ControlKey,
                x.RequiresReporting,
                x.RequiresQualityInspection,
                x.IsOutsourced,
                x.Description,
                x.Enabled,
                x.CreatedAtUtc,
                x.UpdatedAtUtc))
            .ToArrayAsync(cancellationToken);

        return new ListStandardOperationsResponse(items, total);
    }
}

public sealed record GetStandardOperationQuery(string OrganizationId, string EnvironmentId, string OperationCode)
    : IQuery<StandardOperationItem>;

public sealed class GetStandardOperationQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetStandardOperationQuery, StandardOperationItem>
{
    public async Task<StandardOperationItem> Handle(GetStandardOperationQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.StandardOperations
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.OperationCode == request.OperationCode)
            .Select(x => new StandardOperationItem(
                x.OperationCode,
                x.OperationName,
                x.DefaultWorkCenterCode,
                x.StandardSetupMinutes,
                x.StandardRunMinutes,
                x.StandardSetupMinutes + x.StandardRunMinutes,
                x.ControlKey,
                x.RequiresReporting,
                x.RequiresQualityInspection,
                x.IsOutsourced,
                x.Description,
                x.Enabled,
                x.CreatedAtUtc,
                x.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"Standard operation '{request.OperationCode}' was not found.");
    }
}
