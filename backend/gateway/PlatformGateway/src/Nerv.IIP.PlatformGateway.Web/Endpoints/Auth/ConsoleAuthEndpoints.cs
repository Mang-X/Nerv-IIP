using System.Net;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Application.OpenApi;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.PlatformGateway.Web.Endpoints.Auth;

[HttpPost("/api/console/v1/auth/login")]
[GatewayOperationId("loginConsoleUser")]
[AllowAnonymous]
public sealed class LoginConsoleUserEndpoint(IGatewayIamAuthClient iam) : Endpoint<ConsoleLoginRequest, ResponseData<ConsoleAuthResponse>>
{
    public override async Task HandleAsync(ConsoleLoginRequest req, CancellationToken ct)
    {
        try
        {
            var response = await iam.LoginAsync(req, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayAuthException ex)
        {
            await ConsoleAuthEndpointResults.WriteProblemAsync(HttpContext, ex, ct);
        }
    }
}

[HttpPost("/api/console/v1/auth/refresh")]
[GatewayOperationId("refreshConsoleSession")]
[AllowAnonymous]
public sealed class RefreshConsoleSessionEndpoint(IGatewayIamAuthClient iam) : Endpoint<ConsoleRefreshRequest, ResponseData<ConsoleAuthResponse>>
{
    public override async Task HandleAsync(ConsoleRefreshRequest req, CancellationToken ct)
    {
        try
        {
            var response = await iam.RefreshAsync(req, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayAuthException ex)
        {
            await ConsoleAuthEndpointResults.WriteProblemAsync(HttpContext, ex, ct);
        }
    }
}

[HttpPost("/api/console/v1/auth/logout")]
[GatewayOperationId("logoutConsoleSession")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class LogoutConsoleSessionEndpoint(IGatewayIamAuthClient iam) : Endpoint<ConsoleLogoutRequest>
{
    public override async Task HandleAsync(ConsoleLogoutRequest req, CancellationToken ct)
    {
        var bearerToken = await HttpContext.GetTokenAsync("access_token");
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
[GatewayOperationId("getConsolePrincipal")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class GetConsolePrincipalEndpoint(IGatewayIamAuthClient iam) : EndpointWithoutRequest<ResponseData<ConsolePrincipalResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var bearerToken = await HttpContext.GetTokenAsync("access_token");
        if (bearerToken is null)
        {
            await ConsoleAuthEndpointResults.WriteUnauthorizedAsync(HttpContext, ct);
            return;
        }

        try
        {
            var response = await iam.GetMeAsync(bearerToken, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayAuthException ex)
        {
            await ConsoleAuthEndpointResults.WriteProblemAsync(HttpContext, ex, ct);
        }
    }
}

internal static class ConsoleAuthEndpointResults
{
    public static Task WriteUnauthorizedAsync(HttpContext context, CancellationToken cancellationToken)
    {
        return ResponseDataEndpointResults.WriteErrorAsync(
            context,
            StatusCodes.Status401Unauthorized,
            "Unauthorized.",
            cancellationToken);
    }

    public static Task WriteProblemAsync(HttpContext context, GatewayAuthException exception, CancellationToken cancellationToken)
    {
        var status = (int)exception.StatusCode;
        return ResponseDataEndpointResults.WriteErrorAsync(context, status, exception.Reason, cancellationToken);
    }

}
