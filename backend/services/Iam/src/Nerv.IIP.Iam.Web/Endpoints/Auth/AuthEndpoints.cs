using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Iam.Domain;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Web.Application.Auth;

namespace Nerv.IIP.Iam.Web.Endpoints.Auth;

[HttpPost("/api/iam/v1/auth/login")]
[AllowAnonymous]
public sealed class LoginEndpoint(IServiceProvider serviceProvider, IConfiguration configuration) : Endpoint<LoginRequest>
{
    public override async Task HandleAsync(LoginRequest req, CancellationToken ct)
    {
        if (IamEndpointResults.IsPostgreSql(configuration))
        {
            var auth = serviceProvider.GetRequiredService<IamAuthService>();
            await IamEndpointResults.WriteAuthResultAsync(
                HttpContext,
                () => auth.LoginAsync(req.LoginName, req.Password, UserAgent(), RemoteIp(), ct),
                ct);
            return;
        }

        var store = serviceProvider.GetRequiredService<InMemoryIamStore>();
        await IamEndpointResults.WriteAuthResultAsync(HttpContext, () => Task.FromResult(store.Login(req.LoginName, req.Password)), ct);
    }

    private string? UserAgent() => HttpContext.Request.Headers.UserAgent.ToString();
    private string? RemoteIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}

[HttpPost("/api/iam/v1/auth/refresh")]
[AllowAnonymous]
public sealed class RefreshEndpoint(IServiceProvider serviceProvider, IConfiguration configuration) : Endpoint<RefreshRequest>
{
    public override async Task HandleAsync(RefreshRequest req, CancellationToken ct)
    {
        if (IamEndpointResults.IsPostgreSql(configuration))
        {
            var auth = serviceProvider.GetRequiredService<IamAuthService>();
            await IamEndpointResults.WriteAuthResultAsync(
                HttpContext,
                () => auth.RefreshAsync(req.RefreshToken, UserAgent(), RemoteIp(), ct),
                ct);
            return;
        }

        var store = serviceProvider.GetRequiredService<InMemoryIamStore>();
        await IamEndpointResults.WriteAuthResultAsync(HttpContext, () => Task.FromResult(store.Refresh(req.RefreshToken)), ct);
    }

    private string? UserAgent() => HttpContext.Request.Headers.UserAgent.ToString();
    private string? RemoteIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}

[HttpPost("/api/iam/v1/auth/logout")]
[AllowAnonymous]
public sealed class LogoutEndpoint(IServiceProvider serviceProvider, IConfiguration configuration) : Endpoint<LogoutRequest>
{
    public override async Task HandleAsync(LogoutRequest req, CancellationToken ct)
    {
        if (IamEndpointResults.IsPostgreSql(configuration))
        {
            var auth = serviceProvider.GetRequiredService<IamAuthService>();
            await auth.RevokeSessionAsync(req.SessionId ?? string.Empty, "logout", ct);
            HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        var store = serviceProvider.GetRequiredService<InMemoryIamStore>();
        store.Logout(req.SessionId ?? string.Empty);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}

[HttpPost("/api/iam/v1/connectors/credentials/validate")]
[AllowAnonymous]
public sealed class ValidateConnectorCredentialEndpoint(IServiceProvider serviceProvider, IConfiguration configuration) : Endpoint<ValidateConnectorCredentialRequest>
{
    public override async Task HandleAsync(ValidateConnectorCredentialRequest req, CancellationToken ct)
    {
        if (IamEndpointResults.IsPostgreSql(configuration))
        {
            var auth = serviceProvider.GetRequiredService<IamAuthService>();
            await IamEndpointResults.WriteAuthResultAsync(
                HttpContext,
                () => auth.ValidateConnectorCredentialAsync(req.ConnectorHostId, req.Secret, ct),
                ct);
            return;
        }

        var store = serviceProvider.GetRequiredService<InMemoryIamStore>();
        await IamEndpointResults.WriteAuthResultAsync(HttpContext, () => Task.FromResult(store.ValidateConnectorHost(req.ConnectorHostId, req.Secret)), ct);
    }
}

[HttpGet("/api/iam/v1/me")]
[AllowAnonymous]
public sealed class GetMeEndpoint(IServiceProvider serviceProvider, IConfiguration configuration) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (IamEndpointResults.IsPostgreSql(configuration))
        {
            var auth = serviceProvider.GetRequiredService<IamAuthService>();
            var principal = await auth.GetCurrentPrincipalAsync(HttpContext, ct);
            if (principal is null)
            {
                await Send.UnauthorizedAsync(ct);
                return;
            }

            await Send.OkAsync(principal, ct);
            return;
        }

        var store = serviceProvider.GetRequiredService<InMemoryIamStore>();
        var user = IamEndpointResults.ValidateBearer(HttpContext, store);
        if (user is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        try
        {
            var currentPrincipal = store.GetCurrentPrincipal(user);
            await Send.OkAsync(new CurrentPrincipalResponse(
                currentPrincipal.UserId,
                currentPrincipal.LoginName,
                currentPrincipal.Email,
                currentPrincipal.PrincipalType,
                currentPrincipal.OrganizationId,
                currentPrincipal.EnvironmentId,
                currentPrincipal.PermissionVersion), ct);
        }
        catch (UnauthorizedAccessException)
        {
            await Send.UnauthorizedAsync(ct);
        }
    }
}

internal static class IamEndpointResults
{
    public static async Task WriteAuthResultAsync<T>(HttpContext context, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            await context.Response.WriteAsJsonAsync(await action(), cancellationToken);
        }
        catch (UnauthorizedAccessException ex)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { title = "Unauthorized", detail = ex.Message, status = StatusCodes.Status401Unauthorized }, cancellationToken);
        }
    }

    public static bool IsPostgreSql(IConfiguration configuration)
    {
        return string.Equals(configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase);
    }

    public static UserFact? ValidateBearer(HttpContext context, InMemoryIamStore store)
    {
        var value = context.Request.Headers.Authorization.ToString();
        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return store.ValidateAccessToken(value["Bearer ".Length..]);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
