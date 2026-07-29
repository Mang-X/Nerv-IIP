using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.Iam.Web.Application.Auth;
using Nerv.IIP.Iam.Web.Application.Roles;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Iam.Web.Endpoints.Authorization;

[HttpPost("/internal/iam/v1/authorization/check")]
[AllowAnonymous]
public sealed class AuthorizationCheckEndpoint(
    IIamAuthService auth,
    IIamRoleApplicationService roles) : Endpoint<AuthorizationCheckRequest, ResponseData<AuthorizationCheckResponse>>
{
    public override async Task HandleAsync(AuthorizationCheckRequest req, CancellationToken ct)
    {
        var principal = await auth.GetCurrentPrincipalAsync(HttpContext, ct);
        if (principal is null)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status401Unauthorized, "unauthorized", ct);
            return;
        }

        var authorization = await auth.PrincipalHasPermissionAsync(
            principal,
            req.OrganizationId,
            req.EnvironmentId,
            req.PermissionCode,
            req.ResourceType,
            req.ResourceId,
            ct);

        if (!authorization.Allowed)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status403Forbidden, "forbidden", ct);
            return;
        }

        var resolvedRoles = req.IncludePrincipalContext
            ? await roles.ResolveRolesAsync(principal.RoleIds, ct)
            : [];
        await Send.OkAsync(
            new AuthorizationCheckResponse(
                true,
                principal.UserId,
                principal.PrincipalType,
                principal.LoginName,
                null,
                authorization.DataScope,
                req.IncludePrincipalContext ? authorization.ScopeGrants : null,
                resolvedRoles.Select(x => new AuthorizationRole(x.RoleId, x.RoleName)).ToArray()).AsResponseData(),
            ct);
    }
}
