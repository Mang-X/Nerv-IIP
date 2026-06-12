using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.ProductEngineering;

[Tags("Business Console Product Engineering")]
[HttpPost("/api/business-console/v1/engineering/documents")]
[BusinessGatewayOperationId("registerBusinessConsoleEngineeringDocument")]
public sealed class RegisterBusinessConsoleEngineeringDocumentEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleRegisterEngineeringDocumentRequest, BusinessConsoleEngineeringEntityResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringDocumentsManage)
{
    protected override string OrganizationId(BusinessConsoleRegisterEngineeringDocumentRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleRegisterEngineeringDocumentRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleEngineeringEntityResponse> ForwardAsync(
        BusinessConsoleRegisterEngineeringDocumentRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.RegisterEngineeringDocumentAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Product Engineering")]
[HttpPost("/api/business-console/v1/engineering/items")]
[BusinessGatewayOperationId("createBusinessConsoleEngineeringItemRevision")]
public sealed class CreateBusinessConsoleEngineeringItemRevisionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateEngineeringItemRevisionRequest, BusinessConsoleEngineeringEntityResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringItemsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateEngineeringItemRevisionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateEngineeringItemRevisionRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleEngineeringEntityResponse> ForwardAsync(
        BusinessConsoleCreateEngineeringItemRevisionRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.CreateEngineeringItemRevisionAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Product Engineering")]
[HttpPost("/api/business-console/v1/engineering/engineering-boms/release")]
[BusinessGatewayOperationId("releaseBusinessConsoleEngineeringBom")]
public sealed class ReleaseBusinessConsoleEngineeringBomEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleReleaseEngineeringBomRequest, BusinessConsoleEngineeringEntityResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringBomsManage)
{
    protected override string OrganizationId(BusinessConsoleReleaseEngineeringBomRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleReleaseEngineeringBomRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleEngineeringEntityResponse> ForwardAsync(
        BusinessConsoleReleaseEngineeringBomRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.ReleaseEngineeringBomAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Product Engineering")]
[HttpGet("/api/business-console/v1/engineering/engineering-boms")]
[BusinessGatewayOperationId("listBusinessConsoleEngineeringBoms")]
public sealed class ListBusinessConsoleEngineeringBomsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListEngineeringBomsRequest, BusinessConsoleEngineeringBomListResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringBomsRead)
{
    protected override string OrganizationId(BusinessConsoleListEngineeringBomsRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListEngineeringBomsRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleEngineeringBomListResponse> ForwardAsync(
        BusinessConsoleListEngineeringBomsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.ListEngineeringBomsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Product Engineering")]
[HttpGet("/api/business-console/v1/engineering/manufacturing-boms")]
[BusinessGatewayOperationId("listBusinessConsoleEngineeringManufacturingBoms")]
public sealed class ListBusinessConsoleEngineeringManufacturingBomsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListManufacturingBomsRequest, BusinessConsoleManufacturingBomListResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringBomsRead)
{
    protected override string OrganizationId(BusinessConsoleListManufacturingBomsRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListManufacturingBomsRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleManufacturingBomListResponse> ForwardAsync(
        BusinessConsoleListManufacturingBomsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.ListManufacturingBomsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Product Engineering")]
[HttpPost("/api/business-console/v1/engineering/manufacturing-boms/release")]
[BusinessGatewayOperationId("releaseBusinessConsoleEngineeringManufacturingBom")]
public sealed class ReleaseBusinessConsoleEngineeringManufacturingBomEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleReleaseManufacturingBomRequest, BusinessConsoleEngineeringEntityResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringBomsManage)
{
    protected override string OrganizationId(BusinessConsoleReleaseManufacturingBomRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleReleaseManufacturingBomRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleEngineeringEntityResponse> ForwardAsync(
        BusinessConsoleReleaseManufacturingBomRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.ReleaseManufacturingBomAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Product Engineering")]
[HttpGet("/api/business-console/v1/engineering/routings")]
[BusinessGatewayOperationId("listBusinessConsoleEngineeringRoutings")]
public sealed class ListBusinessConsoleEngineeringRoutingsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListRoutingsRequest, BusinessConsoleRoutingListResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringRoutingsRead)
{
    protected override string OrganizationId(BusinessConsoleListRoutingsRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListRoutingsRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleRoutingListResponse> ForwardAsync(
        BusinessConsoleListRoutingsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.ListRoutingsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Product Engineering")]
[HttpPost("/api/business-console/v1/engineering/routings/release")]
[BusinessGatewayOperationId("releaseBusinessConsoleEngineeringRouting")]
public sealed class ReleaseBusinessConsoleEngineeringRoutingEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleReleaseRoutingRequest, BusinessConsoleEngineeringEntityResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringRoutingsManage)
{
    protected override string OrganizationId(BusinessConsoleReleaseRoutingRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleReleaseRoutingRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleEngineeringEntityResponse> ForwardAsync(
        BusinessConsoleReleaseRoutingRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.ReleaseRoutingAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Product Engineering")]
[HttpPost("/api/business-console/v1/engineering/engineering-changes/release")]
[BusinessGatewayOperationId("releaseBusinessConsoleEngineeringChange")]
public sealed class ReleaseBusinessConsoleEngineeringChangeEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleReleaseEngineeringChangeRequest, BusinessConsoleEngineeringEntityResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringChangesManage)
{
    protected override string OrganizationId(BusinessConsoleReleaseEngineeringChangeRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleReleaseEngineeringChangeRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleEngineeringEntityResponse> ForwardAsync(
        BusinessConsoleReleaseEngineeringChangeRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.ReleaseEngineeringChangeAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Product Engineering")]
[HttpGet("/api/business-console/v1/engineering/production-versions")]
[BusinessGatewayOperationId("listBusinessConsoleEngineeringProductionVersions")]
public sealed class ListBusinessConsoleEngineeringProductionVersionsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListProductionVersionsRequest, BusinessConsoleProductionVersionListResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringProductionVersionsRead)
{
    protected override string OrganizationId(BusinessConsoleListProductionVersionsRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListProductionVersionsRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleProductionVersionListResponse> ForwardAsync(
        BusinessConsoleListProductionVersionsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.ListProductionVersionsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Product Engineering")]
[HttpPost("/api/business-console/v1/engineering/production-versions")]
[BusinessGatewayOperationId("createBusinessConsoleEngineeringProductionVersion")]
public sealed class CreateBusinessConsoleEngineeringProductionVersionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateProductionVersionRequest, BusinessConsoleCreateProductionVersionResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringProductionVersionsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateProductionVersionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateProductionVersionRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateProductionVersionResponse> ForwardAsync(
        BusinessConsoleCreateProductionVersionRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.CreateProductionVersionAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Product Engineering")]
[HttpPut("/api/business-console/v1/engineering/production-versions/{productionVersionId}")]
[BusinessGatewayOperationId("updateBusinessConsoleEngineeringProductionVersion")]
public sealed class UpdateBusinessConsoleEngineeringProductionVersionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleUpdateProductionVersionRequest, BusinessConsoleCreateProductionVersionResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringProductionVersionsManage)
{
    protected override string OrganizationId(BusinessConsoleUpdateProductionVersionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleUpdateProductionVersionRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleUpdateProductionVersionRequest request) => "production-version";

    protected override string? ResourceId(BusinessConsoleUpdateProductionVersionRequest request) => Route<string>("productionVersionId") ?? request.ProductionVersionId;

    protected override Task<BusinessConsoleCreateProductionVersionResponse> ForwardAsync(
        BusinessConsoleUpdateProductionVersionRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var productionVersionId = Route<string>("productionVersionId") ?? request.ProductionVersionId;
        return engineering.UpdateProductionVersionAsync(
            tokenProvider.BearerToken,
            productionVersionId,
            request with { ProductionVersionId = productionVersionId },
            cancellationToken);
    }
}

[Tags("Business Console Product Engineering")]
[HttpPost("/api/business-console/v1/engineering/production-versions/{productionVersionId}/archive")]
[BusinessGatewayOperationId("archiveBusinessConsoleEngineeringProductionVersion")]
public sealed class ArchiveBusinessConsoleEngineeringProductionVersionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleArchiveProductionVersionRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringProductionVersionsManage)
{
    protected override string OrganizationId(BusinessConsoleArchiveProductionVersionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleArchiveProductionVersionRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleArchiveProductionVersionRequest request) => "production-version";

    protected override string? ResourceId(BusinessConsoleArchiveProductionVersionRequest request) => Route<string>("productionVersionId") ?? request.ProductionVersionId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleArchiveProductionVersionRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var productionVersionId = Route<string>("productionVersionId") ?? request.ProductionVersionId;
        return engineering.ArchiveProductionVersionAsync(
            tokenProvider.BearerToken,
            productionVersionId,
            request with { ProductionVersionId = productionVersionId },
            cancellationToken);
    }
}

[Tags("Business Console Product Engineering")]
[HttpGet("/api/business-console/v1/engineering/production-versions/resolve")]
[BusinessGatewayOperationId("resolveBusinessConsoleEngineeringProductionVersion")]
public sealed class ResolveBusinessConsoleEngineeringProductionVersionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleResolveProductionVersionRequest, BusinessConsoleResolveProductionVersionResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringProductionVersionsRead)
{
    protected override string OrganizationId(BusinessConsoleResolveProductionVersionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleResolveProductionVersionRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResolveProductionVersionResponse> ForwardAsync(
        BusinessConsoleResolveProductionVersionRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.ResolveProductionVersionAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public sealed class BusinessConsoleRegisterEngineeringDocumentRequestValidator : Validator<BusinessConsoleRegisterEngineeringDocumentRequest>
{
    public BusinessConsoleRegisterEngineeringDocumentRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DocumentNumber).MaximumLength(100);
        RuleFor(x => x.Revision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.FileId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(120);
        RuleFor(x => x.DocumentType).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleCreateEngineeringItemRevisionRequestValidator : Validator<BusinessConsoleCreateEngineeringItemRevisionRequest>
{
    public BusinessConsoleCreateEngineeringItemRevisionRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ItemCode).MaximumLength(100);
        RuleFor(x => x.Revision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
    }
}

public sealed class BusinessConsoleReleaseEngineeringBomRequestValidator : Validator<BusinessConsoleReleaseEngineeringBomRequest>
{
    public BusinessConsoleReleaseEngineeringBomRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BomCode).MaximumLength(100);
        RuleFor(x => x.Revision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ParentItemCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.ComponentCode).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.UnitOfMeasureCode).NotEmpty().MaximumLength(50);
        });
    }
}

public sealed class BusinessConsoleReleaseManufacturingBomRequestValidator : Validator<BusinessConsoleReleaseManufacturingBomRequest>
{
    public BusinessConsoleReleaseManufacturingBomRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BomCode).MaximumLength(100);
        RuleFor(x => x.Revision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EngineeringBomCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EngineeringBomRevision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.MaterialLines).NotEmpty();
        RuleForEach(x => x.MaterialLines).ChildRules(line =>
        {
            line.RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.UnitOfMeasureCode).NotEmpty().MaximumLength(50);
            line.RuleFor(x => x.ScrapRate).GreaterThanOrEqualTo(0);
        });
        RuleForEach(x => x.RecipeLines).ChildRules(line =>
        {
            line.RuleFor(x => x.ParameterCode).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.TargetValue).NotEmpty().MaximumLength(200);
            line.RuleFor(x => x.UnitOfMeasureCode).NotEmpty().MaximumLength(50);
        });
    }
}

public sealed class BusinessConsoleReleaseRoutingRequestValidator : Validator<BusinessConsoleReleaseRoutingRequest>
{
    public BusinessConsoleReleaseRoutingRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RoutingCode).MaximumLength(100);
        RuleFor(x => x.Revision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Operations).NotEmpty();
        RuleForEach(x => x.Operations).ChildRules(operation =>
        {
            operation.RuleFor(x => x.Sequence).GreaterThan(0);
            operation.RuleFor(x => x.WorkCenterCode).NotEmpty().MaximumLength(100);
            operation.RuleFor(x => x.OperationCode).Must(value => !string.IsNullOrWhiteSpace(value)).MaximumLength(100);
            operation.RuleFor(x => x.OperationName).Must(value => !string.IsNullOrWhiteSpace(value)).MaximumLength(200);
            operation.RuleFor(x => x.StandardMinutes).GreaterThan(0);
        });
    }
}

public sealed class BusinessConsoleReleaseEngineeringChangeRequestValidator : Validator<BusinessConsoleReleaseEngineeringChangeRequest>
{
    public BusinessConsoleReleaseEngineeringChangeRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ChangeNumber).MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ApprovalReferenceId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.AffectedVersions).NotEmpty();
        RuleForEach(x => x.AffectedVersions).ChildRules(version =>
        {
            version.RuleFor(x => x.VersionKind).NotEmpty().MaximumLength(100);
            version.RuleFor(x => x.VersionId).NotEmpty().MaximumLength(150);
        });
    }
}

public sealed class BusinessConsoleCreateProductionVersionRequestValidator : Validator<BusinessConsoleCreateProductionVersionRequest>
{
    public BusinessConsoleCreateProductionVersionRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.MbomVersionId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RoutingVersionId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Priority).GreaterThanOrEqualTo(0);
        RuleFor(x => x.LotSizeMin).GreaterThanOrEqualTo(0).When(x => x.LotSizeMin.HasValue);
        RuleFor(x => x.LotSizeMax).GreaterThanOrEqualTo(0).When(x => x.LotSizeMax.HasValue);
    }
}

public sealed class BusinessConsoleUpdateProductionVersionRequestValidator : Validator<BusinessConsoleUpdateProductionVersionRequest>
{
    public BusinessConsoleUpdateProductionVersionRequestValidator()
    {
        RuleFor(x => x.ProductionVersionId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.MbomVersionId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RoutingVersionId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Priority).GreaterThanOrEqualTo(0);
        RuleFor(x => x.LotSizeMin).GreaterThanOrEqualTo(0).When(x => x.LotSizeMin.HasValue);
        RuleFor(x => x.LotSizeMax).GreaterThanOrEqualTo(0).When(x => x.LotSizeMax.HasValue);
    }
}

public sealed class BusinessConsoleArchiveProductionVersionRequestValidator : Validator<BusinessConsoleArchiveProductionVersionRequest>
{
    public BusinessConsoleArchiveProductionVersionRequestValidator()
    {
        RuleFor(x => x.ProductionVersionId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}
