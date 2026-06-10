using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Observability;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Application.Logs;
using Nerv.IIP.PlatformGateway.Web.Application.OpenApi;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.PlatformGateway.Web.Endpoints.Logs;

[HttpPost("/api/console/v1/logs/query")]
[GatewayOperationId("queryConsoleLogs")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class QueryConsoleLogsEndpoint(
    IVictoriaLogsClient logs,
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth)
    : Endpoint<ConsoleLogQueryRequest, ResponseData<ConsoleLogQueryResponse>>
{
    private static readonly TimeSpan MaxWindow = TimeSpan.FromHours(24);

    public override async Task HandleAsync(ConsoleLogQueryRequest req, CancellationToken ct)
    {
        var authorized = await GatewayAuthorization.RequireCurrentPrincipalPermissionAsync(
            HttpContext,
            iam,
            auth,
            GatewayPermissions.ObservabilityLogsRead,
            ct);
        if (authorized is null)
        {
            return;
        }

        if (req.From is null || req.To is null)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "from and to are required.",
                ct);
            return;
        }

        var from = req.From.Value.ToUniversalTime();
        var to = req.To.Value.ToUniversalTime();
        if (to <= from)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "to must be later than from.",
                ct);
            return;
        }

        if (to - from > MaxWindow)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "log query time window cannot exceed 24 hours.",
                ct);
            return;
        }

        var limit = Math.Clamp(req.Limit ?? 100, 1, 200);
        var offset = Math.Max(req.Cursor ?? 0, 0);
        try
        {
            var response = await logs.QueryAsync(
                new VictoriaLogsQueryRequest(
                    from,
                    to,
                    limit,
                    offset,
                    new VictoriaLogsQueryFilter(
                        req.Service,
                        req.CorrelationId,
                        req.TraceId,
                        req.Level,
                        req.Text)),
                ct);
            var data = new ConsoleLogQueryResponse(
                response.Items.Select(ConsoleLogEntryResponse.FromVictoriaLogs).ToArray(),
                response.NextOffset,
                response.Partial,
                response.BackendStatus);
            await Send.OkAsync(data.AsResponseData(), ct);
        }
        catch (HttpRequestException ex)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                HttpContext,
                StatusCodes.Status502BadGateway,
                $"VictoriaLogs unavailable: {ex.Message}",
                ct);
        }
    }
}
