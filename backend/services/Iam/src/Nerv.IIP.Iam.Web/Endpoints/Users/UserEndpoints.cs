using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Web.Endpoints;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Web.Endpoints.Users;

[HttpGet("/api/iam/v1/users")]
[AllowAnonymous]
public sealed class ListUsersEndpoint(IServiceProvider serviceProvider, IConfiguration configuration) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (IsPostgreSql(configuration))
        {
            if (!await IamEndpointAuthorization.RequirePermissionAsync(serviceProvider, HttpContext, "iam.users.read", ct))
            {
                return;
            }

            var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>();
            var notDeleted = new Deleted(false);
            var users = await dbContext.Users
                .AsNoTracking()
                .Where(x => x.Deleted == notDeleted)
                .OrderBy(x => x.LoginName)
                .Select(x => new
                {
                    UserId = x.Id.Id,
                    x.LoginName,
                    x.Email,
                    x.Enabled
                })
                .ToListAsync(ct);
            await HttpContext.Response.WriteAsJsonAsync(users, ct);
            return;
        }

        var store = serviceProvider.GetRequiredService<InMemoryIamStore>();
        await HttpContext.Response.WriteAsJsonAsync(store.Users.Select(x => new { x.UserId, x.LoginName, x.Email, x.Enabled }), ct);
    }

    private static bool IsPostgreSql(IConfiguration configuration)
    {
        return string.Equals(configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase);
    }
}

[HttpPost("/api/iam/v1/users")]
[AllowAnonymous]
public sealed class CreateUserEndpoint(IServiceProvider serviceProvider, IConfiguration configuration) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (IsPostgreSql(configuration))
        {
            if (!await IamEndpointAuthorization.RequirePermissionAsync(serviceProvider, HttpContext, "iam.users.manage", ct))
            {
                return;
            }

            await WriteNotImplementedAsync(HttpContext, "Persisted user creation is not implemented.", ct);
            return;
        }

        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
        await HttpContext.Response.WriteAsJsonAsync(new { userId = "user-placeholder" }, ct);
    }

    private static bool IsPostgreSql(IConfiguration configuration)
    {
        return string.Equals(configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteNotImplementedAsync(HttpContext context, string detail, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status501NotImplemented;
        await context.Response.WriteAsJsonAsync(new { title = "Not Implemented", detail, status = StatusCodes.Status501NotImplemented }, cancellationToken);
    }
}

[HttpPatch("/api/iam/v1/users/{userId}")]
[AllowAnonymous]
public sealed class PatchUserEndpoint(IServiceProvider serviceProvider, IConfiguration configuration) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (IsPostgreSql(configuration))
        {
            if (!await IamEndpointAuthorization.RequirePermissionAsync(serviceProvider, HttpContext, "iam.users.manage", ct))
            {
                return;
            }

            await WriteNotImplementedAsync(HttpContext, "Persisted user updates are not implemented.", ct);
            return;
        }

        await HttpContext.Response.WriteAsJsonAsync(new { userId = Route<string>("userId") }, ct);
    }

    private static bool IsPostgreSql(IConfiguration configuration)
    {
        return string.Equals(configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteNotImplementedAsync(HttpContext context, string detail, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status501NotImplemented;
        await context.Response.WriteAsJsonAsync(new { title = "Not Implemented", detail, status = StatusCodes.Status501NotImplemented }, cancellationToken);
    }
}

[HttpPost("/api/iam/v1/users/{userId}/disable")]
[AllowAnonymous]
public sealed class DisableUserEndpoint(IServiceProvider serviceProvider, IConfiguration configuration) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (IsPostgreSql(configuration))
        {
            if (!await IamEndpointAuthorization.RequirePermissionAsync(serviceProvider, HttpContext, "iam.users.manage", ct))
            {
                return;
            }

            await WriteNotImplementedAsync(HttpContext, "Persisted user disable is not implemented.", ct);
            return;
        }

        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static bool IsPostgreSql(IConfiguration configuration)
    {
        return string.Equals(configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteNotImplementedAsync(HttpContext context, string detail, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status501NotImplemented;
        await context.Response.WriteAsJsonAsync(new { title = "Not Implemented", detail, status = StatusCodes.Status501NotImplemented }, cancellationToken);
    }
}
