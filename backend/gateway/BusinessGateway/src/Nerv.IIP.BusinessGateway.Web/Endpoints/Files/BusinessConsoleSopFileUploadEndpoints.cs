using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Files;

[Tags("Business Console Files")]
[HttpPost("/api/business-console/v1/files/sop-documents/upload-sessions")]
[BusinessGatewayOperationId("createBusinessConsoleSopFileUploadSession")]
public sealed class CreateBusinessConsoleSopFileUploadSessionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessFileStorageClient files,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<
        BusinessConsoleCreateSopFileUploadSessionRequest,
        BusinessConsoleSopFileUploadSessionResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringDocumentsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateSopFileUploadSessionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateSopFileUploadSessionRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleCreateSopFileUploadSessionRequest request) => "engineering-sop-file";

    protected override Task<BusinessConsoleSopFileUploadSessionResponse> ForwardAsync(
        BusinessConsoleCreateSopFileUploadSessionRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        // owner 绑定到认证 principal：文档号可在登记时分配，上传时以认证身份作为稳定归属。
        files.CreateSopFileUploadSessionAsync(
            tokenProvider.BearerToken,
            RequireAuthorizedPrincipalId(),
            request,
            cancellationToken);
}

public sealed class BusinessConsoleCreateSopFileUploadSessionRequestValidator
    : Validator<BusinessConsoleCreateSopFileUploadSessionRequest>
{
    public BusinessConsoleCreateSopFileUploadSessionRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(512);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(256);
        RuleFor(x => x.ExpectedSizeBytes).GreaterThan(0);
    }
}

[Tags("Business Console Files")]
[HttpPost("/api/business-console/v1/files/sop-documents/upload-sessions/{uploadSessionId}/complete")]
[BusinessGatewayOperationId("completeBusinessConsoleSopFileUpload")]
public sealed class CompleteBusinessConsoleSopFileUploadEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessFileStorageClient files,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<
        BusinessConsoleCompleteSopFileUploadRequest,
        BusinessConsoleSopFile>(
        auth,
        BusinessGatewayPermissions.EngineeringDocumentsManage)
{
    protected override string OrganizationId(BusinessConsoleCompleteSopFileUploadRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCompleteSopFileUploadRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleCompleteSopFileUploadRequest request) => "engineering-sop-file";

    protected override string? ResourceId(BusinessConsoleCompleteSopFileUploadRequest request) => Route<string>("uploadSessionId");

    protected override Task<BusinessConsoleSopFile> ForwardAsync(
        BusinessConsoleCompleteSopFileUploadRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        files.CompleteSopFileUploadAsync(
            tokenProvider.BearerToken,
            Route<string>("uploadSessionId")!,
            request,
            cancellationToken);
}

public sealed class BusinessConsoleCompleteSopFileUploadRequestValidator
    : Validator<BusinessConsoleCompleteSopFileUploadRequest>
{
    public BusinessConsoleCompleteSopFileUploadRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

[Tags("Business Console Files")]
[BusinessGatewayOperationId("getBusinessConsoleSopFileTusOffset")]
[Authorize(Policy = BusinessGatewayPolicies.BusinessConsoleAuthenticated)]
public sealed class GetBusinessConsoleSopFileTusOffsetEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessFileTransferClient files,
    IInternalServiceTokenProvider tokenProvider)
    : EndpointWithoutRequest
{
    public override void Configure()
    {
        Head("/api/business-console/v1/files/sop-documents/tus/{uploadSessionId}");
        Policies(BusinessGatewayPolicies.BusinessConsoleAuthenticated);
        Options(x => x.WithTags("Business Console Files"));
    }

    public override Task HandleAsync(CancellationToken ct) =>
        BusinessConsoleFileTransfer.ProxyAsync(
            HttpContext,
            auth,
            BusinessGatewayPermissions.EngineeringDocumentsManage,
            "engineering-sop-file-upload",
            Route<string>("uploadSessionId")!,
            (organizationId, environmentId, cancellationToken) => files.ProxyTusHeadAsync(
                tokenProvider.BearerToken,
                Route<string>("uploadSessionId")!,
                organizationId,
                environmentId,
                HttpContext.Response,
                cancellationToken),
            ct);
}

[Tags("Business Console Files")]
[HttpPatch("/api/business-console/v1/files/sop-documents/tus/{uploadSessionId}")]
[BusinessGatewayOperationId("patchBusinessConsoleSopFileTusUpload")]
[Authorize(Policy = BusinessGatewayPolicies.BusinessConsoleAuthenticated)]
public sealed class PatchBusinessConsoleSopFileTusUploadEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessFileTransferClient files,
    IInternalServiceTokenProvider tokenProvider)
    : EndpointWithoutRequest
{
    public override Task HandleAsync(CancellationToken ct) =>
        BusinessConsoleFileTransfer.ProxyAsync(
            HttpContext,
            auth,
            BusinessGatewayPermissions.EngineeringDocumentsManage,
            "engineering-sop-file-upload",
            Route<string>("uploadSessionId")!,
            (organizationId, environmentId, cancellationToken) => files.ProxyTusPatchAsync(
                tokenProvider.BearerToken,
                Route<string>("uploadSessionId")!,
                organizationId,
                environmentId,
                HttpContext.Request,
                HttpContext.Response,
                cancellationToken),
            ct);
}
