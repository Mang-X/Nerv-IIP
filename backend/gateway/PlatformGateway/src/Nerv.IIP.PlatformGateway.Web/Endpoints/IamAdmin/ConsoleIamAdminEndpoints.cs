using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Application.IamAdmin;
using Nerv.IIP.PlatformGateway.Web.Application.OpenApi;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.PlatformGateway.Web.Endpoints.IamAdmin;

[Tags("Console IAM")]
[HttpGet("/api/console/v1/iam/users")]
[GatewayOperationId("listConsoleIamUsers")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class ListConsoleIamUsersEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayIamAdminClient admin)
    : AuthorizedProxyEndpoint<ConsoleIamListRequest, PagedListResponse<ConsoleIamUserResponse>>(
        iam,
        auth,
        GatewayPermissions.IamUsersRead)
{
    protected override Task<PagedListResponse<ConsoleIamUserResponse>> ForwardAsync(
        string bearerToken,
        ConsoleIamListRequest request,
        CancellationToken cancellationToken) =>
        admin.ListUsersAsync(bearerToken, request, cancellationToken);
}

[Tags("Console IAM")]
[HttpPost("/api/console/v1/iam/users")]
[GatewayOperationId("createConsoleIamUser")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(ResponseData<ConsoleIamUserResponse>), StatusCodes.Status201Created)]
public sealed class CreateConsoleIamUserEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayIamAdminClient admin)
    : AuthorizedProxyCreatedEndpoint<ConsoleCreateIamUserRequest, ConsoleIamUserResponse>(
        iam,
        auth,
        GatewayPermissions.IamUsersManage)
{
    protected override Task<ConsoleIamUserResponse> ForwardAsync(
        string bearerToken,
        ConsoleCreateIamUserRequest request,
        CancellationToken cancellationToken) =>
        admin.CreateUserAsync(bearerToken, request, cancellationToken);
}

[Tags("Console IAM")]
[HttpPatch("/api/console/v1/iam/users/{userId}")]
[GatewayOperationId("updateConsoleIamUser")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class UpdateConsoleIamUserEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayIamAdminClient admin)
    : AuthorizedProxyEndpoint<ConsoleUpdateIamUserRequest, ConsoleIamUserResponse>(
        iam,
        auth,
        GatewayPermissions.IamUsersManage)
{
    protected override Task<ConsoleIamUserResponse> ForwardAsync(
        string bearerToken,
        ConsoleUpdateIamUserRequest request,
        CancellationToken cancellationToken) =>
        admin.UpdateUserAsync(bearerToken, Route<string>("userId")!, request, cancellationToken);
}

[Tags("Console IAM")]
[HttpPost("/api/console/v1/iam/users/{userId}/disable")]
[GatewayOperationId("disableConsoleIamUser")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class DisableConsoleIamUserEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayIamAdminClient admin)
    : AuthorizedProxyNoContentEndpoint(
        iam,
        auth,
        GatewayPermissions.IamUsersManage)
{
    protected override Task ForwardAsync(
        string bearerToken,
        CancellationToken cancellationToken) =>
        admin.DisableUserAsync(bearerToken, Route<string>("userId")!, cancellationToken);
}

[Tags("Console IAM")]
[HttpPost("/api/console/v1/iam/users/{userId}/reset-password")]
[GatewayOperationId("resetConsoleIamUserPassword")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class ResetConsoleIamUserPasswordEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayIamAdminClient admin)
    : AuthorizedProxyNoContentEndpoint<ConsoleResetIamUserPasswordRequest>(
        iam,
        auth,
        GatewayPermissions.IamUsersManage)
{
    protected override Task ForwardAsync(
        string bearerToken,
        ConsoleResetIamUserPasswordRequest request,
        CancellationToken cancellationToken) =>
        admin.ResetUserPasswordAsync(bearerToken, Route<string>("userId")!, request, cancellationToken);
}

