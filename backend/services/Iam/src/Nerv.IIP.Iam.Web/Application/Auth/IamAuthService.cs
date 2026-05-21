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
            return null;
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
}
