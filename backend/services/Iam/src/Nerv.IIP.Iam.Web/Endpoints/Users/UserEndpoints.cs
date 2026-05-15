using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Iam.Infrastructure;

namespace Nerv.IIP.Iam.Web.Endpoints.Users;

[HttpGet("/api/iam/v1/users")]
[AllowAnonymous]
public sealed class ListUsersEndpoint(InMemoryIamStore store) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await HttpContext.Response.WriteAsJsonAsync(store.Users.Select(x => new { x.UserId, x.LoginName, x.Email, x.Enabled }), ct);
    }
}

[HttpPost("/api/iam/v1/users")]
[AllowAnonymous]
public sealed class CreateUserEndpoint : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
        await HttpContext.Response.WriteAsJsonAsync(new { userId = "user-placeholder" }, ct);
    }
}

[HttpPatch("/api/iam/v1/users/{userId}")]
[AllowAnonymous]
public sealed class PatchUserEndpoint : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await HttpContext.Response.WriteAsJsonAsync(new { userId = Route<string>("userId") }, ct);
    }
}

[HttpPost("/api/iam/v1/users/{userId}/disable")]
[AllowAnonymous]
public sealed class DisableUserEndpoint : EndpointWithoutRequest
{
    public override Task HandleAsync(CancellationToken ct)
    {
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
        return Task.CompletedTask;
    }
}
