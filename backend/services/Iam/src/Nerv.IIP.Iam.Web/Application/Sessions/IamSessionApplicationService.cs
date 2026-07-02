using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Infrastructure.Repositories;
using Nerv.IIP.Iam.Web.Application.Auth;
using Nerv.IIP.Iam.Web.Application.SecurityAudit;

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
    Task<PagedListResponse<SessionResponse>> ListSessionsAsync(IamListQueryOptions options, CancellationToken cancellationToken);

    Task RevokeSessionAsync(
        string sessionId,
        SecurityAuditContext? auditContext,
        CancellationToken cancellationToken);
}

public sealed class InMemoryIamSessionApplicationService(InMemoryIamStore store) : IIamSessionApplicationService
{
    public Task<PagedListResponse<SessionResponse>> ListSessionsAsync(IamListQueryOptions options, CancellationToken cancellationToken)
    {
        var sessions = store.Sessions
            .Where(session => options.FilterRevoked is null || (session.RevokedAtUtc is not null) == options.FilterRevoked)
            .Where(session => string.IsNullOrWhiteSpace(options.FilterSearch)
                || session.SessionId.Contains(options.FilterSearch, StringComparison.OrdinalIgnoreCase)
                || session.UserId.Contains(options.FilterSearch, StringComparison.OrdinalIgnoreCase))
            .Select(x => new SessionResponse(
                x.SessionId,
                x.UserId,
                x.IssuedAtUtc,
                x.ExpiresAtUtc,
                x.RevokedAtUtc,
                x.PermissionVersion))
            .ApplySessionSort(options)
            .ToPagedResponse(options);
        return Task.FromResult(sessions);
    }

    public Task RevokeSessionAsync(
        string sessionId,
        SecurityAuditContext? auditContext,
        CancellationToken cancellationToken)
    {
        _ = auditContext;
        store.Logout(sessionId);
        return Task.CompletedTask;
    }
}

public sealed class PostgreSqlIamSessionApplicationService(
    IUserSessionRepository repository,
    IIamAuthService auth) : IIamSessionApplicationService
{
    public async Task<PagedListResponse<SessionResponse>> ListSessionsAsync(IamListQueryOptions options, CancellationToken cancellationToken)
    {
        var sessions = await repository.ListAsync(cancellationToken);
        return sessions
            .Where(session => options.FilterRevoked is null || (session.RevokedAtUtc is not null) == options.FilterRevoked)
            .Where(session => string.IsNullOrWhiteSpace(options.FilterSearch)
                || session.Id.Id.Contains(options.FilterSearch, StringComparison.OrdinalIgnoreCase)
                || session.UserId.Id.Contains(options.FilterSearch, StringComparison.OrdinalIgnoreCase))
            .Select(x => new SessionResponse(
                x.Id.Id,
                x.UserId.Id,
                x.IssuedAtUtc,
                x.ExpiresAtUtc,
                x.RevokedAtUtc,
                x.PermissionVersion))
            .ApplySessionSort(options)
            .ToPagedResponse(options);
    }

    public async Task RevokeSessionAsync(
        string sessionId,
        SecurityAuditContext? auditContext,
        CancellationToken cancellationToken)
    {
        await auth.RevokeSessionAsync(sessionId, "admin-revoke", auditContext, cancellationToken);
    }
}

internal static class SessionListSorting
{
    public static IEnumerable<SessionResponse> ApplySessionSort(this IEnumerable<SessionResponse> sessions, IamListQueryOptions options)
    {
        return (options.SortBy?.ToLowerInvariant(), options.IsDescending) switch
        {
            ("sessionid", true) => sessions.OrderByDescending(x => x.SessionId, StringComparer.Ordinal),
            ("sessionid", false) => sessions.OrderBy(x => x.SessionId, StringComparer.Ordinal),
            ("userid", true) => sessions.OrderByDescending(x => x.UserId, StringComparer.Ordinal),
            ("userid", false) => sessions.OrderBy(x => x.UserId, StringComparer.Ordinal),
            ("expiresatutc", true) => sessions.OrderByDescending(x => x.ExpiresAtUtc),
            ("expiresatutc", false) => sessions.OrderBy(x => x.ExpiresAtUtc),
            ("issuedatutc", false) => sessions.OrderBy(x => x.IssuedAtUtc),
            _ => sessions.OrderByDescending(x => x.IssuedAtUtc)
        };
    }
}
