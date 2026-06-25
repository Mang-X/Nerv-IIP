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
                request.Skip,
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

[Tags("Business Console MasterData")]
[HttpGet("/api/business-console/v1/master-data/product-categories")]
[BusinessGatewayOperationId("listBusinessConsoleProductCategories")]
public sealed class ListBusinessConsoleProductCategoriesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListProductCategoriesRequest, BusinessConsoleProductCategoryListResponse>(
        auth,
        BusinessGatewayPermissions.MasterDataProductsRead)
{
    protected override string OrganizationId(BusinessConsoleListProductCategoriesRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListProductCategoriesRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleProductCategoryListResponse> ForwardAsync(
        BusinessConsoleListProductCategoriesRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.ListProductCategoriesAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpGet("/api/business-console/v1/master-data/product-categories/{categoryCode}")]
[BusinessGatewayOperationId("getBusinessConsoleProductCategory")]
public sealed class GetBusinessConsoleProductCategoryEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleProductCategoryRequest, BusinessConsoleProductCategoryItem>(
        auth,
        BusinessGatewayPermissions.MasterDataProductsRead)
{
    protected override string OrganizationId(BusinessConsoleProductCategoryRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleProductCategoryRequest request) => request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleProductCategoryRequest request) => "product-category";

    protected override string? ResourceId(BusinessConsoleProductCategoryRequest request) => Route<string>("categoryCode") ?? request.CategoryCode;

    protected override Task<BusinessConsoleProductCategoryItem> ForwardAsync(
        BusinessConsoleProductCategoryRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var categoryCode = Route<string>("categoryCode") ?? request.CategoryCode;
        return masterData.GetProductCategoryAsync(tokenProvider.BearerToken, categoryCode, request with { CategoryCode = categoryCode }, cancellationToken);
    }
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/product-categories")]
[BusinessGatewayOperationId("createBusinessConsoleProductCategory")]
public sealed class CreateBusinessConsoleProductCategoryEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateProductCategoryRequest, BusinessConsoleResourceItem>(
        auth,
        BusinessGatewayPermissions.MasterDataProductsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateProductCategoryRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateProductCategoryRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceItem> ForwardAsync(
        BusinessConsoleCreateProductCategoryRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.CreateProductCategoryAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPut("/api/business-console/v1/master-data/product-categories/{categoryCode}")]
[BusinessGatewayOperationId("updateBusinessConsoleProductCategory")]
public sealed class UpdateBusinessConsoleProductCategoryEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleUpdateProductCategoryRequest, BusinessConsoleProductCategoryItem>(
        auth,
        BusinessGatewayPermissions.MasterDataProductsManage)
{
    protected override string OrganizationId(BusinessConsoleUpdateProductCategoryRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleUpdateProductCategoryRequest request) => request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleUpdateProductCategoryRequest request) => "product-category";

    protected override string? ResourceId(BusinessConsoleUpdateProductCategoryRequest request) => Route<string>("categoryCode") ?? request.CategoryCode;

    protected override Task<BusinessConsoleProductCategoryItem> ForwardAsync(
        BusinessConsoleUpdateProductCategoryRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var categoryCode = Route<string>("categoryCode") ?? request.CategoryCode;
        return masterData.UpdateProductCategoryAsync(tokenProvider.BearerToken, categoryCode, request with { CategoryCode = categoryCode }, cancellationToken);
    }
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/product-categories/{categoryCode}/archive")]
[BusinessGatewayOperationId("archiveBusinessConsoleProductCategory")]
public sealed class ArchiveBusinessConsoleProductCategoryEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleArchiveProductCategoryRequest, BusinessConsoleProductCategoryItem>(
        auth,
        BusinessGatewayPermissions.MasterDataProductsManage)
{
    protected override string OrganizationId(BusinessConsoleArchiveProductCategoryRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleArchiveProductCategoryRequest request) => request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleArchiveProductCategoryRequest request) => "product-category";

    protected override string? ResourceId(BusinessConsoleArchiveProductCategoryRequest request) => Route<string>("categoryCode") ?? request.CategoryCode;

    protected override Task<BusinessConsoleProductCategoryItem> ForwardAsync(
        BusinessConsoleArchiveProductCategoryRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var categoryCode = Route<string>("categoryCode") ?? request.CategoryCode;
        return masterData.ArchiveProductCategoryAsync(tokenProvider.BearerToken, categoryCode, request with { CategoryCode = categoryCode }, cancellationToken);
    }
}

[Tags("Business Console MasterData")]
[HttpGet("/api/business-console/v1/master-data/skills")]
[BusinessGatewayOperationId("listBusinessConsoleSkills")]
public sealed class ListBusinessConsoleSkillsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListSkillsRequest, BusinessConsoleSkillListResponse>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesRead)
{
    protected override string OrganizationId(BusinessConsoleListSkillsRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListSkillsRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleSkillListResponse> ForwardAsync(
        BusinessConsoleListSkillsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.ListSkillsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpGet("/api/business-console/v1/master-data/skills/{skillCode}")]
[BusinessGatewayOperationId("getBusinessConsoleSkill")]
public sealed class GetBusinessConsoleSkillEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleSkillRequest, BusinessConsoleSkillItem>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesRead)
{
    protected override string OrganizationId(BusinessConsoleSkillRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleSkillRequest request) => request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleSkillRequest request) => "skill";

    protected override string? ResourceId(BusinessConsoleSkillRequest request) => Route<string>("skillCode") ?? request.SkillCode;

    protected override Task<BusinessConsoleSkillItem> ForwardAsync(
        BusinessConsoleSkillRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var skillCode = Route<string>("skillCode") ?? request.SkillCode;
        return masterData.GetSkillAsync(tokenProvider.BearerToken, skillCode, request with { SkillCode = skillCode }, cancellationToken);
    }
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/skills")]
[BusinessGatewayOperationId("createBusinessConsoleSkill")]
public sealed class CreateBusinessConsoleSkillEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateSkillRequest, BusinessConsoleResourceItem>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesManage)
{
    protected override string OrganizationId(BusinessConsoleCreateSkillRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateSkillRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceItem> ForwardAsync(
        BusinessConsoleCreateSkillRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.CreateSkillAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPut("/api/business-console/v1/master-data/skills/{skillCode}")]
[BusinessGatewayOperationId("updateBusinessConsoleSkill")]
public sealed class UpdateBusinessConsoleSkillEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleUpdateSkillRequest, BusinessConsoleSkillItem>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesManage)
{
    protected override string OrganizationId(BusinessConsoleUpdateSkillRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleUpdateSkillRequest request) => request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleUpdateSkillRequest request) => "skill";

    protected override string? ResourceId(BusinessConsoleUpdateSkillRequest request) => Route<string>("skillCode") ?? request.SkillCode;

    protected override Task<BusinessConsoleSkillItem> ForwardAsync(
        BusinessConsoleUpdateSkillRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var skillCode = Route<string>("skillCode") ?? request.SkillCode;
        return masterData.UpdateSkillAsync(tokenProvider.BearerToken, skillCode, request with { SkillCode = skillCode }, cancellationToken);
    }
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/skills/{skillCode}/archive")]
[BusinessGatewayOperationId("archiveBusinessConsoleSkill")]
public sealed class ArchiveBusinessConsoleSkillEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleArchiveSkillRequest, BusinessConsoleSkillItem>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesManage)
{
    protected override string OrganizationId(BusinessConsoleArchiveSkillRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleArchiveSkillRequest request) => request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleArchiveSkillRequest request) => "skill";

    protected override string? ResourceId(BusinessConsoleArchiveSkillRequest request) => Route<string>("skillCode") ?? request.SkillCode;

    protected override Task<BusinessConsoleSkillItem> ForwardAsync(
        BusinessConsoleArchiveSkillRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var skillCode = Route<string>("skillCode") ?? request.SkillCode;
        return masterData.ArchiveSkillAsync(tokenProvider.BearerToken, skillCode, request with { SkillCode = skillCode }, cancellationToken);
    }
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

public sealed class BusinessConsoleListProductCategoriesRequestValidator : Validator<BusinessConsoleListProductCategoriesRequest>
{
    public BusinessConsoleListProductCategoriesRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.ParentCode).MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class BusinessConsoleProductCategoryRequestValidator : Validator<BusinessConsoleProductCategoryRequest>
{
    public BusinessConsoleProductCategoryRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CategoryCode).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleCreateProductCategoryRequestValidator : Validator<BusinessConsoleCreateProductCategoryRequest>
{
    public BusinessConsoleCreateProductCategoryRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CategoryCode).MaximumLength(100);
        RuleFor(x => x.CategoryName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ParentCode).MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
    }
}

