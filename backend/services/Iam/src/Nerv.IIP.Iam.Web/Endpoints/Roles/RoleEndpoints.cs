using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Iam.Infrastructure;

namespace Nerv.IIP.Iam.Web.Endpoints.Roles;

[HttpGet("/api/iam/v1/roles")]
[AllowAnonymous]
public sealed class ListRolesEndpoint(InMemoryIamStore store) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await HttpContext.Response.WriteAsJsonAsync(store.Roles, ct);
    }
}

[HttpPost("/api/iam/v1/roles")]
[AllowAnonymous]
public sealed class CreateRoleEndpoint : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
        await HttpContext.Response.WriteAsJsonAsync(new { roleId = "role-placeholder" }, ct);
    }
}

[HttpPatch("/api/iam/v1/roles/{roleId}/permissions")]
[AllowAnonymous]
public sealed class PatchRolePermissionsEndpoint : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await HttpContext.Response.WriteAsJsonAsync(new { roleId = Route<string>("roleId") }, ct);
    }
}
