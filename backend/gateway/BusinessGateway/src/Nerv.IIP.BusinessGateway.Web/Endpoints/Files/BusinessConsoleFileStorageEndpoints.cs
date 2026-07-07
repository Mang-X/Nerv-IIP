using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
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

[Tags("Business Console Files")]
[HttpGet("/api/business-console/v1/files/download-grants/{downloadGrantId}/content")]
[BusinessGatewayOperationId("downloadBusinessConsoleSopFileContent")]
[Authorize(Policy = BusinessGatewayPolicies.BusinessConsoleAuthenticated)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK, "application/octet-stream")]
public sealed class DownloadBusinessConsoleSopFileContentEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessFileStorageClient files,
    IInternalServiceTokenProvider tokenProvider)
    : EndpointWithoutRequest
{
    private const string OrganizationHeader = "X-Organization-Id";
    private const string EnvironmentHeader = "X-Environment-Id";

    public override async Task HandleAsync(CancellationToken ct)
    {
        var organizationId = FirstHeaderOrQuery(OrganizationHeader, "organizationId");
        var environmentId = FirstHeaderOrQuery(EnvironmentHeader, "environmentId");
        if (string.IsNullOrWhiteSpace(organizationId) || string.IsNullOrWhiteSpace(environmentId))
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status400BadRequest, "Download grant headers are required.", ct);
            return;
        }

        var downloadGrantId = Route<string>("downloadGrantId")!;
        var bearerToken = await BusinessGatewayAuthorization.RequirePermissionAsync(
            HttpContext,
            auth,
            new BusinessGatewayPermissionRequirement(
                BusinessGatewayPermissions.EngineeringDocumentsRead,
                organizationId,
                environmentId,
                "engineering-sop-download-grant",
                downloadGrantId),
            ct);
        if (bearerToken is null)
        {
            return;
        }

        try
        {
            var response = await files.DownloadSopFileContentAsync(
                tokenProvider.BearerToken,
                downloadGrantId,
                new Dictionary<string, string>
                {
                    [OrganizationHeader] = organizationId,
                    [EnvironmentHeader] = environmentId,
                },
                ct);
            HttpContext.Response.ContentType = response.ContentType;
            if (response.ContentLength is not null)
            {
                HttpContext.Response.ContentLength = response.ContentLength.Value;
            }

            await HttpContext.Response.Body.WriteAsync(response.Content, ct);
        }
        catch (BusinessServiceProxyException ex)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, (int)ex.StatusCode, ex.Message, ct);
        }
    }

    private string? FirstHeaderOrQuery(string headerName, string queryName)
    {
        var header = HttpContext.Request.Headers[headerName].ToString();
        return string.IsNullOrWhiteSpace(header)
            ? HttpContext.Request.Query[queryName].ToString()
            : header;
    }
}
