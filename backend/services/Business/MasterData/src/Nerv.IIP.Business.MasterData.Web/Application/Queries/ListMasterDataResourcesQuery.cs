using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.Business.MasterData.Web.Application.Queries;

public sealed record MasterDataResourceItem(
    string ResourceType,
    string Code,
    string DisplayName,
    bool Active,
    string SnapshotVersion,
    string? PartnerType = null,
    IReadOnlyCollection<string>? PartnerRoles = null,
    string? SiteCode = null,
    string? PlantCode = null,
    string? LineCode = null,
    string? WorkshopCode = null,
    int? CapacityMinutesPerDay = null,
    string? WorkCenterCode = null,
    string? Status = null,
    string? Category = null,
    string? MaterialType = null,
    string? CodeSet = null,
    string? BaseUomCode = null,
    string? TaxId = null,
    string? ParentDepartmentCode = null,
    string? DepartmentCode = null,
    string? ShiftCode = null,
    string? UserId = null,
    string? SkillCode = null,
    string? SkillLevel = null,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null,
    string? FromUomCode = null,
    string? ToUomCode = null,
    decimal? Factor = null,
    decimal? Offset = null,
    int? Precision = null,
    string? RoundingMode = null,
    string? DeviceAssetId = null);

public sealed record ListMasterDataResourcesResponse(
    IReadOnlyCollection<MasterDataResourceItem> Resources,
    int Total,
    bool Truncated = false,
    int? Limit = null);

