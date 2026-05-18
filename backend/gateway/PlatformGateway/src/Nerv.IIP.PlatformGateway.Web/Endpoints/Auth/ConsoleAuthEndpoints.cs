using System.Net;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;

namespace Nerv.IIP.PlatformGateway.Web.Endpoints.Auth;

[HttpPost("/api/console/v1/auth/login")]
[AllowAnonymous]
public sealed class LoginConsoleUserEndpoint(IGatewayIamAuthClient iam) : Endpoint<ConsoleLoginRequest, ConsoleAuthResponse>
{
    public override async Task HandleAsync(ConsoleLoginRequest req, CancellationToken ct)
    {
        try
        {
            await Send.OkAsync(await iam.LoginAsync(req, ct), ct);
        }
        catch (GatewayAuthException ex)
        {
            await ConsoleAuthEndpointResults.WriteProblemAsync(HttpContext, ex, ct);
        }
    }
}

[HttpPost("/api/console/v1/auth/refresh")]
[AllowAnonymous]
public sealed class RefreshConsoleSessionEndpoint(IGatewayIamAuthClient iam) : Endpoint<ConsoleRefreshRequest, ConsoleAuthResponse>
{
    public override async Task HandleAsync(ConsoleRefreshRequest req, CancellationToken ct)
    {
        try
        {
            await Send.OkAsync(await iam.RefreshAsync(req, ct), ct);
        }
        catch (GatewayAuthException ex)
        {
            await ConsoleAuthEndpointResults.WriteProblemAsync(HttpContext, ex, ct);
        }
    }
}

[HttpPost("/api/console/v1/auth/logout")]
[AllowAnonymous]
public sealed class LogoutConsoleSessionEndpoint(IGatewayIamAuthClient iam) : Endpoint<ConsoleLogoutRequest>
{
    public override async Task HandleAsync(ConsoleLogoutRequest req, CancellationToken ct)
    {
        var bearerToken = ConsoleAuthEndpointResults.ExtractBearerToken(HttpContext);
        if (bearerToken is null)
        {
            await ConsoleAuthEndpointResults.WriteUnauthorizedAsync(HttpContext, ct);
            return;
        }

        try
        {
            await iam.LogoutAsync(bearerToken, req, ct);
            HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
        }
        catch (GatewayAuthException ex)
        {
            await ConsoleAuthEndpointResults.WriteProblemAsync(HttpContext, ex, ct);
        }
    }
}

[HttpGet("/api/console/v1/auth/me")]
[AllowAnonymous]
public sealed class GetConsolePrincipalEndpoint(IGatewayIamAuthClient iam) : EndpointWithoutRequest<ConsolePrincipalResponse>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var bearerToken = ConsoleAuthEndpointResults.ExtractBearerToken(HttpContext);
        if (bearerToken is null)
        {
            await ConsoleAuthEndpointResults.WriteUnauthorizedAsync(HttpContext, ct);
            return;
        }

        try
        {
            await Send.OkAsync(await iam.GetMeAsync(bearerToken, ct), ct);
        }
        catch (GatewayAuthException ex)
        {
            await ConsoleAuthEndpointResults.WriteProblemAsync(HttpContext, ex, ct);
        }
    }
}

internal static class ConsoleAuthEndpointResults
{
    public static string? ExtractBearerToken(HttpContext context)
    {
        var value = context.Request.Headers.Authorization.ToString();
        return value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? value["Bearer ".Length..]
            : null;
    }

    public static Task WriteUnauthorizedAsync(HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return context.Response.WriteAsJsonAsync(
            new { title = "Unauthorized", detail = "Unauthorized.", status = StatusCodes.Status401Unauthorized },
            cancellationToken);
    }

    public static Task WriteProblemAsync(HttpContext context, GatewayAuthException exception, CancellationToken cancellationToken)
    {
        var status = (int)exception.StatusCode;
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(
            new { title = TitleFor(exception.StatusCode), detail = exception.Reason, status },
            cancellationToken);
    }

    private static string TitleFor(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "Unauthorized",
        HttpStatusCode.ServiceUnavailable => "Service Unavailable",
        HttpStatusCode.BadGateway => "Bad Gateway",
        _ => "Gateway Error"
    };
}
