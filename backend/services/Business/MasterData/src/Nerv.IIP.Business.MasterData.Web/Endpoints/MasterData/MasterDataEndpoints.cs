using FastEndpoints;
using Nerv.IIP.Business.MasterData.Web.Application.Auth;
using Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;
using Nerv.IIP.Business.MasterData.Web.Application.Queries;
using Nerv.IIP.Contracts.Coding;
using NetCorePal.Extensions.Dto;
using Nerv.IIP.ServiceAuth;
using System.Diagnostics.CodeAnalysis;
using static Nerv.IIP.Business.MasterData.Web.Endpoints.MasterData.MasterDataEndpointMapping;

namespace Nerv.IIP.Business.MasterData.Web.Endpoints.MasterData;

public sealed record MasterDataResourceResponse(string ResourceType, string Code, string DisplayName);

internal static class MasterDataEndpointMapping
{
    public static MasterDataResourceResponse ToResponse(MasterDataResourceResult result)
    {
        return new MasterDataResourceResponse(result.ResourceType, result.Code, result.DisplayName);
    }
}

public abstract class MasterDataEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    protected void ConfigureMasterDataContract(MasterDataEndpointContract contract)
    {
        switch (contract.HttpMethod)
        {
            case "GET":
                Get(contract.Route);
                break;
            case "POST":
                Post(contract.Route);
                break;
            case "PUT":
                Put(contract.Route);
                break;
            case "PATCH":
                Patch(contract.Route);
                break;
            case "DELETE":
                Delete(contract.Route);
                break;
            default:
                throw new NotSupportedException($"HTTP method '{contract.HttpMethod}' is not supported by MasterData endpoints.");
        }

        Tags("Business MasterData");
        Policies(InternalServiceAuthorizationPolicy.Name);
    }
}

public sealed record ListMasterDataResourcesRequest(
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
    string? SkillCode = null);

public sealed record CreateSkuRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string BaseUomCode,
    string Category,
    string MaterialType,
    string BatchTrackingPolicy,
    string SerialTrackingPolicy,
    string ShelfLifePolicyCode,
    string StorageConditionCode,
    string DefaultBarcodeRuleCode,
    bool QualityRequired,
    IReadOnlyCollection<string>? ComplianceTags,
    string? IdempotencyKey = null,
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
    string? LifecycleStatus = "active",
    bool PurchasingEnabled = true,
    bool ManufacturingEnabled = true,
    bool SalesEnabled = true);

