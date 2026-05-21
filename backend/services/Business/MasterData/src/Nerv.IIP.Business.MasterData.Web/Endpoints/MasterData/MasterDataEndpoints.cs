using FastEndpoints;
using Nerv.IIP.Business.MasterData.Web.Application.Auth;
using Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;
using Nerv.IIP.Business.MasterData.Web.Application.Queries;
using NetCorePal.Extensions.Dto;
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

public sealed record ListMasterDataResourcesRequest(
    string OrganizationId,
    string EnvironmentId,
    string ResourceType,
    bool IncludeDisabled = false,
    int Take = 100);

public sealed record CreateSkuRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
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
    IReadOnlyCollection<string>? ComplianceTags);

[Tags("Business MasterData")]
[HttpGet("/api/business/v1/master-data/resources")]
public sealed class ListMasterDataResourcesEndpoint(ISender sender)
    : Endpoint<ListMasterDataResourcesRequest, ResponseData<ListMasterDataResourcesResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<ListMasterDataResourcesEndpoint>();
        Permissions(contract.PermissionCode);
        Description(builder => builder.WithName(contract.OperationId));
    }

    public override async Task HandleAsync(ListMasterDataResourcesRequest req, CancellationToken ct)
    {
        var response = await sender.Send(
            new ListMasterDataResourcesQuery(req.OrganizationId, req.EnvironmentId, req.ResourceType, req.IncludeDisabled, req.Take),
            ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

[Tags("Business MasterData")]
[HttpPost("/api/business/v1/master-data/skus")]
public sealed class CreateSkuEndpoint(ISender sender)
    : Endpoint<CreateSkuRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateSkuEndpoint>();
        Permissions(contract.PermissionCode);
        Description(builder => builder.WithName(contract.OperationId));
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
            req.ComplianceTags ?? []), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed record CreateUnitOfMeasureRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Name,
    string DimensionType,
    int Precision,
    string RoundingMode);

public sealed record CreateUomConversionRequest(
    string OrganizationId,
    string EnvironmentId,
    string FromUomCode,
    string ToUomCode,
    decimal Factor,
    decimal Offset,
    int Precision,
    string RoundingMode,
    DateOnly EffectiveFrom);

[Tags("Business MasterData")]
[HttpPost("/api/business/v1/master-data/units-of-measure")]
public sealed class CreateUnitOfMeasureEndpoint(ISender sender)
    : Endpoint<CreateUnitOfMeasureRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateUnitOfMeasureEndpoint>();
        Permissions(contract.PermissionCode);
        Description(builder => builder.WithName(contract.OperationId));
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
            req.RoundingMode), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

[Tags("Business MasterData")]
[HttpPost("/api/business/v1/master-data/uom-conversions")]
public sealed class CreateUomConversionEndpoint(ISender sender)
    : Endpoint<CreateUomConversionRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateUomConversionEndpoint>();
        Permissions(contract.PermissionCode);
        Description(builder => builder.WithName(contract.OperationId));
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
            req.EffectiveFrom), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed record CreateBusinessPartnerRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string PartnerType,
    string Name);

[Tags("Business MasterData")]
[HttpPost("/api/business/v1/master-data/partners")]
public sealed class CreateBusinessPartnerEndpoint(ISender sender)
    : Endpoint<CreateBusinessPartnerRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateBusinessPartnerEndpoint>();
        Permissions(contract.PermissionCode);
        Description(builder => builder.WithName(contract.OperationId));
    }

    public override async Task HandleAsync(CreateBusinessPartnerRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateBusinessPartnerCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.Code,
            req.PartnerType,
            req.Name), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed record CreateWorkCenterRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Name,
    int CapacityMinutesPerDay,
    string ResourceType,
    string PlantCode,
    string LineCode,
    string DefaultCalendarCode,
    string CapacityUnit,
    bool FiniteCapacity);

public sealed record CreateDepartmentRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Name,
    string? ParentDepartmentCode);

public sealed record CreateTeamRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Name,
    string DepartmentCode,
    string ShiftCode);

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
    string Code,
    string Name,
    string Timezone);

public sealed record CreateProductionLineRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Name,
    string SiteCode);

public sealed record CreateShiftRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Name,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    int PaidMinutes);

public sealed record CreateWorkCalendarRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Name);

[Tags("Business MasterData")]
[HttpPost("/api/business/v1/master-data/departments")]
public sealed class CreateDepartmentEndpoint(ISender sender)
    : Endpoint<CreateDepartmentRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateDepartmentEndpoint>();
        Permissions(contract.PermissionCode);
        Description(builder => builder.WithName(contract.OperationId));
    }

    public override async Task HandleAsync(CreateDepartmentRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateDepartmentCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.Code,
            req.Name,
            req.ParentDepartmentCode), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

