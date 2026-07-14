using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using NetCorePal.Extensions.Dto;
using System.Net;
using System.Text.Json;

namespace Nerv.IIP.BusinessGateway.Web.Application.Auth;

[Authorize(Policy = BusinessGatewayPolicies.BusinessConsoleAuthenticated)]
public abstract class AuthorizedBusinessProxyEndpoint<TRequest, TResponse>(
    IBusinessGatewayAuthorizationClient auth,
    string permissionCode) : Endpoint<TRequest, ResponseData<TResponse>>
    where TRequest : notnull
{
    protected IBusinessGatewayAuthorizationClient AuthorizationClient => auth;

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
            await ResponseDataEndpointResults.WriteDataAsync(HttpContext, StatusCode, response, ct, ResponseJsonOptions);
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

    protected virtual JsonSerializerOptions? ResponseJsonOptions => null;

    protected virtual string? ResourceType(TRequest request) => null;

    protected virtual string? ResourceId(TRequest request) => null;

    protected BusinessGatewayAuthorizationResult? AuthorizationResult =>
        HttpContext.Items.TryGetValue(BusinessGatewayAuthorization.PrincipalItemKey, out var value)
            ? value as BusinessGatewayAuthorizationResult
            : null;

    protected (string ActorType, string ActorRef) RequireAuthorizedPrincipalActor()
    {
        var authorization = AuthorizationResult
            ?? throw new BusinessServiceProxyException(HttpStatusCode.Forbidden, "approval-principal-unresolved");
        var actorRef = authorization.PrincipalId ?? authorization.LoginName;
        if (string.IsNullOrWhiteSpace(actorRef))
        {
            throw new BusinessServiceProxyException(HttpStatusCode.Forbidden, "approval-principal-unresolved");
        }

        var actorType = string.IsNullOrWhiteSpace(authorization.PrincipalType)
            ? "user"
            : authorization.PrincipalType;
        return (actorType, actorRef);
    }

    protected string RequireAuthorizedPrincipalRecipientRef()
        => RequireAuthorizedPrincipalActorReference();

    protected string RequireAuthorizedPrincipalActorReference()
    {
        var (actorType, actorRef) = RequireAuthorizedPrincipalActor();
        return BusinessGatewayPrincipalReferences.ToRecipientRef(actorType, actorRef);
    }

    protected abstract string OrganizationId(TRequest request);

    protected abstract string EnvironmentId(TRequest request);

    protected abstract Task<TResponse> ForwardAsync(
        TRequest request,
        string bearerToken,
        CancellationToken cancellationToken);
}

internal static class BusinessGatewayPrincipalReferences
{
    public static string ToRecipientRef(string actorType, string actorRef) =>
        $"{actorType.Trim().ToLowerInvariant()}:{actorRef.Trim()}";
}