public sealed class ListMasterDataResourcesEndpoint(ISender sender)
    : MasterDataEndpoint<ListMasterDataResourcesRequest, ResponseData<ListMasterDataResourcesResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<ListMasterDataResourcesEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(ListMasterDataResourcesRequest req, CancellationToken ct)
    {
        var response = await sender.Send(
            new ListMasterDataResourcesQuery(
                req.OrganizationId,
                req.EnvironmentId,
                req.ResourceType,
                req.IncludeDisabled,
                req.Skip,
                req.Take,
                req.CodeSet,
                req.ParentCode,
                req.SiteCode,
                req.LineCode,
                req.WorkCenterCode,
                req.Category,
                req.PartnerType,
                req.Keyword,
                req.All,
                req.DepartmentCode,
                req.ShiftCode,
                req.UserId,
                req.SkillCode),
            ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed record GetMasterDataResourceDetailRequest(
    string OrganizationId,
    string EnvironmentId,
    string ResourceType,
    string Code,
    string? CodeSet = null,
    DateOnly? EffectiveFrom = null);

public sealed class GetMasterDataResourceDetailEndpoint(ISender sender)
    : MasterDataEndpoint<GetMasterDataResourceDetailRequest, ResponseData<MasterDataResourceDetail>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<GetMasterDataResourceDetailEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(GetMasterDataResourceDetailRequest req, CancellationToken ct)
    {
        var response = await sender.Send(
            new GetMasterDataResourceDetailQuery(req.OrganizationId, req.EnvironmentId, req.ResourceType, req.Code, req.CodeSet, req.EffectiveFrom),
            ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed record UpdateMasterDataResourceRequest(
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
    bool ClearCreditLimit = false);

public sealed class UpdateMasterDataResourceEndpoint(ISender sender)
    : MasterDataEndpoint<UpdateMasterDataResourceRequest, ResponseData<MasterDataResourceDetail>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<UpdateMasterDataResourceEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(UpdateMasterDataResourceRequest req, CancellationToken ct)
    {
        var response = await sender.Send(
            new UpdateMasterDataResourceCommand(
                req.OrganizationId,
                req.EnvironmentId,
                req.ResourceType,
                req.Code,
                req.CodeSet,
                req.Name,
                req.BaseUomCode,
                req.Category,
                req.MaterialType,
                req.BatchTrackingPolicy,
                req.SerialTrackingPolicy,
                req.ShelfLifePolicyCode,
                req.StorageConditionCode,
                req.DefaultBarcodeRuleCode,
                req.QualityRequired,
                req.PartnerType,
                req.Timezone,
                req.SiteCode,
                req.ParentDepartmentCode,
                req.DepartmentCode,
                req.ShiftCode,
                req.StartsAt,
                req.EndsAt,
                req.PaidMinutes,
                req.ManagerUserId,
                req.Description,
                req.PlantCode,
                req.LineCode,
                req.WorkshopCode,
                req.CapacityMinutesPerDay,
                req.ResourceKind,
                req.DefaultCalendarCode,
                req.CapacityUnit,
                req.FiniteCapacity,
                req.WorkCenterCode,
                req.AssetClassCode,
                req.Model,
                req.Manufacturer,
                req.SerialNo,
                req.MinimumCapacity,
                req.MaximumCapacity,
                req.CapacityUomCode,
                req.Criticality,
                req.Maintainable,
                req.TelemetryEnabled,
                req.DimensionType,
                req.Precision,
                req.RoundingMode,
                req.PartnerRoles,
                req.TaxId,
                req.WorkingTimes,
                req.Holidays,
                req.Exceptions,
                req.Factor,
                req.Offset,
                req.EffectiveFrom,
                req.EffectiveTo,
                req.InventoryUomCode,
                req.PurchaseUomCode,
                req.SalesUomCode,
                req.ManufacturingUomCode,
                req.ProcurementType,
                req.MrpType,
                req.LotSizingPolicy,
                req.MinimumLotSize,
                req.MaximumLotSize,
                req.LotSizeMultiple,
                req.SafetyStockQuantity,
                req.ReorderPointQuantity,
                req.PlannedDeliveryTimeDays,
                req.InHouseProductionTimeDays,
                req.GoodsReceiptProcessingTimeDays,
                req.AbcClass,
                req.LifecycleStatus,
                req.PurchasingEnabled,
                req.ManufacturingEnabled,
                req.SalesEnabled,
                req.TaxRegionCode,
                req.DefaultCurrencyCode,
                req.PaymentTermsCode,
                req.PrimaryAddress,
                req.PrimaryContactName,
                req.PrimaryContactEmail,
                req.PrimaryContactPhone,
                req.UtilizationRate,
                req.EfficiencyRate,
                req.NumberOfCapacities,
                req.CostCenterCode,
                req.Bottleneck,
                req.HolidayCalendarCode,
                req.BreakMinutes,
                req.CreditLimit,
                req.CreditCurrencyCode,
                req.ClearCreditLimit),
            ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed record SetMasterDataResourceEnabledRequest(
    string OrganizationId,
    string EnvironmentId,
    string ResourceType,
    string Code,
    string? CodeSet = null,
    string Reason = "",
    DateOnly? EffectiveFrom = null);

public sealed class DisableMasterDataResourceEndpoint(ISender sender)
    : MasterDataEndpoint<SetMasterDataResourceEnabledRequest, ResponseData<MasterDataResourceDetail>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<DisableMasterDataResourceEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(SetMasterDataResourceEnabledRequest req, CancellationToken ct)
    {
        var response = await sender.Send(
            new SetMasterDataResourceEnabledCommand(req.OrganizationId, req.EnvironmentId, req.ResourceType, req.Code, false, req.CodeSet, req.Reason, req.EffectiveFrom),
            ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class EnableMasterDataResourceEndpoint(ISender sender)
    : MasterDataEndpoint<SetMasterDataResourceEnabledRequest, ResponseData<MasterDataResourceDetail>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<EnableMasterDataResourceEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(SetMasterDataResourceEnabledRequest req, CancellationToken ct)
    {
        var response = await sender.Send(
            new SetMasterDataResourceEnabledCommand(req.OrganizationId, req.EnvironmentId, req.ResourceType, req.Code, true, req.CodeSet, req.Reason, req.EffectiveFrom),
            ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateSkuEndpoint(ISender sender)
    : MasterDataEndpoint<CreateSkuRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateSkuEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(CreateSkuRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateSkuCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.Code,
            req.Name,
            req.BaseUomCode,
            req.Category,
            req.MaterialType,
            req.BatchTrackingPolicy,
            req.SerialTrackingPolicy,
            req.ShelfLifePolicyCode,
            req.StorageConditionCode,
            req.DefaultBarcodeRuleCode,
            req.QualityRequired,
            req.ComplianceTags ?? [],
            req.IdempotencyKey,
            req.InventoryUomCode,
            req.PurchaseUomCode,
            req.SalesUomCode,
            req.ManufacturingUomCode,
            req.ProcurementType,
            req.MrpType,
            req.LotSizingPolicy,
            req.MinimumLotSize,
            req.MaximumLotSize,
            req.LotSizeMultiple,
            req.SafetyStockQuantity,
            req.ReorderPointQuantity,
            req.PlannedDeliveryTimeDays,
            req.InHouseProductionTimeDays,
            req.GoodsReceiptProcessingTimeDays,
            req.AbcClass,
            req.LifecycleStatus,
            req.PurchasingEnabled,
            req.ManufacturingEnabled,
            req.SalesEnabled), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed record CreateUnitOfMeasureRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string DimensionType,
    int Precision,
    string RoundingMode,
    string? IdempotencyKey = null);

public sealed record CreateUomConversionRequest(
    string OrganizationId,
    string EnvironmentId,
    string FromUomCode,
    string ToUomCode,
    decimal Factor,
    decimal Offset,
    int Precision,
    string RoundingMode,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null);

public sealed class CreateUnitOfMeasureEndpoint(ISender sender)
    : MasterDataEndpoint<CreateUnitOfMeasureRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateUnitOfMeasureEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(CreateUnitOfMeasureRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateUnitOfMeasureCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.Code,
            req.Name,
            req.DimensionType,
            req.Precision,
            req.RoundingMode,
            req.IdempotencyKey), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateUomConversionEndpoint(ISender sender)
    : MasterDataEndpoint<CreateUomConversionRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateUomConversionEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(CreateUomConversionRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateUomConversionCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.FromUomCode,
            req.ToUomCode,
            req.Factor,
            req.Offset,
            req.Precision,
            req.RoundingMode,
            req.EffectiveFrom,
            req.EffectiveTo), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed record CreateBusinessPartnerRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string PartnerType,
    string Name,
    IReadOnlyCollection<string>? PartnerRoles = null,
    string? TaxId = null,
    string? IdempotencyKey = null,
    string? TaxRegionCode = null,
    string? DefaultCurrencyCode = null,
    string? PaymentTermsCode = null,
    string? PrimaryAddress = null,
    string? PrimaryContactName = null,
    string? PrimaryContactEmail = null,
    string? PrimaryContactPhone = null,
    decimal? CreditLimit = null,
    string? CreditCurrencyCode = null);

public sealed record GetBusinessPartnerCreditRequest(
    [property: RouteParam] string CustomerCode,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed class CreateBusinessPartnerEndpoint(ISender sender)
    : MasterDataEndpoint<CreateBusinessPartnerRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateBusinessPartnerEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(CreateBusinessPartnerRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateBusinessPartnerCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.Code,
            req.PartnerType,
            req.Name,
            req.PartnerRoles,
            req.TaxId,
            req.IdempotencyKey,
            req.TaxRegionCode,
            req.DefaultCurrencyCode,
            req.PaymentTermsCode,
            req.PrimaryAddress,
            req.PrimaryContactName,
            req.PrimaryContactEmail,
            req.PrimaryContactPhone,
            req.CreditLimit,
            req.CreditCurrencyCode), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed class GetBusinessPartnerCreditEndpoint(ISender sender)
    : MasterDataEndpoint<GetBusinessPartnerCreditRequest, ResponseData<Nerv.IIP.Contracts.MasterData.BusinessPartnerCreditProfile>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<GetBusinessPartnerCreditEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(GetBusinessPartnerCreditRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetBusinessPartnerCreditQuery(req.OrganizationId, req.EnvironmentId, req.CustomerCode), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed record CreateWorkCenterRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    int CapacityMinutesPerDay,
    string ResourceType,
    string PlantCode,
    string LineCode,
    string DefaultCalendarCode,
    string CapacityUnit,
    bool FiniteCapacity,
    string? WorkshopCode = null,
    string? IdempotencyKey = null,
    decimal UtilizationRate = 1m,
    decimal EfficiencyRate = 1m,
    int NumberOfCapacities = 1,
    string? CostCenterCode = null,
    bool Bottleneck = false);

public sealed record CreateDepartmentRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string? ParentDepartmentCode,
    string? IdempotencyKey = null);

public sealed record CreateTeamRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string DepartmentCode,
    string ShiftCode,
    string? IdempotencyKey = null);

public sealed record AssignPersonnelSkillRequest(
    string OrganizationId,
    string EnvironmentId,
    string UserId,
    string SkillCode,
    string Level,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo);

public sealed record CreateSiteRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string Timezone,
    string? IdempotencyKey = null);

public sealed record CreateWorkshopRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string SiteCode,
    string? ManagerUserId,
    string? Description,
    string? IdempotencyKey = null);

public sealed record CreateProductionLineRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string SiteCode,
    string? WorkshopCode = null,
    string? IdempotencyKey = null);

public sealed record CreateShiftRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    int PaidMinutes,
    string? IdempotencyKey = null,
    int BreakMinutes = 0);

public sealed record CreateWorkCalendarRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string? IdempotencyKey = null,
    string Timezone = "UTC",
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null,
    string? HolidayCalendarCode = null);

public sealed class CreateDepartmentEndpoint(ISender sender)
    : MasterDataEndpoint<CreateDepartmentRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateDepartmentEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(CreateDepartmentRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateDepartmentCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.Code,
            req.Name,
            req.ParentDepartmentCode,
            req.IdempotencyKey), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateTeamEndpoint(ISender sender)
    : MasterDataEndpoint<CreateTeamRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateTeamEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(CreateTeamRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateTeamCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.Code,
            req.Name,
            req.DepartmentCode,
            req.ShiftCode,
            req.IdempotencyKey), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed class AssignPersonnelSkillEndpoint(ISender sender)
    : MasterDataEndpoint<AssignPersonnelSkillRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<AssignPersonnelSkillEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(AssignPersonnelSkillRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new AssignPersonnelSkillCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.UserId,
            req.SkillCode,
            req.Level,
            req.EffectiveFrom,
            req.EffectiveTo), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed record ListPersonnelSkillMatrixRequest(
    string OrganizationId,
    string EnvironmentId,
    string? UserId = null,
    string? SkillCode = null,
    bool IncludeDisabled = false);

public sealed class ListPersonnelSkillMatrixEndpoint(ISender sender)
    : MasterDataEndpoint<ListPersonnelSkillMatrixRequest, ResponseData<PersonnelSkillMatrixResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<ListPersonnelSkillMatrixEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(ListPersonnelSkillMatrixRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListPersonnelSkillMatrixQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.UserId,
            req.SkillCode,
            req.IncludeDisabled), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateSiteEndpoint(ISender sender)
    : MasterDataEndpoint<CreateSiteRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateSiteEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(CreateSiteRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateSiteCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.Code,
            req.Name,
            req.Timezone,
            req.IdempotencyKey), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateWorkshopEndpoint(ISender sender)
    : MasterDataEndpoint<CreateWorkshopRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateWorkshopEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(CreateWorkshopRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateWorkshopCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.Code,
            req.Name,
            req.SiteCode,
            req.ManagerUserId,
            req.Description,
            req.IdempotencyKey), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateProductionLineEndpoint(ISender sender)
    : MasterDataEndpoint<CreateProductionLineRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateProductionLineEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(CreateProductionLineRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateProductionLineCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.Code,
            req.Name,
            req.SiteCode,
            req.WorkshopCode,
            req.IdempotencyKey), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateShiftEndpoint(ISender sender)
    : MasterDataEndpoint<CreateShiftRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateShiftEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(CreateShiftRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateShiftCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.Code,
            req.Name,
            req.StartsAt,
            req.EndsAt,
            req.PaidMinutes,
            req.IdempotencyKey,
            req.BreakMinutes), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateWorkCalendarEndpoint(ISender sender)
    : MasterDataEndpoint<CreateWorkCalendarRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateWorkCalendarEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(CreateWorkCalendarRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateWorkCalendarCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.Code,
            req.Name,
            req.IdempotencyKey,
            req.Timezone,
            req.EffectiveFrom,
            req.EffectiveTo,
            req.HolidayCalendarCode), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateWorkCenterEndpoint(ISender sender)
    : MasterDataEndpoint<CreateWorkCenterRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateWorkCenterEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(CreateWorkCenterRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateWorkCenterCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.Code,
            req.Name,
            req.CapacityMinutesPerDay,
            req.ResourceType,
            req.PlantCode,
            req.LineCode,
            req.DefaultCalendarCode,
            req.CapacityUnit,
            req.FiniteCapacity,
            req.WorkshopCode,
            req.IdempotencyKey,
            req.UtilizationRate,
            req.EfficiencyRate,
            req.NumberOfCapacities,
            req.CostCenterCode,
            req.Bottleneck), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed record AddTeamMemberRequest(
    string OrganizationId,
    string EnvironmentId,
    string TeamCode,
    string UserId,
    bool IsLeader,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

public sealed record ListTeamMembersRequest(
    string OrganizationId,
    string EnvironmentId,
    string TeamCode,
    bool IncludeDisabled = false);

public sealed record RemoveTeamMemberRequest(
    string OrganizationId,
    string EnvironmentId,
    string TeamCode,
    string UserId,
    string Reason = "");

public sealed class AddTeamMemberEndpoint(ISender sender)
    : MasterDataEndpoint<AddTeamMemberRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<AddTeamMemberEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(AddTeamMemberRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new AddTeamMemberCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.TeamCode,
            req.UserId,
            req.IsLeader,
            req.EffectiveFrom,
            req.EffectiveTo), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListTeamMembersEndpoint(ISender sender)
    : MasterDataEndpoint<ListTeamMembersRequest, ResponseData<ListTeamMembersResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<ListTeamMembersEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(ListTeamMembersRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListTeamMembersQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.TeamCode,
            req.IncludeDisabled), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class RemoveTeamMemberEndpoint(ISender sender)
    : MasterDataEndpoint<RemoveTeamMemberRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<RemoveTeamMemberEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(RemoveTeamMemberRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new RemoveTeamMemberCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.TeamCode,
            req.UserId,
            req.Reason), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed record RegisterDeviceAssetRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Model,
    string LineCode,
    string WorkCenterCode,
    string AssetClassCode,
    string Manufacturer,
    string SerialNo,
    decimal? MinimumCapacity,
    decimal? MaximumCapacity,
    string CapacityUomCode,
    string Criticality,
    bool Maintainable,
    bool TelemetryEnabled,
    IReadOnlyDictionary<string, string>? ExternalReferences,
    string? IdempotencyKey = null);

public sealed class RegisterDeviceAssetEndpoint(ISender sender)
    : MasterDataEndpoint<RegisterDeviceAssetRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<RegisterDeviceAssetEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(RegisterDeviceAssetRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new RegisterDeviceAssetCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.Code,
            req.Model,
            req.LineCode,
            req.WorkCenterCode,
            req.AssetClassCode,
            req.Manufacturer,
            req.SerialNo,
            req.MinimumCapacity,
            req.MaximumCapacity,
            req.CapacityUomCode,
            req.Criticality,
            req.Maintainable,
            req.TelemetryEnabled,
            req.ExternalReferences ?? new Dictionary<string, string>(),
            req.IdempotencyKey), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed record CreateReferenceDataCodeRequest(
    string OrganizationId,
    string EnvironmentId,
    string CodeSet,
    string Code,
    string Name);

public sealed record ListProductCategoriesRequest(
    string OrganizationId,
    string EnvironmentId,
    bool? Enabled = null,
    string? Search = null,
    string? ParentCode = null,
    int Skip = 0,
    int Take = 100);

public sealed record ProductCategoryRequest(
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string CategoryCode);

public sealed record CreateProductCategoryRequest(
    string OrganizationId,
    string EnvironmentId,
    string? CategoryCode,
    string CategoryName,
    string? ParentCode,
    string? Description,
    string? IdempotencyKey = null);

public sealed record UpdateProductCategoryRequest(
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string CategoryCode,
    string CategoryName,
    string? ParentCode,
    string? Description);

public sealed record ArchiveProductCategoryRequest(
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string CategoryCode,
    string Reason = "");

public sealed record ListSkillsRequest(
    string OrganizationId,
    string EnvironmentId,
    bool? Enabled = null,
    string? Search = null,
    string? GroupName = null,
    int Skip = 0,
    int Take = 100);

public sealed record SkillRequest(
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string SkillCode);

public sealed record CreateSkillRequest(
    string OrganizationId,
    string EnvironmentId,
    string? SkillCode,
    string SkillName,
    string GroupName,
    bool RequiresCertification,
    int? ValidityMonths,
    string? Description,
    string? IdempotencyKey = null);

public sealed record UpdateSkillRequest(
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string SkillCode,
    string SkillName,
    string GroupName,
    bool RequiresCertification,
    int? ValidityMonths,
    string? Description);

public sealed record ArchiveSkillRequest(
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string SkillCode,
    string Reason = "");

public sealed class CreateReferenceDataCodeEndpoint(ISender sender)
    : MasterDataEndpoint<CreateReferenceDataCodeRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateReferenceDataCodeEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(CreateReferenceDataCodeRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateReferenceDataCodeCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.CodeSet,
            req.Code,
            req.Name), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListProductCategoriesEndpoint(ISender sender)
    : MasterDataEndpoint<ListProductCategoriesRequest, ResponseData<ProductCategoryListResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<ListProductCategoriesEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(ListProductCategoriesRequest req, CancellationToken ct)
    {
        var response = await sender.Send(
            new ListProductCategoriesQuery(req.OrganizationId, req.EnvironmentId, req.Enabled, req.Search, req.ParentCode, req.Skip, req.Take),
            ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class GetProductCategoryEndpoint(ISender sender)
    : MasterDataEndpoint<ProductCategoryRequest, ResponseData<ProductCategoryItem>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<GetProductCategoryEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(ProductCategoryRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetProductCategoryQuery(req.OrganizationId, req.EnvironmentId, req.CategoryCode), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateProductCategoryEndpoint(ISender sender)
    : MasterDataEndpoint<CreateProductCategoryRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateProductCategoryEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(CreateProductCategoryRequest req, CancellationToken ct)
    {
        var result = await sender.Send(
            new CreateProductCategoryCommand(req.OrganizationId, req.EnvironmentId, req.CategoryCode, req.CategoryName, req.ParentCode, req.Description, req.IdempotencyKey),
            ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed class UpdateProductCategoryEndpoint(ISender sender)
    : MasterDataEndpoint<UpdateProductCategoryRequest, ResponseData<ProductCategoryItem>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<UpdateProductCategoryEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(UpdateProductCategoryRequest req, CancellationToken ct)
    {
        var categoryCode = Route<string>("categoryCode") ?? req.CategoryCode;
        var response = await sender.Send(
            new UpdateProductCategoryCommand(req.OrganizationId, req.EnvironmentId, categoryCode, req.CategoryName, req.ParentCode, req.Description),
            ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ArchiveProductCategoryEndpoint(ISender sender)
    : MasterDataEndpoint<ArchiveProductCategoryRequest, ResponseData<ProductCategoryItem>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<ArchiveProductCategoryEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(ArchiveProductCategoryRequest req, CancellationToken ct)
    {
        var categoryCode = Route<string>("categoryCode") ?? req.CategoryCode;
        var response = await sender.Send(
            new ArchiveProductCategoryCommand(req.OrganizationId, req.EnvironmentId, categoryCode, req.Reason),
            ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListSkillsEndpoint(ISender sender)
    : MasterDataEndpoint<ListSkillsRequest, ResponseData<SkillListResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<ListSkillsEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(ListSkillsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(
            new ListSkillsQuery(req.OrganizationId, req.EnvironmentId, req.Enabled, req.Search, req.GroupName, req.Skip, req.Take),
            ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class GetSkillEndpoint(ISender sender)
    : MasterDataEndpoint<SkillRequest, ResponseData<SkillItem>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<GetSkillEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(SkillRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetSkillQuery(req.OrganizationId, req.EnvironmentId, req.SkillCode), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateSkillEndpoint(ISender sender)
    : MasterDataEndpoint<CreateSkillRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateSkillEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(CreateSkillRequest req, CancellationToken ct)
    {
        var result = await sender.Send(
            new CreateSkillCommand(req.OrganizationId, req.EnvironmentId, req.SkillCode, req.SkillName, req.GroupName, req.RequiresCertification, req.ValidityMonths, req.Description, req.IdempotencyKey),
            ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed class UpdateSkillEndpoint(ISender sender)
    : MasterDataEndpoint<UpdateSkillRequest, ResponseData<SkillItem>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<UpdateSkillEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(UpdateSkillRequest req, CancellationToken ct)
    {
        var skillCode = Route<string>("skillCode") ?? req.SkillCode;
        var response = await sender.Send(
            new UpdateSkillCommand(req.OrganizationId, req.EnvironmentId, skillCode, req.SkillName, req.GroupName, req.RequiresCertification, req.ValidityMonths, req.Description),
            ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ArchiveSkillEndpoint(ISender sender)
    : MasterDataEndpoint<ArchiveSkillRequest, ResponseData<SkillItem>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<ArchiveSkillEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(ArchiveSkillRequest req, CancellationToken ct)
    {
        var skillCode = Route<string>("skillCode") ?? req.SkillCode;
        var response = await sender.Send(
            new ArchiveSkillCommand(req.OrganizationId, req.EnvironmentId, skillCode, req.Reason),
            ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed record ListCodeRulesRequest(string OrganizationId, string EnvironmentId);

public sealed class ListCodeRulesEndpoint(ISender sender)
    : MasterDataEndpoint<ListCodeRulesRequest, ResponseData<ListCodeRulesResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<ListCodeRulesEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(ListCodeRulesRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListCodeRulesQuery(req.OrganizationId, req.EnvironmentId), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed record GetCodeRuleDetailRequest(string OrganizationId, string EnvironmentId, string RuleKey);

public sealed class GetCodeRuleDetailEndpoint(ISender sender)
    : MasterDataEndpoint<GetCodeRuleDetailRequest, ResponseData<CodeRuleDetailResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<GetCodeRuleDetailEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(GetCodeRuleDetailRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetCodeRuleDetailQuery(req.OrganizationId, req.EnvironmentId, req.RuleKey), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed record CreateCodeRuleVersionRequest(
    string OrganizationId,
    string EnvironmentId,
    string RuleKey,
    string DisplayName,
    string AppliesTo,
    ScopeDimension Scope,
    IReadOnlyList<CodeRuleSegment> Segments,
    bool IsActive,
    DateTimeOffset? EffectiveFromUtc,
    string CreatedBy,
    string ChangeReason);

public sealed class CreateCodeRuleVersionEndpoint(ISender sender)
    : MasterDataEndpoint<CreateCodeRuleVersionRequest, ResponseData<CodeRuleVersionResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateCodeRuleVersionEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(CreateCodeRuleVersionRequest req, CancellationToken ct)
    {
        var response = await sender.Send(
            new CreateCodeRuleVersionCommand(
                req.OrganizationId,
                req.EnvironmentId,
                req.RuleKey,
                req.DisplayName,
                req.AppliesTo,
                req.Scope,
                req.Segments,
                req.IsActive,
                req.EffectiveFromUtc ?? DateTimeOffset.UtcNow,
                req.CreatedBy,
                req.ChangeReason),
            ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed record PreviewCodeRuleRequest(
    string OrganizationId,
    string EnvironmentId,
    string RuleKey,
    IReadOnlyList<CodeRuleSegment> Segments,
    IReadOnlyDictionary<string, string>? Fields,
    string SiteCode = "");

public sealed class PreviewCodeRuleEndpoint(ISender sender)
    : MasterDataEndpoint<PreviewCodeRuleRequest, ResponseData<CodeRulePreviewResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<PreviewCodeRuleEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(PreviewCodeRuleRequest req, CancellationToken ct)
    {
        var response = await sender.Send(
            new PreviewCodeRuleCommand(req.OrganizationId, req.EnvironmentId, req.RuleKey, req.Segments, req.Fields, req.SiteCode),
            ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ResolveMasterDataReferencesEndpoint(ISender sender)
    : MasterDataEndpoint<ResolveMasterDataReferencesQuery, ResponseData<ResolveMasterDataReferencesResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<ResolveMasterDataReferencesEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(ResolveMasterDataReferencesQuery req, CancellationToken ct)
    {
        var response = await sender.Send(req, ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ValidateMasterDataReferencesEndpoint(ISender sender)
    : MasterDataEndpoint<ValidateMasterDataReferencesQuery, ResponseData<ValidateMasterDataReferencesResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<ValidateMasterDataReferencesEndpoint>();
        ConfigureMasterDataContract(contract);
    }

    public override async Task HandleAsync(ValidateMasterDataReferencesQuery req, CancellationToken ct)
    {
        var response = await sender.Send(req, ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed record MasterDataEndpointContract(
    Type EndpointType,
    string HttpMethod,
    string Route,
    string PermissionCode,
    string OperationId);

public static class MasterDataEndpointContracts
{
    public static readonly IReadOnlyCollection<MasterDataEndpointContract> All =
    [
        new(typeof(ListMasterDataResourcesEndpoint), "GET", "/api/business/v1/master-data/resources", BusinessPermissionCodes.MasterDataResourcesRead, "listBusinessMasterDataResources"),
        new(typeof(GetMasterDataResourceDetailEndpoint), "GET", "/api/business/v1/master-data/resources/{ResourceType}/{Code}", BusinessPermissionCodes.MasterDataResourcesRead, "getBusinessMasterDataResourceDetail"),
        new(typeof(UpdateMasterDataResourceEndpoint), "PATCH", "/api/business/v1/master-data/resources/{ResourceType}/{Code}", BusinessPermissionCodes.MasterDataResourcesManage, "updateBusinessMasterDataResource"),
        new(typeof(DisableMasterDataResourceEndpoint), "POST", "/api/business/v1/master-data/resources/{ResourceType}/{Code}/disable", BusinessPermissionCodes.MasterDataResourcesManage, "disableBusinessMasterDataResource"),
        new(typeof(EnableMasterDataResourceEndpoint), "POST", "/api/business/v1/master-data/resources/{ResourceType}/{Code}/enable", BusinessPermissionCodes.MasterDataResourcesManage, "enableBusinessMasterDataResource"),
        new(typeof(CreateSkuEndpoint), "POST", "/api/business/v1/master-data/skus", BusinessPermissionCodes.MasterDataProductsManage, "createBusinessMasterDataSku"),
        new(typeof(CreateUnitOfMeasureEndpoint), "POST", "/api/business/v1/master-data/units-of-measure", BusinessPermissionCodes.MasterDataProductsManage, "createBusinessMasterDataUnitOfMeasure"),
        new(typeof(CreateUomConversionEndpoint), "POST", "/api/business/v1/master-data/uom-conversions", BusinessPermissionCodes.MasterDataProductsManage, "createBusinessMasterDataUomConversion"),
        new(typeof(CreateBusinessPartnerEndpoint), "POST", "/api/business/v1/master-data/partners", BusinessPermissionCodes.MasterDataPartnersManage, "createBusinessMasterDataPartner"),
        new(typeof(GetBusinessPartnerCreditEndpoint), "GET", "/api/business/v1/master-data/partners/{customerCode}/credit", BusinessPermissionCodes.MasterDataPartnersRead, "getBusinessMasterDataPartnerCredit"),
        new(typeof(CreateDepartmentEndpoint), "POST", "/api/business/v1/master-data/departments", BusinessPermissionCodes.MasterDataResourcesManage, "createBusinessMasterDataDepartment"),
        new(typeof(CreateTeamEndpoint), "POST", "/api/business/v1/master-data/teams", BusinessPermissionCodes.MasterDataResourcesManage, "createBusinessMasterDataTeam"),
        new(typeof(CreateWorkshopEndpoint), "POST", "/api/business/v1/master-data/workshops", BusinessPermissionCodes.MasterDataResourcesManage, "createBusinessMasterDataWorkshop"),
        new(typeof(AddTeamMemberEndpoint), "POST", "/api/business/v1/master-data/teams/{teamCode}/members", BusinessPermissionCodes.MasterDataResourcesManage, "addBusinessMasterDataTeamMember"),
        new(typeof(ListTeamMembersEndpoint), "GET", "/api/business/v1/master-data/teams/{teamCode}/members", BusinessPermissionCodes.MasterDataResourcesRead, "listBusinessMasterDataTeamMembers"),
        new(typeof(RemoveTeamMemberEndpoint), "DELETE", "/api/business/v1/master-data/teams/{teamCode}/members/{userId}", BusinessPermissionCodes.MasterDataResourcesManage, "removeBusinessMasterDataTeamMember"),
        new(typeof(AssignPersonnelSkillEndpoint), "POST", "/api/business/v1/master-data/personnel-skills", BusinessPermissionCodes.MasterDataResourcesManage, "assignBusinessMasterDataPersonnelSkill"),
        new(typeof(ListPersonnelSkillMatrixEndpoint), "GET", "/api/business/v1/master-data/personnel-skills/matrix", BusinessPermissionCodes.MasterDataResourcesRead, "listBusinessMasterDataPersonnelSkillMatrix"),
        new(typeof(CreateSiteEndpoint), "POST", "/api/business/v1/master-data/sites", BusinessPermissionCodes.MasterDataResourcesManage, "createBusinessMasterDataSite"),
        new(typeof(CreateProductionLineEndpoint), "POST", "/api/business/v1/master-data/production-lines", BusinessPermissionCodes.MasterDataResourcesManage, "createBusinessMasterDataProductionLine"),
        new(typeof(CreateShiftEndpoint), "POST", "/api/business/v1/master-data/shifts", BusinessPermissionCodes.MasterDataResourcesManage, "createBusinessMasterDataShift"),
        new(typeof(CreateWorkCalendarEndpoint), "POST", "/api/business/v1/master-data/work-calendars", BusinessPermissionCodes.MasterDataResourcesManage, "createBusinessMasterDataWorkCalendar"),
        new(typeof(CreateWorkCenterEndpoint), "POST", "/api/business/v1/master-data/work-centers", BusinessPermissionCodes.MasterDataResourcesManage, "createBusinessMasterDataWorkCenter"),
        new(typeof(RegisterDeviceAssetEndpoint), "POST", "/api/business/v1/master-data/device-assets", BusinessPermissionCodes.MasterDataResourcesManage, "registerBusinessMasterDataDeviceAsset"),
        new(typeof(CreateReferenceDataCodeEndpoint), "POST", "/api/business/v1/master-data/reference-data", BusinessPermissionCodes.MasterDataResourcesManage, "createBusinessMasterDataReferenceDataCode"),
        new(typeof(ListProductCategoriesEndpoint), "GET", "/api/business/v1/master-data/product-categories", BusinessPermissionCodes.MasterDataProductsRead, "listBusinessMasterDataProductCategories"),
        new(typeof(GetProductCategoryEndpoint), "GET", "/api/business/v1/master-data/product-categories/{categoryCode}", BusinessPermissionCodes.MasterDataProductsRead, "getBusinessMasterDataProductCategory"),
        new(typeof(CreateProductCategoryEndpoint), "POST", "/api/business/v1/master-data/product-categories", BusinessPermissionCodes.MasterDataProductsManage, "createBusinessMasterDataProductCategory"),
        new(typeof(UpdateProductCategoryEndpoint), "PUT", "/api/business/v1/master-data/product-categories/{categoryCode}", BusinessPermissionCodes.MasterDataProductsManage, "updateBusinessMasterDataProductCategory"),
        new(typeof(ArchiveProductCategoryEndpoint), "POST", "/api/business/v1/master-data/product-categories/{categoryCode}/archive", BusinessPermissionCodes.MasterDataProductsManage, "archiveBusinessMasterDataProductCategory"),
        new(typeof(ListSkillsEndpoint), "GET", "/api/business/v1/master-data/skills", BusinessPermissionCodes.MasterDataResourcesRead, "listBusinessMasterDataSkills"),
        new(typeof(GetSkillEndpoint), "GET", "/api/business/v1/master-data/skills/{skillCode}", BusinessPermissionCodes.MasterDataResourcesRead, "getBusinessMasterDataSkill"),
        new(typeof(CreateSkillEndpoint), "POST", "/api/business/v1/master-data/skills", BusinessPermissionCodes.MasterDataResourcesManage, "createBusinessMasterDataSkill"),
        new(typeof(UpdateSkillEndpoint), "PUT", "/api/business/v1/master-data/skills/{skillCode}", BusinessPermissionCodes.MasterDataResourcesManage, "updateBusinessMasterDataSkill"),
        new(typeof(ArchiveSkillEndpoint), "POST", "/api/business/v1/master-data/skills/{skillCode}/archive", BusinessPermissionCodes.MasterDataResourcesManage, "archiveBusinessMasterDataSkill"),
        new(typeof(ListCodeRulesEndpoint), "GET", "/api/business/v1/master-data/code-rules", BusinessPermissionCodes.MasterDataResourcesRead, "listBusinessMasterDataCodeRules"),
        new(typeof(GetCodeRuleDetailEndpoint), "GET", "/api/business/v1/master-data/code-rules/{ruleKey}", BusinessPermissionCodes.MasterDataResourcesRead, "getBusinessMasterDataCodeRule"),
        new(typeof(CreateCodeRuleVersionEndpoint), "POST", "/api/business/v1/master-data/code-rules/{ruleKey}/versions", BusinessPermissionCodes.MasterDataResourcesManage, "createBusinessMasterDataCodeRuleVersion"),
        new(typeof(PreviewCodeRuleEndpoint), "POST", "/api/business/v1/master-data/code-rules/{ruleKey}/preview", BusinessPermissionCodes.MasterDataResourcesRead, "previewBusinessMasterDataCodeRule"),
        new(typeof(ResolveMasterDataReferencesEndpoint), "POST", "/api/business/v1/master-data/references/resolve", BusinessPermissionCodes.MasterDataResourcesRead, "resolveBusinessMasterDataReferences"),
        new(typeof(ValidateMasterDataReferencesEndpoint), "POST", "/api/business/v1/master-data/references/validate", BusinessPermissionCodes.MasterDataResourcesRead, "validateBusinessMasterDataReferences"),
    ];

    public static MasterDataEndpointContract Get<TEndpoint>()
    {
        return All.Single(x => x.EndpointType == typeof(TEndpoint));
    }

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out MasterDataEndpointContract? contract)
    {
        contract = All.SingleOrDefault(x => x.EndpointType == endpointType);
        return contract is not null;
    }
}
