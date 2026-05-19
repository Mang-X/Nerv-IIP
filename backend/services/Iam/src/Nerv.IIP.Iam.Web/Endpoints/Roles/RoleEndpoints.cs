using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Iam.Web.Endpoints;
using Nerv.IIP.Iam.Web.Application.Roles;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Iam.Web.Endpoints.Roles;

[HttpGet("/api/iam/v1/roles")]
[AllowAnonymous]
public sealed class ListRolesEndpoint(
    IIamPermissionAuthorizer authorizer,
    IIamRoleApplicationService roles) : EndpointWithoutRequest<ResponseData<IReadOnlyList<RoleResponse>>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.roles.read", ct))
        {
            return;
        }

        var response = await roles.ListRolesAsync(ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

[HttpPost("/api/iam/v1/roles")]
[AllowAnonymous]
public sealed class CreateRoleEndpoint(
    IIamPermissionAuthorizer authorizer,
    IIamRoleApplicationService roles) : EndpointWithoutRequest<ResponseData<RoleMutationResponse>>
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

        await ResponseDataEndpointResults.WriteDataAsync(HttpContext, StatusCodes.Status201Created, result.Response!, ct);
    }
}

[HttpPatch("/api/iam/v1/roles/{roleId}/permissions")]
[AllowAnonymous]
public sealed class PatchRolePermissionsEndpoint(
    IIamPermissionAuthorizer authorizer,
    IIamRoleApplicationService roles) : EndpointWithoutRequest<ResponseData<RoleMutationResponse>>
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

        await Send.OkAsync(result.Response!.AsResponseData(), ct);
    }
}

internal static class RoleEndpointResults
{
    public static async Task WriteNotImplementedAsync(HttpContext context, string detail, CancellationToken cancellationToken)
    {
        await ResponseDataEndpointResults.WriteErrorAsync(context, StatusCodes.Status501NotImplemented, detail, cancellationToken);
    }
}
