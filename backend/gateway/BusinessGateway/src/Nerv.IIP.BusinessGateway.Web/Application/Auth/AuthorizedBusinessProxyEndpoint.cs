using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.BusinessGateway.Web.Application.Auth;

[Authorize(Policy = BusinessGatewayPolicies.BusinessConsoleAuthenticated)]
public abstract class AuthorizedBusinessProxyEndpoint<TRequest, TResponse>(
    IBusinessGatewayAuthorizationClient auth,
    string permissionCode) : Endpoint<TRequest, ResponseData<TResponse>>
    where TRequest : notnull
{
    public override async Task HandleAsync(TRequest req, CancellationToken ct)
    {
        var bearerToken = await BusinessGatewayAuthorization.RequirePermissionAsync(
            HttpContext,
            auth,
            new BusinessGatewayPermissionRequirement(
                permissionCode,
                OrganizationId(req),
                EnvironmentId(req),
                ResourceType(req),
                ResourceId(req)),
            ct);
        if (bearerToken is null)
        {
            return;
        }

        try
        {
            var response = await ForwardAsync(req, bearerToken, ct);
            await ResponseDataEndpointResults.WriteDataAsync(HttpContext, StatusCode, response, ct);
        }
        catch (BusinessServiceProxyException ex)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                HttpContext,
                (int)ex.StatusCode,
                ex.Message,
                ct);
        }
    }

    protected virtual int StatusCode => StatusCodes.Status200OK;

    protected virtual string? ResourceType(TRequest request) => null;

    protected virtual string? ResourceId(TRequest request) => null;

    protected abstract string OrganizationId(TRequest request);

    protected abstract string EnvironmentId(TRequest request);

    protected abstract Task<TResponse> ForwardAsync(
        TRequest request,
        string bearerToken,
        CancellationToken cancellationToken);
}
