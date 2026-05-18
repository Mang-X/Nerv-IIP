using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Web.Application.Auth;
using Nerv.IIP.Iam.Web.Endpoints.Auth;

namespace Nerv.IIP.Iam.Web.Endpoints.Authorization;

[HttpPost("/internal/iam/v1/authorization/check")]
[AllowAnonymous]
public sealed class AuthorizationCheckEndpoint(
    IServiceProvider serviceProvider,
    IConfiguration configuration) : Endpoint<AuthorizationCheckRequest, AuthorizationCheckResponse>
{
    public override async Task HandleAsync(AuthorizationCheckRequest req, CancellationToken ct)
    {
        if (!IamEndpointResults.IsPostgreSql(configuration))
        {
            await HandleInMemoryAsync(req, ct);
            return;
        }

        var auth = serviceProvider.GetRequiredService<IamAuthService>();
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

    private async Task HandleInMemoryAsync(AuthorizationCheckRequest req, CancellationToken ct)
    {
        var store = serviceProvider.GetRequiredService<InMemoryIamStore>();
        var user = IamEndpointResults.ValidateBearer(HttpContext, store);
        if (user is null)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await HttpContext.Response.WriteAsJsonAsync(
                new AuthorizationCheckResponse(false, null, null, null, "unauthorized"),
                ct);
            return;
        }

        if (!store.UserHasPermission(user.UserId, req.OrganizationId, req.EnvironmentId, req.PermissionCode))
        {
            HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            await HttpContext.Response.WriteAsJsonAsync(
                new AuthorizationCheckResponse(false, user.UserId, "user", user.LoginName, "forbidden"),
                ct);
            return;
        }

        await HttpContext.Response.WriteAsJsonAsync(
            new AuthorizationCheckResponse(true, user.UserId, "user", user.LoginName, null),
            ct);
    }
}
