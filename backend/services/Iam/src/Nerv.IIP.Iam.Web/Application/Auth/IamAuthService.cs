using Nerv.IIP.Iam.Domain.AggregatesModel.ConnectorHostCredentialAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserSessionAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Infrastructure.Repositories;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Web.Application.Auth;

public sealed class PostgreSqlIamAuthService(
    IUserRepository userRepository,
    IUserSessionRepository userSessionRepository,
    IMembershipRepository membershipRepository,
    IConnectorHostCredentialRepository connectorHostCredentialRepository,
    IExternalClientRepository externalClientRepository,
    IamPasswordService passwordService,
    IamTokenService tokenService)
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

        if (!passwordService.Verify(user, password))
        {
            user.RecordFailedLogin();
            await userRepository.PersistFailedLoginAsync(user, cancellationToken);
            throw Unauthorized();
        }

        user.RecordSuccessfulLogin(DateTimeOffset.UtcNow);
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
        var session = await userSessionRepository.GetActiveByRefreshTokenHashAsync(refreshTokenHash, now, cancellationToken);
        if (session is null)
        {
            throw Unauthorized();
        }

        var user = await userRepository.GetByIdAsync(session.UserId, cancellationToken);
        if (user is null || !user.Enabled)
        {
            throw Unauthorized();
        }

        session.Revoke(now, "refresh-rotated");
        return await CreateSessionResponseAsync(user, clientInfo, ipAddress, cancellationToken);
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

        var membership = await membershipRepository.GetFirstByUserIdAsync(userId, cancellationToken);
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
        var secretHash = tokenService.HashSecret(secret);
        var credential = await connectorHostCredentialRepository.GetByConnectorHostAndSecretHashAsync(
            connectorHostId,
            secretHash,
            cancellationToken);
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
        var secretHash = tokenService.HashSecret(clientSecret);
        var externalClient = await externalClientRepository.GetByClientIdAsync(clientId, cancellationToken);
        if (externalClient is null || !externalClient.CanAuthenticate(secretHash, now))
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
        CancellationToken cancellationToken)
    {
        if (!string.Equals(principal.PrincipalType, "user", StringComparison.Ordinal))
        {
            return await externalClientRepository.HasActiveGrantAsync(
                    principal.UserId,
                    new OrganizationId(organizationId),
                    new IamEnvironmentId(environmentId),
                    permissionCode,
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

    private async Task<AuthResponse> CreateSessionResponseAsync(
        User user,
        string? clientInfo,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var refreshToken = tokenService.CreateRefreshToken();
        var now = DateTimeOffset.UtcNow;
        var session = new UserSession(
            new UserSessionId($"session-{Guid.NewGuid():N}"),
            user.Id,
            tokenService.HashSecret(refreshToken),
            now,
            now.AddDays(14),
            user.PermissionVersion,
            clientInfo,
            ipAddress);

        await userSessionRepository.AddAsync(session, cancellationToken);
        var membership = await membershipRepository.GetFirstByUserIdAsync(user.Id, cancellationToken);
        var issuedAtUtc = DateTimeOffset.UtcNow;
        var accessToken = membership is null
            ? tokenService.CreateAccessToken(user, session)
            : tokenService.CreateAccessToken(
                user,
                session,
                membership.OrganizationId.Id,
                membership.EnvironmentId.Id);
        var expiresAtUtc = tokenService.GetAccessTokenExpiresAtUtc(issuedAtUtc);
        return new AuthResponse(accessToken, refreshToken, session.Id.Id, expiresAtUtc);
    }

    private static UnauthorizedAccessException Unauthorized()
    {
        return new UnauthorizedAccessException("Unauthorized.");
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
}
