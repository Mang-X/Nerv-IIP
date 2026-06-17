using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ReferenceDataAggregate;
using Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;

namespace Nerv.IIP.Business.MasterData.Web.Application.Queries;

public sealed record WorkCalendarWorkingTimeDetail(DayOfWeek DayOfWeek);
public sealed record WorkCalendarHolidayDetail(DateOnly Date, string Name);
public sealed record WorkCalendarExceptionDetail(DateOnly Date, bool IsWorkingDay, TimeOnly? StartsAt, TimeOnly? EndsAt, string? Reason);

public sealed record MasterDataResourceDetail(
    string ResourceType,
    string Code,
    string DisplayName,
    bool Active,
    string SnapshotVersion,
    string OrganizationId,
    string EnvironmentId,
    string? Name = null,
    string? BaseUomCode = null,
    string? InventoryUomCode = null,
    string? PurchaseUomCode = null,
    string? SalesUomCode = null,
    string? ManufacturingUomCode = null,
    string? Category = null,
    string? MaterialType = null,
    string? BatchTrackingPolicy = null,
    string? SerialTrackingPolicy = null,
    string? ShelfLifePolicyCode = null,
    string? StorageConditionCode = null,
    string? DefaultBarcodeRuleCode = null,
    bool? QualityRequired = null,
    string? PartnerType = null,
    IReadOnlyCollection<string>? PartnerRoles = null,
    string? Timezone = null,
    string? SiteCode = null,
    string? ParentDepartmentCode = null,
    string? DepartmentCode = null,
    string? ShiftCode = null,
    TimeOnly? StartsAt = null,
    TimeOnly? EndsAt = null,
    int? PaidMinutes = null,
    string? ManagerUserId = null,
    string? Description = null,
    string? PlantCode = null,
    string? LineCode = null,
    string? WorkshopCode = null,
    int? CapacityMinutesPerDay = null,
    string? ResourceKind = null,
    string? DefaultCalendarCode = null,
    string? CapacityUnit = null,
    bool? FiniteCapacity = null,
    string? WorkCenterCode = null,
    string? AssetClassCode = null,
    string? Model = null,
    string? Manufacturer = null,
    string? SerialNo = null,
    decimal? MinimumCapacity = null,
    decimal? MaximumCapacity = null,
    string? CapacityUomCode = null,
    string? Criticality = null,
    bool? Maintainable = null,
    bool? TelemetryEnabled = null,
    string? CodeSet = null,
    string? DimensionType = null,
    int? Precision = null,
    string? RoundingMode = null,
    string? TaxId = null,
    string? Status = null,
    IReadOnlyCollection<WorkCalendarWorkingTimeDetail>? WorkingTimes = null,
    IReadOnlyCollection<WorkCalendarHolidayDetail>? Holidays = null,
    IReadOnlyCollection<WorkCalendarExceptionDetail>? Exceptions = null,
    string? FromUomCode = null,
    string? ToUomCode = null,
    decimal? Factor = null,
    decimal? Offset = null,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null,
    string? UserId = null,
    string? SkillCode = null,
    string? SkillLevel = null,
    string? ProcurementType = null,
    string? MrpType = null,
    string? LotSizingPolicy = null,
    decimal? MinimumLotSize = null,
    decimal? MaximumLotSize = null,
    decimal? LotSizeMultiple = null,
    decimal? SafetyStockQuantity = null,
    decimal? ReorderPointQuantity = null,
    int? PlannedDeliveryTimeDays = null,
    int? InHouseProductionTimeDays = null,
    int? GoodsReceiptProcessingTimeDays = null,
    string? AbcClass = null,
    string? LifecycleStatus = null,
    bool? PurchasingEnabled = null,
    bool? ManufacturingEnabled = null,
    bool? SalesEnabled = null,
    string? TaxRegionCode = null,
    string? DefaultCurrencyCode = null,
    string? PaymentTermsCode = null,
    string? PrimaryAddress = null,
    string? PrimaryContactName = null,
    string? PrimaryContactEmail = null,
    string? PrimaryContactPhone = null,
    decimal? UtilizationRate = null,
    decimal? EfficiencyRate = null,
    int? NumberOfCapacities = null,
    decimal? EffectiveCapacityMinutesPerDay = null,
    string? CostCenterCode = null,
    bool? Bottleneck = null,
    string? HolidayCalendarCode = null,
    int? BreakMinutes = null);

public sealed record GetMasterDataResourceDetailQuery(
    string OrganizationId,
    string EnvironmentId,
    string ResourceType,
    string Code,
    string? CodeSet = null,
    DateOnly? EffectiveFrom = null) : IQuery<MasterDataResourceDetail>;

public sealed class GetMasterDataResourceDetailQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetMasterDataResourceDetailQuery, MasterDataResourceDetail>
{
    public async Task<MasterDataResourceDetail> Handle(GetMasterDataResourceDetailQuery request, CancellationToken cancellationToken)
    {
        var type = NormalizeType(request.ResourceType);
        return type switch
        {
            "sku" => UpdateMasterDataResourceCommandHandler.Detail(
                await dbContext.Skus.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
                ?? throw NotFound(type, request.Code)),
            "unit-of-measure" => UpdateMasterDataResourceCommandHandler.Detail(
                await dbContext.UnitsOfMeasure.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
                ?? throw NotFound(type, request.Code)),
            "uom-conversion" => UpdateMasterDataResourceCommandHandler.Detail(
                await FindUomConversionAsync(request, cancellationToken)
                ?? throw NotFound(type, request.Code)),
            "business-partner" => UpdateMasterDataResourceCommandHandler.Detail(
                await dbContext.BusinessPartners.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
                ?? throw NotFound(type, request.Code)),
            "site" => UpdateMasterDataResourceCommandHandler.Detail(
                await dbContext.Sites.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
                ?? throw NotFound(type, request.Code)),
            "workshop" => UpdateMasterDataResourceCommandHandler.Detail(
                await dbContext.Workshops.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
                ?? throw NotFound(type, request.Code)),
            "department" => UpdateMasterDataResourceCommandHandler.Detail(
                await dbContext.Departments.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
                ?? throw NotFound(type, request.Code)),
            "team" => UpdateMasterDataResourceCommandHandler.Detail(
                await dbContext.Teams.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
                ?? throw NotFound(type, request.Code)),
            "shift" => UpdateMasterDataResourceCommandHandler.Detail(
                await dbContext.Shifts.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
                ?? throw NotFound(type, request.Code)),
            "work-calendar" => UpdateMasterDataResourceCommandHandler.Detail(
                await dbContext.WorkCalendars.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
                ?? throw NotFound(type, request.Code)),
            "personnel-skill" => UpdateMasterDataResourceCommandHandler.Detail(
                await FindPersonnelSkillAsync(request, cancellationToken)
                ?? throw NotFound(type, request.Code)),
            "production-line" => UpdateMasterDataResourceCommandHandler.Detail(
                await dbContext.ProductionLines.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
                ?? throw NotFound(type, request.Code)),
            "work-center" => UpdateMasterDataResourceCommandHandler.Detail(
                await dbContext.WorkCenters.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
                ?? throw NotFound(type, request.Code)),
            "device-asset" => UpdateMasterDataResourceCommandHandler.Detail(
                await dbContext.DeviceAssets.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
                ?? throw NotFound(type, request.Code)),
            "reference-data" => UpdateMasterDataResourceCommandHandler.Detail(
                await FindReferenceDataCodeAsync(request, cancellationToken)
                ?? throw NotFound(type, request.Code)),
            _ => throw new KnownException($"Unsupported master data resource type '{request.ResourceType}'."),
        };
    }

    private async Task<Domain.AggregatesModel.UomConversionAggregate.UomConversion?> FindUomConversionAsync(
        GetMasterDataResourceDetailQuery request,
        CancellationToken cancellationToken)
    {
        var (fromUomCode, toUomCode) = ParseConversionCode(request.Code);
        var query = dbContext.UomConversions.AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.FromUomCode == fromUomCode &&
                x.ToUomCode == toUomCode);
        if (request.EffectiveFrom.HasValue)
        {
            query = query.Where(x => x.EffectiveFrom == request.EffectiveFrom.Value);
        }

        return await query
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Domain.AggregatesModel.PersonnelSkillAggregate.PersonnelSkill?> FindPersonnelSkillAsync(
        GetMasterDataResourceDetailQuery request,
        CancellationToken cancellationToken)
    {
        var (userId, skillCode) = ParsePersonnelSkillCode(request.Code);
        var query = dbContext.PersonnelSkills.AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.UserId == userId &&
                x.SkillCode == skillCode);
        if (request.EffectiveFrom.HasValue)
        {
            query = query.Where(x => x.EffectiveFrom == request.EffectiveFrom.Value);
        }

        return await query
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<ReferenceDataCode?> FindReferenceDataCodeAsync(
        GetMasterDataResourceDetailQuery request,
        CancellationToken cancellationToken)
    {
        var codeSet = RequireReferenceDataCodeSet(request.CodeSet);
        return await dbContext.ReferenceDataCodes.AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.CodeSet == codeSet &&
                x.Code == request.Code,
                cancellationToken);
    }

    internal static string NormalizeType(string resourceType)
    {
        return resourceType.Trim().ToLowerInvariant() switch
        {
            "uom" => "unit-of-measure",
            "partner" => "business-partner",
            "reference-data-code" => "reference-data",
            var value => value,
        };
    }

    internal static string RequireReferenceDataCodeSet(string? codeSet)
    {
        if (string.IsNullOrWhiteSpace(codeSet))
        {
            throw new KnownException("Reference data codeSet is required when addressing a reference-data resource.");
        }

        return codeSet.Trim();
    }

    internal static (string FromUomCode, string ToUomCode) ParseConversionCode(string code)
    {
        var parts = code.Split("->", StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            throw new KnownException("UOM conversion code must use 'from->to' format.");
        }

        return (parts[0], parts[1]);
    }

    internal static (string UserId, string SkillCode) ParsePersonnelSkillCode(string code)
    {
        var parts = code.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            throw new KnownException("Personnel skill code must use 'userId:skillCode' format.");
        }

        return (parts[0], parts[1]);
    }

    private static KnownException NotFound(string resourceType, string code) =>
        new($"Master data resource '{resourceType}:{code}' was not found.");
}
