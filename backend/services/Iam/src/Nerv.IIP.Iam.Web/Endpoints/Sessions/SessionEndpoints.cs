using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Web.Application.Auth;

namespace Nerv.IIP.Iam.Web.Endpoints.Sessions;

[HttpGet("/api/iam/v1/sessions")]
[AllowAnonymous]
public sealed class ListSessionsEndpoint(IServiceProvider serviceProvider, IConfiguration configuration) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (IsPostgreSql(configuration))
        {
            var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>();
            var sessions = await dbContext.UserSessions
                .AsNoTracking()
                .OrderByDescending(x => x.IssuedAtUtc)
                .Select(x => new
                {
                    SessionId = x.Id.Id,
                    UserId = x.UserId.Id,
                    x.IssuedAtUtc,
                    x.ExpiresAtUtc,
                    x.RevokedAtUtc,
                    x.PermissionVersion
                })
                .ToListAsync(ct);
            await HttpContext.Response.WriteAsJsonAsync(sessions, ct);
            return;
        }

        var store = serviceProvider.GetRequiredService<InMemoryIamStore>();
        await HttpContext.Response.WriteAsJsonAsync(store.Sessions, ct);
    }

    private static bool IsPostgreSql(IConfiguration configuration)
    {
        return string.Equals(configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase);
    }
}

[HttpPost("/api/iam/v1/sessions/{sessionId}/revoke")]
[AllowAnonymous]
public sealed class RevokeSessionEndpoint(IServiceProvider serviceProvider, IConfiguration configuration) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (IsPostgreSql(configuration))
        {
            var auth = serviceProvider.GetRequiredService<IamAuthService>();
            await auth.RevokeSessionAsync(Route<string>("sessionId")!, "admin-revoke", ct);
            HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        var store = serviceProvider.GetRequiredService<InMemoryIamStore>();
        store.Logout(Route<string>("sessionId")!);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static bool IsPostgreSql(IConfiguration configuration)
    {
        return string.Equals(configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase);
    }
}
