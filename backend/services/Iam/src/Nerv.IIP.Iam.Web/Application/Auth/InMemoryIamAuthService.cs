using Nerv.IIP.Iam.Domain;
using Nerv.IIP.Iam.Infrastructure;

namespace Nerv.IIP.Iam.Web.Application.Auth;

public sealed class InMemoryIamAuthService(InMemoryIamStore store, IamTokenService tokenService) : IIamAuthService
{
    public Task<AuthResponse> LoginAsync(
        string loginName,
        string password,
        string? clientInfo,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ToResponse(store.Login(loginName, password)));
    }

    public Task<AuthResponse> RefreshAsync(
        string refreshToken,
        string? clientInfo,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ToResponse(store.Refresh(refreshToken)));
    }

    public Task RevokeSessionAsync(string sessionId, string reason, CancellationToken cancellationToken)
    {
        store.Logout(sessionId);
        return Task.CompletedTask;
    }

    public Task<CurrentPrincipalResponse?> GetCurrentPrincipalAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var user = ValidateBearer(httpContext);
        if (user is null)
        {
            return Task.FromResult<CurrentPrincipalResponse?>(null);
        }

        try
        {
            var currentPrincipal = store.GetCurrentPrincipal(user);
            return Task.FromResult<CurrentPrincipalResponse?>(new CurrentPrincipalResponse(
                currentPrincipal.UserId,
                currentPrincipal.LoginName,
                currentPrincipal.Email,
                currentPrincipal.PrincipalType,
                currentPrincipal.OrganizationId,
                currentPrincipal.EnvironmentId,
                currentPrincipal.PermissionVersion,
                currentPrincipal.PermissionCodes));
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult<CurrentPrincipalResponse?>(null);
        }
    }

    public Task<bool> UserHasPermissionAsync(string userId, string permissionCode, CancellationToken cancellationToken)
    {
        var allowed = store.UserHasPermission(userId, "org-001", "env-dev", permissionCode);
        return Task.FromResult(allowed);
    }

    public Task<bool> UserHasPermissionAsync(
        string userId,
        string organizationId,
        string environmentId,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(store.UserHasPermission(userId, organizationId, environmentId, permissionCode));
    }

    public Task<ConnectorPrincipalResponse> ValidateConnectorCredentialAsync(
        string connectorHostId,
        string secret,
        CancellationToken cancellationToken)
    {
        var principal = store.ValidateConnectorHost(connectorHostId, secret);
        return Task.FromResult(new ConnectorPrincipalResponse(
            principal.PrincipalType,
            principal.OrganizationId,
            principal.EnvironmentId,
            principal.ConnectorHostId));
    }

    private UserFact? ValidateBearer(HttpContext context)
    {
        var value = context.Request.Headers.Authorization.ToString();
        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var principal = tokenService.TryReadPrincipal(value["Bearer ".Length..]);
            if (principal is null)
            {
                return null;
            }

            return store.ValidateAccessTokenPrincipal(
                principal.SessionId,
                principal.UserId,
                principal.SecurityStamp,
                principal.PermissionVersion);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private AuthResponse ToResponse(AuthResult result)
    {
        var accessToken = tokenService.CreateAccessToken(
            result.UserId,
            result.SessionId,
            result.SecurityStamp,
            result.PermissionVersion,
            result.LoginName,
            result.Email,
            result.OrganizationId,
            result.EnvironmentId);
        return new AuthResponse(accessToken, result.RefreshToken, result.SessionId, result.ExpiresAtUtc);
    }
}
