using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Files;

[Tags("Business Console Files")]
[HttpPost("/api/business-console/v1/files/{fileId}/download-grants")]
[BusinessGatewayOperationId("createBusinessConsoleSopFileDownloadGrant")]
public sealed class CreateBusinessConsoleSopFileDownloadGrantEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessFileStorageClient files,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateSopFileDownloadGrantRequest, BusinessConsoleSopFileDownloadGrantResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringDocumentsRead)
{
    protected override string OrganizationId(BusinessConsoleCreateSopFileDownloadGrantRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateSopFileDownloadGrantRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleCreateSopFileDownloadGrantRequest request) => "engineering-sop-file";

    protected override string? ResourceId(BusinessConsoleCreateSopFileDownloadGrantRequest request) => Route<string>("fileId");

    protected override Task<BusinessConsoleSopFileDownloadGrantResponse> ForwardAsync(
        BusinessConsoleCreateSopFileDownloadGrantRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        files.CreateSopFileDownloadGrantAsync(tokenProvider.BearerToken, Route<string>("fileId")!, request, cancellationToken);
}

public sealed class BusinessConsoleCreateSopFileDownloadGrantRequestValidator : Validator<BusinessConsoleCreateSopFileDownloadGrantRequest>
{
    public BusinessConsoleCreateSopFileDownloadGrantRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}
