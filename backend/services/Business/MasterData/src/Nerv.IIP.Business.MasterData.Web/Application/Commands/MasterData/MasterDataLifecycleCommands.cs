using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DepartmentAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.PersonnelSkillAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductionLineAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ReferenceDataAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ShiftAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SiteAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UnitOfMeasureAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UomConversionAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCalendarAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkshopAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure.Repositories;
using Nerv.IIP.Business.MasterData.Web.Application.Queries;
using Nerv.IIP.Business.MasterData.Web.Application.Seed;

namespace Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;

public sealed record UpdateMasterDataResourceCommand(
    string OrganizationId,
    string EnvironmentId,
    string ResourceType,
    string Code,
    string? CodeSet = null,
    string? Name = null,
    string? BaseUomCode = null,
    string? Category = null,
    string? MaterialType = null,
    string? BatchTrackingPolicy = null,
    string? SerialTrackingPolicy = null,
    string? ShelfLifePolicyCode = null,
    string? StorageConditionCode = null,
    string? DefaultBarcodeRuleCode = null,
    bool? QualityRequired = null,
    string? PartnerType = null,
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
    string? DimensionType = null,
    int? Precision = null,
    string? RoundingMode = null,
    IReadOnlyCollection<string>? PartnerRoles = null,
    string? TaxId = null,
    IReadOnlyCollection<WorkCalendarWorkingTimeDetail>? WorkingTimes = null,
    IReadOnlyCollection<WorkCalendarHolidayDetail>? Holidays = null,
    IReadOnlyCollection<WorkCalendarExceptionDetail>? Exceptions = null,
    decimal? Factor = null,
    decimal? Offset = null,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null,
    string? InventoryUomCode = null,
    string? PurchaseUomCode = null,
    string? SalesUomCode = null,
    string? ManufacturingUomCode = null,
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
    string? CostCenterCode = null,
    bool? Bottleneck = null,
    string? HolidayCalendarCode = null,
    int? BreakMinutes = null,
    decimal? CreditLimit = null,
    string? CreditCurrencyCode = null,
    bool ClearCreditLimit = false) : ICommand<MasterDataResourceDetail>;

public sealed record SetMasterDataResourceEnabledCommand(
    string OrganizationId,
    string EnvironmentId,
    string ResourceType,
    string Code,
    bool Enabled,
    string? CodeSet = null,
    string Reason = "",
    DateOnly? EffectiveFrom = null) : ICommand<MasterDataResourceDetail>;