public sealed class BusinessConsoleUpdateProductCategoryRequestValidator : Validator<BusinessConsoleUpdateProductCategoryRequest>
{
    public BusinessConsoleUpdateProductCategoryRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CategoryCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CategoryName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ParentCode).MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class BusinessConsoleArchiveProductCategoryRequestValidator : Validator<BusinessConsoleArchiveProductCategoryRequest>
{
    public BusinessConsoleArchiveProductCategoryRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CategoryCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class BusinessConsoleListSkillsRequestValidator : Validator<BusinessConsoleListSkillsRequest>
{
    public BusinessConsoleListSkillsRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.GroupName).MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class BusinessConsoleSkillRequestValidator : Validator<BusinessConsoleSkillRequest>
{
    public BusinessConsoleSkillRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkillCode).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleCreateSkillRequestValidator : Validator<BusinessConsoleCreateSkillRequest>
{
    public BusinessConsoleCreateSkillRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkillCode).MaximumLength(100);
        RuleFor(x => x.SkillName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.GroupName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ValidityMonths).GreaterThan(0).When(x => x.ValidityMonths.HasValue);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
    }
}

public sealed class BusinessConsoleUpdateSkillRequestValidator : Validator<BusinessConsoleUpdateSkillRequest>
{
    public BusinessConsoleUpdateSkillRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkillCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkillName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.GroupName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ValidityMonths).GreaterThan(0).When(x => x.ValidityMonths.HasValue);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class BusinessConsoleArchiveSkillRequestValidator : Validator<BusinessConsoleArchiveSkillRequest>
{
    public BusinessConsoleArchiveSkillRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkillCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class BusinessConsoleCreateBusinessPartnerRequestValidator : Validator<BusinessConsoleCreateBusinessPartnerRequest>
{
    public BusinessConsoleCreateBusinessPartnerRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).MaximumLength(100);
        RuleFor(x => x.PartnerType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CreditLimit).GreaterThanOrEqualTo(0m).When(x => x.CreditLimit.HasValue);
        RuleFor(x => x.CreditCurrencyCode).MaximumLength(10);
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
    }
}

