using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Iam.Domain;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Web.Application.SecurityAudit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Nerv.IIP.Iam.Web.Application.Auth;

public sealed class InMemoryIamAuthService(
    InMemoryIamStore store,
    IamTokenService tokenService,
    IOptions<IamAuthenticationOptions> authenticationOptions,
    IOptions<EnterpriseIdentityOptions> enterpriseIdentityOptions,
    IMfaChallengeStore mfaChallenges,
    IHostEnvironment environment) : IIamAuthService
{
    public Task<AuthResponse> LoginAsync(
        string loginName,
        string password,
        string? clientInfo,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ToResponse(store.Login(
            loginName,
            password,
            authenticationOptions.Value.FailedLoginLockoutThreshold,
            authenticationOptions.Value.FailedLoginLockoutWindow)));
    }

    public Task<AuthResponse> RefreshAsync(
        string refreshToken,
        string? clientInfo,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ToResponse(store.Refresh(refreshToken)));
    }

    public Task RevokeSessionAsync(
        string sessionId,
        string reason,
        SecurityAuditContext? auditContext,
        CancellationToken cancellationToken)
    {
        _ = reason;
        _ = auditContext;
        store.Logout(sessionId);
        return Task.CompletedTask;
    }

    public Task<CurrentPrincipalResponse?> GetCurrentPrincipalAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var user = ValidateBearer(httpContext);
        if (user is not null)
        {
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

        var externalClient = tokenService.TryReadExternalClientPrincipal(httpContext);
        if (externalClient is null)
        {
            return Task.FromResult<CurrentPrincipalResponse?>(null);
        }

        return Task.FromResult<CurrentPrincipalResponse?>(new CurrentPrincipalResponse(
            externalClient.ClientId,
            externalClient.ClientId,
            string.Empty,
            "external-client",
            externalClient.OrganizationId,
            externalClient.EnvironmentId,
            externalClient.PermissionVersion,
            externalClient.Scope));
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

    public Task<ClientCredentialsTokenResponse> IssueClientCredentialsTokenAsync(
        string clientId,
        string clientSecret,
        string? scope,
        CancellationToken cancellationToken)
    {
        var principal = store.IssueExternalClientToken(clientId, clientSecret, scope);
        var accessToken = tokenService.CreateExternalClientAccessToken(
            principal.ClientId,
            principal.OrganizationId,
            principal.EnvironmentId,
            principal.PermissionVersion,
            principal.Scope);
        return Task.FromResult(new ClientCredentialsTokenResponse(
            accessToken,
            "Bearer",
            tokenService.GetAccessTokenExpiresAtUtc(DateTimeOffset.UtcNow),
            string.Join(' ', principal.Scope)));
    }

    public Task<bool> PrincipalHasPermissionAsync(
        CurrentPrincipalResponse principal,
        string organizationId,
        string environmentId,
        string permissionCode,
        string? resourceType,
        string? resourceId,
        CancellationToken cancellationToken)
    {
        if (principal.PrincipalType == "external-client")
        {
            return Task.FromResult(store.ExternalClientHasPermission(
                principal.UserId,
                organizationId,
                environmentId,
                permissionCode,
                resourceType,
                resourceId));
        }

        return UserHasPermissionAsync(principal.UserId, organizationId, environmentId, permissionCode, cancellationToken);
    }

    public Task<EnterpriseAuthResponse> HandleOidcCallbackAsync(
        OidcLoginCallbackRequest request,
        string? clientInfo,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        EnsureEnterpriseIdentityStubAllowed();
        var provider = GetEnabledProvider(request.Provider);
        EnsureCallbackSecret(request.CallbackSecret, provider);
        EnsureAllowedEmailDomain(request.Email, provider);
        var user = store.FindUserByEmail(request.Email) ?? throw new UnauthorizedAccessException("External identity is not mapped to an enabled user.");
        if (!store.UserHasMembership(user.UserId, request.OrganizationId, request.EnvironmentId))
        {
            throw new UnauthorizedAccessException("User is not a member of the requested organization environment.");
        }

        if (provider.RequireMfa)
        {
            var challengeId = mfaChallenges.Create(new MfaChallengeContext(
                user.UserId,
                request.Provider,
                request.Subject,
                request.OrganizationId,
                request.EnvironmentId,
                DateTimeOffset.UtcNow.AddMinutes(GetMfaChallengeMinutes())));
            return Task.FromResult(EnterpriseAuthResponse.Challenge(challengeId));
        }

        var session = store.CreateEnterpriseSession(
            user.UserId,
            request.OrganizationId,
            request.EnvironmentId,
            "oidc",
            request.Provider,
            request.Subject,
            null);
        return Task.FromResult(EnterpriseAuthResponse.Authenticated(ToResponse(session)));
    }

    public Task<EnterpriseAuthResponse> VerifyMfaChallengeAsync(
        string challengeId,
        string code,
        string? clientInfo,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        EnsureEnterpriseIdentityStubAllowed();
        var context = mfaChallenges.Consume(
            challengeId,
            code,
            enterpriseIdentityOptions.Value.Mfa.DevelopmentCode);
        if (context is null)
        {
            throw new UnauthorizedAccessException("Invalid MFA challenge.");
        }

        var session = store.CreateEnterpriseSession(
            context.UserId,
            context.OrganizationId,
            context.EnvironmentId,
            "oidc",
            context.Provider,
            context.Subject,
            DateTimeOffset.UtcNow);
        return Task.FromResult(EnterpriseAuthResponse.Authenticated(ToResponse(session)));
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
        return new AuthResponse(result.AccessToken, result.RefreshToken, result.SessionId, result.ExpiresAtUtc);
    }

    private OidcProviderOptions GetEnabledProvider(string provider)
    {
        if (!enterpriseIdentityOptions.Value.OidcProviders.TryGetValue(provider, out var options)
            || !options.Enabled)
        {
            throw new UnauthorizedAccessException("OIDC provider is not enabled.");
        }

        return options;
    }

    private void EnsureEnterpriseIdentityStubAllowed()
    {
        if (!environment.IsDevelopment())
        {
            throw new UnauthorizedAccessException("Unauthorized.");
        }
    }

    private static void EnsureCallbackSecret(string callbackSecret, OidcProviderOptions provider)
    {
        if (string.IsNullOrWhiteSpace(provider.CallbackSecret)
            || !FixedTimeEquals(callbackSecret, provider.CallbackSecret))
        {
            throw new UnauthorizedAccessException("OIDC callback secret is invalid.");
        }
    }

    private static void EnsureAllowedEmailDomain(string email, OidcProviderOptions provider)
    {
        if (string.IsNullOrWhiteSpace(provider.AllowedEmailDomain))
        {
            return;
        }

        var suffix = $"@{provider.AllowedEmailDomain.TrimStart('@')}";
        if (!email.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("OIDC email domain is not allowed.");
        }
    }

    private int GetMfaChallengeMinutes()
    {
        return enterpriseIdentityOptions.Value.Mfa.ChallengeMinutes > 0
            ? enterpriseIdentityOptions.Value.Mfa.ChallengeMinutes
            : 5;
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        var leftHash = SHA256.HashData(leftBytes);
        var rightHash = SHA256.HashData(rightBytes);
        return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
    }
}
