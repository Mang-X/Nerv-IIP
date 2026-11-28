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
    int PermissionVersion,
    string LoginName,
    string Status);

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
        var now = DateTimeOffset.UtcNow;
        var users = store.Users.ToDictionary(x => x.UserId, x => x.LoginName);
        var sessions = store.Sessions
            .Select(x => new SessionResponse(
                x.SessionId, x.UserId, x.IssuedAtUtc, x.ExpiresAtUtc, x.RevokedAtUtc, x.PermissionVersion,
                users.GetValueOrDefault(x.UserId, x.UserId),
                SessionListSorting.GetStatus(x.RevokedAtUtc, x.ExpiresAtUtc, now)))
            .Where(session => options.FilterStatus is null || session.Status == options.FilterStatus)
            .Where(session => string.IsNullOrWhiteSpace(options.FilterSearch)
                || session.SessionId.Equals(options.FilterSearch, StringComparison.Ordinal)
                || session.UserId.Equals(options.FilterSearch, StringComparison.Ordinal)
                || session.LoginName.Contains(options.FilterSearch, StringComparison.OrdinalIgnoreCase))
            .ApplySessionSort(options)
            .ThenBy(x => x.SessionId, StringComparer.Ordinal)
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
        var now = DateTimeOffset.UtcNow;
        var (total, page) = await repository.ListPageAsync(new UserSessionListFilter(
            options.FilterStatus, options.FilterSearch, options.SortBy, options.IsDescending, options.Skip, options.PageSize),
            now, cancellationToken);
        var items = page.Select(x => new SessionResponse(
            x.SessionId.Id, x.UserId.Id, x.IssuedAtUtc, x.ExpiresAtUtc,
            x.RevokedAtUtc, x.PermissionVersion, x.LoginName,
            SessionListSorting.GetStatus(x.RevokedAtUtc, x.ExpiresAtUtc, now))).ToArray();
        return new PagedListResponse<SessionResponse>(options.PageIndex, options.PageSize, total, items);
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
    public static string GetStatus(DateTimeOffset? revokedAt, DateTimeOffset expiresAt, DateTimeOffset now) =>
        revokedAt is not null ? "revoked" : expiresAt <= now ? "expired" : "active";

    public static IOrderedEnumerable<SessionResponse> ApplySessionSort(this IEnumerable<SessionResponse> sessions, IamListQueryOptions options)
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
