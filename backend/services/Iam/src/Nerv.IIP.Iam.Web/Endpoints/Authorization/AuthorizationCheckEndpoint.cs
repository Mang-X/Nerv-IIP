using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.Iam.Web.Application.Auth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Iam.Web.Endpoints.Authorization;

[HttpPost("/internal/iam/v1/authorization/check")]
[AllowAnonymous]
public sealed class AuthorizationCheckEndpoint(IIamAuthService auth) : Endpoint<AuthorizationCheckRequest, ResponseData<AuthorizationCheckResponse>>
{
    public override async Task HandleAsync(AuthorizationCheckRequest req, CancellationToken ct)
    {
        var principal = await auth.GetCurrentPrincipalAsync(HttpContext, ct);
        if (principal is null)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status401Unauthorized, "unauthorized", ct);
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
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status403Forbidden, "forbidden", ct);
            return;
        }

        await Send.OkAsync(
            new AuthorizationCheckResponse(true, principal.UserId, principal.PrincipalType, principal.LoginName, null).AsResponseData(),
            ct);
    }
}
