using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Iam.Web.Application;
using Nerv.IIP.Iam.Web.Application.Auth;
using Nerv.IIP.Iam.Web.Application.Commands.Roles;
using Nerv.IIP.Iam.Web.Application.Permissions;
using Nerv.IIP.Iam.Web.Application.SecurityAudit;
using Nerv.IIP.Iam.Web.Endpoints;
using Nerv.IIP.Iam.Web.Application.Roles;
using NetCorePal.Extensions.Dto;

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
    IIamRoleApplicationService roles) : Endpoint<ListRolesRequest, ResponseData<PagedListResponse<RoleResponse>>>
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
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

[HttpPost("/api/iam/v1/roles")]
[AllowAnonymous]
public sealed class CreateRoleEndpoint(
    IIamPermissionAuthorizer authorizer,
    IMediator mediator) : EndpointWithoutRequest<ResponseData<RoleResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.roles.manage", ct))
        {
            return;
        }

        var req = await HttpContext.Request.ReadFromJsonAsync<CreateRoleRequest>(ct)
            ?? throw new BadHttpRequestException("Request body is required.");
        var response = await mediator.Send(new CreateRoleCommand(req.RoleName, req.PermissionCodes), ct);
        await ResponseDataEndpointResults.WriteDataAsync(HttpContext, StatusCodes.Status201Created, response, ct);
    }
}

[HttpPatch("/api/iam/v1/roles/{roleId}/permissions")]
[AllowAnonymous]
public sealed class PatchRolePermissionsEndpoint(
    IIamPermissionAuthorizer authorizer,
    IIamAuthService auth,
    IMediator mediator) : EndpointWithoutRequest<ResponseData<RoleResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.roles.manage", ct))
        {
            return;
        }

        var req = await HttpContext.Request.ReadFromJsonAsync<PatchRolePermissionsRequest>(ct)
            ?? throw new BadHttpRequestException("Request body is required.");
        var principal = await auth.GetCurrentPrincipalAsync(HttpContext, ct);
        var response = await mediator.Send(
            new PatchRolePermissionsCommand(
                Route<string>("roleId")!,
                req.PermissionCodes,
                IamSecurityAuditEndpointContext.Create(HttpContext, principal)),
            ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}


[HttpGet("/api/iam/v1/permissions")]
[AllowAnonymous]
public sealed class ListPermissionCatalogEndpoint(IIamPermissionAuthorizer authorizer)
    : EndpointWithoutRequest<ResponseData<PermissionCatalogResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.roles.read", ct))
        {
            return;
        }

        await Send.OkAsync(IamPermissionCatalog.List().AsResponseData(), ct);
    }
}
