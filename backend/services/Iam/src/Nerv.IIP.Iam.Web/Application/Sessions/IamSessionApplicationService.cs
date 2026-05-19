using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Web.Application.Auth;

namespace Nerv.IIP.Iam.Web.Application.Sessions;

public sealed record SessionResponse(
    string SessionId,
    string UserId,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc,
    int PermissionVersion);

public interface IIamSessionApplicationService
{
    Task<IReadOnlyList<SessionResponse>> ListSessionsAsync(CancellationToken cancellationToken);

    Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken);
}

public sealed class InMemoryIamSessionApplicationService(InMemoryIamStore store) : IIamSessionApplicationService
{
    public Task<IReadOnlyList<SessionResponse>> ListSessionsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SessionResponse> sessions = store.Sessions
            .Select(x => new SessionResponse(
                x.SessionId,
                x.UserId,
                x.IssuedAtUtc,
                x.ExpiresAtUtc,
                x.RevokedAtUtc,
                x.PermissionVersion))
            .ToArray();
        return Task.FromResult(sessions);
    }

    public Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        store.Logout(sessionId);
        return Task.CompletedTask;
    }
}

public sealed class PostgreSqlIamSessionApplicationService(
    ApplicationDbContext dbContext,
    IIamAuthService auth) : IIamSessionApplicationService
{
    public async Task<IReadOnlyList<SessionResponse>> ListSessionsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.UserSessions
            .AsNoTracking()
            .OrderByDescending(x => x.IssuedAtUtc)
            .Select(x => new SessionResponse(
                x.Id.Id,
                x.UserId.Id,
                x.IssuedAtUtc,
                x.ExpiresAtUtc,
                x.RevokedAtUtc,
                x.PermissionVersion))
            .ToListAsync(cancellationToken);
    }

    public async Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        await auth.RevokeSessionAsync(sessionId, "admin-revoke", cancellationToken);
    }
}
