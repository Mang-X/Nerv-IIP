using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Iam.Web.Application.Auth;

namespace Nerv.IIP.Iam.Web.Endpoints.Auth;

[HttpPost("/api/iam/v1/auth/login")]
[AllowAnonymous]
public sealed class LoginEndpoint(IMediator mediator) : Endpoint<LoginRequest>
{
    public override async Task HandleAsync(LoginRequest req, CancellationToken ct)
    {
        await IamEndpointResults.WriteAuthCommandResultAsync(
            HttpContext,
            () => mediator.Send(new LoginCommand(req.LoginName, req.Password, UserAgent(), RemoteIp()), ct),
            ct);
    }

    private string? UserAgent() => HttpContext.Request.Headers.UserAgent.ToString();
    private string? RemoteIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}

[HttpPost("/api/iam/v1/auth/refresh")]
[AllowAnonymous]
public sealed class RefreshEndpoint(IMediator mediator) : Endpoint<RefreshRequest>
{
    public override async Task HandleAsync(RefreshRequest req, CancellationToken ct)
    {
        await IamEndpointResults.WriteAuthCommandResultAsync(
            HttpContext,
            () => mediator.Send(new RefreshCommand(req.RefreshToken, UserAgent(), RemoteIp()), ct),
            ct);
    }

    private string? UserAgent() => HttpContext.Request.Headers.UserAgent.ToString();
    private string? RemoteIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}

[HttpPost("/api/iam/v1/auth/logout")]
[AllowAnonymous]
public sealed class LogoutEndpoint(IMediator mediator) : Endpoint<LogoutRequest>
{
    public override async Task HandleAsync(LogoutRequest req, CancellationToken ct)
    {
        await mediator.Send(new LogoutCommand(req.SessionId ?? string.Empty), ct);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}

[HttpPost("/api/iam/v1/connectors/credentials/validate")]
[AllowAnonymous]
public sealed class ValidateConnectorCredentialEndpoint(IIamAuthService auth) : Endpoint<ValidateConnectorCredentialRequest>
{
    public override async Task HandleAsync(ValidateConnectorCredentialRequest req, CancellationToken ct)
    {
        await IamEndpointResults.WriteAuthResultAsync(
            HttpContext,
            () => auth.ValidateConnectorCredentialAsync(req.ConnectorHostId, req.Secret, ct),
            ct);
    }
}

[HttpGet("/api/iam/v1/me")]
[AllowAnonymous]
public sealed class GetMeEndpoint(IIamAuthService auth) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var principal = await auth.GetCurrentPrincipalAsync(HttpContext, ct);
        if (principal is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        await Send.OkAsync(principal, ct);
    }
}

internal static class IamEndpointResults
{
    public static async Task WriteAuthCommandResultAsync<T>(
        HttpContext context,
        Func<Task<AuthCommandResult<T>>> action,
        CancellationToken cancellationToken)
    {
        var result = await action();
        if (!result.IsAuthorized)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(
                new { title = "Unauthorized", detail = result.Detail, status = StatusCodes.Status401Unauthorized },
                cancellationToken);
            return;
        }

        await context.Response.WriteAsJsonAsync(result.Response, cancellationToken);
    }

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

}
