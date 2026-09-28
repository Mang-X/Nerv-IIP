using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Iam.Web.Application;
using Nerv.IIP.Iam.Web.Application.Auth;
using Nerv.IIP.Iam.Web.Application.Commands.Users;
using Nerv.IIP.Iam.Web.Application.DataScopes;
using Nerv.IIP.Iam.Web.Application.Queries.Users;
using Nerv.IIP.Iam.Web.Application.SecurityAudit;
using Nerv.IIP.Iam.Web.Application.Users;
using Nerv.IIP.Iam.Web.Endpoints;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Iam.Web.Endpoints.Users;

public sealed record CreateUserRequest(string LoginName, string Email, string Password, DateTimeOffset? AccountExpiresAtUtc);
public sealed record UpdateUserRequest(string LoginName, string Email, bool Enabled, DateTimeOffset? AccountExpiresAtUtc);
public sealed record ResetUserPasswordRequest(string NewPassword);
public sealed record ReplaceUserMembershipRolesRequest(IReadOnlyList<string>? RoleIds);
public sealed record ListUsersRequest(
    int? PageIndex,
    int? PageSize,
    string? SortBy,
    string? SortOrder,
    string? FilterSearch,
    bool? FilterEnabled);

public sealed record WorkerDirectoryUserResponse(
    string UserId,
    string DisplayName,
    string? EmployeeNo,
    string? Department,
    string Status,
    string? Email);

[HttpGet("/api/iam/v1/users")]
[AllowAnonymous]
public sealed class ListUsersEndpoint(IIamPermissionAuthorizer authorizer, IMediator mediator)
    : Endpoint<ListUsersRequest, ResponseData<PagedListResponse<UserResponse>>>
{
    public override async Task HandleAsync(ListUsersRequest req, CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.users.read", ct))
        {
            return;
        }

        var users = await mediator.Send(new ListUsersQuery(IamListQueryOptions.Create(
            req.PageIndex,
            req.PageSize,
            req.SortBy,
            req.SortOrder,
            req.FilterSearch,
            filterEnabled: req.FilterEnabled)), ct);
        await Send.OkAsync(users.AsResponseData(), ct);
    }
}

[HttpGet("/internal/iam/v1/workers")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class ListWorkerDirectoryEndpoint(IMediator mediator)
    : Endpoint<ListUsersRequest, ResponseData<PagedListResponse<WorkerDirectoryUserResponse>>>
{
    public override async Task HandleAsync(ListUsersRequest req, CancellationToken ct)
    {
        var users = await mediator.Send(new ListUsersQuery(IamListQueryOptions.Create(
            req.PageIndex,
            req.PageSize,
            req.SortBy,
            req.SortOrder,
            req.FilterSearch,
            filterEnabled: req.FilterEnabled)), ct);

        var response = new PagedListResponse<WorkerDirectoryUserResponse>(
            users.PageIndex,
            users.PageSize,
            users.TotalCount,
            users.Items.Select(ToWorker).ToArray());
        await Send.OkAsync(response.AsResponseData(), ct);
    }

    private static WorkerDirectoryUserResponse ToWorker(UserResponse user)
    {
        var status = user.Enabled ? "active" : "disabled";
        return new WorkerDirectoryUserResponse(
            user.UserId,
            // 工人档案缺失时回落到登录名，绝不编造姓名。
            string.IsNullOrWhiteSpace(user.DisplayName) ? user.LoginName : user.DisplayName,
            user.EmployeeNo,
            user.DepartmentName,
            status,
            user.Email);
    }
}

public sealed record ListMemberAccountsRequest(
    string? OrganizationId,
    string? EnvironmentId,
    string? Keyword,
    string[]? UserIds,
    bool? IncludeDisabled,
    int? PageIndex,
    int? PageSize);

/// <summary>
/// 组织/环境成员账号目录，仅供服务间调用（业务网关「关联登录账号」的候选与核验）。
/// 只回账号 ID、登录名、显示名与启用状态；默认只列启用账号。
/// </summary>
[HttpGet("/internal/iam/v1/member-accounts")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class ListMemberAccountsEndpoint(IMediator mediator)
    : Endpoint<ListMemberAccountsRequest, ResponseData<PagedListResponse<MemberAccountResponse>>>
{
    public override async Task HandleAsync(ListMemberAccountsRequest req, CancellationToken ct)
    {
        var organizationId = req.OrganizationId?.Trim();
        var environmentId = req.EnvironmentId?.Trim();
        if (string.IsNullOrEmpty(organizationId) || string.IsNullOrEmpty(environmentId))
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status400BadRequest, "member-accounts-scope-required", ct);
            return;
        }

        var pageIndex = req.PageIndex ?? 1;
        var pageSize = req.PageSize ?? 20;
        if (pageIndex < 1 || pageSize is < 1 or > MemberAccountListOptions.MaxPageSize)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status400BadRequest, "member-accounts-page-invalid", ct);
            return;
        }

        string[]? userIds = null;
        if (req.UserIds is { Length: > 0 })
        {
            userIds = req.UserIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (userIds.Length > MemberAccountListOptions.MaxUserIds)
            {
                await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status400BadRequest, "member-accounts-user-ids-too-many", ct);
                return;
            }
        }

        var keyword = string.IsNullOrWhiteSpace(req.Keyword) ? null : req.Keyword.Trim();
        var response = await mediator.Send(
            new ListMemberAccountsQuery(new MemberAccountListOptions(
                organizationId,
                environmentId,
                keyword,
                userIds,
                req.IncludeDisabled ?? false,
                pageIndex,
                pageSize)),
            ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

[HttpPost("/api/iam/v1/users")]
[AllowAnonymous]
public sealed class CreateUserEndpoint(IIamPermissionAuthorizer authorizer, IMediator mediator)
    : EndpointWithoutRequest<ResponseData<UserResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.users.manage", ct))
        {
            return;
        }

        var req = await HttpContext.Request.ReadFromJsonAsync<CreateUserRequest>(ct)
            ?? throw new BadHttpRequestException("Request body is required.");
        var response = await mediator.Send(new CreateUserCommand(req.LoginName, req.Email, req.Password, req.AccountExpiresAtUtc), ct);
        await ResponseDataEndpointResults.WriteDataAsync(HttpContext, StatusCodes.Status201Created, response, ct);
    }
}

