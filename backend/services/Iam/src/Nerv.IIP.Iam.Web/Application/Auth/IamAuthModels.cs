namespace Nerv.IIP.Iam.Web.Application.Auth;

public sealed record LoginRequest(string LoginName, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string? SessionId);
public sealed record ValidateConnectorCredentialRequest(string ConnectorHostId, string Secret);
public sealed record ClientCredentialsTokenRequest(string ClientId, string ClientSecret, string? Scope);
public sealed record AuthResponse(string AccessToken, string RefreshToken, string SessionId, DateTimeOffset ExpiresAtUtc);
public sealed record ClientCredentialsTokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc, string Scope);
public sealed record CurrentPrincipalResponse(
    string UserId,
    string LoginName,
    string Email,
    string PrincipalType,
    string OrganizationId,
    string EnvironmentId,
    int PermissionVersion,
    IReadOnlyList<string> PermissionCodes);
public sealed record ConnectorPrincipalResponse(string PrincipalType, string OrganizationId, string EnvironmentId, string ConnectorHostId);

public interface IIamAuthService
{
    Task<AuthResponse> LoginAsync(
        string loginName,
        string password,
        string? clientInfo,
        string? ipAddress,
        CancellationToken cancellationToken);

    Task<AuthResponse> RefreshAsync(
        string refreshToken,
        string? clientInfo,
        string? ipAddress,
        CancellationToken cancellationToken);

    Task RevokeSessionAsync(string sessionId, string reason, CancellationToken cancellationToken);

    Task<CurrentPrincipalResponse?> GetCurrentPrincipalAsync(HttpContext httpContext, CancellationToken cancellationToken);

    Task<bool> UserHasPermissionAsync(string userId, string permissionCode, CancellationToken cancellationToken);

    Task<bool> UserHasPermissionAsync(
        string userId,
        string organizationId,
        string environmentId,
        string permissionCode,
        CancellationToken cancellationToken);

    Task<ConnectorPrincipalResponse> ValidateConnectorCredentialAsync(
        string connectorHostId,
        string secret,
        CancellationToken cancellationToken);

    Task<ClientCredentialsTokenResponse> IssueClientCredentialsTokenAsync(
        string clientId,
        string clientSecret,
        string? scope,
        CancellationToken cancellationToken);

    Task<bool> PrincipalHasPermissionAsync(
        CurrentPrincipalResponse principal,
        string organizationId,
        string environmentId,
        string permissionCode,
        CancellationToken cancellationToken);
}
