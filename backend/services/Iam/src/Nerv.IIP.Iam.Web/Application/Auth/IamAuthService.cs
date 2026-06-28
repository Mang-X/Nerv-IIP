using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Iam.Domain.AggregatesModel.ConnectorHostCredentialAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserSessionAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Infrastructure.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Web.Application.Auth;

public sealed class PostgreSqlIamAuthService(
    IUserRepository userRepository,
    IUserSessionRepository userSessionRepository,
    IMembershipRepository membershipRepository,
    IConnectorHostCredentialRepository connectorHostCredentialRepository,
    IExternalClientRepository externalClientRepository,
    IamPasswordService passwordService,
    IamTokenService tokenService,
    IOptions<IamAuthenticationOptions> authenticationOptions,
    IOptions<EnterpriseIdentityOptions> enterpriseIdentityOptions,
    IMfaChallengeStore mfaChallenges,
    ILogger<PostgreSqlIamAuthService> logger,
    IHostEnvironment environment)
    : IIamAuthService
{
    public async Task<AuthResponse> LoginAsync(
        string loginName,
        string password,
        string? clientInfo,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByLoginNameAsync(loginName, cancellationToken);
        if (user is null || !user.Enabled)
        {
            throw Unauthorized();
        }

        var now = DateTimeOffset.UtcNow;
        if (user.IsLockedOut(now))
        {
            throw Unauthorized();
        }

        if (!passwordService.Verify(user, password))
        {
            user.RecordFailedLogin(
                now,
                authenticationOptions.Value.FailedLoginLockoutThreshold,
                authenticationOptions.Value.FailedLoginLockoutWindow);
            await userRepository.PersistFailedLoginAsync(user, cancellationToken);
            throw Unauthorized();
        }

        user.RecordSuccessfulLogin(now);
        return await CreateSessionResponseAsync(user, clientInfo, ipAddress, cancellationToken);
    }

    public async Task<AuthResponse> RefreshAsync(
        string refreshToken,
        string? clientInfo,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var refreshTokenHash = tokenService.HashSecret(refreshToken);
        var session = await userSessionRepository.ConsumeActiveRefreshTokenAsync(
            refreshTokenHash,
            now,
            "refresh-rotated",
            cancellationToken);
        if (session is null)
        {
            var replayedSession = await userSessionRepository.GetByRefreshTokenHashAsync(refreshTokenHash, cancellationToken);
            if (replayedSession is not null && replayedSession.RevokedAtUtc is not null)
            {
                var revokedCount = await userSessionRepository.RevokeFamilyAsync(
                    replayedSession.TokenFamilyId,
                    now,
                    "refresh-reuse-detected",
                    cancellationToken);
                logger.LogWarning(
                    "RefreshTokenReuseDetected UserId={UserId} TokenFamilyId={TokenFamilyId} ReplayedSessionId={SessionId} RevokedSessions={RevokedSessions}",
                    replayedSession.UserId.Id,
                    replayedSession.TokenFamilyId,
                    replayedSession.Id.Id,
                    revokedCount);
            }

            throw Unauthorized();
        }

        var user = await userRepository.GetByIdAsync(session.UserId, cancellationToken);
        if (user is null || !user.Enabled)
        {
            throw Unauthorized();
        }

        return await CreateSessionResponseAsync(user, clientInfo, ipAddress, cancellationToken, previousSession: session);
    }

    public async Task RevokeSessionAsync(string sessionId, string reason, CancellationToken cancellationToken)
    {
        var session = await userSessionRepository.GetByIdAsync(new UserSessionId(sessionId), cancellationToken);
        if (session is null)
        {
            return;
        }

        session.Revoke(DateTimeOffset.UtcNow, reason);
    }

    public async Task<CurrentPrincipalResponse?> GetCurrentPrincipalAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var principal = tokenService.TryReadPrincipal(httpContext);
        if (principal is null)
        {
            var externalClientPrincipal = tokenService.TryReadExternalClientPrincipal(httpContext);
            if (externalClientPrincipal is null)
            {
                return null;
            }

            var externalClient = await externalClientRepository.GetByClientIdAsync(
                externalClientPrincipal.ClientId,
                cancellationToken);
            if (externalClient is null
                || !externalClient.Enabled
                || externalClient.PermissionVersion != externalClientPrincipal.PermissionVersion
                || !string.Equals(externalClient.OrganizationId.Id, externalClientPrincipal.OrganizationId, StringComparison.Ordinal)
                || !string.Equals(externalClient.EnvironmentId.Id, externalClientPrincipal.EnvironmentId, StringComparison.Ordinal))
            {
                return null;
            }

            return new CurrentPrincipalResponse(
                externalClient.ClientId,
                externalClient.DisplayName,
                string.Empty,
                "external-client",
                externalClient.OrganizationId.Id,
                externalClient.EnvironmentId.Id,
                externalClient.PermissionVersion,
                externalClientPrincipal.Scope);
        }

        var now = DateTimeOffset.UtcNow;
        var sessionId = new UserSessionId(principal.SessionId);
        var userId = new UserId(principal.UserId);
        var session = await userSessionRepository.GetByPrincipalAsync(sessionId, userId, cancellationToken);
        if (session is null || !session.CanRefresh(now) || session.PermissionVersion != principal.PermissionVersion)
        {
            return null;
        }

        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null
            || !user.Enabled
            || !string.Equals(user.SecurityStamp, principal.SecurityStamp, StringComparison.Ordinal)
            || user.PermissionVersion != principal.PermissionVersion)
        {
            return null;
        }

        var membership = await GetTokenMembershipAsync(principal, userId, cancellationToken);
        if (membership is null)
        {
            return null;
        }

        return new CurrentPrincipalResponse(
            user.Id.Id,
            user.LoginName,
            user.Email,
            "user",
            membership.OrganizationId.Id,
            membership.EnvironmentId.Id,
            user.PermissionVersion,
            await membershipRepository.ListPermissionCodesAsync(
                userId,
                membership.OrganizationId,
                membership.EnvironmentId,
                cancellationToken));
    }

    public async Task<bool> UserHasPermissionAsync(string userId, string permissionCode, CancellationToken cancellationToken)
    {
        return await membershipRepository.UserHasPermissionAsync(new UserId(userId), permissionCode, cancellationToken);
    }

    public async Task<bool> UserHasPermissionAsync(
        string userId,
        string organizationId,
        string environmentId,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        var userIdValue = new UserId(userId);
        var organizationIdValue = new OrganizationId(organizationId);
        var environmentIdValue = new IamEnvironmentId(environmentId);

        return await membershipRepository.UserHasPermissionAsync(
            userIdValue,
            organizationIdValue,
            environmentIdValue,
            permissionCode,
            cancellationToken);
    }

    public async Task<ConnectorPrincipalResponse> ValidateConnectorCredentialAsync(
        string connectorHostId,
        string secret,
        CancellationToken cancellationToken)
    {
        var credentials = await connectorHostCredentialRepository.ListByConnectorHostIdAsync(
            connectorHostId,
            cancellationToken);
        var now = DateTimeOffset.UtcNow;
        ConnectorHostCredential? credential = null;
        foreach (var candidate in credentials)
        {
            var secretMatches = tokenService.VerifySecret(secret, candidate.SecretHash);
            if (credential is null && candidate.IsValidAt(now) && secretMatches)
            {
                credential = candidate;
            }
        }

        if (credential is null || !credential.IsValidAt(DateTimeOffset.UtcNow))
        {
            throw Unauthorized();
        }

        return new ConnectorPrincipalResponse(
            "connector-host",
            credential.OrganizationId.Id,
            credential.EnvironmentId.Id,
            credential.ConnectorHostId);
    }

    public async Task<ClientCredentialsTokenResponse> IssueClientCredentialsTokenAsync(
        string clientId,
        string clientSecret,
        string? scope,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var externalClient = await externalClientRepository.GetByClientIdAsync(clientId, cancellationToken);
        if (externalClient is null
            || !externalClient.CanAuthenticate(now)
            || !tokenService.VerifySecret(clientSecret, externalClient.SecretHash))
        {
            throw Unauthorized();
        }

        var grantedPermissions = await externalClientRepository.ListActiveGrantPermissionCodesAsync(
                externalClient.ClientId,
                externalClient.OrganizationId,
                externalClient.EnvironmentId,
                now,
                cancellationToken);

        var requestedPermissions = SplitScope(scope);
        if (requestedPermissions.Count == 0)
        {
            requestedPermissions = grantedPermissions.ToHashSet(StringComparer.Ordinal);
        }
        else if (!requestedPermissions.IsSubsetOf(grantedPermissions.ToHashSet(StringComparer.Ordinal)))
        {
            throw Unauthorized();
        }

        var orderedScope = requestedPermissions.Order(StringComparer.Ordinal).ToArray();
        var accessToken = tokenService.CreateExternalClientAccessToken(
            externalClient.ClientId,
            externalClient.OrganizationId.Id,
            externalClient.EnvironmentId.Id,
            externalClient.PermissionVersion,
            orderedScope);
        return new ClientCredentialsTokenResponse(
            accessToken,
            "Bearer",
            tokenService.GetAccessTokenExpiresAtUtc(now),
            string.Join(' ', orderedScope));
    }

    public async Task<bool> PrincipalHasPermissionAsync(
        CurrentPrincipalResponse principal,
        string organizationId,
        string environmentId,
        string permissionCode,
        string? resourceType,
        string? resourceId,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(principal.PrincipalType, "user", StringComparison.Ordinal))
        {
            return await externalClientRepository.HasActiveGrantAsync(
                    principal.UserId,
                    new OrganizationId(organizationId),
                    new IamEnvironmentId(environmentId),
                    permissionCode,
                    resourceType,
                    resourceId,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
        }

        return await UserHasPermissionAsync(
            principal.UserId,
            organizationId,
            environmentId,
            permissionCode,
            cancellationToken);
    }

    public async Task<EnterpriseAuthResponse> HandleOidcCallbackAsync(
        OidcLoginCallbackRequest request,
        string? clientInfo,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        EnsureEnterpriseIdentityStubAllowed();
        var provider = GetEnabledProvider(request.Provider);
        EnsureCallbackSecret(request.CallbackSecret, provider);
        EnsureAllowedEmailDomain(request.Email, provider);
        var user = await userRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (user is null || !user.Enabled)
        {
            throw Unauthorized();
        }

        var organizationId = new OrganizationId(request.OrganizationId);
        var environmentId = new IamEnvironmentId(request.EnvironmentId);
        if (!await membershipRepository.UserHasMembershipAsync(user.Id, organizationId, environmentId, cancellationToken))
        {
            throw Unauthorized();
        }

        if (provider.RequireMfa)
        {
            var challengeId = mfaChallenges.Create(new MfaChallengeContext(
                user.Id.Id,
                request.Provider,
                request.Subject,
                request.OrganizationId,
                request.EnvironmentId,
                DateTimeOffset.UtcNow.AddMinutes(GetMfaChallengeMinutes())));
            return EnterpriseAuthResponse.Challenge(challengeId);
        }

        user.RecordSuccessfulLogin(DateTimeOffset.UtcNow);
        return EnterpriseAuthResponse.Authenticated(await CreateSessionResponseAsync(
            user,
            clientInfo,
            ipAddress,
            cancellationToken,
            "oidc",
            request.Provider,
            request.Subject,
            null));
    }

    public async Task<EnterpriseAuthResponse> VerifyMfaChallengeAsync(
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
            throw Unauthorized();
        }

        var user = await userRepository.GetByIdAsync(new UserId(context.UserId), cancellationToken);
        if (user is null || !user.Enabled)
        {
            throw Unauthorized();
        }

        if (!await membershipRepository.UserHasMembershipAsync(
                user.Id,
                new OrganizationId(context.OrganizationId),
                new IamEnvironmentId(context.EnvironmentId),
                cancellationToken))
        {
            throw Unauthorized();
        }

        user.RecordSuccessfulLogin(DateTimeOffset.UtcNow);
        return EnterpriseAuthResponse.Authenticated(await CreateSessionResponseAsync(
            user,
            clientInfo,
            ipAddress,
            cancellationToken,
            "oidc",
            context.Provider,
            context.Subject,
            DateTimeOffset.UtcNow));
    }

    private async Task<AuthResponse> CreateSessionResponseAsync(
        User user,
        string? clientInfo,
        string? ipAddress,
        CancellationToken cancellationToken,
        string authenticationMethod = "password",
        string? externalProvider = null,
        string? externalSubject = null,
        DateTimeOffset? mfaVerifiedAtUtc = null,
        UserSession? previousSession = null)
    {
        var refreshToken = tokenService.CreateRefreshToken();
        var now = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(externalProvider)
            && !string.IsNullOrWhiteSpace(externalSubject))
        {
            var activeSsoSessions = await userSessionRepository.ListActiveByExternalIdentityAsync(
                externalProvider,
                externalSubject,
                now,
                cancellationToken);
            foreach (var activeSsoSession in activeSsoSessions)
            {
                activeSsoSession.Revoke(now, "sso-rotated");
            }
        }

        var session = new UserSession(
            new UserSessionId($"session-{Guid.CreateVersion7():N}"),
            user.Id,
            tokenService.HashSecret(refreshToken),
            now,
            now.AddDays(14),
            user.PermissionVersion,
            clientInfo,
            ipAddress,
            authenticationMethod,
            externalProvider,
            externalSubject,
            mfaVerifiedAtUtc,
            previousSession?.TokenFamilyId,
            previousSession?.Id.Id);

        await userSessionRepository.AddAsync(session, cancellationToken);
        var membership = await membershipRepository.GetFirstByUserIdAsync(user.Id, cancellationToken);
        var issuedAtUtc = DateTimeOffset.UtcNow;
        var accessToken = membership is null
            ? tokenService.CreateAccessToken(user, session, issuedAtUtc)
            : tokenService.CreateAccessToken(
                user,
                session,
                membership.OrganizationId.Id,
                membership.EnvironmentId.Id,
                issuedAtUtc);
        var expiresAtUtc = tokenService.GetAccessTokenExpiresAtUtc(issuedAtUtc);
        return new AuthResponse(accessToken, refreshToken, session.Id.Id, expiresAtUtc);
    }

    private static UnauthorizedAccessException Unauthorized()
    {
        return new UnauthorizedAccessException("Unauthorized.");
    }

    private async Task<Membership?> GetTokenMembershipAsync(
        AccessTokenPrincipal principal,
        UserId userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(principal.OrganizationId)
            || string.IsNullOrWhiteSpace(principal.EnvironmentId))
        {
            return await membershipRepository.GetFirstByUserIdAsync(userId, cancellationToken);
        }

        return await membershipRepository.GetByUserIdAndOrgEnvAsync(
            userId,
            new OrganizationId(principal.OrganizationId),
            new IamEnvironmentId(principal.EnvironmentId),
            cancellationToken);
    }

    private static HashSet<string> SplitScope(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return [];
        }

        return scope
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    private OidcProviderOptions GetEnabledProvider(string provider)
    {
        if (!enterpriseIdentityOptions.Value.OidcProviders.TryGetValue(provider, out var options)
            || !options.Enabled)
        {
            throw Unauthorized();
        }

        return options;
    }

    private void EnsureEnterpriseIdentityStubAllowed()
    {
        if (!environment.IsDevelopment())
        {
            throw Unauthorized();
        }
    }

    private static void EnsureCallbackSecret(string callbackSecret, OidcProviderOptions provider)
    {
        if (string.IsNullOrWhiteSpace(provider.CallbackSecret)
            || !FixedTimeEquals(callbackSecret, provider.CallbackSecret))
        {
            throw Unauthorized();
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
            throw Unauthorized();
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