public sealed record ListMasterDataResourcesQuery(
    string OrganizationId,
    string EnvironmentId,
    string ResourceType,
    bool IncludeDisabled = false,
    int Skip = 0,
    int Take = 100,
    string? CodeSet = null,
    string? ParentCode = null,
    string? SiteCode = null,
    string? LineCode = null,
    string? WorkCenterCode = null,
    string? Category = null,
    string? PartnerType = null,
    string? Keyword = null,
    bool All = false,
    string? DepartmentCode = null,
    string? ShiftCode = null,
    string? UserId = null,
    string? SkillCode = null) : IQuery<ListMasterDataResourcesResponse>;

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
            "workshop" => ListWorkshops(request, type),
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
        var limit = request.All ? 5000 : Math.Clamp(request.Take, 1, 500);
        var resources = await query
            .Skip(request.All ? 0 : Math.Max(0, request.Skip))
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new ListMasterDataResourcesResponse(resources, total, request.All && total > limit, request.All ? limit : null);
    }

    private IQueryable<MasterDataResourceItem> ListSkus(ListMasterDataResourcesQuery request, string resourceType)
    {
        var keyword = NormalizeKeyword(request.Keyword);
        return dbContext.Skus
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => string.IsNullOrWhiteSpace(request.Category) || x.Category == request.Category)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc, null, null, null, null, null, null, null, null, x.Disabled ? "disabled" : "active", x.Category, x.MaterialType, null, x.BaseUomCode));
    }

    private IQueryable<MasterDataResourceItem> ListUnits(ListMasterDataResourcesQuery request, string resourceType)
    {
        var keyword = NormalizeKeyword(request.Keyword);
        return dbContext.UnitsOfMeasure
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc, null, null, null, null, null, null, null, null, x.Disabled ? "disabled" : "active"));
    }

    private IQueryable<MasterDataResourceItem> ListUomConversions(ListMasterDataResourcesQuery request, string resourceType)
    {
        var keyword = NormalizeKeyword(request.Keyword);
        return dbContext.UomConversions
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => keyword == null || x.FromUomCode.ToLower().Contains(keyword) || x.ToUomCode.ToLower().Contains(keyword))
            .OrderBy(x => x.FromUomCode)
            .ThenBy(x => x.ToUomCode)
            .Select(x => Item(
                resourceType,
                $"{x.FromUomCode}->{x.ToUomCode}",
                $"{x.FromUomCode} to {x.ToUomCode}",
                !x.Disabled,
                x.UpdatedAtUtc,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                x.Disabled ? "disabled" : "active",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                x.EffectiveFrom,
                x.EffectiveTo,
                x.FromUomCode,
                x.ToUomCode,
                x.Factor,
                x.Offset,
                x.Precision,
                x.RoundingMode));
    }

    private IQueryable<MasterDataResourceItem> ListPartners(ListMasterDataResourcesQuery request, string resourceType)
    {
        var keyword = NormalizeKeyword(request.Keyword);
        return dbContext.BusinessPartners
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => string.IsNullOrWhiteSpace(request.PartnerType) || x.PartnerType == request.PartnerType || x.PartnerRoles.Contains(request.PartnerType))
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc, x.PartnerType, x.PartnerRoles, null, null, null, null, null, null, x.Disabled ? "disabled" : "active", null, null, null, null, x.TaxId));
    }

    private IQueryable<MasterDataResourceItem> ListDepartments(ListMasterDataResourcesQuery request, string resourceType)
    {
        var keyword = NormalizeKeyword(request.Keyword);
        return dbContext.Departments
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => string.IsNullOrWhiteSpace(request.ParentCode) || x.ParentDepartmentCode == request.ParentCode)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc, null, null, null, null, null, null, null, null, x.Disabled ? "disabled" : "active", null, null, null, null, null, x.ParentDepartmentCode));
    }

    private IQueryable<MasterDataResourceItem> ListTeams(ListMasterDataResourcesQuery request, string resourceType)
    {
        var keyword = NormalizeKeyword(request.Keyword);
        return dbContext.Teams
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => string.IsNullOrWhiteSpace(request.DepartmentCode) || x.DepartmentCode == request.DepartmentCode)
            .Where(x => string.IsNullOrWhiteSpace(request.ShiftCode) || x.ShiftCode == request.ShiftCode)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc, null, null, null, null, null, null, null, null, x.Disabled ? "disabled" : "active", null, null, null, null, null, null, x.DepartmentCode, x.ShiftCode));
    }

    private IQueryable<MasterDataResourceItem> ListPersonnelSkills(ListMasterDataResourcesQuery request, string resourceType)
    {
        var keyword = NormalizeKeyword(request.Keyword);
        return dbContext.PersonnelSkills
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => string.IsNullOrWhiteSpace(request.UserId) || x.UserId == request.UserId)
            .Where(x => string.IsNullOrWhiteSpace(request.SkillCode) || x.SkillCode == request.SkillCode)
            .Where(x => keyword == null || x.UserId.ToLower().Contains(keyword) || x.SkillCode.ToLower().Contains(keyword) || x.Level.ToLower().Contains(keyword))
            .OrderBy(x => x.UserId)
            .ThenBy(x => x.SkillCode)
            .Select(x => Item(resourceType, $"{x.UserId}:{x.SkillCode}", x.Level, !x.Disabled, x.UpdatedAtUtc, null, null, null, null, null, null, null, null, x.Disabled ? "disabled" : "active", null, null, null, null, null, null, null, null, x.UserId, x.SkillCode, x.Level, x.EffectiveFrom, x.EffectiveTo));
    }

    private IQueryable<MasterDataResourceItem> ListWorkshops(ListMasterDataResourcesQuery request, string resourceType)
    {
        var keyword = NormalizeKeyword(request.Keyword);
        return dbContext.Workshops
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => string.IsNullOrWhiteSpace(request.SiteCode) || x.SiteCode == request.SiteCode)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc, null, null, x.SiteCode, null, null, null, null, null, x.Disabled ? "disabled" : "active"));
    }

    private IQueryable<MasterDataResourceItem> ListWorkCenters(ListMasterDataResourcesQuery request, string resourceType)
    {
        var keyword = NormalizeKeyword(request.Keyword);
        return dbContext.WorkCenters
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => string.IsNullOrWhiteSpace(request.LineCode) || x.LineCode == request.LineCode)
            .Where(x => string.IsNullOrWhiteSpace(request.SiteCode) || x.PlantCode == request.SiteCode)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc, null, null, null, x.PlantCode, x.LineCode, x.WorkshopCode, x.CapacityMinutesPerDay, null, x.Disabled ? "disabled" : "active"));
    }

    private IQueryable<MasterDataResourceItem> ListWorkCalendars(ListMasterDataResourcesQuery request, string resourceType)
    {
        var keyword = NormalizeKeyword(request.Keyword);
        return dbContext.WorkCalendars
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc, null, null, null, null, null, null, null, null, x.Disabled ? "disabled" : "active"));
    }

    private IQueryable<MasterDataResourceItem> ListDeviceAssets(ListMasterDataResourcesQuery request, string resourceType)
    {
        var keyword = NormalizeKeyword(request.Keyword);
        return dbContext.DeviceAssets
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => string.IsNullOrWhiteSpace(request.LineCode) || x.LineCode == request.LineCode)
            .Where(x => string.IsNullOrWhiteSpace(request.WorkCenterCode) || x.WorkCenterCode == request.WorkCenterCode)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Model.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Model, !x.Disabled, x.UpdatedAtUtc, null, null, null, null, x.LineCode, null, null, x.WorkCenterCode, x.Disabled ? "disabled" : "active", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, x.Id.ToString()));
    }

    private IQueryable<MasterDataResourceItem> ListSites(ListMasterDataResourcesQuery request, string resourceType)
    {
        var keyword = NormalizeKeyword(request.Keyword);
        return dbContext.Sites
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => string.IsNullOrWhiteSpace(request.SiteCode) || x.Code == request.SiteCode)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc, null, null, null, null, null, null, null, null, x.Disabled ? "disabled" : "active"));
    }

    private IQueryable<MasterDataResourceItem> ListProductionLines(ListMasterDataResourcesQuery request, string resourceType)
    {
        var keyword = NormalizeKeyword(request.Keyword);
        return dbContext.ProductionLines
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => string.IsNullOrWhiteSpace(request.SiteCode) || x.SiteCode == request.SiteCode)
            .Where(x => string.IsNullOrWhiteSpace(request.LineCode) || x.Code == request.LineCode)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc, null, null, x.SiteCode, null, null, x.WorkshopCode, null, null, x.Disabled ? "disabled" : "active"));
    }

    private IQueryable<MasterDataResourceItem> ListShifts(ListMasterDataResourcesQuery request, string resourceType)
    {
        var keyword = NormalizeKeyword(request.Keyword);
        return dbContext.Shifts
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc, null, null, null, null, null, null, null, null, x.Disabled ? "disabled" : "active"));
    }

    private IQueryable<MasterDataResourceItem> ListReferenceDataCodes(ListMasterDataResourcesQuery request, string resourceType)
    {
        var keyword = NormalizeKeyword(request.Keyword);
        return dbContext.ReferenceDataCodes
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => string.IsNullOrWhiteSpace(request.CodeSet) || x.CodeSet == request.CodeSet)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword) || x.CodeSet.ToLower().Contains(keyword))
            .OrderBy(x => x.CodeSet)
            .ThenBy(x => x.Code)
            .Select(x => Item(resourceType, string.IsNullOrWhiteSpace(request.CodeSet) ? $"{x.CodeSet}:{x.Code}" : x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc, null, null, null, null, null, null, null, null, x.Disabled ? "disabled" : "active", null, null, x.CodeSet));
    }

    private static MasterDataResourceItem Item(
        string resourceType,
        string code,
        string displayName,
        bool active,
        DateTime updatedAtUtc,
        string? PartnerType = null,
        IReadOnlyCollection<string>? PartnerRoles = null,
        string? SiteCode = null,
        string? PlantCode = null,
        string? LineCode = null,
        string? WorkshopCode = null,
        int? CapacityMinutesPerDay = null,
        string? WorkCenterCode = null,
        string? Status = null,
        string? Category = null,
        string? MaterialType = null,
        string? CodeSet = null,
        string? BaseUomCode = null,
        string? TaxId = null,
        string? ParentDepartmentCode = null,
        string? DepartmentCode = null,
        string? ShiftCode = null,
        string? UserId = null,
        string? SkillCode = null,
        string? SkillLevel = null,
        DateOnly? EffectiveFrom = null,
        DateOnly? EffectiveTo = null,
        string? FromUomCode = null,
        string? ToUomCode = null,
        decimal? Factor = null,
        decimal? Offset = null,
        int? Precision = null,
        string? RoundingMode = null,
        string? DeviceAssetId = null)
    {
        return new MasterDataResourceItem(
            resourceType,
            code,
            displayName,
            active,
            updatedAtUtc.ToString("O"),
            PartnerType,
            PartnerRoles,
            SiteCode,
            PlantCode,
            LineCode,
            WorkshopCode,
            CapacityMinutesPerDay,
            WorkCenterCode,
            Status,
            Category,
            MaterialType,
            CodeSet,
            BaseUomCode,
            TaxId,
            ParentDepartmentCode,
            DepartmentCode,
            ShiftCode,
            UserId,
            SkillCode,
            SkillLevel,
            EffectiveFrom,
            EffectiveTo,
            FromUomCode,
            ToUomCode,
            Factor,
            Offset,
            Precision,
            RoundingMode,
            DeviceAssetId);
    }

    private static string? NormalizeKeyword(string? keyword)
    {
        return string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim().ToLowerInvariant();
    }
}
