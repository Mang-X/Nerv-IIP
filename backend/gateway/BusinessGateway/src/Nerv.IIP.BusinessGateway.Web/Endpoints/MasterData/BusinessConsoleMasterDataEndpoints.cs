using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.MasterData;

[Tags("Business Console MasterData")]
[HttpGet("/api/business-console/v1/master-data/resources")]
[BusinessGatewayOperationId("listBusinessConsoleMasterDataResources")]
public sealed class ListBusinessConsoleMasterDataResourcesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListResourcesRequest, BusinessConsoleResourceListResponse>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesRead)
{
    protected override string OrganizationId(BusinessConsoleListResourcesRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListResourcesRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceListResponse> ForwardAsync(
        BusinessConsoleListResourcesRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.ListResourcesAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpGet("/api/business-console/v1/master-data/skus")]
[BusinessGatewayOperationId("listBusinessConsoleSkus")]
public sealed class ListBusinessConsoleSkusEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListSkusRequest, BusinessConsoleResourceListResponse>(
        auth,
        BusinessGatewayPermissions.MasterDataProductsRead)
{
    protected override string OrganizationId(BusinessConsoleListSkusRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListSkusRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceListResponse> ForwardAsync(
        BusinessConsoleListSkusRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.ListResourcesAsync(
            tokenProvider.BearerToken,
            new BusinessConsoleListResourcesRequest(
                request.OrganizationId,
                request.EnvironmentId,
                "sku",
                request.IncludeDisabled,
                request.Take),
            cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/skus")]
[BusinessGatewayOperationId("createBusinessConsoleSku")]
public sealed class CreateBusinessConsoleSkuEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateSkuRequest, BusinessConsoleResourceItem>(
        auth,
        BusinessGatewayPermissions.MasterDataProductsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateSkuRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateSkuRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceItem> ForwardAsync(
        BusinessConsoleCreateSkuRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.CreateSkuAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public sealed class BusinessConsoleCreateSkuRequestValidator : Validator<BusinessConsoleCreateSkuRequest>
{
    public BusinessConsoleCreateSkuRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).MaximumLength(64);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BaseUomCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Category).NotEmpty().MaximumLength(100);
        RuleFor(x => x.MaterialType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BatchTrackingPolicy).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SerialTrackingPolicy).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ShelfLifePolicyCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.StorageConditionCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DefaultBarcodeRuleCode).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleCreateBusinessPartnerRequestValidator : Validator<BusinessConsoleCreateBusinessPartnerRequest>
{
    public BusinessConsoleCreateBusinessPartnerRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PartnerType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public sealed class BusinessConsoleCreateUnitOfMeasureRequestValidator : Validator<BusinessConsoleCreateUnitOfMeasureRequest>
{
    public BusinessConsoleCreateUnitOfMeasureRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DimensionType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Precision).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RoundingMode).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleCreateUomConversionRequestValidator : Validator<BusinessConsoleCreateUomConversionRequest>
{
    public BusinessConsoleCreateUomConversionRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.FromUomCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ToUomCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Factor).GreaterThan(0);
        RuleFor(x => x.Precision).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RoundingMode).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleCreateSiteRequestValidator : Validator<BusinessConsoleCreateSiteRequest>
{
    public BusinessConsoleCreateSiteRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Timezone).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleCreateProductionLineRequestValidator : Validator<BusinessConsoleCreateProductionLineRequest>
{
    public BusinessConsoleCreateProductionLineRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleCreateWorkCenterRequestValidator : Validator<BusinessConsoleCreateWorkCenterRequest>
{
    public BusinessConsoleCreateWorkCenterRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CapacityMinutesPerDay).GreaterThan(0);
        RuleFor(x => x.ResourceType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PlantCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LineCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DefaultCalendarCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CapacityUnit).NotEmpty().MaximumLength(50);
    }
}

public sealed class BusinessConsoleRegisterDeviceAssetRequestValidator : Validator<BusinessConsoleRegisterDeviceAssetRequest>
{
    public BusinessConsoleRegisterDeviceAssetRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Model).NotEmpty().MaximumLength(200);
        RuleFor(x => x.LineCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkCenterCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AssetClassCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Manufacturer).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SerialNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CapacityUomCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Criticality).NotEmpty().MaximumLength(100);
        RuleFor(x => x.MaximumCapacity).GreaterThanOrEqualTo(x => x.MinimumCapacity)
            .When(x => x.MinimumCapacity.HasValue && x.MaximumCapacity.HasValue);
    }
}

