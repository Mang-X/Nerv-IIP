using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Nerv.IIP.PlatformGateway.Web.Application.Auth;

public sealed class HttpGatewayIamAuthClient(HttpClient httpClient) : IGatewayIamAuthClient
{
    public async Task<ConsoleAuthResponse> LoginAsync(ConsoleLoginRequest request, CancellationToken cancellationToken)
    {
        var session = await SendForJsonAsync<IamAuthResponse>(
            () => JsonContent.Create(request),
            HttpMethod.Post,
            "/api/iam/v1/auth/login",
            null,
            cancellationToken);
        var principal = await GetMeAsync(session.AccessToken, cancellationToken);
        return ToConsoleAuthResponse(session, principal);
    }

    public async Task<ConsoleAuthResponse> RefreshAsync(ConsoleRefreshRequest request, CancellationToken cancellationToken)
    {
        var session = await SendForJsonAsync<IamAuthResponse>(
            () => JsonContent.Create(request),
            HttpMethod.Post,
            "/api/iam/v1/auth/refresh",
            null,
            cancellationToken);
        var principal = await GetMeAsync(session.AccessToken, cancellationToken);
        return ToConsoleAuthResponse(session, principal);
    }

    public async Task LogoutAsync(string bearerToken, ConsoleLogoutRequest request, CancellationToken cancellationToken)
    {
        using var _ = await SendAsync(
            () => JsonContent.Create(request),
            HttpMethod.Post,
            "/api/iam/v1/auth/logout",
            bearerToken,
            cancellationToken);
    }

    public async Task<ConsolePrincipalResponse> GetMeAsync(string bearerToken, CancellationToken cancellationToken)
    {
        var principal = await SendForJsonAsync<IamCurrentPrincipalResponse>(
            () => null,
            HttpMethod.Get,
            "/api/iam/v1/me",
            bearerToken,
            cancellationToken);

        return new ConsolePrincipalResponse(
            principal.UserId,
            principal.PrincipalType,
            principal.LoginName,
            principal.Email,
            principal.OrganizationId,
            principal.EnvironmentId,
            principal.PermissionVersion);
    }

    private static ConsoleAuthResponse ToConsoleAuthResponse(IamAuthResponse session, ConsolePrincipalResponse principal) =>
        new(session.AccessToken, session.RefreshToken, session.SessionId, session.ExpiresAtUtc, principal);

    private async Task<T> SendForJsonAsync<T>(
        Func<HttpContent?> contentFactory,
        HttpMethod method,
        string requestUri,
        string? bearerToken,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(contentFactory, method, requestUri, bearerToken, cancellationToken);
        try
        {
            var body = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            return body ?? throw GatewayAuthException.BadGateway("iam-empty-response");
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

    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpContent?> contentFactory,
        HttpMethod method,
        string requestUri,
        string? bearerToken,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, requestUri);
            request.Content = contentFactory();
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            }

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
        catch (HttpRequestException ex)
        {
            throw GatewayAuthException.Unavailable($"iam-unavailable: {ex.Message}");
        }
    }

    private static GatewayAuthException ToGatewayException(HttpStatusCode statusCode)
    {
        if (statusCode == HttpStatusCode.Unauthorized)
        {
            return GatewayAuthException.Unauthorized("iam-unauthorized");
        }

        if ((int)statusCode >= 500)
        {
            return GatewayAuthException.Unavailable("iam-unavailable");
        }

        return GatewayAuthException.BadGateway($"iam-unexpected-status-{(int)statusCode}");
    }

    private sealed record IamAuthResponse(
        string AccessToken,
        string RefreshToken,
        string SessionId,
        DateTimeOffset ExpiresAtUtc);

    private sealed record IamCurrentPrincipalResponse(
        string UserId,
        string LoginName,
        string Email,
        string PrincipalType,
        string OrganizationId,
        string EnvironmentId,
        int PermissionVersion);
}
