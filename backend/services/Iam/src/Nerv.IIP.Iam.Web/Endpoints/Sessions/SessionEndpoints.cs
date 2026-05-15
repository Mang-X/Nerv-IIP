using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Iam.Infrastructure;

namespace Nerv.IIP.Iam.Web.Endpoints.Sessions;

[HttpGet("/api/iam/v1/sessions")]
[AllowAnonymous]
public sealed class ListSessionsEndpoint(InMemoryIamStore store) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await HttpContext.Response.WriteAsJsonAsync(store.Sessions, ct);
    }
}

[HttpPost("/api/iam/v1/sessions/{sessionId}/revoke")]
[AllowAnonymous]
public sealed class RevokeSessionEndpoint(InMemoryIamStore store) : EndpointWithoutRequest
{
    public override Task HandleAsync(CancellationToken ct)
    {
        store.Logout(Route<string>("sessionId")!);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
        return Task.CompletedTask;
    }
}
