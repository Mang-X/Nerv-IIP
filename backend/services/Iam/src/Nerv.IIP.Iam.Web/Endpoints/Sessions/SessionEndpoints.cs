using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Iam.Web.Endpoints;
using Nerv.IIP.Iam.Web.Application.Sessions;

namespace Nerv.IIP.Iam.Web.Endpoints.Sessions;

[HttpGet("/api/iam/v1/sessions")]
[AllowAnonymous]
public sealed class ListSessionsEndpoint(
    IIamPermissionAuthorizer authorizer,
    IIamSessionApplicationService sessions) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.sessions.read", ct))
        {
            return;
        }

        await HttpContext.Response.WriteAsJsonAsync(await sessions.ListSessionsAsync(ct), ct);
    }
}

[HttpPost("/api/iam/v1/sessions/{sessionId}/revoke")]
[AllowAnonymous]
public sealed class RevokeSessionEndpoint(
    IIamPermissionAuthorizer authorizer,
    IIamSessionApplicationService sessions) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.sessions.revoke", ct))
        {
            return;
        }

        await sessions.RevokeSessionAsync(Route<string>("sessionId")!, ct);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}
