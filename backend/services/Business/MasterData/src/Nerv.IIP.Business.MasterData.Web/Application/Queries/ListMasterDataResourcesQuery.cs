using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.Business.MasterData.Web.Application.Queries;

public sealed record MasterDataResourceItem(string ResourceType, string Code, string DisplayName, bool Active, string SnapshotVersion);

public sealed record ListMasterDataResourcesResponse(IReadOnlyCollection<MasterDataResourceItem> Resources);

public sealed record ListMasterDataResourcesQuery(
    string OrganizationId,
    string EnvironmentId,
    string ResourceType,
    bool IncludeDisabled = false,
    int Take = 100) : IQuery<ListMasterDataResourcesResponse>;

public sealed class ListMasterDataResourcesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListMasterDataResourcesQuery, ListMasterDataResourcesResponse>
{
    public async Task<ListMasterDataResourcesResponse> Handle(ListMasterDataResourcesQuery request, CancellationToken cancellationToken)
    {
        var type = request.ResourceType.Trim().ToLowerInvariant();
        var take = Math.Clamp(request.Take, 1, 500);
        var resources = type switch
        {
            "sku" => await ListSkusAsync(request, type, take, cancellationToken),
            "unit-of-measure" or "uom" => await ListUnitsAsync(request, "unit-of-measure", take, cancellationToken),
            "uom-conversion" => await ListUomConversionsAsync(request, type, take, cancellationToken),
            "business-partner" or "partner" => await ListPartnersAsync(request, "business-partner", take, cancellationToken),
            "department" => await ListDepartmentsAsync(request, type, take, cancellationToken),
            "team" => await ListTeamsAsync(request, type, take, cancellationToken),
            "personnel-skill" => await ListPersonnelSkillsAsync(request, type, take, cancellationToken),
            "work-center" => await ListWorkCentersAsync(request, type, take, cancellationToken),
            "work-calendar" => await ListWorkCalendarsAsync(request, type, take, cancellationToken),
            "device-asset" => await ListDeviceAssetsAsync(request, type, take, cancellationToken),
            "site" => await ListSitesAsync(request, type, take, cancellationToken),
            "production-line" => await ListProductionLinesAsync(request, type, take, cancellationToken),
            "shift" => await ListShiftsAsync(request, type, take, cancellationToken),
            "reference-data" or "reference-data-code" => await ListReferenceDataCodesAsync(request, "reference-data", take, cancellationToken),
            _ => [],
        };
        return new ListMasterDataResourcesResponse(resources);
    }

    private async Task<List<MasterDataResourceItem>> ListSkusAsync(ListMasterDataResourcesQuery request, string resourceType, int take, CancellationToken cancellationToken)
    {
        return await dbContext.Skus
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.Code)
            .Take(take)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<MasterDataResourceItem>> ListUnitsAsync(ListMasterDataResourcesQuery request, string resourceType, int take, CancellationToken cancellationToken)
    {
        return await dbContext.UnitsOfMeasure
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.Code)
            .Take(take)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<MasterDataResourceItem>> ListUomConversionsAsync(ListMasterDataResourcesQuery request, string resourceType, int take, CancellationToken cancellationToken)
    {
        return await dbContext.UomConversions
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .OrderBy(x => x.FromUomCode)
            .ThenBy(x => x.ToUomCode)
            .Take(take)
            .Select(x => Item(resourceType, $"{x.FromUomCode}->{x.ToUomCode}", $"{x.FromUomCode} to {x.ToUomCode}", true, x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<MasterDataResourceItem>> ListPartnersAsync(ListMasterDataResourcesQuery request, string resourceType, int take, CancellationToken cancellationToken)
    {
        return await dbContext.BusinessPartners
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.Code)
            .Take(take)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<MasterDataResourceItem>> ListDepartmentsAsync(ListMasterDataResourcesQuery request, string resourceType, int take, CancellationToken cancellationToken)
    {
        return await dbContext.Departments
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.Code)
            .Take(take)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<MasterDataResourceItem>> ListTeamsAsync(ListMasterDataResourcesQuery request, string resourceType, int take, CancellationToken cancellationToken)
    {
        return await dbContext.Teams
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.Code)
            .Take(take)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<MasterDataResourceItem>> ListPersonnelSkillsAsync(ListMasterDataResourcesQuery request, string resourceType, int take, CancellationToken cancellationToken)
    {
        return await dbContext.PersonnelSkills
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.UserId)
            .ThenBy(x => x.SkillCode)
            .Take(take)
            .Select(x => Item(resourceType, $"{x.UserId}:{x.SkillCode}", x.Level, !x.Disabled, x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<MasterDataResourceItem>> ListWorkCentersAsync(ListMasterDataResourcesQuery request, string resourceType, int take, CancellationToken cancellationToken)
    {
        return await dbContext.WorkCenters
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.Code)
            .Take(take)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<MasterDataResourceItem>> ListWorkCalendarsAsync(ListMasterDataResourcesQuery request, string resourceType, int take, CancellationToken cancellationToken)
    {
        return await dbContext.WorkCalendars
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.Code)
            .Take(take)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<MasterDataResourceItem>> ListDeviceAssetsAsync(ListMasterDataResourcesQuery request, string resourceType, int take, CancellationToken cancellationToken)
    {
        return await dbContext.DeviceAssets
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.Code)
            .Take(take)
            .Select(x => Item(resourceType, x.Code, x.Model, !x.Disabled, x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<MasterDataResourceItem>> ListSitesAsync(ListMasterDataResourcesQuery request, string resourceType, int take, CancellationToken cancellationToken)
    {
        return await dbContext.Sites
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.Code)
            .Take(take)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<MasterDataResourceItem>> ListProductionLinesAsync(ListMasterDataResourcesQuery request, string resourceType, int take, CancellationToken cancellationToken)
    {
        return await dbContext.ProductionLines
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.Code)
            .Take(take)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<MasterDataResourceItem>> ListShiftsAsync(ListMasterDataResourcesQuery request, string resourceType, int take, CancellationToken cancellationToken)
    {
        return await dbContext.Shifts
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.Code)
            .Take(take)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<MasterDataResourceItem>> ListReferenceDataCodesAsync(ListMasterDataResourcesQuery request, string resourceType, int take, CancellationToken cancellationToken)
    {
        return await dbContext.ReferenceDataCodes
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.CodeSet)
            .ThenBy(x => x.Code)
            .Take(take)
            .Select(x => Item(resourceType, $"{x.CodeSet}:{x.Code}", x.Name, !x.Disabled, x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    private static MasterDataResourceItem Item(string resourceType, string code, string displayName, bool active, DateTime updatedAtUtc)
    {
        return new MasterDataResourceItem(resourceType, code, displayName, active, updatedAtUtc.ToString("O"));
    }
}