public sealed class BusinessConsoleCreateShiftRequestValidator : Validator<BusinessConsoleCreateShiftRequest>
{
    public BusinessConsoleCreateShiftRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PaidMinutes).GreaterThan(0);
    }
}

public sealed class BusinessConsoleCreateWorkCalendarRequestValidator : Validator<BusinessConsoleCreateWorkCalendarRequest>
{
    public BusinessConsoleCreateWorkCalendarRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public sealed class BusinessConsoleCreateTeamRequestValidator : Validator<BusinessConsoleCreateTeamRequest>
{
    public BusinessConsoleCreateTeamRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DepartmentCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ShiftCode).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleCreateDepartmentRequestValidator : Validator<BusinessConsoleCreateDepartmentRequest>
{
    public BusinessConsoleCreateDepartmentRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ParentDepartmentCode).MaximumLength(100);
    }
}

public sealed class BusinessConsoleAssignPersonnelSkillRequestValidator : Validator<BusinessConsoleAssignPersonnelSkillRequest>
{
    public BusinessConsoleAssignPersonnelSkillRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkillCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Level).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EffectiveTo).GreaterThanOrEqualTo(x => x.EffectiveFrom);
    }
}

public sealed class BusinessConsoleCreateReferenceDataCodeRequestValidator : Validator<BusinessConsoleCreateReferenceDataCodeRequest>
{
    public BusinessConsoleCreateReferenceDataCodeRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CodeSet).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/business-partners")]
