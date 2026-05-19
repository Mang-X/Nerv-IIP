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

        try
        {
            var response = await roles.CreateRoleAsync(ct);
            HttpContext.Response.StatusCode = StatusCodes.Status201Created;
            await HttpContext.Response.WriteAsJsonAsync(response, ct);
        }
        catch (NotImplementedException ex)
        {
            await WriteNotImplementedAsync(HttpContext, ex.Message, ct);
        }
    }

    private static async Task WriteNotImplementedAsync(HttpContext context, string detail, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status501NotImplemented;
        await context.Response.WriteAsJsonAsync(new { title = "Not Implemented", detail, status = StatusCodes.Status501NotImplemented }, cancellationToken);
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

        try
        {
            var response = await roles.PatchRolePermissionsAsync(Route<string>("roleId")!, ct);
            await HttpContext.Response.WriteAsJsonAsync(response, ct);
        }
        catch (NotImplementedException ex)
        {
            await WriteNotImplementedAsync(HttpContext, ex.Message, ct);
        }
    }

    private static async Task WriteNotImplementedAsync(HttpContext context, string detail, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status501NotImplemented;
        await context.Response.WriteAsJsonAsync(new { title = "Not Implemented", detail, status = StatusCodes.Status501NotImplemented }, cancellationToken);
    }
}
