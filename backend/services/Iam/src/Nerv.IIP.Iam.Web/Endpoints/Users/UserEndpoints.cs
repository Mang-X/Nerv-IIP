using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Iam.Web.Application.Commands.Users;
using Nerv.IIP.Iam.Web.Application.Queries.Users;
using Nerv.IIP.Iam.Web.Application.Users;
using Nerv.IIP.Iam.Web.Endpoints;

namespace Nerv.IIP.Iam.Web.Endpoints.Users;

public sealed record CreateUserRequest(string LoginName, string Email, string Password);
public sealed record UpdateUserRequest(string LoginName, string Email, bool Enabled);

[HttpGet("/api/iam/v1/users")]
[AllowAnonymous]
public sealed class ListUsersEndpoint(IServiceProvider serviceProvider, IConfiguration configuration, IMediator mediator)
    : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (IsPostgreSql(configuration)
            && !await IamEndpointAuthorization.RequirePermissionAsync(serviceProvider, HttpContext, "iam.users.read", ct))
        {
            return;
        }

        var users = await mediator.Send(new ListUsersQuery(), ct);
        await HttpContext.Response.WriteAsJsonAsync(users, ct);
    }

    private static bool IsPostgreSql(IConfiguration configuration)
    {
        return string.Equals(configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase);
    }
}

[HttpPost("/api/iam/v1/users")]
[AllowAnonymous]
public sealed class CreateUserEndpoint(IServiceProvider serviceProvider, IConfiguration configuration, IMediator mediator)
    : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (IsPostgreSql(configuration)
            && !await IamEndpointAuthorization.RequirePermissionAsync(serviceProvider, HttpContext, "iam.users.manage", ct))
        {
            return;
        }

        var req = await HttpContext.Request.ReadFromJsonAsync<CreateUserRequest>(ct)
            ?? throw new BadHttpRequestException("Request body is required.");
        var response = await mediator.Send(new CreateUserCommand(req.LoginName, req.Email, req.Password), ct);
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
        await HttpContext.Response.WriteAsJsonAsync(response, ct);
    }

    private static bool IsPostgreSql(IConfiguration configuration)
    {
        return string.Equals(configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase);
    }
}

[HttpPatch("/api/iam/v1/users/{userId}")]
[AllowAnonymous]
public sealed class PatchUserEndpoint(IServiceProvider serviceProvider, IConfiguration configuration, IMediator mediator)
    : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (IsPostgreSql(configuration)
            && !await IamEndpointAuthorization.RequirePermissionAsync(serviceProvider, HttpContext, "iam.users.manage", ct))
        {
            return;
        }

        var req = await HttpContext.Request.ReadFromJsonAsync<UpdateUserRequest>(ct)
            ?? throw new BadHttpRequestException("Request body is required.");
        var userId = Route<string>("userId") ?? string.Empty;
        var response = await mediator.Send(new UpdateUserCommand(userId, req.LoginName, req.Email, req.Enabled), ct);
        await HttpContext.Response.WriteAsJsonAsync(response, ct);
    }

    private static bool IsPostgreSql(IConfiguration configuration)
    {
        return string.Equals(configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase);
    }
}

[HttpPost("/api/iam/v1/users/{userId}/disable")]
[AllowAnonymous]
public sealed class DisableUserEndpoint(IServiceProvider serviceProvider, IConfiguration configuration, IMediator mediator)
    : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (IsPostgreSql(configuration)
            && !await IamEndpointAuthorization.RequirePermissionAsync(serviceProvider, HttpContext, "iam.users.manage", ct))
        {
            return;
        }

        var userId = Route<string>("userId") ?? string.Empty;
        await mediator.Send(new DisableUserCommand(userId), ct);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static bool IsPostgreSql(IConfiguration configuration)
    {
        return string.Equals(configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase);
    }
}
