using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Observability;
using Nerv.IIP.PlatformGateway.Web;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Application.Logs;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayConsoleLogsTests
{
    [Fact]
    public async Task Console_logs_query_requires_permission_validates_window_and_maps_to_victorialogs_client()
    {
        var auth = FakeGatewayAuthorizationClient.Allowed();
        var logs = new FakeVictoriaLogsClient();
        await using var factory = CreateFactory(auth, logs);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", GatewayTestTokens.ValidAccessToken());

        var response = await client.PostAsJsonAsync("/api/console/v1/logs/query", new
        {
            from = "2026-06-10T01:00:00Z",
            to = "2026-06-10T02:00:00Z",
            service = "platform-gateway",
            correlationId = "corr-001",
            traceId = "trace-001",
            level = "Error",
            text = "timeout",
            limit = 20
        });

        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<ConsoleLogQueryResponse>>();
        Assert.NotNull(envelope);
        Assert.True(envelope.Success, envelope.Message);
        Assert.NotNull(envelope.Data);
        Assert.Equal("observability.logs.read", auth.LastRequirement!.PermissionCode);
        Assert.Equal("org-001", auth.LastRequirement.OrganizationId);
        Assert.Equal("env-dev", auth.LastRequirement.EnvironmentId);
        Assert.Equal("platform-gateway", logs.LastRequest!.Filter.Service);
        Assert.Equal("corr-001", logs.LastRequest.Filter.CorrelationId);
        Assert.Equal("trace-001", logs.LastRequest.Filter.TraceId);
        Assert.Equal("Error", logs.LastRequest.Filter.Level);
        Assert.Equal("timeout", logs.LastRequest.Filter.Text);
        Assert.Equal(20, logs.LastRequest.Limit);
        Assert.Single(envelope.Data.Items);
        Assert.Equal("victoriaLogs", envelope.Data.BackendStatus);
    }

    [Fact]
    public async Task Console_logs_query_rejects_oversized_time_windows_before_backend_query()
    {
        var auth = FakeGatewayAuthorizationClient.Allowed();
        var logs = new FakeVictoriaLogsClient();
        await using var factory = CreateFactory(auth, logs);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", GatewayTestTokens.ValidAccessToken());

        var response = await client.PostAsJsonAsync("/api/console/v1/logs/query", new
        {
            from = "2026-06-08T01:00:00Z",
            to = "2026-06-10T02:00:00Z",
            service = "platform-gateway"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(logs.LastRequest);
    }

    [Fact]
    public async Task Console_logs_query_returns_not_implemented_when_victorialogs_is_disabled()
    {
        var auth = FakeGatewayAuthorizationClient.Allowed();
        var logs = new FakeVictoriaLogsClient();
        await using var factory = CreateFactory(auth, logs, victoriaLogsEnabled: false);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", GatewayTestTokens.ValidAccessToken());

        var response = await client.PostAsJsonAsync("/api/console/v1/logs/query", new
        {
            from = "2026-06-10T01:00:00Z",
            to = "2026-06-10T02:00:00Z"
        });

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Null(logs.LastRequest);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        FakeGatewayAuthorizationClient auth,
        FakeVictoriaLogsClient logs,
        bool victoriaLogsEnabled = true) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IGatewayAuthorizationClient>();
            services.AddSingleton<IGatewayAuthorizationClient>(auth);
            services.RemoveAll<IVictoriaLogsClient>();
            services.AddSingleton<IVictoriaLogsClient>(logs);
            services.RemoveAll<VictoriaLogsOptions>();
            services.AddSingleton(new VictoriaLogsOptions(
                victoriaLogsEnabled,
                new Uri("http://victoria-logs:9428"),
                "30d",
                "/victoria-logs-data",
                new Dictionary<string, string>()));
        }));

    private sealed class FakeVictoriaLogsClient : IVictoriaLogsClient
    {
        public VictoriaLogsQueryRequest? LastRequest { get; private set; }

        public Task<VictoriaLogsQueryResponse> QueryAsync(VictoriaLogsQueryRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new VictoriaLogsQueryResponse(
                [
                    new VictoriaLogsLogEntry(
                        DateTimeOffset.Parse("2026-06-10T01:10:00Z"),
                        "Error",
                        "platform-gateway",
                        "timeout",
                        null,
                        null,
                        "corr-001",
                        "trace-001",
                        "victoriaLogs",
                        new Dictionary<string, string>(),
                        new Dictionary<string, string>())
                ],
                20,
                false,
                "victoriaLogs"));
        }
    }

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
