using System.Security.Cryptography;
using System.Text;
using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.AppHub.Domain;
using Nerv.IIP.AppHub.Web.Application.Commands;
using Nerv.IIP.Contracts.ConnectorProtocol;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.AppHub.Web.Endpoints.Connectors;

[HttpPost("/api/connectors/v1/registrations")]
[AllowAnonymous]
public sealed class RegisterApplicationEndpoint(IMediator mediator) : Endpoint<ApplicationRegistration, ResponseData<RegistrationResult>>
{
    public override async Task HandleAsync(ApplicationRegistration req, CancellationToken ct)
    {
        if (!ConnectorEndpointResults.ConnectorHostAuthorized(HttpContext, req.Context.ConnectorHostId))
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
        if (!ConnectorEndpointResults.ConnectorHostAuthorized(HttpContext, req.Context.ConnectorHostId))
        {
            await ConnectorEndpointResults.WriteUnauthorizedAsync(HttpContext, ct);
            return;
        }

        await mediator.Send(new RecordApplicationHeartbeatCommand(req), ct);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}

[HttpPost("/api/connectors/v1/state-snapshots")]
[AllowAnonymous]
public sealed class RecordStateSnapshotEndpoint(IMediator mediator) : Endpoint<InstanceStateSnapshot>
{
    public override async Task HandleAsync(InstanceStateSnapshot req, CancellationToken ct)
    {
        if (!ConnectorEndpointResults.ConnectorHostAuthorized(HttpContext, req.Context.ConnectorHostId))
        {
            await ConnectorEndpointResults.WriteUnauthorizedAsync(HttpContext, ct);
            return;
        }

        await mediator.Send(new RecordInstanceStateSnapshotCommand(req), ct);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}

internal static class ConnectorEndpointResults
{
    private const string DevelopmentConnectorSecret = "local-connector-secret";

    public static bool ConnectorHostAuthorized(HttpContext context, string connectorHostId)
    {
        if (!context.Request.Headers.TryGetValue("X-Connector-Host-Id", out var hostId)
            || !context.Request.Headers.TryGetValue("X-Connector-Secret", out var secret)
            || !string.Equals(hostId.ToString(), connectorHostId, StringComparison.Ordinal))
        {
            return false;
        }

        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
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
}
