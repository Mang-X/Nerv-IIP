using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.Iam.Web.Application.Auth;

namespace Nerv.IIP.Iam.Web.Endpoints.Authorization;

[HttpPost("/internal/iam/v1/authorization/check")]
[AllowAnonymous]
public sealed class AuthorizationCheckEndpoint(IIamAuthService auth) : Endpoint<AuthorizationCheckRequest, AuthorizationCheckResponse>
{
    public override async Task HandleAsync(AuthorizationCheckRequest req, CancellationToken ct)
    {
        var principal = await auth.GetCurrentPrincipalAsync(HttpContext, ct);
        if (principal is null)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await HttpContext.Response.WriteAsJsonAsync(
                new AuthorizationCheckResponse(false, null, null, null, "unauthorized"),
                ct);
            return;
        }

        var allowed = await auth.UserHasPermissionAsync(
            principal.UserId,
            req.OrganizationId,
            req.EnvironmentId,
            req.PermissionCode,
            ct);

        if (!allowed)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            await HttpContext.Response.WriteAsJsonAsync(
                new AuthorizationCheckResponse(false, principal.UserId, principal.PrincipalType, principal.LoginName, "forbidden"),
                ct);
            return;
        }

        await HttpContext.Response.WriteAsJsonAsync(
            new AuthorizationCheckResponse(true, principal.UserId, principal.PrincipalType, principal.LoginName, null),
            ct);
    }
}
