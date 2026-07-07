using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;

namespace Nerv.IIP.PlatformGateway.Web.Application.IamAdmin;

public interface IGatewayIamAdminClient
{
    Task<PagedListResponse<ConsoleIamUserResponse>> ListUsersAsync(
        string bearerToken,
        ConsoleIamListRequest request,
        CancellationToken cancellationToken);

    Task<ConsoleIamUserResponse> CreateUserAsync(
        string bearerToken,
        ConsoleCreateIamUserRequest request,
        CancellationToken cancellationToken);

    Task<ConsoleIamUserResponse> UpdateUserAsync(
        string bearerToken,
        string userId,
        ConsoleUpdateIamUserRequest request,
        CancellationToken cancellationToken);

    Task DisableUserAsync(string bearerToken, string userId, CancellationToken cancellationToken);

    Task EnableUserAsync(string bearerToken, string userId, CancellationToken cancellationToken);

    Task ResetUserPasswordAsync(
        string bearerToken,
        string userId,
        ConsoleResetIamUserPasswordRequest request,
        CancellationToken cancellationToken);

    Task<PagedListResponse<ConsoleIamRoleResponse>> ListRolesAsync(
        string bearerToken,
        ConsoleIamListRequest request,
        CancellationToken cancellationToken);

    Task<ConsoleIamRoleResponse> CreateRoleAsync(
        string bearerToken,
        ConsoleCreateIamRoleRequest request,
        CancellationToken cancellationToken);

    Task<ConsoleIamRoleResponse> UpdateRolePermissionsAsync(
        string bearerToken,
        string roleId,
        ConsoleUpdateIamRolePermissionsRequest request,
        CancellationToken cancellationToken);

    Task<ConsoleIamPermissionCatalogResponse> ListPermissionsAsync(
        string bearerToken,
        CancellationToken cancellationToken);

    Task<PagedListResponse<ConsoleIamSessionResponse>> ListSessionsAsync(
        string bearerToken,
        ConsoleIamListRequest request,
        CancellationToken cancellationToken);

    Task RevokeSessionAsync(string bearerToken, string sessionId, CancellationToken cancellationToken);
}