[BusinessGatewayOperationId("createBusinessConsoleBusinessPartner")]
public sealed class CreateBusinessConsoleBusinessPartnerEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateBusinessPartnerRequest, BusinessConsoleResourceItem>(
        auth,
        BusinessGatewayPermissions.MasterDataPartnersManage)
{
    protected override string OrganizationId(BusinessConsoleCreateBusinessPartnerRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateBusinessPartnerRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceItem> ForwardAsync(
        BusinessConsoleCreateBusinessPartnerRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.CreateBusinessPartnerAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/units-of-measure")]
[BusinessGatewayOperationId("createBusinessConsoleUnitOfMeasure")]
public sealed class CreateBusinessConsoleUnitOfMeasureEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateUnitOfMeasureRequest, BusinessConsoleResourceItem>(
        auth,
        BusinessGatewayPermissions.MasterDataProductsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateUnitOfMeasureRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateUnitOfMeasureRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceItem> ForwardAsync(
        BusinessConsoleCreateUnitOfMeasureRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.CreateUnitOfMeasureAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/uom-conversions")]
[BusinessGatewayOperationId("createBusinessConsoleUomConversion")]
public sealed class CreateBusinessConsoleUomConversionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateUomConversionRequest, BusinessConsoleResourceItem>(
        auth,
        BusinessGatewayPermissions.MasterDataProductsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateUomConversionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateUomConversionRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceItem> ForwardAsync(
        BusinessConsoleCreateUomConversionRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.CreateUomConversionAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/sites")]
[BusinessGatewayOperationId("createBusinessConsoleSite")]
public sealed class CreateBusinessConsoleSiteEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateSiteRequest, BusinessConsoleResourceItem>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesManage)
{
    protected override string OrganizationId(BusinessConsoleCreateSiteRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateSiteRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceItem> ForwardAsync(
        BusinessConsoleCreateSiteRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.CreateSiteAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/production-lines")]
[BusinessGatewayOperationId("createBusinessConsoleProductionLine")]
public sealed class CreateBusinessConsoleProductionLineEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateProductionLineRequest, BusinessConsoleResourceItem>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesManage)
{
    protected override string OrganizationId(BusinessConsoleCreateProductionLineRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateProductionLineRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceItem> ForwardAsync(
        BusinessConsoleCreateProductionLineRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.CreateProductionLineAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/work-centers")]
[BusinessGatewayOperationId("createBusinessConsoleWorkCenter")]
public sealed class CreateBusinessConsoleWorkCenterEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateWorkCenterRequest, BusinessConsoleResourceItem>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesManage)
{
    protected override string OrganizationId(BusinessConsoleCreateWorkCenterRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateWorkCenterRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceItem> ForwardAsync(
        BusinessConsoleCreateWorkCenterRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.CreateWorkCenterAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/device-assets")]
[BusinessGatewayOperationId("registerBusinessConsoleDeviceAsset")]
public sealed class RegisterBusinessConsoleDeviceAssetEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleRegisterDeviceAssetRequest, BusinessConsoleResourceItem>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesManage)
{
    protected override string OrganizationId(BusinessConsoleRegisterDeviceAssetRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleRegisterDeviceAssetRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceItem> ForwardAsync(
        BusinessConsoleRegisterDeviceAssetRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.RegisterDeviceAssetAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/shifts")]
[BusinessGatewayOperationId("createBusinessConsoleShift")]
public sealed class CreateBusinessConsoleShiftEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateShiftRequest, BusinessConsoleResourceItem>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesManage)
{
    protected override string OrganizationId(BusinessConsoleCreateShiftRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateShiftRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceItem> ForwardAsync(
        BusinessConsoleCreateShiftRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.CreateShiftAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/work-calendars")]
[BusinessGatewayOperationId("createBusinessConsoleWorkCalendar")]
public sealed class CreateBusinessConsoleWorkCalendarEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateWorkCalendarRequest, BusinessConsoleResourceItem>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesManage)
{
    protected override string OrganizationId(BusinessConsoleCreateWorkCalendarRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateWorkCalendarRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceItem> ForwardAsync(
        BusinessConsoleCreateWorkCalendarRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.CreateWorkCalendarAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/teams")]
[BusinessGatewayOperationId("createBusinessConsoleTeam")]
public sealed class CreateBusinessConsoleTeamEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateTeamRequest, BusinessConsoleResourceItem>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesManage)
{
    protected override string OrganizationId(BusinessConsoleCreateTeamRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateTeamRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceItem> ForwardAsync(
        BusinessConsoleCreateTeamRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.CreateTeamAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/departments")]
[BusinessGatewayOperationId("createBusinessConsoleDepartment")]
public sealed class CreateBusinessConsoleDepartmentEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateDepartmentRequest, BusinessConsoleResourceItem>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesManage)
{
    protected override string OrganizationId(BusinessConsoleCreateDepartmentRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateDepartmentRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceItem> ForwardAsync(
        BusinessConsoleCreateDepartmentRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.CreateDepartmentAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/personnel-skills")]
[BusinessGatewayOperationId("assignBusinessConsolePersonnelSkill")]
public sealed class AssignBusinessConsolePersonnelSkillEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleAssignPersonnelSkillRequest, BusinessConsoleResourceItem>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesManage)
{
    protected override string OrganizationId(BusinessConsoleAssignPersonnelSkillRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleAssignPersonnelSkillRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceItem> ForwardAsync(
        BusinessConsoleAssignPersonnelSkillRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.AssignPersonnelSkillAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/reference-data")]
[BusinessGatewayOperationId("createBusinessConsoleReferenceDataCode")]
public sealed class CreateBusinessConsoleReferenceDataCodeEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateReferenceDataCodeRequest, BusinessConsoleResourceItem>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesManage)
{
    protected override string OrganizationId(BusinessConsoleCreateReferenceDataCodeRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateReferenceDataCodeRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceItem> ForwardAsync(
        BusinessConsoleCreateReferenceDataCodeRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.CreateReferenceDataCodeAsync(tokenProvider.BearerToken, request, cancellationToken);
}
