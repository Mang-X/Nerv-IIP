using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Application.IamAdmin;
using NetCorePal.Extensions.Dto;
using System.Text.Json;

namespace Nerv.IIP.PlatformGateway.Web.Endpoints.IamAdmin;

[Tags("Console IAM")]
[HttpGet("/api/console/v1/iam/users")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class ListConsoleIamUsersEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayIamAdminClient admin) : Endpoint<ConsoleIamListRequest, ResponseData<PagedListResponse<ConsoleIamUserResponse>>>
{
    public override async Task HandleAsync(ConsoleIamListRequest req, CancellationToken ct)
    {
        var authorized = await ConsoleIamAdminEndpointResults.AuthorizeAsync(
            HttpContext,
            iam,
            auth,
            GatewayPermissions.IamUsersRead,
            ct);
        if (authorized is null)
        {
            return;
        }

        try
        {
            var response = await admin.ListUsersAsync(authorized.Value.BearerToken, req, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayAuthException ex)
        {
            await ConsoleIamAdminEndpointResults.WriteProblemAsync(HttpContext, ex, ct);
        }
    }
}

[Tags("Console IAM")]
[HttpPost("/api/console/v1/iam/users")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(ResponseData<ConsoleIamUserResponse>), StatusCodes.Status201Created)]
public sealed class CreateConsoleIamUserEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayIamAdminClient admin) : Endpoint<ConsoleCreateIamUserRequest>
{
    public override async Task HandleAsync(ConsoleCreateIamUserRequest req, CancellationToken ct)
    {
        var authorized = await ConsoleIamAdminEndpointResults.AuthorizeAsync(
            HttpContext,
            iam,
            auth,
            GatewayPermissions.IamUsersManage,
            ct);
        if (authorized is null)
        {
            return;
        }

        try
        {
            var response = await admin.CreateUserAsync(authorized.Value.BearerToken, req, ct);
            await ConsoleIamAdminEndpointResults.WriteDataAsync(HttpContext, StatusCodes.Status201Created, response, ct);
        }
        catch (GatewayAuthException ex)
        {
            await ConsoleIamAdminEndpointResults.WriteProblemAsync(HttpContext, ex, ct);
        }
    }
}

[Tags("Console IAM")]
[HttpPatch("/api/console/v1/iam/users/{userId}")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class UpdateConsoleIamUserEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayIamAdminClient admin) : Endpoint<ConsoleUpdateIamUserRequest, ResponseData<ConsoleIamUserResponse>>
{
    public override async Task HandleAsync(ConsoleUpdateIamUserRequest req, CancellationToken ct)
    {
        var authorized = await ConsoleIamAdminEndpointResults.AuthorizeAsync(
            HttpContext,
            iam,
            auth,
            GatewayPermissions.IamUsersManage,
            ct);
        if (authorized is null)
        {
            return;
        }

        try
        {
            var response = await admin.UpdateUserAsync(authorized.Value.BearerToken, Route<string>("userId")!, req, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayAuthException ex)
        {
            await ConsoleIamAdminEndpointResults.WriteProblemAsync(HttpContext, ex, ct);
        }
    }
}

[Tags("Console IAM")]
[HttpPost("/api/console/v1/iam/users/{userId}/disable")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class DisableConsoleIamUserEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayIamAdminClient admin) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var authorized = await ConsoleIamAdminEndpointResults.AuthorizeAsync(
            HttpContext,
            iam,
            auth,
            GatewayPermissions.IamUsersManage,
            ct);
        if (authorized is null)
        {
            return;
        }

        try
        {
            await admin.DisableUserAsync(authorized.Value.BearerToken, Route<string>("userId")!, ct);
            HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
        }
        catch (GatewayAuthException ex)
        {
            await ConsoleIamAdminEndpointResults.WriteProblemAsync(HttpContext, ex, ct);
        }
    }
}

[Tags("Console IAM")]
[HttpPost("/api/console/v1/iam/users/{userId}/reset-password")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class ResetConsoleIamUserPasswordEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayIamAdminClient admin) : Endpoint<ConsoleResetIamUserPasswordRequest>
{
    public override async Task HandleAsync(ConsoleResetIamUserPasswordRequest req, CancellationToken ct)
    {
        var authorized = await ConsoleIamAdminEndpointResults.AuthorizeAsync(
            HttpContext,
            iam,
            auth,
            GatewayPermissions.IamUsersManage,
            ct);
        if (authorized is null)
        {
            return;
        }

        try
        {
            await admin.ResetUserPasswordAsync(authorized.Value.BearerToken, Route<string>("userId")!, req, ct);
            HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
        }
        catch (GatewayAuthException ex)
        {
            await ConsoleIamAdminEndpointResults.WriteProblemAsync(HttpContext, ex, ct);
        }
    }
}

