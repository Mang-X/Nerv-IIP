using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Web.Endpoints;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Web.Endpoints.Roles;

[HttpGet("/api/iam/v1/roles")]
[AllowAnonymous]
public sealed class ListRolesEndpoint(IServiceProvider serviceProvider, IConfiguration configuration) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (IsPostgreSql(configuration))
        {
            if (!await IamEndpointAuthorization.RequirePermissionAsync(serviceProvider, HttpContext, "iam.roles.read", ct))
            {
                return;
            }

            var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>();
            var notDeleted = new Deleted(false);
            var roles = await dbContext.Roles
                .AsNoTracking()
                .Where(x => x.Deleted == notDeleted)
                .OrderBy(x => x.RoleName)
                .Select(x => new
                {
                    RoleId = x.Id.Id,
                    x.RoleName,
                    PermissionCodes = x.Permissions
                        .Select(p => p.PermissionCode)
                        .OrderBy(code => code)
                        .ToArray()
                })
                .ToListAsync(ct);

            await HttpContext.Response.WriteAsJsonAsync(roles, ct);
            return;
        }

        var store = serviceProvider.GetRequiredService<InMemoryIamStore>();
        await HttpContext.Response.WriteAsJsonAsync(store.Roles, ct);
    }

    private static bool IsPostgreSql(IConfiguration configuration)
    {
        return string.Equals(configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase);
    }
}

[HttpPost("/api/iam/v1/roles")]
[AllowAnonymous]
public sealed class CreateRoleEndpoint(IServiceProvider serviceProvider, IConfiguration configuration) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (IsPostgreSql(configuration))
        {
            if (!await IamEndpointAuthorization.RequirePermissionAsync(serviceProvider, HttpContext, "iam.roles.manage", ct))
            {
                return;
            }

            await WriteNotImplementedAsync(HttpContext, "Persisted role creation is not implemented.", ct);
            return;
        }

        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
        await HttpContext.Response.WriteAsJsonAsync(new { roleId = "role-placeholder" }, ct);
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

[HttpPatch("/api/iam/v1/roles/{roleId}/permissions")]
[AllowAnonymous]
public sealed class PatchRolePermissionsEndpoint(IServiceProvider serviceProvider, IConfiguration configuration) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (IsPostgreSql(configuration))
        {
            if (!await IamEndpointAuthorization.RequirePermissionAsync(serviceProvider, HttpContext, "iam.roles.manage", ct))
            {
                return;
            }

            await WriteNotImplementedAsync(HttpContext, "Persisted role permission updates are not implemented.", ct);
            return;
        }

        await HttpContext.Response.WriteAsJsonAsync(new { roleId = Route<string>("roleId") }, ct);
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
