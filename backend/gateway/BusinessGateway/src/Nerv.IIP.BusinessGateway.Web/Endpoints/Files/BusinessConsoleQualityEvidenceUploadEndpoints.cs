using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Files;

[Tags("Business Console Files")]
[HttpPost("/api/business-console/v1/files/quality-evidence/upload-sessions")]
[BusinessGatewayOperationId("createBusinessConsoleQualityEvidenceUploadSession")]
public sealed class CreateBusinessConsoleQualityEvidenceUploadSessionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessFileStorageClient files,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<
        BusinessConsoleCreateQualityEvidenceUploadSessionRequest,
        BusinessConsoleQualityEvidenceUploadSessionResponse>(
        auth,
        BusinessGatewayPermissions.QualityNcrManage)
{
    protected override string OrganizationId(BusinessConsoleCreateQualityEvidenceUploadSessionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateQualityEvidenceUploadSessionRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleCreateQualityEvidenceUploadSessionRequest request) => "ncr";

    protected override string? ResourceId(BusinessConsoleCreateQualityEvidenceUploadSessionRequest request) => request.NcrId;

    protected override async Task<BusinessConsoleQualityEvidenceUploadSessionResponse> ForwardAsync(
        BusinessConsoleCreateQualityEvidenceUploadSessionRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var ncr = await quality.GetNcrAsync(tokenProvider.BearerToken,
            new BusinessConsoleQualityNcrDetailRequest(request.NcrId, request.OrganizationId, request.EnvironmentId),
            cancellationToken);
        return await files.CreateQualityEvidenceUploadSessionAsync(
            tokenProvider.BearerToken, ncr.Id, request, cancellationToken);
    }
}

public sealed class BusinessConsoleCreateQualityEvidenceUploadSessionRequestValidator
    : Validator<BusinessConsoleCreateQualityEvidenceUploadSessionRequest>
{
    public BusinessConsoleCreateQualityEvidenceUploadSessionRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.NcrId).NotEmpty().Must(value => Guid.TryParse(value, out _));
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(512);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(256);
        RuleFor(x => x.ExpectedSizeBytes).GreaterThan(0);
    }
}

[Tags("Business Console Files")]
[HttpPost("/api/business-console/v1/files/quality-evidence/upload-sessions/{uploadSessionId}/complete")]
[BusinessGatewayOperationId("completeBusinessConsoleQualityEvidenceUpload")]
public sealed class CompleteBusinessConsoleQualityEvidenceUploadEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessFileStorageClient files,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<
        BusinessConsoleCompleteQualityEvidenceUploadRequest,
        BusinessConsoleQualityEvidenceFile>(
        auth,
        BusinessGatewayPermissions.QualityNcrManage)
{
    protected override string OrganizationId(BusinessConsoleCompleteQualityEvidenceUploadRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCompleteQualityEvidenceUploadRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleCompleteQualityEvidenceUploadRequest request) => "ncr";

    protected override string? ResourceId(BusinessConsoleCompleteQualityEvidenceUploadRequest request) => request.NcrId;

    protected override async Task<BusinessConsoleQualityEvidenceFile> ForwardAsync(
        BusinessConsoleCompleteQualityEvidenceUploadRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var ncr = await quality.GetNcrAsync(tokenProvider.BearerToken,
            new BusinessConsoleQualityNcrDetailRequest(request.NcrId, request.OrganizationId, request.EnvironmentId),
            cancellationToken);
        return await files.CompleteQualityEvidenceUploadAsync(tokenProvider.BearerToken,
            Route<string>("uploadSessionId")!, request with { NcrId = ncr.Id }, cancellationToken);
    }
}

public sealed class BusinessConsoleCompleteQualityEvidenceUploadRequestValidator
    : Validator<BusinessConsoleCompleteQualityEvidenceUploadRequest>
{
    public BusinessConsoleCompleteQualityEvidenceUploadRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.NcrId).NotEmpty().Must(value => Guid.TryParse(value, out _));
    }
}

[Tags("Business Console Files")]
[BusinessGatewayOperationId("getBusinessConsoleQualityEvidenceTusOffset")]
[Authorize(Policy = BusinessGatewayPolicies.BusinessConsoleAuthenticated)]
public sealed class GetBusinessConsoleQualityEvidenceTusOffsetEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessFileTransferClient files,
    IInternalServiceTokenProvider tokenProvider)
    : EndpointWithoutRequest
{
    public override void Configure()
    {
        Head("/api/business-console/v1/files/quality-evidence/tus/{uploadSessionId}");
        Policies(BusinessGatewayPolicies.BusinessConsoleAuthenticated);
        Options(x => x.WithTags("Business Console Files"));
        Description(x => x.ClearDefaultProduces(StatusCodes.Status200OK).Produces(StatusCodes.Status200OK));
        DontAutoSendResponse();
    }

    public override Task HandleAsync(CancellationToken ct) =>
        BusinessConsoleFileTransfer.ProxyAsync(
            HttpContext,
            auth,
            BusinessGatewayPermissions.QualityNcrManage,
            "quality-ncr-evidence-upload",
            Route<string>("uploadSessionId")!,
            (organizationId, environmentId, cancellationToken) => files.ProxyTusHeadAsync(
                tokenProvider.BearerToken,
                Route<string>("uploadSessionId")!,
                organizationId,
                environmentId,
                HttpContext.Response,
                cancellationToken,
                QualityNcrEvidence.FilePurpose),
            ct);
}

[Tags("Business Console Files")]
[BusinessGatewayOperationId("patchBusinessConsoleQualityEvidenceTusUpload")]
[Authorize(Policy = BusinessGatewayPolicies.BusinessConsoleAuthenticated)]
public sealed class PatchBusinessConsoleQualityEvidenceTusUploadEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessFileTransferClient files,
    IInternalServiceTokenProvider tokenProvider)
    : EndpointWithoutRequest
{
    public override void Configure()
    {
        Patch("/api/business-console/v1/files/quality-evidence/tus/{uploadSessionId}");
        Policies(BusinessGatewayPolicies.BusinessConsoleAuthenticated);
        Options(x => x.WithTags("Business Console Files"));
        Description(x => x.Accepts<Stream>("application/offset+octet-stream"));
        DontAutoSendResponse();
    }

    public override Task HandleAsync(CancellationToken ct) =>
        BusinessConsoleFileTransfer.ProxyAsync(
            HttpContext,
            auth,
            BusinessGatewayPermissions.QualityNcrManage,
            "quality-ncr-evidence-upload",
            Route<string>("uploadSessionId")!,
            (organizationId, environmentId, cancellationToken) => files.ProxyTusPatchAsync(
                tokenProvider.BearerToken,
                Route<string>("uploadSessionId")!,
                organizationId,
                environmentId,
                HttpContext.Request,
                HttpContext.Response,
                cancellationToken,
                QualityNcrEvidence.FilePurpose),
            ct);
}