[Tags("Business MasterData")]
[HttpPost("/api/business/v1/master-data/teams")]
public sealed class CreateTeamEndpoint(ISender sender)
    : Endpoint<CreateTeamRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateTeamEndpoint>();
        Permissions(contract.PermissionCode);
        Description(builder => builder.WithName(contract.OperationId));
    }

    public override async Task HandleAsync(CreateTeamRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateTeamCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.Code,
            req.Name,
            req.DepartmentCode,
            req.ShiftCode), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

[Tags("Business MasterData")]
[HttpPost("/api/business/v1/master-data/personnel-skills")]
public sealed class AssignPersonnelSkillEndpoint(ISender sender)
    : Endpoint<AssignPersonnelSkillRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<AssignPersonnelSkillEndpoint>();
        Permissions(contract.PermissionCode);
        Description(builder => builder.WithName(contract.OperationId));
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

[Tags("Business MasterData")]
[HttpPost("/api/business/v1/master-data/sites")]
public sealed class CreateSiteEndpoint(ISender sender)
    : Endpoint<CreateSiteRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateSiteEndpoint>();
        Permissions(contract.PermissionCode);
        Description(builder => builder.WithName(contract.OperationId));
    }

    public override async Task HandleAsync(CreateSiteRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateSiteCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.Code,
            req.Name,
            req.Timezone), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

[Tags("Business MasterData")]
[HttpPost("/api/business/v1/master-data/production-lines")]
public sealed class CreateProductionLineEndpoint(ISender sender)
    : Endpoint<CreateProductionLineRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateProductionLineEndpoint>();
        Permissions(contract.PermissionCode);
        Description(builder => builder.WithName(contract.OperationId));
    }

    public override async Task HandleAsync(CreateProductionLineRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateProductionLineCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.Code,
            req.Name,
            req.SiteCode), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

[Tags("Business MasterData")]
[HttpPost("/api/business/v1/master-data/shifts")]
public sealed class CreateShiftEndpoint(ISender sender)
    : Endpoint<CreateShiftRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateShiftEndpoint>();
        Permissions(contract.PermissionCode);
        Description(builder => builder.WithName(contract.OperationId));
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
            req.PaidMinutes), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

[Tags("Business MasterData")]
[HttpPost("/api/business/v1/master-data/work-calendars")]
public sealed class CreateWorkCalendarEndpoint(ISender sender)
    : Endpoint<CreateWorkCalendarRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateWorkCalendarEndpoint>();
        Permissions(contract.PermissionCode);
        Description(builder => builder.WithName(contract.OperationId));
    }

    public override async Task HandleAsync(CreateWorkCalendarRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateWorkCalendarCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.Code,
            req.Name), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

[Tags("Business MasterData")]
[HttpPost("/api/business/v1/master-data/work-centers")]
public sealed class CreateWorkCenterEndpoint(ISender sender)
    : Endpoint<CreateWorkCenterRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateWorkCenterEndpoint>();
        Permissions(contract.PermissionCode);
        Description(builder => builder.WithName(contract.OperationId));
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
            req.FiniteCapacity), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed record RegisterDeviceAssetRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
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
    IReadOnlyDictionary<string, string>? ExternalReferences);

[Tags("Business MasterData")]
[HttpPost("/api/business/v1/master-data/device-assets")]
public sealed class RegisterDeviceAssetEndpoint(ISender sender)
    : Endpoint<RegisterDeviceAssetRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<RegisterDeviceAssetEndpoint>();
        Permissions(contract.PermissionCode);
        Description(builder => builder.WithName(contract.OperationId));
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
            req.ExternalReferences ?? new Dictionary<string, string>()), ct);
        await Send.OkAsync(ToResponse(result).AsResponseData(), cancellation: ct);
    }
}

public sealed record CreateReferenceDataCodeRequest(
    string OrganizationId,
    string EnvironmentId,
    string CodeSet,
    string Code,
    string Name);

[Tags("Business MasterData")]
[HttpPost("/api/business/v1/master-data/reference-data")]
public sealed class CreateReferenceDataCodeEndpoint(ISender sender)
    : Endpoint<CreateReferenceDataCodeRequest, ResponseData<MasterDataResourceResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<CreateReferenceDataCodeEndpoint>();
        Permissions(contract.PermissionCode);
        Description(builder => builder.WithName(contract.OperationId));
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

