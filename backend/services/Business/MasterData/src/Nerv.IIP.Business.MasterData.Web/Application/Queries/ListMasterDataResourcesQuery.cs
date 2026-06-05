using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.Business.MasterData.Web.Application.Queries;

public sealed record MasterDataResourceItem(string ResourceType, string Code, string DisplayName, bool Active, string SnapshotVersion);

public sealed record ListMasterDataResourcesResponse(
    IReadOnlyCollection<MasterDataResourceItem> Resources,
    int Total);

public sealed record ListMasterDataResourcesQuery(
    string OrganizationId,
    string EnvironmentId,
    string ResourceType,
    bool IncludeDisabled = false,
    int Skip = 0,
    int Take = 100) : IQuery<ListMasterDataResourcesResponse>;

public sealed class ListMasterDataResourcesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListMasterDataResourcesQuery, ListMasterDataResourcesResponse>
{
    public async Task<ListMasterDataResourcesResponse> Handle(ListMasterDataResourcesQuery request, CancellationToken cancellationToken)
    {
        var type = request.ResourceType.Trim().ToLowerInvariant();
        var query = type switch
        {
            "sku" => ListSkus(request, type),
            "unit-of-measure" or "uom" => ListUnits(request, "unit-of-measure"),
            "uom-conversion" => ListUomConversions(request, type),
            "business-partner" or "partner" => ListPartners(request, "business-partner"),
            "department" => ListDepartments(request, type),
            "team" => ListTeams(request, type),
            "personnel-skill" => ListPersonnelSkills(request, type),
            "work-center" => ListWorkCenters(request, type),
            "work-calendar" => ListWorkCalendars(request, type),
            "device-asset" => ListDeviceAssets(request, type),
            "site" => ListSites(request, type),
            "production-line" => ListProductionLines(request, type),
            "shift" => ListShifts(request, type),
            "reference-data" or "reference-data-code" => ListReferenceDataCodes(request, "reference-data"),
            _ => null,
        };
        return query is null
            ? new ListMasterDataResourcesResponse([], 0)
            : await ToPageAsync(query, request, cancellationToken);
    }

    private static async Task<ListMasterDataResourcesResponse> ToPageAsync(
        IQueryable<MasterDataResourceItem> query,
        ListMasterDataResourcesQuery request,
        CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var resources = await query
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 500))
            .ToListAsync(cancellationToken);
        return new ListMasterDataResourcesResponse(resources, total);
    }

    private IQueryable<MasterDataResourceItem> ListSkus(ListMasterDataResourcesQuery request, string resourceType)
    {
        return dbContext.Skus
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc));
    }

    private IQueryable<MasterDataResourceItem> ListUnits(ListMasterDataResourcesQuery request, string resourceType)
    {
        return dbContext.UnitsOfMeasure
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc));
    }

    private IQueryable<MasterDataResourceItem> ListUomConversions(ListMasterDataResourcesQuery request, string resourceType)
    {
        return dbContext.UomConversions
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .OrderBy(x => x.FromUomCode)
            .ThenBy(x => x.ToUomCode)
            .Select(x => Item(resourceType, $"{x.FromUomCode}->{x.ToUomCode}", $"{x.FromUomCode} to {x.ToUomCode}", true, x.UpdatedAtUtc));
    }

    private IQueryable<MasterDataResourceItem> ListPartners(ListMasterDataResourcesQuery request, string resourceType)
    {
        return dbContext.BusinessPartners
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc));
    }

    private IQueryable<MasterDataResourceItem> ListDepartments(ListMasterDataResourcesQuery request, string resourceType)
    {
        return dbContext.Departments
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc));
    }

    private IQueryable<MasterDataResourceItem> ListTeams(ListMasterDataResourcesQuery request, string resourceType)
    {
        return dbContext.Teams
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc));
    }

    private IQueryable<MasterDataResourceItem> ListPersonnelSkills(ListMasterDataResourcesQuery request, string resourceType)
    {
        return dbContext.PersonnelSkills
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.UserId)
            .ThenBy(x => x.SkillCode)
            .Select(x => Item(resourceType, $"{x.UserId}:{x.SkillCode}", x.Level, !x.Disabled, x.UpdatedAtUtc));
    }

    private IQueryable<MasterDataResourceItem> ListWorkCenters(ListMasterDataResourcesQuery request, string resourceType)
    {
        return dbContext.WorkCenters
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc));
    }

    private IQueryable<MasterDataResourceItem> ListWorkCalendars(ListMasterDataResourcesQuery request, string resourceType)
    {
        return dbContext.WorkCalendars
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc));
    }

    private IQueryable<MasterDataResourceItem> ListDeviceAssets(ListMasterDataResourcesQuery request, string resourceType)
    {
        return dbContext.DeviceAssets
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Model, !x.Disabled, x.UpdatedAtUtc));
    }

    private IQueryable<MasterDataResourceItem> ListSites(ListMasterDataResourcesQuery request, string resourceType)
    {
        return dbContext.Sites
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc));
    }

    private IQueryable<MasterDataResourceItem> ListProductionLines(ListMasterDataResourcesQuery request, string resourceType)
    {
        return dbContext.ProductionLines
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc));
    }

    private IQueryable<MasterDataResourceItem> ListShifts(ListMasterDataResourcesQuery request, string resourceType)
    {
        return dbContext.Shifts
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc));
    }

    private IQueryable<MasterDataResourceItem> ListReferenceDataCodes(ListMasterDataResourcesQuery request, string resourceType)
    {
        return dbContext.ReferenceDataCodes
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderBy(x => x.CodeSet)
            .ThenBy(x => x.Code)
            .Select(x => Item(resourceType, $"{x.CodeSet}:{x.Code}", x.Name, !x.Disabled, x.UpdatedAtUtc));
    }

    private static MasterDataResourceItem Item(string resourceType, string code, string displayName, bool active, DateTime updatedAtUtc)
    {
        return new MasterDataResourceItem(resourceType, code, displayName, active, updatedAtUtc.ToString("O"));
    }
}