[Tags("Console IAM")]
[HttpGet("/api/console/v1/iam/roles")]
[GatewayOperationId("listConsoleIamRoles")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class ListConsoleIamRolesEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayIamAdminClient admin)
    : AuthorizedProxyEndpoint<ConsoleIamListRequest, PagedListResponse<ConsoleIamRoleResponse>>(
        iam,
        auth,
        GatewayPermissions.IamRolesRead)
{
    protected override Task<PagedListResponse<ConsoleIamRoleResponse>> ForwardAsync(
        string bearerToken,
        ConsoleIamListRequest request,
        CancellationToken cancellationToken) =>
        admin.ListRolesAsync(bearerToken, request, cancellationToken);
}

[Tags("Console IAM")]
[HttpPost("/api/console/v1/iam/roles")]
[GatewayOperationId("createConsoleIamRole")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(ResponseData<ConsoleIamRoleResponse>), StatusCodes.Status201Created)]
public sealed class CreateConsoleIamRoleEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayIamAdminClient admin)
    : AuthorizedProxyCreatedEndpoint<ConsoleCreateIamRoleRequest, ConsoleIamRoleResponse>(
        iam,
        auth,
        GatewayPermissions.IamRolesManage)
{
    protected override Task<ConsoleIamRoleResponse> ForwardAsync(
        string bearerToken,
        ConsoleCreateIamRoleRequest request,
        CancellationToken cancellationToken) =>
        admin.CreateRoleAsync(bearerToken, request, cancellationToken);
}

[Tags("Console IAM")]
[HttpPatch("/api/console/v1/iam/roles/{roleId}/permissions")]
[GatewayOperationId("updateConsoleIamRolePermissions")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class UpdateConsoleIamRolePermissionsEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayIamAdminClient admin)
    : AuthorizedProxyEndpoint<ConsoleUpdateIamRolePermissionsRequest, ConsoleIamRoleResponse>(
        iam,
        auth,
        GatewayPermissions.IamRolesManage)
{
    protected override Task<ConsoleIamRoleResponse> ForwardAsync(
        string bearerToken,
        ConsoleUpdateIamRolePermissionsRequest request,
        CancellationToken cancellationToken) =>
        admin.UpdateRolePermissionsAsync(bearerToken, Route<string>("roleId")!, request, cancellationToken);
}

[Tags("Console IAM")]
[HttpGet("/api/console/v1/iam/permissions")]
[GatewayOperationId("listConsoleIamPermissions")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class ListConsoleIamPermissionsEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayIamAdminClient admin)
    : AuthorizedProxyEndpoint<ConsoleIamPermissionCatalogResponse>(
        iam,
        auth,
        GatewayPermissions.IamRolesRead)
{
    protected override Task<ConsoleIamPermissionCatalogResponse> ForwardAsync(
        string bearerToken,
        CancellationToken cancellationToken) =>
        admin.ListPermissionsAsync(bearerToken, cancellationToken);
}

[Tags("Console IAM")]
[HttpGet("/api/console/v1/iam/sessions")]
[GatewayOperationId("listConsoleIamSessions")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class ListConsoleIamSessionsEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayIamAdminClient admin)
    : AuthorizedProxyEndpoint<ConsoleIamListRequest, PagedListResponse<ConsoleIamSessionResponse>>(
        iam,
        auth,
        GatewayPermissions.IamSessionsRead)
{
    protected override Task<PagedListResponse<ConsoleIamSessionResponse>> ForwardAsync(
        string bearerToken,
        ConsoleIamListRequest request,
        CancellationToken cancellationToken) =>
        admin.ListSessionsAsync(bearerToken, request, cancellationToken);
}

[Tags("Console IAM")]
[HttpPost("/api/console/v1/iam/sessions/{sessionId}/revoke")]
[GatewayOperationId("revokeConsoleIamSession")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class RevokeConsoleIamSessionEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayIamAdminClient admin)
    : AuthorizedProxyNoContentEndpoint(
        iam,
        auth,
        GatewayPermissions.IamSessionsRevoke)
{
    protected override Task ForwardAsync(
        string bearerToken,
        CancellationToken cancellationToken) =>
        admin.RevokeSessionAsync(bearerToken, Route<string>("sessionId")!, cancellationToken);
}
