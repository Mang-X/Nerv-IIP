using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Iam.Web.Application;
using Nerv.IIP.Iam.Web.Endpoints;
using Nerv.IIP.Iam.Web.Application.Roles;

namespace Nerv.IIP.Iam.Web.Endpoints.Roles;

public sealed record ListRolesRequest(
    int? PageIndex,
    int? PageSize,
    string? SortBy,
    string? SortOrder,
    string? FilterSearch);

[HttpGet("/api/iam/v1/roles")]
[AllowAnonymous]
public sealed class ListRolesEndpoint(
    IIamPermissionAuthorizer authorizer,
    IIamRoleApplicationService roles) : Endpoint<ListRolesRequest, PagedListResponse<RoleResponse>>
{
    public override async Task HandleAsync(ListRolesRequest req, CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.roles.read", ct))
        {
            return;
        }

        var response = await roles.ListRolesAsync(IamListQueryOptions.Create(
            req.PageIndex,
            req.PageSize,
            req.SortBy,
            req.SortOrder,
            req.FilterSearch), ct);
        await HttpContext.Response.WriteAsJsonAsync(response, ct);
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
