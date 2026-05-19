using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Iam.Web.Application.Auth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Iam.Web.Endpoints.Auth;

[HttpPost("/api/iam/v1/auth/login")]
[AllowAnonymous]
public sealed class LoginEndpoint(IMediator mediator) : Endpoint<LoginRequest, ResponseData<AuthResponse>>
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
public sealed class RefreshEndpoint(IMediator mediator) : Endpoint<RefreshRequest, ResponseData<AuthResponse>>
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
public sealed class ValidateConnectorCredentialEndpoint(IIamAuthService auth) : Endpoint<ValidateConnectorCredentialRequest, ResponseData<ConnectorPrincipalResponse>>
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
public sealed class GetMeEndpoint(IIamAuthService auth) : EndpointWithoutRequest<ResponseData<CurrentPrincipalResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var principal = await auth.GetCurrentPrincipalAsync(HttpContext, ct);
        if (principal is null)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status401Unauthorized, "Unauthorized.", ct);
            return;
        }

        await Send.OkAsync(principal.AsResponseData(), ct);
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
            await ResponseDataEndpointResults.WriteErrorAsync(
                context,
                StatusCodes.Status401Unauthorized,
                result.Detail ?? "Unauthorized.",
                cancellationToken);
            return;
        }

        await ResponseDataEndpointResults.WriteDataAsync(context, StatusCodes.Status200OK, result.Response!, cancellationToken);
    }

    public static async Task WriteAuthResultAsync<T>(HttpContext context, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            var response = await action();
            await ResponseDataEndpointResults.WriteDataAsync(context, StatusCodes.Status200OK, response, cancellationToken);
        }
        catch (UnauthorizedAccessException ex)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(context, StatusCodes.Status401Unauthorized, ex.Message, cancellationToken);
        }
    }

}
