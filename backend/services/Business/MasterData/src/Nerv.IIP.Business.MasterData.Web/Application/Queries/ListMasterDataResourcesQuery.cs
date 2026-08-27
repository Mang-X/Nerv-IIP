using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using System.Text;

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
    string? DeviceAssetId = null,
    DateOnly? PurchaseDate = null,
    decimal? PurchaseCost = null,
    string? PurchaseCurrencyCode = null,
    DateOnly? WarrantyExpiresOn = null,
    string? SupplierPartnerCode = null,
    string? StationCode = null,
    string? ParentDeviceId = null,
    DateOnly? RetiredOn = null,
    decimal? CreditLimit = null,
    string? CreditCurrencyCode = null,
    string? JobTitle = null,
    string? EmploymentStatus = null,
    string? Phone = null,
    string? Timezone = null,
    TimeOnly? StartsAt = null,
    TimeOnly? EndsAt = null,
    bool? CrossesMidnight = null,
    int? PaidMinutes = null,
    int? BreakMinutes = null);

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
    int Take = OffsetPage.DefaultTake,
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
    string? SkillCode = null,
    string? WorkshopCode = null,
    string? DeviceAssetId = null) : IQuery<ListMasterDataResourcesResponse>;

public sealed class ListMasterDataResourcesQueryValidator : AbstractValidator<ListMasterDataResourcesQuery>
{
    public ListMasterDataResourcesQueryValidator()
    {
        this.AddTenantRules(query => query.OrganizationId, query => query.EnvironmentId);
    }
}

