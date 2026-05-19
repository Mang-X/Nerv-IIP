using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Iam.Web.Endpoints;
using Nerv.IIP.Iam.Web.Application.Roles;

namespace Nerv.IIP.Iam.Web.Endpoints.Roles;

[HttpGet("/api/iam/v1/roles")]
[AllowAnonymous]
public sealed class ListRolesEndpoint(
    IIamPermissionAuthorizer authorizer,
    IIamRoleApplicationService roles) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.roles.read", ct))
        {
            return;
        }

        await HttpContext.Response.WriteAsJsonAsync(await roles.ListRolesAsync(ct), ct);
    }
}

[HttpPost("/api/iam/v1/roles")]
[AllowAnonymous]
public sealed class CreateRoleEndpoint(
    IIamPermissionAuthorizer authorizer,
    IIamRoleApplicationService roles) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.roles.manage", ct))
        {
            return;
        }

        var result = await roles.CreateRoleAsync(ct);
        if (!result.IsImplemented)
        {
            await RoleEndpointResults.WriteNotImplementedAsync(HttpContext, result.Detail ?? "Role creation is not implemented.", ct);
            return;
        }

        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
        await HttpContext.Response.WriteAsJsonAsync(result.Response, ct);
    }
}

[HttpPatch("/api/iam/v1/roles/{roleId}/permissions")]
[AllowAnonymous]
public sealed class PatchRolePermissionsEndpoint(
    IIamPermissionAuthorizer authorizer,
    IIamRoleApplicationService roles) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.roles.manage", ct))
        {
            return;
        }

        var result = await roles.PatchRolePermissionsAsync(Route<string>("roleId")!, ct);
        if (!result.IsImplemented)
        {
            await RoleEndpointResults.WriteNotImplementedAsync(
                HttpContext,
                result.Detail ?? "Role permission updates are not implemented.",
                ct);
            return;
        }

        await HttpContext.Response.WriteAsJsonAsync(result.Response, ct);
    }
}

internal static class RoleEndpointResults
{
    public static async Task WriteNotImplementedAsync(HttpContext context, string detail, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status501NotImplemented;
        await context.Response.WriteAsJsonAsync(new { title = "Not Implemented", detail, status = StatusCodes.Status501NotImplemented }, cancellationToken);
    }
}
