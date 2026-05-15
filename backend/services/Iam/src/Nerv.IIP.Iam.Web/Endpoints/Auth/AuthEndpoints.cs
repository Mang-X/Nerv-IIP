using System.Text;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Iam.Domain;
using Nerv.IIP.Iam.Infrastructure;

namespace Nerv.IIP.Iam.Web.Endpoints.Auth;

public sealed record LoginRequest(string LoginName, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string SessionId);
public sealed record ValidateConnectorCredentialRequest(string ConnectorHostId, string Secret);

[HttpPost("/api/iam/v1/auth/login")]
[AllowAnonymous]
public sealed class LoginEndpoint(InMemoryIamStore store) : Endpoint<LoginRequest>
{
    public override async Task HandleAsync(LoginRequest req, CancellationToken ct)
    {
        await IamEndpointResults.WriteAuthResultAsync(HttpContext, () => store.Login(req.LoginName, req.Password), ct);
    }
}

[HttpPost("/api/iam/v1/auth/refresh")]
[AllowAnonymous]
public sealed class RefreshEndpoint(InMemoryIamStore store) : Endpoint<RefreshRequest>
{
    public override async Task HandleAsync(RefreshRequest req, CancellationToken ct)
    {
        await IamEndpointResults.WriteAuthResultAsync(HttpContext, () => store.Refresh(req.RefreshToken), ct);
    }
}

[HttpPost("/api/iam/v1/auth/logout")]
[AllowAnonymous]
public sealed class LogoutEndpoint(InMemoryIamStore store) : Endpoint<LogoutRequest>
{
    public override async Task HandleAsync(LogoutRequest req, CancellationToken ct)
    {
        store.Logout(req.SessionId);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
        await Task.CompletedTask;
    }
}

[HttpPost("/api/iam/v1/connectors/credentials/validate")]
[AllowAnonymous]
public sealed class ValidateConnectorCredentialEndpoint(InMemoryIamStore store) : Endpoint<ValidateConnectorCredentialRequest>
{
    public override async Task HandleAsync(ValidateConnectorCredentialRequest req, CancellationToken ct)
    {
        await IamEndpointResults.WriteAuthResultAsync(HttpContext, () => store.ValidateConnectorHost(req.ConnectorHostId, req.Secret), ct);
    }
}

[HttpGet("/api/iam/v1/me")]
[AllowAnonymous]
public sealed class GetMeEndpoint(InMemoryIamStore store) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var user = IamEndpointResults.ValidateBearer(HttpContext, store);
        if (user is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        await Send.OkAsync(new { user.UserId, user.LoginName, user.Email, principalType = "user" }, ct);
    }
}

internal static class IamEndpointResults
{
    public static async Task WriteAuthResultAsync<T>(HttpContext context, Func<T> action, CancellationToken cancellationToken)
    {
        try
        {
            await context.Response.WriteAsJsonAsync(action(), cancellationToken);
        }
        catch (UnauthorizedAccessException ex)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { title = "Unauthorized", detail = ex.Message, status = StatusCodes.Status401Unauthorized }, cancellationToken);
        }
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