public sealed class ListMasterDataResourcesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListMasterDataResourcesQuery, ListMasterDataResourcesResponse>
{
    public async Task<ListMasterDataResourcesResponse> Handle(ListMasterDataResourcesQuery request, CancellationToken cancellationToken)
    {
        var tenant = TenantScope.From(request.OrganizationId, request.EnvironmentId);
        var page = OffsetPage.From(request.Skip, request.Take);
        var keyword = SearchTerm.From(request.Keyword);
        var type = request.ResourceType.Trim().ToLowerInvariant();
        DeviceAssetId? resolvedDeviceAssetId = null;
        if (type == "device-asset" && !string.IsNullOrWhiteSpace(request.DeviceAssetId))
        {
            var reference = request.DeviceAssetId.Trim();
            var parsedDeviceAssetId = Guid.TryParse(reference, out var parsed) && parsed != Guid.Empty
                ? new DeviceAssetId(parsed)
                : null;
            var matches = await dbContext.DeviceAssets
                .AsNoTracking()
                .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId)
                .Where(x => x.Code == reference || (parsedDeviceAssetId != null && x.Id == parsedDeviceAssetId))
                .Select(x => x.Id)
                .Take(2)
                .ToArrayAsync(cancellationToken);
            if (matches.Length == 0)
            {
                return new ListMasterDataResourcesResponse([], 0);
            }
            if (matches.Length > 1)
            {
                throw new KnownException($"主数据设备引用 '{reference}' 对应多条记录，无法唯一确定。");
            }
            resolvedDeviceAssetId = matches[0];
        }
        var query = type switch
        {
            "sku" => ListSkus(request, tenant, keyword.Value, type),
            "unit-of-measure" or "uom" => ListUnits(request, tenant, keyword.Value, "unit-of-measure"),
            "uom-conversion" => ListUomConversions(request, tenant, keyword.Value, type),
            "business-partner" or "partner" => ListPartners(request, tenant, keyword.Value, "business-partner"),
            "department" => ListDepartments(request, tenant, keyword.Value, type),
            "team" => ListTeams(request, tenant, keyword.Value, type),
            "worker" => ListWorkers(request, tenant, keyword.Value, type),
            "personnel-skill" => ListPersonnelSkills(request, tenant, keyword.Value, type),
            "workshop" => ListWorkshops(request, tenant, keyword.Value, type),
            "work-center" => ListWorkCenters(request, tenant, keyword.Value, type),
            "work-calendar" => ListWorkCalendars(request, tenant, keyword.Value, type),
            "device-asset" => ListDeviceAssets(request, tenant, keyword.Value, type, resolvedDeviceAssetId),
            "station" => ListStations(request, tenant, keyword.Value, type),
            "site" => ListSites(request, tenant, keyword.Value, type),
            "production-line" => ListProductionLines(request, tenant, keyword.Value, type),
            "shift" => ListShifts(request, tenant, keyword.Value, type),
            "reference-data" or "reference-data-code" => ListReferenceDataCodes(request, tenant, keyword.Value, "reference-data"),
            _ => null,
        };
        return query is null
            ? new ListMasterDataResourcesResponse([], 0)
            : await ToPageAsync(query, request, tenant, page, cancellationToken);
    }

    private static async Task<ListMasterDataResourcesResponse> ToPageAsync(
        IQueryable<MasterDataResourceItem> query,
        ListMasterDataResourcesQuery request,
        TenantScope tenant,
        OffsetPage page,
        CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var limit = request.All ? 5000 : page.Take;
        var resources = await query
            .Skip(request.All ? 0 : page.Skip)
            .Take(limit)
            .ToListAsync(cancellationToken);
        if (string.Equals(request.ResourceType, "station", StringComparison.OrdinalIgnoreCase))
        {
            resources = resources
                .Select(resource => resource with
                {
                    Code = StableStationId(
                        tenant.OrganizationId,
                        tenant.EnvironmentId,
                        resource.SiteCode,
                        resource.WorkshopCode,
                        resource.LineCode,
                        resource.WorkCenterCode,
                        resource.StationCode),
                })
                .ToList();
        }

        return new ListMasterDataResourcesResponse(resources, total, request.All && total > limit, request.All ? limit : null);
    }

    private static string StableStationId(params string?[] components)
    {
        var builder = new StringBuilder("station:");
        foreach (var component in components)
        {
            var value = component ?? string.Empty;
            builder.Append(Encoding.UTF8.GetByteCount(value));
            builder.Append(':');
            builder.Append(value);
        }

        return builder.ToString();
    }

    private IQueryable<MasterDataResourceItem> ListSkus(ListMasterDataResourcesQuery request, TenantScope tenant, string? keyword, string resourceType)
    {
        return dbContext.Skus
            .AsNoTracking()
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => string.IsNullOrWhiteSpace(request.Category) || x.Category == request.Category)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc, null, null, null, null, null, null, null, null, x.Disabled ? "disabled" : "active", x.Category, x.MaterialType, null, x.BaseUomCode));
    }

    private IQueryable<MasterDataResourceItem> ListUnits(ListMasterDataResourcesQuery request, TenantScope tenant, string? keyword, string resourceType)
    {
        return dbContext.UnitsOfMeasure
            .AsNoTracking()
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc, null, null, null, null, null, null, null, null, x.Disabled ? "disabled" : "active"));
    }

    private IQueryable<MasterDataResourceItem> ListUomConversions(ListMasterDataResourcesQuery request, TenantScope tenant, string? keyword, string resourceType)
    {
        return dbContext.UomConversions
            .AsNoTracking()
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId)
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

    private IQueryable<MasterDataResourceItem> ListPartners(ListMasterDataResourcesQuery request, TenantScope tenant, string? keyword, string resourceType)
    {
        return dbContext.BusinessPartners
            .AsNoTracking()
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => string.IsNullOrWhiteSpace(request.PartnerType) || x.PartnerType == request.PartnerType || x.PartnerRoles.Contains(request.PartnerType))
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => new MasterDataResourceItem(
                resourceType,
                x.Code,
                x.Name,
                !x.Disabled,
                x.UpdatedAtUtc.ToString("O"))
            {
                PartnerType = x.PartnerType,
                PartnerRoles = x.PartnerRoles,
                Status = x.Disabled ? "disabled" : "active",
                TaxId = x.TaxId,
                CreditLimit = x.CreditLimit,
                CreditCurrencyCode = x.CreditCurrencyCode,
            });
    }

    private IQueryable<MasterDataResourceItem> ListDepartments(ListMasterDataResourcesQuery request, TenantScope tenant, string? keyword, string resourceType)
    {
        return dbContext.Departments
            .AsNoTracking()
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => string.IsNullOrWhiteSpace(request.ParentCode) || x.ParentDepartmentCode == request.ParentCode)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc, null, null, null, null, null, null, null, null, x.Disabled ? "disabled" : "active", null, null, null, null, null, x.ParentDepartmentCode));
    }

    private IQueryable<MasterDataResourceItem> ListTeams(ListMasterDataResourcesQuery request, TenantScope tenant, string? keyword, string resourceType)
    {
        return dbContext.Teams
            .AsNoTracking()
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => string.IsNullOrWhiteSpace(request.DepartmentCode) || x.DepartmentCode == request.DepartmentCode)
            .Where(x => string.IsNullOrWhiteSpace(request.ShiftCode) || x.ShiftCode == request.ShiftCode)
            .Where(x => string.IsNullOrWhiteSpace(request.WorkshopCode) || x.WorkshopCode == request.WorkshopCode)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc, null, null, null, null, null, x.WorkshopCode, null, null, x.Disabled ? "disabled" : "active", null, null, null, null, null, null, x.DepartmentCode, x.ShiftCode));
    }

    private IQueryable<MasterDataResourceItem> ListWorkers(ListMasterDataResourcesQuery request, TenantScope tenant, string? keyword, string resourceType)
    {
        return dbContext.Workers
            .AsNoTracking()
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => string.IsNullOrWhiteSpace(request.DepartmentCode) || x.DepartmentCode == request.DepartmentCode)
            .Where(x => string.IsNullOrWhiteSpace(request.UserId) || x.UserId == request.UserId)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc, null, null, null, null, null, null, null, null, x.Disabled ? "disabled" : "active", null, null, null, null, null, null, x.DepartmentCode, null, x.UserId, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, x.JobTitle, x.EmploymentStatus, x.Phone));
    }

    private IQueryable<MasterDataResourceItem> ListPersonnelSkills(ListMasterDataResourcesQuery request, TenantScope tenant, string? keyword, string resourceType)
    {
        return dbContext.PersonnelSkills
            .AsNoTracking()
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => string.IsNullOrWhiteSpace(request.UserId) || x.UserId == request.UserId)
            .Where(x => string.IsNullOrWhiteSpace(request.SkillCode) || x.SkillCode == request.SkillCode)
            .Where(x => keyword == null || x.UserId.ToLower().Contains(keyword) || x.SkillCode.ToLower().Contains(keyword) || x.Level.ToLower().Contains(keyword))
            .OrderBy(x => x.UserId)
            .ThenBy(x => x.SkillCode)
            .Select(x => Item(resourceType, $"{x.UserId}:{x.SkillCode}", x.Level, !x.Disabled, x.UpdatedAtUtc, null, null, null, null, null, null, null, null, x.Disabled ? "disabled" : "active", null, null, null, null, null, null, null, null, x.UserId, x.SkillCode, x.Level, x.EffectiveFrom, x.EffectiveTo));
    }

    private IQueryable<MasterDataResourceItem> ListWorkshops(ListMasterDataResourcesQuery request, TenantScope tenant, string? keyword, string resourceType)
    {
        return dbContext.Workshops
            .AsNoTracking()
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => string.IsNullOrWhiteSpace(request.SiteCode) || x.SiteCode == request.SiteCode)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc, null, null, x.SiteCode, null, null, null, null, null, x.Disabled ? "disabled" : "active"));
    }

    private IQueryable<MasterDataResourceItem> ListWorkCenters(ListMasterDataResourcesQuery request, TenantScope tenant, string? keyword, string resourceType)
    {
        return dbContext.WorkCenters
            .AsNoTracking()
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => string.IsNullOrWhiteSpace(request.LineCode) || x.LineCode == request.LineCode)
            .Where(x => string.IsNullOrWhiteSpace(request.SiteCode) || x.PlantCode == request.SiteCode)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc, null, null, null, x.PlantCode, x.LineCode, x.WorkshopCode, x.CapacityMinutesPerDay, null, x.Disabled ? "disabled" : "active"));
    }

    private IQueryable<MasterDataResourceItem> ListWorkCalendars(ListMasterDataResourcesQuery request, TenantScope tenant, string? keyword, string resourceType)
    {
        return dbContext.WorkCalendars
            .AsNoTracking()
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc, null, null, null, null, null, null, null, null, x.Disabled ? "disabled" : "active"));
    }

    private IQueryable<MasterDataResourceItem> ListDeviceAssets(
        ListMasterDataResourcesQuery request,
        TenantScope tenant,
        string? keyword,
        string resourceType,
        DeviceAssetId? resolvedDeviceAssetId)
    {
        var query = dbContext.DeviceAssets
            .AsNoTracking()
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => string.IsNullOrWhiteSpace(request.LineCode) || x.LineCode == request.LineCode)
            .Where(x => string.IsNullOrWhiteSpace(request.WorkCenterCode) || x.WorkCenterCode == request.WorkCenterCode)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Model.ToLower().Contains(keyword));
        if (resolvedDeviceAssetId is not null)
        {
            query = query.Where(x => x.Id == resolvedDeviceAssetId);
        }

        return query
            .OrderBy(x => x.Code)
            .Select(x => new MasterDataResourceItem(
                resourceType,
                x.Code,
                x.Model,
                !x.Disabled,
                x.UpdatedAtUtc.ToString("O"))
            {
                LineCode = x.LineCode,
                SiteCode = x.SiteCode,
                WorkshopCode = x.WorkshopCode,
                WorkCenterCode = x.WorkCenterCode,
                Status = x.Disabled ? "disabled" : "active",
                DeviceAssetId = x.Id.ToString(),
                PurchaseDate = x.PurchaseDate,
                PurchaseCost = x.PurchaseCost,
                PurchaseCurrencyCode = x.PurchaseCurrencyCode,
                WarrantyExpiresOn = x.WarrantyExpiresOn,
                SupplierPartnerCode = x.SupplierPartnerCode,
                StationCode = x.StationCode,
                ParentDeviceId = x.ParentDeviceId,
                RetiredOn = x.RetiredOn,
            });
    }

    private IQueryable<MasterDataResourceItem> ListStations(ListMasterDataResourcesQuery request, TenantScope tenant, string? keyword, string resourceType)
    {
        return dbContext.DeviceAssets
            .AsNoTracking()
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => x.StationCode != null && x.StationCode != "")
            .Where(x => string.IsNullOrWhiteSpace(request.WorkCenterCode) || x.WorkCenterCode == request.WorkCenterCode)
            .Where(x => keyword == null || x.StationCode!.ToLower().Contains(keyword))
            .GroupBy(x => new { x.SiteCode, x.WorkshopCode, x.LineCode, x.WorkCenterCode, StationCode = x.StationCode! })
            .OrderBy(x => x.Key.StationCode)
            .ThenBy(x => x.Key.SiteCode)
            .ThenBy(x => x.Key.WorkshopCode)
            .ThenBy(x => x.Key.LineCode)
            .ThenBy(x => x.Key.WorkCenterCode)
            .Select(x => new MasterDataResourceItem(
                resourceType,
                x.Key.StationCode,
                x.Key.StationCode,
                true,
                x.Max(asset => asset.UpdatedAtUtc).ToString("O"))
            {
                SiteCode = x.Key.SiteCode,
                WorkshopCode = x.Key.WorkshopCode,
                LineCode = x.Key.LineCode,
                WorkCenterCode = x.Key.WorkCenterCode,
                StationCode = x.Key.StationCode,
                Status = "active",
            });
    }

    private IQueryable<MasterDataResourceItem> ListSites(ListMasterDataResourcesQuery request, TenantScope tenant, string? keyword, string resourceType)
    {
        return dbContext.Sites
            .AsNoTracking()
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => string.IsNullOrWhiteSpace(request.SiteCode) || x.Code == request.SiteCode)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => new MasterDataResourceItem(
                resourceType,
                x.Code,
                x.Name,
                !x.Disabled,
                x.UpdatedAtUtc.ToString("O"))
            {
                Status = x.Disabled ? "disabled" : "active",
                Timezone = x.Timezone,
            });
    }

    private IQueryable<MasterDataResourceItem> ListProductionLines(ListMasterDataResourcesQuery request, TenantScope tenant, string? keyword, string resourceType)
    {
        return dbContext.ProductionLines
            .AsNoTracking()
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => string.IsNullOrWhiteSpace(request.SiteCode) || x.SiteCode == request.SiteCode)
            .Where(x => string.IsNullOrWhiteSpace(request.LineCode) || x.Code == request.LineCode)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => Item(resourceType, x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc, null, null, x.SiteCode, null, null, x.WorkshopCode, null, null, x.Disabled ? "disabled" : "active"));
    }

    private IQueryable<MasterDataResourceItem> ListShifts(ListMasterDataResourcesQuery request, TenantScope tenant, string? keyword, string resourceType)
    {
        return dbContext.Shifts
            .AsNoTracking()
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .Where(x => string.IsNullOrWhiteSpace(request.ShiftCode) || x.Code == request.ShiftCode)
            .Where(x => keyword == null || x.Code.ToLower().Contains(keyword) || x.Name.ToLower().Contains(keyword))
            .OrderBy(x => x.Code)
            .Select(x => new MasterDataResourceItem(
                resourceType,
                x.Code,
                x.Name,
                !x.Disabled,
                x.UpdatedAtUtc.ToString("O"))
            {
                Status = x.Disabled ? "disabled" : "active",
                StartsAt = x.StartsAt,
                EndsAt = x.EndsAt,
                CrossesMidnight = x.CrossesMidnight,
                PaidMinutes = x.PaidMinutes,
                BreakMinutes = x.BreakMinutes,
            });
    }

    private IQueryable<MasterDataResourceItem> ListReferenceDataCodes(ListMasterDataResourcesQuery request, TenantScope tenant, string? keyword, string resourceType)
    {
        return dbContext.ReferenceDataCodes
            .AsNoTracking()
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId)
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
        string? DeviceAssetId = null,
        DateOnly? PurchaseDate = null,
        decimal? PurchaseCost = null,
        string? PurchaseCurrencyCode = null,
        DateOnly? WarrantyExpiresOn = null,
        string? SupplierPartnerCode = null,
        string? StationCode = null,
        string? ParentDeviceId = null,
        DateOnly? RetiredOn = null,
        decimal? CreditLimit = null,
        string? CreditCurrencyCode = null,
        string? JobTitle = null,
        string? EmploymentStatus = null,
        string? Phone = null)
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
            DeviceAssetId,
            PurchaseDate,
            PurchaseCost,
            PurchaseCurrencyCode,
            WarrantyExpiresOn,
            SupplierPartnerCode,
            StationCode,
            ParentDeviceId,
            RetiredOn,
            CreditLimit,
            CreditCurrencyCode,
            JobTitle,
            EmploymentStatus,
            Phone);
    }

}
