using FastEndpoints;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.PlatformGateway.Web.Application.Auth;

public readonly record struct AuthorizedProxyRequestContext(
    string BearerToken,
    ConsolePrincipalResponse Principal);

public abstract class AuthorizedProxyEndpoint<TRequest, TResponse>(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    string permissionCode) : Endpoint<TRequest, ResponseData<TResponse>>
    where TRequest : notnull
{
    public override Task HandleAsync(TRequest req, CancellationToken ct) =>
        AuthorizedProxyEndpointExecutor.ExecuteAsync(
            HttpContext,
            iam,
            auth,
            permissionCode,
            async (context, cancellationToken) =>
            {
                var response = await ForwardAsync(context.BearerToken, req, cancellationToken);
                await ResponseDataEndpointResults.WriteDataAsync(
                    HttpContext,
                    StatusCodes.Status200OK,
                    response,
                    cancellationToken);
            },
            ct);

    protected abstract Task<TResponse> ForwardAsync(
        string bearerToken,
        TRequest request,
        CancellationToken cancellationToken);
}

public abstract class AuthorizedProxyEndpoint<TResponse>(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    string permissionCode) : EndpointWithoutRequest<ResponseData<TResponse>>
{
    public override Task HandleAsync(CancellationToken ct) =>
        AuthorizedProxyEndpointExecutor.ExecuteAsync(
            HttpContext,
            iam,
            auth,
            permissionCode,
            async (context, cancellationToken) =>
            {
                var response = await ForwardAsync(context.BearerToken, cancellationToken);
                await ResponseDataEndpointResults.WriteDataAsync(
                    HttpContext,
                    StatusCodes.Status200OK,
                    response,
                    cancellationToken);
            },
            ct);

    protected abstract Task<TResponse> ForwardAsync(
        string bearerToken,
        CancellationToken cancellationToken);
}

public abstract class AuthorizedProxyCreatedEndpoint<TRequest, TResponse>(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    string permissionCode) : Endpoint<TRequest>
    where TRequest : notnull
{
    public override Task HandleAsync(TRequest req, CancellationToken ct) =>
        AuthorizedProxyEndpointExecutor.ExecuteAsync(
            HttpContext,
            iam,
            auth,
            permissionCode,
            async (context, cancellationToken) =>
            {
                var response = await ForwardAsync(context.BearerToken, req, cancellationToken);
                await ResponseDataEndpointResults.WriteDataAsync(
                    HttpContext,
                    StatusCodes.Status201Created,
                    response,
                    cancellationToken);
            },
            ct);

    protected abstract Task<TResponse> ForwardAsync(
        string bearerToken,
        TRequest request,
        CancellationToken cancellationToken);
}

public abstract class AuthorizedProxyNoContentEndpoint<TRequest>(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    string permissionCode) : Endpoint<TRequest>
    where TRequest : notnull
{
    public override Task HandleAsync(TRequest req, CancellationToken ct) =>
        AuthorizedProxyEndpointExecutor.ExecuteAsync(
            HttpContext,
            iam,
            auth,
            permissionCode,
            async (context, cancellationToken) =>
            {
                await ForwardAsync(context.BearerToken, req, cancellationToken);
                HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
            },
            ct);

    protected abstract Task ForwardAsync(
        string bearerToken,
        TRequest request,
        CancellationToken cancellationToken);
}

public abstract class AuthorizedProxyNoContentEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    string permissionCode) : EndpointWithoutRequest
{
    public override Task HandleAsync(CancellationToken ct) =>
        AuthorizedProxyEndpointExecutor.ExecuteAsync(
            HttpContext,
            iam,
            auth,
            permissionCode,
            async (context, cancellationToken) =>
            {
                await ForwardAsync(context.BearerToken, cancellationToken);
                HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
            },
            ct);

    protected abstract Task ForwardAsync(
        string bearerToken,
        CancellationToken cancellationToken);
}

internal static class AuthorizedProxyEndpointExecutor
{
    public static async Task ExecuteAsync(
        HttpContext context,
        IGatewayIamAuthClient iam,
        IGatewayAuthorizationClient auth,
        string permissionCode,
        Func<AuthorizedProxyRequestContext, CancellationToken, Task> forward,
        CancellationToken cancellationToken)
    {
        var authorized = await GatewayAuthorization.RequireCurrentPrincipalPermissionAsync(
            context,
            iam,
            auth,
            permissionCode,
            cancellationToken);
        if (authorized is null)
        {
            return;
        }

        try
        {
            await forward(
                new AuthorizedProxyRequestContext(
                    authorized.Value.BearerToken,
                    authorized.Value.Principal),
                cancellationToken);
        }
        catch (GatewayAuthException ex)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                context,
                (int)ex.StatusCode,
                ex.Reason,
                cancellationToken);
        }
    }
}