[HttpPatch("/api/iam/v1/users/{userId}")]
[AllowAnonymous]
public sealed class PatchUserEndpoint(IIamPermissionAuthorizer authorizer, IMediator mediator)
    : EndpointWithoutRequest<ResponseData<UserResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.users.manage", ct))
        {
            return;
        }

        var req = await HttpContext.Request.ReadFromJsonAsync<UpdateUserRequest>(ct)
            ?? throw new BadHttpRequestException("Request body is required.");
        var userId = Route<string>("userId") ?? string.Empty;
        var response = await mediator.Send(new UpdateUserCommand(userId, req.LoginName, req.Email, req.Enabled, req.AccountExpiresAtUtc), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

[HttpPost("/api/iam/v1/users/{userId}/enable")]
[AllowAnonymous]
public sealed class EnableUserEndpoint(IIamPermissionAuthorizer authorizer, IMediator mediator)
    : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.users.manage", ct))
        {
            return;
        }

        var userId = Route<string>("userId") ?? string.Empty;
        await mediator.Send(new EnableUserCommand(userId), ct);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}

[HttpPost("/api/iam/v1/users/{userId}/disable")]
[AllowAnonymous]
public sealed class DisableUserEndpoint(IIamPermissionAuthorizer authorizer, IMediator mediator)
    : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.users.manage", ct))
        {
            return;
        }

        var userId = Route<string>("userId") ?? string.Empty;
        await mediator.Send(new DisableUserCommand(userId), ct);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}

[HttpPost("/api/iam/v1/users/{userId}/reset-password")]
[AllowAnonymous]
public sealed class ResetUserPasswordEndpoint(IIamPermissionAuthorizer authorizer, IMediator mediator)
    : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.users.manage", ct))
        {
            return;
        }

        var req = await HttpContext.Request.ReadFromJsonAsync<ResetUserPasswordRequest>(ct)
            ?? throw new BadHttpRequestException("Request body is required.");
        var userId = Route<string>("userId") ?? string.Empty;
        await mediator.Send(new ResetUserPasswordCommand(userId, SensitivePassword.From(req.NewPassword)), ct);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}

[HttpPatch("/api/iam/v1/users/{userId}/membership-data-scopes")]
[AllowAnonymous]
public sealed class PatchUserMembershipDataScopesEndpoint(
    IIamPermissionAuthorizer authorizer,
    IIamAuthService auth,
    IMediator mediator) : EndpointWithoutRequest<ResponseData<DataScopeListResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.users.manage", ct))
        {
            return;
        }

        var req = await HttpContext.Request.ReadFromJsonAsync<PatchMembershipDataScopesRequest>(ct)
            ?? throw new BadHttpRequestException("Request body is required.");
        var principal = await auth.GetCurrentPrincipalAsync(HttpContext, ct);
        var userId = Route<string>("userId") ?? string.Empty;
        var response = await mediator.Send(
            new PatchMembershipDataScopesCommand(
                userId,
                req.OrganizationId,
                req.EnvironmentId,
                req.DataScopes,
                IamSecurityAuditEndpointContext.Create(HttpContext, principal)),
            ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

[HttpGet("/api/iam/v1/users/{userId}/membership")]
[AllowAnonymous]
public sealed class GetUserMembershipEndpoint(
    IIamPermissionAuthorizer authorizer,
    IIamAuthService auth,
    IMediator mediator) : EndpointWithoutRequest<ResponseData<UserMembershipResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.users.read", ct))
        {
            return;
        }

        var principal = await auth.GetCurrentPrincipalAsync(HttpContext, ct);
        if (principal is null)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status401Unauthorized, "Unauthorized.", ct);
            return;
        }

        var userId = Route<string>("userId") ?? string.Empty;
        var response = await mediator.Send(
            new GetUserMembershipQuery(userId, principal.OrganizationId, principal.EnvironmentId),
            ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

/// <summary>
/// 在调用者当前组织环境里替换用户的角色；没有成员关系时新建，<c>roleIds</c> 为空时移除成员关系。
/// </summary>
[HttpPut("/api/iam/v1/users/{userId}/membership")]
[AllowAnonymous]
public sealed class ReplaceUserMembershipRolesEndpoint(
    IIamPermissionAuthorizer authorizer,
    IIamAuthService auth,
    IMediator mediator) : EndpointWithoutRequest<ResponseData<UserMembershipResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.users.manage", ct))
        {
            return;
        }

        var principal = await auth.GetCurrentPrincipalAsync(HttpContext, ct);
        if (principal is null)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status401Unauthorized, "Unauthorized.", ct);
            return;
        }

        var req = await HttpContext.Request.ReadFromJsonAsync<ReplaceUserMembershipRolesRequest>(ct);
        if (req?.RoleIds is null)
        {
            throw new BadHttpRequestException("roleIds is required.");
        }

        var userId = Route<string>("userId") ?? string.Empty;
        var response = await mediator.Send(
            new ReplaceUserMembershipRolesCommand(
                userId,
                principal.OrganizationId,
                principal.EnvironmentId,
                req.RoleIds,
                IamSecurityAuditEndpointContext.Create(HttpContext, principal)),
            ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}