public sealed class BusinessConsoleCreateUnitOfMeasureRequestValidator : Validator<BusinessConsoleCreateUnitOfMeasureRequest>
{
    public BusinessConsoleCreateUnitOfMeasureRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DimensionType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Precision).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RoundingMode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
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

public sealed class BusinessConsoleListWorkshopsRequestValidator : Validator<BusinessConsoleListWorkshopsRequest>
{
    public BusinessConsoleListWorkshopsRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleWorkerDirectoryRequestValidator : Validator<BusinessConsoleWorkerDirectoryRequest>
{
    public BusinessConsoleWorkerDirectoryRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Keyword).MaximumLength(100);
        RuleFor(x => x.PageIndex).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

public sealed class BusinessConsoleCreateWorkshopRequestValidator : Validator<BusinessConsoleCreateWorkshopRequest>
{
    public BusinessConsoleCreateWorkshopRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ManagerUserId).MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
    }
}

public sealed class BusinessConsoleCreateSiteRequestValidator : Validator<BusinessConsoleCreateSiteRequest>
{
    public BusinessConsoleCreateSiteRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Timezone).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
    }
}

public sealed class BusinessConsoleCreateProductionLineRequestValidator : Validator<BusinessConsoleCreateProductionLineRequest>
{
    public BusinessConsoleCreateProductionLineRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkshopCode).MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
    }
}