public sealed class UpdateMasterDataResourceCommandHandler(ApplicationDbContext dbContext, IReferenceDataCodeRepository referenceDataRepository)
    : ICommandHandler<UpdateMasterDataResourceCommand, MasterDataResourceDetail>
{
    public async Task<MasterDataResourceDetail> Handle(UpdateMasterDataResourceCommand request, CancellationToken cancellationToken)
    {
        var type = GetMasterDataResourceDetailQueryHandler.NormalizeType(request.ResourceType);
        switch (type)
        {
            case "sku":
                var sku = await FindSkuAsync(request, cancellationToken);
                await ValidateSkuControlledReferenceDataAsync(request, cancellationToken);
                var nextBaseUomCode = request.BaseUomCode ?? sku.BaseUomCode;
                var nextInventoryUomCode = request.InventoryUomCode ?? sku.InventoryUomCode;
                var nextPurchaseUomCode = request.PurchaseUomCode ?? sku.PurchaseUomCode;
                var nextSalesUomCode = request.SalesUomCode ?? sku.SalesUomCode;
                var nextManufacturingUomCode = request.ManufacturingUomCode ?? sku.ManufacturingUomCode;
                await SkuChannelUomValidator.ValidateAsync(
                    dbContext,
                    request.OrganizationId,
                    request.EnvironmentId,
                    nextBaseUomCode,
                    [nextInventoryUomCode, nextPurchaseUomCode, nextSalesUomCode, nextManufacturingUomCode],
                    cancellationToken);
                sku.UpdateIndustrial(
                    request.Name ?? sku.Name,
                    nextBaseUomCode,
                    request.Category ?? sku.Category,
                    request.MaterialType ?? sku.MaterialType,
                    request.BatchTrackingPolicy ?? sku.BatchTrackingPolicy,
                    request.SerialTrackingPolicy ?? sku.SerialTrackingPolicy,
                    request.ShelfLifePolicyCode ?? sku.ShelfLifePolicyCode,
                    request.StorageConditionCode ?? sku.StorageConditionCode,
                    request.DefaultBarcodeRuleCode ?? sku.DefaultBarcodeRuleCode,
                    request.QualityRequired ?? sku.QualityRequired,
                    nextInventoryUomCode,
                    nextPurchaseUomCode,
                    nextSalesUomCode,
                    nextManufacturingUomCode,
                    request.ProcurementType ?? sku.ProcurementType,
                    request.MrpType ?? sku.MrpType,
                    request.LotSizingPolicy ?? sku.LotSizingPolicy,
                    request.MinimumLotSize ?? sku.MinimumLotSize,
                    request.MaximumLotSize ?? sku.MaximumLotSize,
                    request.LotSizeMultiple ?? sku.LotSizeMultiple,
                    request.SafetyStockQuantity ?? sku.SafetyStockQuantity,
                    request.ReorderPointQuantity ?? sku.ReorderPointQuantity,
                    request.PlannedDeliveryTimeDays ?? sku.PlannedDeliveryTimeDays,
                    request.InHouseProductionTimeDays ?? sku.InHouseProductionTimeDays,
                    request.GoodsReceiptProcessingTimeDays ?? sku.GoodsReceiptProcessingTimeDays,
                    request.AbcClass ?? sku.AbcClass,
                    request.LifecycleStatus ?? sku.LifecycleStatus,
                    request.PurchasingEnabled ?? sku.PurchasingEnabled,
                    request.ManufacturingEnabled ?? sku.ManufacturingEnabled,
                    request.SalesEnabled ?? sku.SalesEnabled);
                return Detail(sku);
            case "unit-of-measure":
                var uom = await FindUnitOfMeasureAsync(request, cancellationToken);
                uom.Update(
                    request.Name ?? uom.Name,
                    request.DimensionType ?? uom.DimensionType,
                    request.Precision ?? uom.Precision,
                    request.RoundingMode ?? uom.RoundingMode);
                return Detail(uom);
            case "uom-conversion":
                var conversion = await FindUomConversionAsync(request, cancellationToken);
                await ValidateUomConversionUnitsAsync(conversion, cancellationToken);
                conversion.Update(
                    request.Factor ?? conversion.Factor,
                    request.Offset ?? conversion.Offset,
                    request.Precision ?? conversion.Precision,
                    request.RoundingMode ?? conversion.RoundingMode,
                    request.EffectiveTo ?? conversion.EffectiveTo);
                return Detail(conversion);
            case "business-partner":
                var partner = await FindBusinessPartnerAsync(request, cancellationToken);
                var taxId = string.IsNullOrWhiteSpace(request.TaxId) ? null : request.TaxId.Trim();
                if (taxId is not null &&
                    !string.Equals(taxId, partner.TaxId, StringComparison.Ordinal) &&
                    await dbContext.BusinessPartners.AnyAsync(x =>
                        x.OrganizationId == request.OrganizationId &&
                        x.EnvironmentId == request.EnvironmentId &&
                        x.Code != partner.Code &&
                        !x.Disabled &&
                        x.TaxId == taxId,
                        cancellationToken))
                {
                    throw new KnownException($"Business partner tax id '{taxId}' already exists.");
                }

                var partnerName = request.Name ?? partner.Name;
                var partnerTaxId = taxId ?? partner.TaxId;
                var taxRegionCode = request.TaxRegionCode ?? partner.TaxRegionCode;
                var defaultCurrencyCode = request.DefaultCurrencyCode ?? partner.DefaultCurrencyCode;
                var paymentTermsCode = request.PaymentTermsCode ?? partner.PaymentTermsCode;
                var primaryAddress = request.PrimaryAddress ?? partner.PrimaryAddress;
                var primaryContactName = request.PrimaryContactName ?? partner.PrimaryContactName;
                var primaryContactEmail = request.PrimaryContactEmail ?? partner.PrimaryContactEmail;
                var primaryContactPhone = request.PrimaryContactPhone ?? partner.PrimaryContactPhone;
                var creditLimit = request.ClearCreditLimit ? null : request.CreditLimit ?? partner.CreditLimit;
                var creditCurrencyCode = request.ClearCreditLimit ? null : request.CreditCurrencyCode ?? partner.CreditCurrencyCode;
                if (request.PartnerRoles is null && request.PartnerType is not null)
                {
                    partner.ChangePrimaryRole(
                        partnerName,
                        request.PartnerType,
                        partnerTaxId,
                        taxRegionCode,
                        defaultCurrencyCode,
                        paymentTermsCode,
                        primaryAddress,
                        primaryContactName,
                        primaryContactEmail,
                        primaryContactPhone,
                        creditLimit,
                        creditCurrencyCode);
                }
                else
                {
                    partner.Update(
                        partnerName,
                        request.PartnerRoles,
                        partnerTaxId,
                        taxRegionCode,
                        defaultCurrencyCode,
                        paymentTermsCode,
                        primaryAddress,
                        primaryContactName,
                        primaryContactEmail,
                        primaryContactPhone,
                        creditLimit,
                        creditCurrencyCode);
                }

                return Detail(partner);
            case "site":
                var site = await FindSiteAsync(request, cancellationToken);
                site.Update(request.Name ?? site.Name, request.Timezone ?? site.Timezone);
                return Detail(site);
            case "workshop":
                var workshop = await FindWorkshopAsync(request, cancellationToken);
                workshop.Update(
                    request.Name ?? workshop.Name,
                    request.SiteCode ?? workshop.SiteCode,
                    request.ManagerUserId ?? workshop.ManagerUserId,
                    request.Description ?? workshop.Description);
                return Detail(workshop);
            case "department":
                var department = await FindDepartmentAsync(request, cancellationToken);
                department.Update(
                    request.Name ?? department.Name,
                    request.ParentDepartmentCode ?? department.ParentDepartmentCode);
                return Detail(department);
            case "team":
                var team = await FindTeamAsync(request, cancellationToken);
                team.Update(
                    request.Name ?? team.Name,
                    request.DepartmentCode ?? team.DepartmentCode,
                    request.ShiftCode ?? team.ShiftCode);
                return Detail(team);
            case "shift":
                var shift = await FindShiftAsync(request, cancellationToken);
                shift.Update(
                    request.Name ?? shift.Name,
                    request.StartsAt ?? shift.StartsAt,
                    request.EndsAt ?? shift.EndsAt,
                    request.PaidMinutes ?? shift.PaidMinutes,
                    request.BreakMinutes ?? shift.BreakMinutes);
                return Detail(shift);
            case "work-calendar":
                var calendar = await FindWorkCalendarAsync(request, cancellationToken);
                calendar.Update(
                    request.Name ?? calendar.Name,
                    request.WorkingTimes?.Select(x => new WorkCalendarWorkingTime(x.DayOfWeek)).ToArray(),
                    request.Holidays?.Select(x => new WorkCalendarHoliday(x.Date, x.Name)).ToArray(),
                    request.Exceptions?.Select(x => new WorkCalendarException(x.Date, x.IsWorkingDay, x.StartsAt, x.EndsAt, x.Reason)).ToArray(),
                    request.Timezone ?? calendar.Timezone,
                    request.EffectiveFrom ?? calendar.EffectiveFrom,
                    request.EffectiveTo ?? calendar.EffectiveTo,
                    request.HolidayCalendarCode ?? calendar.HolidayCalendarCode);
                return Detail(calendar);
            case "production-line":
                var line = await FindProductionLineAsync(request, cancellationToken);
                line.Update(request.Name ?? line.Name, request.SiteCode ?? line.SiteCode, request.WorkshopCode ?? line.WorkshopCode);
                return Detail(line);
            case "work-center":
                var workCenter = await FindWorkCenterAsync(request, cancellationToken);
                workCenter.UpdateResource(
                    request.Name ?? workCenter.Name,
                    request.CapacityMinutesPerDay ?? workCenter.CapacityMinutesPerDay,
                    request.ResourceKind ?? workCenter.ResourceType,
                    request.PlantCode ?? workCenter.PlantCode,
                    request.LineCode ?? workCenter.LineCode,
                    request.WorkshopCode ?? workCenter.WorkshopCode,
                    request.DefaultCalendarCode ?? workCenter.DefaultCalendarCode,
                    request.CapacityUnit ?? workCenter.CapacityUnit,
                    request.FiniteCapacity ?? workCenter.FiniteCapacity,
                    request.UtilizationRate ?? workCenter.UtilizationRate,
                    request.EfficiencyRate ?? workCenter.EfficiencyRate,
                    request.NumberOfCapacities ?? workCenter.NumberOfCapacities,
                    request.CostCenterCode ?? workCenter.CostCenterCode,
                    request.Bottleneck ?? workCenter.Bottleneck);
                return Detail(workCenter);
            case "device-asset":
                var device = await FindDeviceAssetAsync(request, cancellationToken);
                device.UpdateCapability(
                    request.Model ?? device.Model,
                    request.LineCode ?? device.LineCode,
                    request.WorkCenterCode ?? device.WorkCenterCode,
                    request.AssetClassCode ?? device.AssetClassCode,
                    request.Manufacturer ?? device.Manufacturer,
                    request.SerialNo ?? device.SerialNo,
                    request.MinimumCapacity ?? device.MinimumCapacity,
                    request.MaximumCapacity ?? device.MaximumCapacity,
                    request.CapacityUomCode ?? device.CapacityUomCode,
                    request.Criticality ?? device.Criticality,
                    request.Maintainable ?? device.Maintainable,
                    request.TelemetryEnabled ?? device.TelemetryEnabled);
                return Detail(device);
            case "reference-data":
                var referenceData = await FindReferenceDataCodeAsync(request, cancellationToken);
                EnsureReferenceDataIsMutable(referenceData);
                referenceData.Update(request.Name ?? referenceData.Name);
                return Detail(referenceData);
            default:
                throw new KnownException($"Unsupported master data resource type '{request.ResourceType}'.");
        }
    }

    private async Task<Sku> FindSkuAsync(UpdateMasterDataResourceCommand request, CancellationToken cancellationToken) =>
        await dbContext.Skus.SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
        ?? throw NotFound(request.ResourceType, request.Code);

    private async Task<UnitOfMeasure> FindUnitOfMeasureAsync(UpdateMasterDataResourceCommand request, CancellationToken cancellationToken) =>
        await dbContext.UnitsOfMeasure.SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
        ?? throw NotFound(request.ResourceType, request.Code);

    private async Task<UomConversion> FindUomConversionAsync(UpdateMasterDataResourceCommand request, CancellationToken cancellationToken)
    {
        var (fromUomCode, toUomCode) = GetMasterDataResourceDetailQueryHandler.ParseConversionCode(request.Code);
        var query = dbContext.UomConversions
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
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw NotFound(request.ResourceType, request.Code);
    }

    private async Task<BusinessPartner> FindBusinessPartnerAsync(UpdateMasterDataResourceCommand request, CancellationToken cancellationToken) =>
        await dbContext.BusinessPartners.SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
        ?? throw NotFound(request.ResourceType, request.Code);

    private async Task<Site> FindSiteAsync(UpdateMasterDataResourceCommand request, CancellationToken cancellationToken) =>
        await dbContext.Sites.SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
        ?? throw NotFound(request.ResourceType, request.Code);

    private async Task<Workshop> FindWorkshopAsync(UpdateMasterDataResourceCommand request, CancellationToken cancellationToken) =>
        await dbContext.Workshops.SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
        ?? throw NotFound(request.ResourceType, request.Code);

    private async Task<Department> FindDepartmentAsync(UpdateMasterDataResourceCommand request, CancellationToken cancellationToken) =>
        await dbContext.Departments.SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
        ?? throw NotFound(request.ResourceType, request.Code);

    private async Task<Team> FindTeamAsync(UpdateMasterDataResourceCommand request, CancellationToken cancellationToken) =>
        await dbContext.Teams.SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
        ?? throw NotFound(request.ResourceType, request.Code);

    private async Task<Shift> FindShiftAsync(UpdateMasterDataResourceCommand request, CancellationToken cancellationToken) =>
        await dbContext.Shifts.SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
        ?? throw NotFound(request.ResourceType, request.Code);

    private async Task<WorkCalendar> FindWorkCalendarAsync(UpdateMasterDataResourceCommand request, CancellationToken cancellationToken) =>
        await dbContext.WorkCalendars
            .Include(x => x.WorkingTimes)
            .Include(x => x.Holidays)
            .Include(x => x.Exceptions)
            .SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
        ?? throw NotFound(request.ResourceType, request.Code);

    private async Task<ProductionLine> FindProductionLineAsync(UpdateMasterDataResourceCommand request, CancellationToken cancellationToken) =>
        await dbContext.ProductionLines.SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
        ?? throw NotFound(request.ResourceType, request.Code);

    private async Task<WorkCenter> FindWorkCenterAsync(UpdateMasterDataResourceCommand request, CancellationToken cancellationToken) =>
        await dbContext.WorkCenters.SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
        ?? throw NotFound(request.ResourceType, request.Code);

    private async Task<DeviceAsset> FindDeviceAssetAsync(UpdateMasterDataResourceCommand request, CancellationToken cancellationToken) =>
        await dbContext.DeviceAssets.SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
        ?? throw NotFound(request.ResourceType, request.Code);

    private async Task<ReferenceDataCode> FindReferenceDataCodeAsync(UpdateMasterDataResourceCommand request, CancellationToken cancellationToken)
    {
        var codeSet = GetMasterDataResourceDetailQueryHandler.RequireReferenceDataCodeSet(request.CodeSet);
        return await dbContext.ReferenceDataCodes
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.CodeSet == codeSet &&
                x.Code == request.Code,
                cancellationToken)
        ?? throw NotFound(request.ResourceType, request.Code);
    }

    private async Task ValidateSkuControlledReferenceDataAsync(UpdateMasterDataResourceCommand request, CancellationToken cancellationToken)
    {
        // SKU update does not expose compliance tag changes yet, so only editable dictionary-backed fields are validated here.
        foreach (var reference in MasterDataDictionaryRules.GetUpdateSkuReferences(
            request.Category,
            request.MaterialType,
            request.BatchTrackingPolicy,
            request.SerialTrackingPolicy,
            request.ShelfLifePolicyCode,
            request.StorageConditionCode,
            request.DefaultBarcodeRuleCode))
        {
            if (string.IsNullOrWhiteSpace(reference.Code))
            {
                throw new KnownException($"SKU field '{reference.Field}' must reference an active '{reference.CodeSet}' code.");
            }

            var exists = await referenceDataRepository.ExistsActiveAsync(
                request.OrganizationId,
                request.EnvironmentId,
                reference.CodeSet,
                reference.Code.Trim(),
                cancellationToken);
            if (!exists)
            {
                throw new KnownException($"SKU field '{reference.Field}' references inactive or missing reference data '{reference.CodeSet}:{reference.Code}'.");
            }
        }
    }

    private async Task ValidateUomConversionUnitsAsync(UomConversion conversion, CancellationToken cancellationToken)
    {
        await UomConversionValidator.ValidateUnitsAsync(
            dbContext,
            conversion.OrganizationId,
            conversion.EnvironmentId,
            conversion.FromUomCode,
            conversion.ToUomCode,
            requireActiveUnits: false,
            cancellationToken);
    }

    internal static void EnsureReferenceDataIsMutable(ReferenceDataCode referenceData)
    {
        if (MasterDataDictionaryRules.IsSystemManagedReferenceData(referenceData.CodeSet, referenceData.Code))
        {
            throw new KnownException($"system-managed reference data '{referenceData.CodeSet}:{referenceData.Code}' cannot be updated.");
        }
    }

    internal static KnownException NotFound(string resourceType, string code) =>
        new($"Master data resource '{resourceType}:{code}' was not found.");

    internal static MasterDataResourceDetail Detail(Sku x) =>
        new(
            "sku",
            x.Code,
            x.Name,
            !x.Disabled,
            x.UpdatedAtUtc.ToString("O"),
            x.OrganizationId,
            x.EnvironmentId,
            x.Name,
            x.BaseUomCode,
            x.InventoryUomCode,
            x.PurchaseUomCode,
            x.SalesUomCode,
            x.ManufacturingUomCode,
            x.Category,
            x.MaterialType,
            x.BatchTrackingPolicy,
            x.SerialTrackingPolicy,
            x.ShelfLifePolicyCode,
            x.StorageConditionCode,
            x.DefaultBarcodeRuleCode,
            x.QualityRequired,
            Status: x.Disabled ? "disabled" : x.LifecycleStatus,
            ProcurementType: x.ProcurementType,
            MrpType: x.MrpType,
            LotSizingPolicy: x.LotSizingPolicy,
            MinimumLotSize: x.MinimumLotSize,
            MaximumLotSize: x.MaximumLotSize,
            LotSizeMultiple: x.LotSizeMultiple,
            SafetyStockQuantity: x.SafetyStockQuantity,
            ReorderPointQuantity: x.ReorderPointQuantity,
            PlannedDeliveryTimeDays: x.PlannedDeliveryTimeDays,
            InHouseProductionTimeDays: x.InHouseProductionTimeDays,
            GoodsReceiptProcessingTimeDays: x.GoodsReceiptProcessingTimeDays,
            AbcClass: x.AbcClass,
            LifecycleStatus: x.LifecycleStatus,
            PurchasingEnabled: x.PurchasingEnabled,
            ManufacturingEnabled: x.ManufacturingEnabled,
            SalesEnabled: x.SalesEnabled);

    internal static MasterDataResourceDetail Detail(UnitOfMeasure x) =>
        new("unit-of-measure", x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc.ToString("O"), x.OrganizationId, x.EnvironmentId, x.Name, DimensionType: x.DimensionType, Precision: x.Precision, RoundingMode: x.RoundingMode, Status: x.Disabled ? "disabled" : "active");

    internal static MasterDataResourceDetail Detail(UomConversion x) =>
        new("uom-conversion", $"{x.FromUomCode}->{x.ToUomCode}", $"{x.FromUomCode} to {x.ToUomCode}", !x.Disabled, x.UpdatedAtUtc.ToString("O"), x.OrganizationId, x.EnvironmentId, Status: x.Disabled ? "disabled" : "active", FromUomCode: x.FromUomCode, ToUomCode: x.ToUomCode, Factor: x.Factor, Offset: x.Offset, Precision: x.Precision, RoundingMode: x.RoundingMode, EffectiveFrom: x.EffectiveFrom, EffectiveTo: x.EffectiveTo);

    internal static MasterDataResourceDetail Detail(BusinessPartner x) =>
        new("business-partner", x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc.ToString("O"), x.OrganizationId, x.EnvironmentId, x.Name, PartnerType: x.PartnerType, PartnerRoles: x.PartnerRoles, TaxId: x.TaxId, Status: x.Disabled ? "disabled" : "active", TaxRegionCode: x.TaxRegionCode, DefaultCurrencyCode: x.DefaultCurrencyCode, PaymentTermsCode: x.PaymentTermsCode, PrimaryAddress: x.PrimaryAddress, PrimaryContactName: x.PrimaryContactName, PrimaryContactEmail: x.PrimaryContactEmail, PrimaryContactPhone: x.PrimaryContactPhone, CreditLimit: x.CreditLimit, CreditCurrencyCode: x.CreditCurrencyCode);

    internal static MasterDataResourceDetail Detail(Site x) =>
        new("site", x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc.ToString("O"), x.OrganizationId, x.EnvironmentId, x.Name, Timezone: x.Timezone, Status: x.Disabled ? "disabled" : "active");

    internal static MasterDataResourceDetail Detail(Workshop x) =>
        new("workshop", x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc.ToString("O"), x.OrganizationId, x.EnvironmentId, x.Name, SiteCode: x.SiteCode, ManagerUserId: x.ManagerUserId, Description: x.Description, Status: x.Disabled ? "disabled" : "active");

    internal static MasterDataResourceDetail Detail(Department x) =>
        new("department", x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc.ToString("O"), x.OrganizationId, x.EnvironmentId, x.Name, ParentDepartmentCode: x.ParentDepartmentCode, Status: x.Disabled ? "disabled" : "active");

    internal static MasterDataResourceDetail Detail(Team x) =>
        new("team", x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc.ToString("O"), x.OrganizationId, x.EnvironmentId, x.Name, DepartmentCode: x.DepartmentCode, ShiftCode: x.ShiftCode, Status: x.Disabled ? "disabled" : "active");

    internal static MasterDataResourceDetail Detail(Shift x) =>
        new("shift", x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc.ToString("O"), x.OrganizationId, x.EnvironmentId, x.Name, StartsAt: x.StartsAt, EndsAt: x.EndsAt, PaidMinutes: x.PaidMinutes, Status: x.Disabled ? "disabled" : "active", BreakMinutes: x.BreakMinutes);

    internal static MasterDataResourceDetail Detail(WorkCalendar x) =>
        new(
            "work-calendar",
            x.Code,
            x.Name,
            !x.Disabled,
            x.UpdatedAtUtc.ToString("O"),
            x.OrganizationId,
            x.EnvironmentId,
            x.Name,
            Status: x.Disabled ? "disabled" : "active",
            Timezone: x.Timezone,
            EffectiveFrom: x.EffectiveFrom,
            EffectiveTo: x.EffectiveTo,
            HolidayCalendarCode: x.HolidayCalendarCode,
            WorkingTimes: x.WorkingTimes.Select(y => new WorkCalendarWorkingTimeDetail(y.DayOfWeek)).ToArray(),
            Holidays: x.Holidays.Select(y => new WorkCalendarHolidayDetail(y.Date, y.Name)).ToArray(),
            Exceptions: x.Exceptions.Select(y => new WorkCalendarExceptionDetail(y.Date, y.IsWorkingDay, y.StartsAt, y.EndsAt, y.Reason)).ToArray());

    internal static MasterDataResourceDetail Detail(ProductionLine x) =>
        new("production-line", x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc.ToString("O"), x.OrganizationId, x.EnvironmentId, x.Name, SiteCode: x.SiteCode, WorkshopCode: x.WorkshopCode, Status: x.Disabled ? "disabled" : "active");

    internal static MasterDataResourceDetail Detail(WorkCenter x) =>
        new("work-center", x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc.ToString("O"), x.OrganizationId, x.EnvironmentId, x.Name, PlantCode: x.PlantCode, LineCode: x.LineCode, WorkshopCode: x.WorkshopCode, CapacityMinutesPerDay: x.CapacityMinutesPerDay, ResourceKind: x.ResourceType, DefaultCalendarCode: x.DefaultCalendarCode, CapacityUnit: x.CapacityUnit, FiniteCapacity: x.FiniteCapacity, Status: x.Disabled ? "disabled" : "active", UtilizationRate: x.UtilizationRate, EfficiencyRate: x.EfficiencyRate, NumberOfCapacities: x.NumberOfCapacities, EffectiveCapacityMinutesPerDay: x.EffectiveCapacityMinutesPerDay, CostCenterCode: x.CostCenterCode, Bottleneck: x.Bottleneck);

    internal static MasterDataResourceDetail Detail(DeviceAsset x) =>
        new("device-asset", x.Code, x.Model, !x.Disabled, x.UpdatedAtUtc.ToString("O"), x.OrganizationId, x.EnvironmentId, Model: x.Model, LineCode: x.LineCode, WorkCenterCode: x.WorkCenterCode, AssetClassCode: x.AssetClassCode, Manufacturer: x.Manufacturer, SerialNo: x.SerialNo, MinimumCapacity: x.MinimumCapacity, MaximumCapacity: x.MaximumCapacity, CapacityUomCode: x.CapacityUomCode, Criticality: x.Criticality, Maintainable: x.Maintainable, TelemetryEnabled: x.TelemetryEnabled, Status: x.Disabled ? "disabled" : "active");

    internal static MasterDataResourceDetail Detail(ReferenceDataCode x) =>
        new("reference-data", x.Code, x.Name, !x.Disabled, x.UpdatedAtUtc.ToString("O"), x.OrganizationId, x.EnvironmentId, x.Name, CodeSet: x.CodeSet, Status: x.Disabled ? "disabled" : "active");

    internal static MasterDataResourceDetail Detail(PersonnelSkill x) =>
        new("personnel-skill", $"{x.UserId}:{x.SkillCode}", x.Level, !x.Disabled, x.UpdatedAtUtc.ToString("O"), x.OrganizationId, x.EnvironmentId, Status: x.Disabled ? "disabled" : "active", EffectiveFrom: x.EffectiveFrom, EffectiveTo: x.EffectiveTo, UserId: x.UserId, SkillCode: x.SkillCode, SkillLevel: x.Level);
}

public sealed class SetMasterDataResourceEnabledCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<SetMasterDataResourceEnabledCommand, MasterDataResourceDetail>
{
    public async Task<MasterDataResourceDetail> Handle(SetMasterDataResourceEnabledCommand request, CancellationToken cancellationToken)
    {
        var reason = request.Reason;
        if (string.IsNullOrWhiteSpace(reason))
        {
            reason = request.Enabled ? "enabled" : "disabled";
        }
        var type = GetMasterDataResourceDetailQueryHandler.NormalizeType(request.ResourceType);
        switch (type)
        {
            case "sku":
                var sku = await FindAsync(dbContext.Skus, request, cancellationToken);
                if (request.Enabled) sku.Enable(reason); else sku.Disable(reason);
                return UpdateMasterDataResourceCommandHandler.Detail(sku);
            case "unit-of-measure":
                var uom = await FindAsync(dbContext.UnitsOfMeasure, request, cancellationToken);
                if (request.Enabled) uom.Enable(reason); else uom.Disable(reason);
                return UpdateMasterDataResourceCommandHandler.Detail(uom);
            case "uom-conversion":
                var conversion = await FindUomConversionAsync(dbContext, request, cancellationToken);
                if (request.Enabled) conversion.Enable(reason); else conversion.Disable(reason);
                return UpdateMasterDataResourceCommandHandler.Detail(conversion);
            case "business-partner":
                var partner = await FindAsync(dbContext.BusinessPartners, request, cancellationToken);
                if (request.Enabled) partner.Enable(reason); else partner.Disable(reason);
                return UpdateMasterDataResourceCommandHandler.Detail(partner);
            case "site":
                var site = await FindAsync(dbContext.Sites, request, cancellationToken);
                if (request.Enabled) site.Enable(reason); else site.Disable(reason);
                return UpdateMasterDataResourceCommandHandler.Detail(site);
            case "workshop":
                var workshop = await FindAsync(dbContext.Workshops, request, cancellationToken);
                if (request.Enabled) workshop.Enable(reason); else workshop.Disable(reason);
                return UpdateMasterDataResourceCommandHandler.Detail(workshop);
            case "department":
                var department = await FindAsync(dbContext.Departments, request, cancellationToken);
                if (request.Enabled) department.Enable(reason); else department.Disable(reason);
                return UpdateMasterDataResourceCommandHandler.Detail(department);
            case "team":
                var team = await FindAsync(dbContext.Teams, request, cancellationToken);
                if (request.Enabled) team.Enable(reason); else team.Disable(reason);
                return UpdateMasterDataResourceCommandHandler.Detail(team);
            case "shift":
                var shift = await FindAsync(dbContext.Shifts, request, cancellationToken);
                if (request.Enabled) shift.Enable(reason); else shift.Disable(reason);
                return UpdateMasterDataResourceCommandHandler.Detail(shift);
            case "work-calendar":
                var calendar = await FindAsync(dbContext.WorkCalendars, request, cancellationToken);
                if (request.Enabled) calendar.Enable(reason); else calendar.Disable(reason);
                return UpdateMasterDataResourceCommandHandler.Detail(calendar);
            case "production-line":
                var line = await FindAsync(dbContext.ProductionLines, request, cancellationToken);
                if (request.Enabled) line.Enable(reason); else line.Disable(reason);
                return UpdateMasterDataResourceCommandHandler.Detail(line);
            case "work-center":
                var workCenter = await FindAsync(dbContext.WorkCenters, request, cancellationToken);
                if (request.Enabled) workCenter.Enable(reason); else workCenter.Disable(reason);
                return UpdateMasterDataResourceCommandHandler.Detail(workCenter);
            case "device-asset":
                var device = await FindAsync(dbContext.DeviceAssets, request, cancellationToken);
                if (request.Enabled) device.Enable(reason); else device.Disable(reason);
                return UpdateMasterDataResourceCommandHandler.Detail(device);
            case "reference-data":
                var codeSet = GetMasterDataResourceDetailQueryHandler.RequireReferenceDataCodeSet(request.CodeSet);
                var referenceData = await dbContext.ReferenceDataCodes.SingleOrDefaultAsync(x =>
                    x.OrganizationId == request.OrganizationId &&
                    x.EnvironmentId == request.EnvironmentId &&
                    x.CodeSet == codeSet &&
                    x.Code == request.Code,
                    cancellationToken)
                    ?? throw UpdateMasterDataResourceCommandHandler.NotFound(request.ResourceType, request.Code);
                if (request.Enabled) referenceData.Enable(reason); else referenceData.Disable(reason);
                return UpdateMasterDataResourceCommandHandler.Detail(referenceData);
            default:
                throw new KnownException($"Unsupported master data resource type '{request.ResourceType}'.");
        }
    }

    private static async Task<T> FindAsync<T>(IQueryable<T> query, SetMasterDataResourceEnabledCommand request, CancellationToken cancellationToken)
        where T : class
    {
        var item = await query.SingleOrDefaultAsync(x =>
            EF.Property<string>(x, "OrganizationId") == request.OrganizationId &&
            EF.Property<string>(x, "EnvironmentId") == request.EnvironmentId &&
            EF.Property<string>(x, "Code") == request.Code,
            cancellationToken);
        return item ?? throw UpdateMasterDataResourceCommandHandler.NotFound(request.ResourceType, request.Code);
    }

    private static async Task<UomConversion> FindUomConversionAsync(ApplicationDbContext dbContext, SetMasterDataResourceEnabledCommand request, CancellationToken cancellationToken)
    {
        var (fromUomCode, toUomCode) = GetMasterDataResourceDetailQueryHandler.ParseConversionCode(request.Code);
        var query = dbContext.UomConversions
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
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw UpdateMasterDataResourceCommandHandler.NotFound(request.ResourceType, request.Code);
    }
}
