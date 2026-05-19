using System.Net;

namespace Nerv.IIP.PlatformGateway.Web.Application.Auth;

public sealed record ConsoleLoginRequest(string LoginName, string Password);
public sealed record ConsoleRefreshRequest(string RefreshToken);
public sealed record ConsoleLogoutRequest(string? SessionId);

public sealed record ConsolePrincipalResponse(
    string PrincipalId,
    string PrincipalType,
    string LoginName,
    string Email,
    string OrganizationId,
    string EnvironmentId,
    int PermissionVersion);

public sealed record ConsoleAuthResponse(
    string AccessToken,
    string RefreshToken,
    string SessionId,
    DateTimeOffset ExpiresAtUtc,
    ConsolePrincipalResponse Principal);

public interface IGatewayIamAuthClient
{
    Task<ConsoleAuthResponse> LoginAsync(ConsoleLoginRequest request, CancellationToken cancellationToken);
    Task<ConsoleAuthResponse> RefreshAsync(ConsoleRefreshRequest request, CancellationToken cancellationToken);
    Task LogoutAsync(string bearerToken, ConsoleLogoutRequest request, CancellationToken cancellationToken);
    Task<ConsolePrincipalResponse> GetMeAsync(string bearerToken, CancellationToken cancellationToken);
}

public sealed class GatewayAuthException(HttpStatusCode statusCode, string reason) : Exception(reason)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Reason { get; } = reason;

    public static GatewayAuthException Unauthorized(string reason) => new(HttpStatusCode.Unauthorized, reason);
    public static GatewayAuthException BadGateway(string reason) => new(HttpStatusCode.BadGateway, reason);
    public static GatewayAuthException Unavailable(string reason) => new(HttpStatusCode.ServiceUnavailable, reason);
}
