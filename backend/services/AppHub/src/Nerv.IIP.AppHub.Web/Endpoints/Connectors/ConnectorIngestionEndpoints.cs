using System.Security.Cryptography;
using System.Text;
using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.AppHub.Web.Application.Connectors;
using Nerv.IIP.AppHub.Web.Application.Commands;
using Nerv.IIP.Contracts.ConnectorProtocol;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.AppHub.Web.Endpoints.Connectors;

[HttpPost("/api/connectors/v1/registrations")]
[AllowAnonymous]
public sealed class RegisterApplicationEndpoint(IMediator mediator) : Endpoint<ApplicationRegistration, ResponseData<ApplicationRegistrationResult>>
{
    public override async Task HandleAsync(ApplicationRegistration req, CancellationToken ct)
    {
        if (!ConnectorEndpointResults.ConnectorHostRegistrationAuthorized(HttpContext, req.Context))
        {
            await ConnectorEndpointResults.WriteUnauthorizedAsync(HttpContext, ct);
            return;
        }

        var result = await mediator.Send(new RegisterApplicationCommand(req), ct);
        await Send.OkAsync(result.AsResponseData(), ct);
    }
}

[HttpPost("/api/connectors/v1/heartbeats")]
[AllowAnonymous]
public sealed class RecordHeartbeatEndpoint(IMediator mediator) : Endpoint<ApplicationHeartbeat>
{
    public override async Task HandleAsync(ApplicationHeartbeat req, CancellationToken ct)
    {
        if (!ConnectorEndpointResults.ConnectorIngestionAuthorized(HttpContext, req.Context, req.InstanceKey, out var identity))
        {
            await ConnectorEndpointResults.WriteUnauthorizedAsync(HttpContext, ct);
            return;
        }

        var trusted = req with { Context = identity.Bind(req.Context), InstanceKey = identity.InstanceKey };
        await mediator.Send(new RecordApplicationHeartbeatCommand(trusted), ct);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}

[HttpPost("/api/connectors/v1/state-snapshots")]
[AllowAnonymous]
public sealed class RecordStateSnapshotEndpoint(IMediator mediator) : Endpoint<InstanceStateSnapshot>
{
    public override async Task HandleAsync(InstanceStateSnapshot req, CancellationToken ct)
    {
        if (!ConnectorEndpointResults.ConnectorIngestionAuthorized(HttpContext, req.Context, req.InstanceKey, out var identity))
        {
            await ConnectorEndpointResults.WriteUnauthorizedAsync(HttpContext, ct);
            return;
        }

        var trusted = req with { Context = identity.Bind(req.Context), InstanceKey = identity.InstanceKey };
        await mediator.Send(new RecordInstanceStateSnapshotCommand(trusted), ct);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}

internal static class ConnectorEndpointResults
{
    private const string DevelopmentConnectorSecret = "local-connector-secret";

    public static bool ConnectorHostRegistrationAuthorized(HttpContext context, ConnectorRequestContext requestContext)
    {
        if (!context.Request.Headers.TryGetValue("X-Connector-Host-Id", out var hostId)
            || !context.Request.Headers.TryGetValue("X-Connector-Secret", out var secret)
            || !context.Request.Headers.TryGetValue("X-Organization-Id", out var organizationId)
            || !context.Request.Headers.TryGetValue("X-Environment-Id", out var environmentId)
            || !string.Equals(hostId.ToString(), requestContext.ConnectorHostId, StringComparison.Ordinal)
            || !string.Equals(organizationId.ToString(), requestContext.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(environmentId.ToString(), requestContext.EnvironmentId, StringComparison.Ordinal))
        {
            return false;
        }

        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
        if (!ConfiguredValueMatches(configuration["ConnectorHostCredential:ConnectorHostId"], requestContext.ConnectorHostId)
            || !ConfiguredValueMatches(configuration["ConnectorHostCredential:OrganizationId"], requestContext.OrganizationId)
            || !ConfiguredValueMatches(configuration["ConnectorHostCredential:EnvironmentId"], requestContext.EnvironmentId))
        {
            return false;
        }

        var environment = context.RequestServices.GetRequiredService<IHostEnvironment>();
        var expectedSecret = configuration["ConnectorHostCredential:Secret"];
        if (string.IsNullOrWhiteSpace(expectedSecret))
        {
            if (!environment.IsDevelopment())
            {
                return false;
            }

            expectedSecret = DevelopmentConnectorSecret;
        }

        return SecretEquals(secret.ToString(), expectedSecret);
    }

    public static bool ConnectorIngestionAuthorized(
        HttpContext context,
        ConnectorRequestContext requestContext,
        string instanceKey,
        out ConnectorIngestionIdentity identity)
    {
        identity = null!;
        if (!context.Request.Headers.TryGetValue("X-Connector-Ingestion-Token", out var token))
        {
            return false;
        }

        var tokenService = context.RequestServices.GetRequiredService<IConnectorIngestionTokenService>();
        return tokenService.TryValidateToken(token.ToString(), out identity)
            && identity.Matches(requestContext, instanceKey);
    }

    public static async Task WriteUnauthorizedAsync(HttpContext context, CancellationToken cancellationToken)
    {
        await ResponseDataEndpointResults.WriteErrorAsync(
            context,
            StatusCodes.Status401Unauthorized,
            "Invalid Connector Host credential.",
            cancellationToken);
    }

    private static bool SecretEquals(string actual, string expected)
    {
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return actualBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }

    private static bool ConfiguredValueMatches(string? expected, string actual) =>
        string.IsNullOrWhiteSpace(expected)
        || string.Equals(expected, actual, StringComparison.Ordinal);
}
