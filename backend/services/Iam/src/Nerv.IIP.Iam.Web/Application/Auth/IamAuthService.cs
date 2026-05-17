using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Iam.Domain.AggregatesModel.ConnectorHostCredentialAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserSessionAggregate;
using Nerv.IIP.Iam.Infrastructure;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Web.Application.Auth;

public sealed class IamAuthService(
    IServiceProvider serviceProvider,
    IamPasswordService passwordService,
    IamTokenService tokenService)
{
    private static readonly Deleted NotDeleted = new(false);

    public async Task<AuthResponse> LoginAsync(
        string loginName,
        string password,
        string? clientInfo,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var dbContext = GetDbContext();
        var user = await dbContext.Users
            .SingleOrDefaultAsync(x => x.LoginName == loginName && x.Deleted == NotDeleted, cancellationToken);
        if (user is null || !user.Enabled)
        {
            throw Unauthorized();
        }

        if (!passwordService.Verify(user, password))
        {
            user.RecordFailedLogin();
            await dbContext.SaveChangesAsync(cancellationToken);
            throw Unauthorized();
        }

        user.RecordSuccessfulLogin(DateTimeOffset.UtcNow);
        var response = CreateSessionResponse(dbContext, user, clientInfo, ipAddress);
        await dbContext.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<AuthResponse> RefreshAsync(
        string refreshToken,
        string? clientInfo,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var dbContext = GetDbContext();
        var now = DateTimeOffset.UtcNow;
        var refreshTokenHash = tokenService.HashSecret(refreshToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var session = await dbContext.UserSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.RefreshTokenHash == refreshTokenHash && x.RevokedAtUtc == null && x.ExpiresAtUtc > now,
                cancellationToken);
        if (session is null)
        {
            throw Unauthorized();
        }

        var user = await dbContext.Users
            .SingleOrDefaultAsync(x => x.Id == session.UserId && x.Deleted == NotDeleted, cancellationToken);
        if (user is null || !user.Enabled)
        {
            throw Unauthorized();
        }

        var revokedRows = await dbContext.UserSessions
            .Where(x => x.Id == session.Id && x.RevokedAtUtc == null && x.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.RevokedAtUtc, now)
                    .SetProperty(x => x.RevokedReason, "refresh-rotated"),
                cancellationToken);
        if (revokedRows != 1)
        {
            throw Unauthorized();
        }

        var response = CreateSessionResponse(dbContext, user, clientInfo, ipAddress);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task RevokeSessionAsync(string sessionId, string reason, CancellationToken cancellationToken)
    {
        var dbContext = GetDbContext();
        var session = await dbContext.UserSessions.FindAsync([new UserSessionId(sessionId)], cancellationToken);
        if (session is null)
        {
            return;
        }

        session.Revoke(DateTimeOffset.UtcNow, reason);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<CurrentPrincipalResponse?> GetCurrentPrincipalAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var dbContext = GetDbContext();
        var principal = tokenService.TryReadPrincipal(httpContext);
        if (principal is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var sessionId = new UserSessionId(principal.SessionId);
        var userId = new UserId(principal.UserId);
        var session = await dbContext.UserSessions
            .SingleOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, cancellationToken);
        if (session is null || !session.CanRefresh(now) || session.PermissionVersion != principal.PermissionVersion)
        {
            return null;
        }

        var user = await dbContext.Users
            .SingleOrDefaultAsync(x => x.Id == userId && x.Deleted == NotDeleted, cancellationToken);
        if (user is null
            || !user.Enabled
            || !string.Equals(user.SecurityStamp, principal.SecurityStamp, StringComparison.Ordinal)
            || user.PermissionVersion != principal.PermissionVersion)
        {
            return null;
        }

        return new CurrentPrincipalResponse(user.Id.Id, user.LoginName, user.Email, "user");
    }

    public async Task<ConnectorPrincipalResponse> ValidateConnectorCredentialAsync(
        string connectorHostId,
        string secret,
        CancellationToken cancellationToken)
    {
        var dbContext = GetDbContext();
        var secretHash = tokenService.HashSecret(secret);
        var credential = await dbContext.ConnectorHostCredentials
            .SingleOrDefaultAsync(x => x.ConnectorHostId == connectorHostId && x.SecretHash == secretHash, cancellationToken);
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

    private AuthResponse CreateSessionResponse(ApplicationDbContext dbContext, User user, string? clientInfo, string? ipAddress)
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

        dbContext.UserSessions.Add(session);
        var accessToken = tokenService.CreateAccessToken(user, session);
        return new AuthResponse(accessToken, refreshToken, session.Id.Id);
    }

    private static UnauthorizedAccessException Unauthorized()
    {
        return new UnauthorizedAccessException("Unauthorized.");
    }

    private ApplicationDbContext GetDbContext()
    {
        return serviceProvider.GetRequiredService<ApplicationDbContext>();
    }
}