public sealed class BusinessConsoleCreateWorkCenterRequestValidator : Validator<BusinessConsoleCreateWorkCenterRequest>
{
    public BusinessConsoleCreateWorkCenterRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CapacityMinutesPerDay).GreaterThan(0);
        RuleFor(x => x.ResourceType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PlantCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LineCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkshopCode).MaximumLength(100);
        RuleFor(x => x.DefaultCalendarCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CapacityUnit).NotEmpty().MaximumLength(50);
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
    }
}

public sealed class BusinessConsoleRegisterDeviceAssetRequestValidator : Validator<BusinessConsoleRegisterDeviceAssetRequest>
{
    public BusinessConsoleRegisterDeviceAssetRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).MaximumLength(100);
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
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
    }
}

public sealed class BusinessConsoleCreateShiftRequestValidator : Validator<BusinessConsoleCreateShiftRequest>
{
    public BusinessConsoleCreateShiftRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PaidMinutes).GreaterThan(0);
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
    }
}

public sealed class BusinessConsoleCreateWorkCalendarRequestValidator : Validator<BusinessConsoleCreateWorkCalendarRequest>
{
    public BusinessConsoleCreateWorkCalendarRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
    }
}

public sealed class BusinessConsoleCreateTeamRequestValidator : Validator<BusinessConsoleCreateTeamRequest>
{
    public BusinessConsoleCreateTeamRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DepartmentCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ShiftCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
    }
}

public sealed class BusinessConsoleAddTeamMemberRequestValidator : Validator<BusinessConsoleAddTeamMemberRequest>
{
    public BusinessConsoleAddTeamMemberRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TeamCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EffectiveTo).GreaterThanOrEqualTo(x => x.EffectiveFrom).When(x => x.EffectiveTo.HasValue);
    }
}

public sealed class BusinessConsoleListTeamMembersRequestValidator : Validator<BusinessConsoleListTeamMembersRequest>
{
    public BusinessConsoleListTeamMembersRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TeamCode).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleRemoveTeamMemberRequestValidator : Validator<BusinessConsoleRemoveTeamMemberRequest>
{
    public BusinessConsoleRemoveTeamMemberRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TeamCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class BusinessConsoleCreateDepartmentRequestValidator : Validator<BusinessConsoleCreateDepartmentRequest>
{
    public BusinessConsoleCreateDepartmentRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ParentDepartmentCode).MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
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

public sealed class BusinessConsoleCodeRuleContextRequestValidator : Validator<BusinessConsoleCodeRuleContextRequest>
{
    public BusinessConsoleCodeRuleContextRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleCodeRuleRequestValidator : Validator<BusinessConsoleCodeRuleRequest>
{
    public BusinessConsoleCodeRuleRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RuleKey).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleCreateCodeRuleVersionRequestValidator : Validator<BusinessConsoleCreateCodeRuleVersionRequest>
{
    public BusinessConsoleCreateCodeRuleVersionRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RuleKey).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AppliesTo).MaximumLength(200);
        RuleFor(x => x.Segments).NotEmpty();
        RuleFor(x => x.CreatedBy).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ChangeReason).MaximumLength(500);
    }
}