[Tags("Business MasterData")]
[HttpPost("/api/business/v1/master-data/references/resolve")]
public sealed class ResolveMasterDataReferencesEndpoint(ISender sender)
    : Endpoint<ResolveMasterDataReferencesQuery, ResponseData<ResolveMasterDataReferencesResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<ResolveMasterDataReferencesEndpoint>();
        Permissions(contract.PermissionCode);
        Description(builder => builder.WithName(contract.OperationId));
    }

    public override async Task HandleAsync(ResolveMasterDataReferencesQuery req, CancellationToken ct)
    {
        var response = await sender.Send(req, ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

[Tags("Business MasterData")]
[HttpPost("/api/business/v1/master-data/references/validate")]
public sealed class ValidateMasterDataReferencesEndpoint(ISender sender)
    : Endpoint<ValidateMasterDataReferencesQuery, ResponseData<ValidateMasterDataReferencesResponse>>
{
    public override void Configure()
    {
        var contract = MasterDataEndpointContracts.Get<ValidateMasterDataReferencesEndpoint>();
        Permissions(contract.PermissionCode);
        Description(builder => builder.WithName(contract.OperationId));
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
        new(typeof(CreateSkuEndpoint), "POST", "/api/business/v1/master-data/skus", BusinessPermissionCodes.MasterDataProductsManage, "createBusinessMasterDataSku"),
        new(typeof(CreateUnitOfMeasureEndpoint), "POST", "/api/business/v1/master-data/units-of-measure", BusinessPermissionCodes.MasterDataProductsManage, "createBusinessMasterDataUnitOfMeasure"),
        new(typeof(CreateUomConversionEndpoint), "POST", "/api/business/v1/master-data/uom-conversions", BusinessPermissionCodes.MasterDataProductsManage, "createBusinessMasterDataUomConversion"),
        new(typeof(CreateBusinessPartnerEndpoint), "POST", "/api/business/v1/master-data/partners", BusinessPermissionCodes.MasterDataPartnersManage, "createBusinessMasterDataPartner"),
        new(typeof(CreateDepartmentEndpoint), "POST", "/api/business/v1/master-data/departments", BusinessPermissionCodes.MasterDataResourcesManage, "createBusinessMasterDataDepartment"),
        new(typeof(CreateTeamEndpoint), "POST", "/api/business/v1/master-data/teams", BusinessPermissionCodes.MasterDataResourcesManage, "createBusinessMasterDataTeam"),
        new(typeof(AssignPersonnelSkillEndpoint), "POST", "/api/business/v1/master-data/personnel-skills", BusinessPermissionCodes.MasterDataResourcesManage, "assignBusinessMasterDataPersonnelSkill"),
        new(typeof(CreateSiteEndpoint), "POST", "/api/business/v1/master-data/sites", BusinessPermissionCodes.MasterDataResourcesManage, "createBusinessMasterDataSite"),
        new(typeof(CreateProductionLineEndpoint), "POST", "/api/business/v1/master-data/production-lines", BusinessPermissionCodes.MasterDataResourcesManage, "createBusinessMasterDataProductionLine"),
        new(typeof(CreateShiftEndpoint), "POST", "/api/business/v1/master-data/shifts", BusinessPermissionCodes.MasterDataResourcesManage, "createBusinessMasterDataShift"),
        new(typeof(CreateWorkCalendarEndpoint), "POST", "/api/business/v1/master-data/work-calendars", BusinessPermissionCodes.MasterDataResourcesManage, "createBusinessMasterDataWorkCalendar"),
        new(typeof(CreateWorkCenterEndpoint), "POST", "/api/business/v1/master-data/work-centers", BusinessPermissionCodes.MasterDataResourcesManage, "createBusinessMasterDataWorkCenter"),
        new(typeof(RegisterDeviceAssetEndpoint), "POST", "/api/business/v1/master-data/device-assets", BusinessPermissionCodes.MasterDataResourcesManage, "registerBusinessMasterDataDeviceAsset"),
        new(typeof(CreateReferenceDataCodeEndpoint), "POST", "/api/business/v1/master-data/reference-data", BusinessPermissionCodes.MasterDataResourcesManage, "createBusinessMasterDataReferenceDataCode"),
        new(typeof(ResolveMasterDataReferencesEndpoint), "POST", "/api/business/v1/master-data/references/resolve", BusinessPermissionCodes.MasterDataResourcesRead, "resolveBusinessMasterDataReferences"),
        new(typeof(ValidateMasterDataReferencesEndpoint), "POST", "/api/business/v1/master-data/references/validate", BusinessPermissionCodes.MasterDataResourcesRead, "validateBusinessMasterDataReferences"),
    ];

    public static MasterDataEndpointContract Get<TEndpoint>()
    {
        return All.Single(x => x.EndpointType == typeof(TEndpoint));
    }
}