public sealed class HttpGatewayIamAdminClient(HttpClient httpClient) : IGatewayIamAdminClient
{
    public Task<PagedListResponse<ConsoleIamUserResponse>> ListUsersAsync(
        string bearerToken,
        ConsoleIamListRequest request,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<PagedListResponse<ConsoleIamUserResponse>>(
            () => null,
            HttpMethod.Get,
            "/api/iam/v1/users" + BuildListQuery(request),
            bearerToken,
            cancellationToken);

    public Task<ConsoleIamUserResponse> CreateUserAsync(
        string bearerToken,
        ConsoleCreateIamUserRequest request,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<ConsoleIamUserResponse>(
            () => JsonContent.Create(request),
            HttpMethod.Post,
            "/api/iam/v1/users",
            bearerToken,
            cancellationToken);

    public Task<ConsoleIamUserResponse> UpdateUserAsync(
        string bearerToken,
        string userId,
        ConsoleUpdateIamUserRequest request,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<ConsoleIamUserResponse>(
            () => JsonContent.Create(request),
            HttpMethod.Patch,
            $"/api/iam/v1/users/{Uri.EscapeDataString(userId)}",
            bearerToken,
            cancellationToken);

    public Task DisableUserAsync(string bearerToken, string userId, CancellationToken cancellationToken) =>
        SendNoContentAsync(
            () => null,
            HttpMethod.Post,
            $"/api/iam/v1/users/{Uri.EscapeDataString(userId)}/disable",
            bearerToken,
            cancellationToken);

    public Task EnableUserAsync(string bearerToken, string userId, CancellationToken cancellationToken) =>
        SendNoContentAsync(
            () => null,
            HttpMethod.Post,
            $"/api/iam/v1/users/{Uri.EscapeDataString(userId)}/enable",
            bearerToken,
            cancellationToken);

    public Task ResetUserPasswordAsync(
        string bearerToken,
        string userId,
        ConsoleResetIamUserPasswordRequest request,
        CancellationToken cancellationToken) =>
        SendNoContentAsync(
            () => JsonContent.Create(request),
            HttpMethod.Post,
            $"/api/iam/v1/users/{Uri.EscapeDataString(userId)}/reset-password",
            bearerToken,
            cancellationToken);

    public Task<PagedListResponse<ConsoleIamRoleResponse>> ListRolesAsync(
        string bearerToken,
        ConsoleIamListRequest request,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<PagedListResponse<ConsoleIamRoleResponse>>(
            () => null,
            HttpMethod.Get,
            "/api/iam/v1/roles" + BuildListQuery(request, includeUserFilters: false, includeSessionFilters: false),
            bearerToken,
            cancellationToken);

    public Task<ConsoleIamRoleResponse> CreateRoleAsync(
        string bearerToken,
        ConsoleCreateIamRoleRequest request,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<ConsoleIamRoleResponse>(
            () => JsonContent.Create(request),
            HttpMethod.Post,
            "/api/iam/v1/roles",
            bearerToken,
            cancellationToken);

    public Task<ConsoleIamRoleResponse> UpdateRolePermissionsAsync(
        string bearerToken,
        string roleId,
        ConsoleUpdateIamRolePermissionsRequest request,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<ConsoleIamRoleResponse>(
            () => JsonContent.Create(request),
            HttpMethod.Patch,
            $"/api/iam/v1/roles/{Uri.EscapeDataString(roleId)}/permissions",
            bearerToken,
            cancellationToken);

    public Task<ConsoleIamPermissionCatalogResponse> ListPermissionsAsync(
        string bearerToken,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<ConsoleIamPermissionCatalogResponse>(
            () => null,
            HttpMethod.Get,
            "/api/iam/v1/permissions",
            bearerToken,
            cancellationToken);

    public Task<PagedListResponse<ConsoleIamSessionResponse>> ListSessionsAsync(
        string bearerToken,
        ConsoleIamListRequest request,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<PagedListResponse<ConsoleIamSessionResponse>>(
            () => null,
            HttpMethod.Get,
            "/api/iam/v1/sessions" + BuildListQuery(request, includeUserFilters: false),
            bearerToken,
            cancellationToken);

    public Task RevokeSessionAsync(string bearerToken, string sessionId, CancellationToken cancellationToken) =>
        SendNoContentAsync(
            () => null,
            HttpMethod.Post,
            $"/api/iam/v1/sessions/{Uri.EscapeDataString(sessionId)}/revoke",
            bearerToken,
            cancellationToken);

    private async Task<T> SendForJsonAsync<T>(
        Func<HttpContent?> contentFactory,
        HttpMethod method,
        string requestUri,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(contentFactory, method, requestUri, bearerToken, cancellationToken);
        try
        {
            var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>(cancellationToken);
            if (envelope is null)
            {
                throw GatewayAuthException.BadGateway("iam-empty-response");
            }

            if (!envelope.Success)
            {
                throw GatewayAuthException.BadGateway(envelope.Message);
            }

            return envelope.Data ?? throw GatewayAuthException.BadGateway("iam-empty-response");
        }
        catch (JsonException)
        {
            throw GatewayAuthException.BadGateway("iam-invalid-response");
        }
        catch (NotSupportedException)
        {
            throw GatewayAuthException.BadGateway("iam-invalid-response");
        }
    }

    private async Task SendNoContentAsync(
        Func<HttpContent?> contentFactory,
        HttpMethod method,
        string requestUri,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        using var _ = await SendAsync(contentFactory, method, requestUri, bearerToken, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpContent?> contentFactory,
        HttpMethod method,
        string requestUri,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, requestUri);
            request.Content = contentFactory();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

            var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var statusCode = response.StatusCode;
            response.Dispose();
            throw ToGatewayException(statusCode);
        }
        catch (GatewayAuthException)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode statusCode)
        {
            throw ToGatewayException(statusCode);
        }
        catch (HttpRequestException)
        {
            throw GatewayAuthException.Unavailable("iam-unavailable");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw GatewayAuthException.Unavailable("iam-unavailable");
        }
    }

    private static string BuildListQuery(
        ConsoleIamListRequest request,
        bool includeUserFilters = true,
        bool includeSessionFilters = true)
    {
        var values = new List<string>();
        Add(values, "pageIndex", request.PageIndex?.ToString());
        Add(values, "pageSize", request.PageSize?.ToString());
        Add(values, "sortBy", request.SortBy);
        Add(values, "sortOrder", request.SortOrder);
        Add(values, "filterSearch", request.FilterSearch);
        if (includeUserFilters)
        {
            Add(values, "filterEnabled", request.FilterEnabled?.ToString().ToLowerInvariant());
        }

        if (includeSessionFilters)
        {
            Add(values, "filterRevoked", request.FilterRevoked?.ToString().ToLowerInvariant());
        }

        return values.Count == 0 ? string.Empty : "?" + string.Join("&", values);
    }

    private static void Add(List<string> values, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        values.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
    }

    private static GatewayAuthException ToGatewayException(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.BadRequest => new GatewayAuthException(statusCode, "iam-bad-request"),
            HttpStatusCode.Unauthorized => GatewayAuthException.Unauthorized("iam-unauthorized"),
            HttpStatusCode.Forbidden => new GatewayAuthException(statusCode, "iam-forbidden"),
            HttpStatusCode.NotFound => new GatewayAuthException(statusCode, "iam-not-found"),
            HttpStatusCode.Conflict => new GatewayAuthException(statusCode, "iam-conflict"),
            _ when (int)statusCode >= 500 => GatewayAuthException.Unavailable("iam-unavailable"),
            _ => GatewayAuthException.BadGateway($"iam-unexpected-status-{(int)statusCode}")
        };
    }

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