public sealed class BusinessConsolePreviewCodeRuleRequestValidator : Validator<BusinessConsolePreviewCodeRuleRequest>
{
    public BusinessConsolePreviewCodeRuleRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RuleKey).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Segments).NotEmpty();
        RuleFor(x => x.SiteCode).MaximumLength(100);
    }
}

public sealed class BusinessConsoleMasterDataResourceRequestValidator : Validator<BusinessConsoleMasterDataResourceRequest>
{
    public BusinessConsoleMasterDataResourceRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ResourceType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CodeSet).MaximumLength(100);
    }
}

public sealed class BusinessConsoleUpdateMasterDataResourceRequestValidator : Validator<BusinessConsoleUpdateMasterDataResourceRequest>
{
    public BusinessConsoleUpdateMasterDataResourceRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ResourceType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CodeSet).MaximumLength(100);
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.BaseUomCode).MaximumLength(50);
        RuleFor(x => x.Category).MaximumLength(100);
        RuleFor(x => x.MaterialType).MaximumLength(100);
        RuleFor(x => x.PartnerType).MaximumLength(100);
        RuleFor(x => x.ParentDepartmentCode).MaximumLength(100);
        RuleFor(x => x.DepartmentCode).MaximumLength(100);
        RuleFor(x => x.ShiftCode).MaximumLength(100);
        RuleFor(x => x.PaidMinutes).GreaterThan(0).When(x => x.PaidMinutes.HasValue);
        RuleFor(x => x.ManagerUserId).MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.WorkshopCode).MaximumLength(100);
        RuleFor(x => x.CapacityMinutesPerDay).GreaterThan(0).When(x => x.CapacityMinutesPerDay.HasValue);
        RuleFor(x => x.MaximumCapacity).GreaterThanOrEqualTo(x => x.MinimumCapacity)
            .When(x => x.MinimumCapacity.HasValue && x.MaximumCapacity.HasValue);
        RuleFor(x => x.Precision).GreaterThanOrEqualTo(0).When(x => x.Precision.HasValue);
        RuleFor(x => x.CreditLimit).GreaterThanOrEqualTo(0m).When(x => x.CreditLimit.HasValue);
        RuleFor(x => x.CreditCurrencyCode).MaximumLength(10);
    }
}

public sealed class BusinessConsoleSetMasterDataResourceEnabledRequestValidator : Validator<BusinessConsoleSetMasterDataResourceEnabledRequest>
{
    public BusinessConsoleSetMasterDataResourceEnabledRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ResourceType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CodeSet).MaximumLength(100);
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