[Tags("Console IAM")]
[HttpGet("/api/console/v1/iam/roles")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class ListConsoleIamRolesEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayIamAdminClient admin) : Endpoint<ConsoleIamListRequest, ResponseData<PagedListResponse<ConsoleIamRoleResponse>>>
{
    public override async Task HandleAsync(ConsoleIamListRequest req, CancellationToken ct)
    {
        var authorized = await ConsoleIamAdminEndpointResults.AuthorizeAsync(
            HttpContext,
            iam,
            auth,
            GatewayPermissions.IamRolesRead,
            ct);
        if (authorized is null)
        {
            return;
        }

        try
        {
            var response = await admin.ListRolesAsync(authorized.Value.BearerToken, req, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayAuthException ex)
        {
            await ConsoleIamAdminEndpointResults.WriteProblemAsync(HttpContext, ex, ct);
        }
    }
}

[Tags("Console IAM")]
[HttpPost("/api/console/v1/iam/roles")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(ResponseData<ConsoleIamRoleResponse>), StatusCodes.Status201Created)]
public sealed class CreateConsoleIamRoleEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayIamAdminClient admin) : Endpoint<ConsoleCreateIamRoleRequest>
{
    public override async Task HandleAsync(ConsoleCreateIamRoleRequest req, CancellationToken ct)
    {
        var authorized = await ConsoleIamAdminEndpointResults.AuthorizeAsync(
            HttpContext,
            iam,
            auth,
            GatewayPermissions.IamRolesManage,
            ct);
        if (authorized is null)
        {
            return;
        }

        try
        {
            var response = await admin.CreateRoleAsync(authorized.Value.BearerToken, req, ct);
            await ConsoleIamAdminEndpointResults.WriteDataAsync(HttpContext, StatusCodes.Status201Created, response, ct);
        }
        catch (GatewayAuthException ex)
        {
            await ConsoleIamAdminEndpointResults.WriteProblemAsync(HttpContext, ex, ct);
        }
    }
}

[Tags("Console IAM")]
[HttpPatch("/api/console/v1/iam/roles/{roleId}/permissions")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class UpdateConsoleIamRolePermissionsEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayIamAdminClient admin) : Endpoint<ConsoleUpdateIamRolePermissionsRequest, ResponseData<ConsoleIamRoleResponse>>
{
    public override async Task HandleAsync(ConsoleUpdateIamRolePermissionsRequest req, CancellationToken ct)
    {
        var authorized = await ConsoleIamAdminEndpointResults.AuthorizeAsync(
            HttpContext,
            iam,
            auth,
            GatewayPermissions.IamRolesManage,
            ct);
        if (authorized is null)
        {
            return;
        }

        try
        {
            var response = await admin.UpdateRolePermissionsAsync(authorized.Value.BearerToken, Route<string>("roleId")!, req, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayAuthException ex)
        {
            await ConsoleIamAdminEndpointResults.WriteProblemAsync(HttpContext, ex, ct);
        }
    }
}

[Tags("Console IAM")]
[HttpGet("/api/console/v1/iam/permissions")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class ListConsoleIamPermissionsEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayIamAdminClient admin) : EndpointWithoutRequest<ResponseData<ConsoleIamPermissionCatalogResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var authorized = await ConsoleIamAdminEndpointResults.AuthorizeAsync(
            HttpContext,
            iam,
            auth,
            GatewayPermissions.IamRolesRead,
            ct);
        if (authorized is null)
        {
            return;
        }

        try
        {
            var response = await admin.ListPermissionsAsync(authorized.Value.BearerToken, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayAuthException ex)
        {
            await ConsoleIamAdminEndpointResults.WriteProblemAsync(HttpContext, ex, ct);
        }
    }
}

[Tags("Console IAM")]
[HttpGet("/api/console/v1/iam/sessions")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class ListConsoleIamSessionsEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayIamAdminClient admin) : Endpoint<ConsoleIamListRequest, ResponseData<PagedListResponse<ConsoleIamSessionResponse>>>
{
    public override async Task HandleAsync(ConsoleIamListRequest req, CancellationToken ct)
    {
        var authorized = await ConsoleIamAdminEndpointResults.AuthorizeAsync(
            HttpContext,
            iam,
            auth,
            GatewayPermissions.IamSessionsRead,
            ct);
        if (authorized is null)
        {
            return;
        }

        try
        {
            var response = await admin.ListSessionsAsync(authorized.Value.BearerToken, req, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayAuthException ex)
        {
            await ConsoleIamAdminEndpointResults.WriteProblemAsync(HttpContext, ex, ct);
        }
    }
}

[Tags("Console IAM")]
[HttpPost("/api/console/v1/iam/sessions/{sessionId}/revoke")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class RevokeConsoleIamSessionEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayIamAdminClient admin) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var authorized = await ConsoleIamAdminEndpointResults.AuthorizeAsync(
            HttpContext,
            iam,
            auth,
            GatewayPermissions.IamSessionsRevoke,
            ct);
        if (authorized is null)
        {
            return;
        }

        try
        {
            await admin.RevokeSessionAsync(authorized.Value.BearerToken, Route<string>("sessionId")!, ct);
            HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
        }
        catch (GatewayAuthException ex)
        {
            await ConsoleIamAdminEndpointResults.WriteProblemAsync(HttpContext, ex, ct);
        }
    }
}

internal static class ConsoleIamAdminEndpointResults
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task<(string BearerToken, ConsolePrincipalResponse Principal)?> AuthorizeAsync(
        HttpContext context,
        IGatewayIamAuthClient iam,
        IGatewayAuthorizationClient auth,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        return GatewayAuthorization.RequireCurrentPrincipalPermissionAsync(
            context,
            iam,
            auth,
            permissionCode,
            cancellationToken);
    }

    public static Task WriteProblemAsync(
        HttpContext context,
        GatewayAuthException exception,
        CancellationToken cancellationToken)
    {
        return ResponseDataEndpointResults.WriteErrorAsync(
            context,
            (int)exception.StatusCode,
            exception.Reason,
            cancellationToken);
    }

    public static async Task WriteDataAsync<T>(
        HttpContext context,
        int statusCode,
        T data,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            data.AsResponseData(),
            JsonOptions,
            cancellationToken);
    }
}