[Tags("Business Console MasterData")]
[HttpGet("/api/business-console/v1/master-data/resources/{ResourceType}/{Code}")]
[BusinessGatewayOperationId("getBusinessConsoleMasterDataResourceDetail")]
public sealed class GetBusinessConsoleMasterDataResourceDetailEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMasterDataResourceRequest, BusinessConsoleMasterDataResourceDetail>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesRead)
{
    protected override string OrganizationId(BusinessConsoleMasterDataResourceRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMasterDataResourceRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMasterDataResourceDetail> ForwardAsync(
        BusinessConsoleMasterDataResourceRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.GetResourceDetailAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPatch("/api/business-console/v1/master-data/resources/{ResourceType}/{Code}")]
[BusinessGatewayOperationId("updateBusinessConsoleMasterDataResource")]
public sealed class UpdateBusinessConsoleMasterDataResourceEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleUpdateMasterDataResourceRequest, BusinessConsoleMasterDataResourceDetail>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesManage)
{
    protected override string OrganizationId(BusinessConsoleUpdateMasterDataResourceRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleUpdateMasterDataResourceRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMasterDataResourceDetail> ForwardAsync(
        BusinessConsoleUpdateMasterDataResourceRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.UpdateResourceAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/resources/{ResourceType}/{Code}/disable")]
[BusinessGatewayOperationId("disableBusinessConsoleMasterDataResource")]
public sealed class DisableBusinessConsoleMasterDataResourceEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleSetMasterDataResourceEnabledRequest, BusinessConsoleMasterDataResourceDetail>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesManage)
{
    protected override string OrganizationId(BusinessConsoleSetMasterDataResourceEnabledRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleSetMasterDataResourceEnabledRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMasterDataResourceDetail> ForwardAsync(
        BusinessConsoleSetMasterDataResourceEnabledRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.SetResourceEnabledAsync(tokenProvider.BearerToken, request, false, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/resources/{ResourceType}/{Code}/enable")]
[BusinessGatewayOperationId("enableBusinessConsoleMasterDataResource")]
public sealed class EnableBusinessConsoleMasterDataResourceEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleSetMasterDataResourceEnabledRequest, BusinessConsoleMasterDataResourceDetail>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesManage)
{
    protected override string OrganizationId(BusinessConsoleSetMasterDataResourceEnabledRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleSetMasterDataResourceEnabledRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMasterDataResourceDetail> ForwardAsync(
        BusinessConsoleSetMasterDataResourceEnabledRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.SetResourceEnabledAsync(tokenProvider.BearerToken, request, true, cancellationToken);
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
[HttpGet("/api/business-console/v1/master-data/workshops")]
[BusinessGatewayOperationId("listBusinessConsoleWorkshops")]
public sealed class ListBusinessConsoleWorkshopsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListWorkshopsRequest, BusinessConsoleResourceListResponse>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesRead)
{
    protected override string OrganizationId(BusinessConsoleListWorkshopsRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListWorkshopsRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceListResponse> ForwardAsync(
        BusinessConsoleListWorkshopsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.ListResourcesAsync(
            tokenProvider.BearerToken,
            new BusinessConsoleListResourcesRequest(
                request.OrganizationId,
                request.EnvironmentId,
                "workshop",
                request.IncludeDisabled,
                request.Skip,
                request.Take),
            cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/workshops")]
[BusinessGatewayOperationId("createBusinessConsoleWorkshop")]
public sealed class CreateBusinessConsoleWorkshopEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateWorkshopRequest, BusinessConsoleResourceItem>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesManage)
{
    protected override string OrganizationId(BusinessConsoleCreateWorkshopRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateWorkshopRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceItem> ForwardAsync(
        BusinessConsoleCreateWorkshopRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.CreateWorkshopAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpGet("/api/business-console/v1/master-data/workers")]
[BusinessGatewayOperationId("listBusinessConsoleWorkers")]
public sealed class ListBusinessConsoleWorkersEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessIamDirectoryClient iamDirectory,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleWorkerDirectoryRequest, BusinessConsoleWorkerDirectoryResponse>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesRead)
{
    protected override string OrganizationId(BusinessConsoleWorkerDirectoryRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleWorkerDirectoryRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleWorkerDirectoryResponse> ForwardAsync(
        BusinessConsoleWorkerDirectoryRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        // IAM users are currently platform-global; organization/environment are enforced as BusinessGateway auth scope.
        return iamDirectory.ListWorkersAsync(tokenProvider.BearerToken, request, cancellationToken);
    }
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
[HttpGet("/api/business-console/v1/master-data/teams/{TeamCode}/members")]
[BusinessGatewayOperationId("listBusinessConsoleTeamMembers")]
public sealed class ListBusinessConsoleTeamMembersEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListTeamMembersRequest, BusinessConsoleTeamMemberListResponse>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesRead)
{
    protected override string OrganizationId(BusinessConsoleListTeamMembersRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListTeamMembersRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleTeamMemberListResponse> ForwardAsync(
        BusinessConsoleListTeamMembersRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.ListTeamMembersAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/teams/{TeamCode}/members")]
[BusinessGatewayOperationId("addBusinessConsoleTeamMember")]
public sealed class AddBusinessConsoleTeamMemberEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleAddTeamMemberRequest, BusinessConsoleResourceItem>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesManage)
{
    protected override string OrganizationId(BusinessConsoleAddTeamMemberRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleAddTeamMemberRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceItem> ForwardAsync(
        BusinessConsoleAddTeamMemberRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.AddTeamMemberAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpDelete("/api/business-console/v1/master-data/teams/{TeamCode}/members/{UserId}")]
[BusinessGatewayOperationId("removeBusinessConsoleTeamMember")]
public sealed class RemoveBusinessConsoleTeamMemberEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleRemoveTeamMemberRequest, BusinessConsoleResourceItem>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesManage)
{
    protected override string OrganizationId(BusinessConsoleRemoveTeamMemberRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleRemoveTeamMemberRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceItem> ForwardAsync(
        BusinessConsoleRemoveTeamMemberRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.RemoveTeamMemberAsync(tokenProvider.BearerToken, request, cancellationToken);
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
[HttpGet("/api/business-console/v1/master-data/personnel-skills/matrix")]
[BusinessGatewayOperationId("listBusinessConsolePersonnelSkillMatrix")]
public sealed class ListBusinessConsolePersonnelSkillMatrixEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsolePersonnelSkillMatrixRequest, BusinessConsolePersonnelSkillMatrixResponse>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesRead)
{
    protected override string OrganizationId(BusinessConsolePersonnelSkillMatrixRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsolePersonnelSkillMatrixRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsolePersonnelSkillMatrixResponse> ForwardAsync(
        BusinessConsolePersonnelSkillMatrixRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.ListPersonnelSkillMatrixAsync(tokenProvider.BearerToken, request, cancellationToken);
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

[Tags("Business Console MasterData")]
[HttpGet("/api/business-console/v1/master-data/code-rules")]
[BusinessGatewayOperationId("listBusinessConsoleCodeRules")]
public sealed class ListBusinessConsoleCodeRulesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCodeRuleContextRequest, BusinessConsoleCodeRuleListResponse>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesRead)
{
    protected override string OrganizationId(BusinessConsoleCodeRuleContextRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCodeRuleContextRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCodeRuleListResponse> ForwardAsync(
        BusinessConsoleCodeRuleContextRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.ListCodeRulesAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpGet("/api/business-console/v1/master-data/code-rules/{RuleKey}")]
[BusinessGatewayOperationId("getBusinessConsoleCodeRule")]
public sealed class GetBusinessConsoleCodeRuleEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCodeRuleRequest, BusinessConsoleCodeRuleDetailResponse>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesRead)
{
    protected override string OrganizationId(BusinessConsoleCodeRuleRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCodeRuleRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCodeRuleDetailResponse> ForwardAsync(
        BusinessConsoleCodeRuleRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.GetCodeRuleAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/code-rules/{RuleKey}/versions")]
[BusinessGatewayOperationId("createBusinessConsoleCodeRuleVersion")]
public sealed class CreateBusinessConsoleCodeRuleVersionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateCodeRuleVersionRequest, BusinessConsoleCodeRuleVersionResponse>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesManage)
{
    protected override string OrganizationId(BusinessConsoleCreateCodeRuleVersionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateCodeRuleVersionRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCodeRuleVersionResponse> ForwardAsync(
        BusinessConsoleCreateCodeRuleVersionRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.CreateCodeRuleVersionAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/code-rules/{RuleKey}/preview")]
[BusinessGatewayOperationId("previewBusinessConsoleCodeRule")]
public sealed class PreviewBusinessConsoleCodeRuleEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsolePreviewCodeRuleRequest, BusinessConsoleCodeRulePreviewResponse>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesRead)
{
    protected override string OrganizationId(BusinessConsolePreviewCodeRuleRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsolePreviewCodeRuleRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCodeRulePreviewResponse> ForwardAsync(
        BusinessConsolePreviewCodeRuleRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.PreviewCodeRuleAsync(tokenProvider.BearerToken, request, cancellationToken);
}
