using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.AppHub.Web.Application.Connectors;

namespace Nerv.IIP.AppHub.Web.Tests;

public sealed partial class AppHubConnectorEndpointTests
{
    // #4169 PublicContract: v1/Web JSON + HMAC-SHA256 向量由 Python json/hmac/base64 独立推导。
    // Web JSON 的 Unicode 使用大写 \uXXXX 转义；三组 payload 字节长度模 3 覆盖 0/1/2。
    [Theory]
    [InlineData("旧实例", "v1.eyJyZWdpc3RyYXRpb25JZCI6InJlZy0wMDEiLCJvcmdhbml6YXRpb25JZCI6Im9yZy0wMDEiLCJlbnZpcm9ubWVudElkIjoiZW52LWRldiIsImNvbm5lY3Rvckhvc3RJZCI6ImNvbm5lY3Rvci1ob3N0LTAwMSIsImluc3RhbmNlS2V5IjoiXHU2NUU3XHU1QjlFXHU0RjhCIiwiaXNzdWVkQXRVdGMiOiIyMDI2LTA1LTE1VDAwOjAwOjAwKzAwOjAwIiwiZXhwaXJlc0F0VXRjIjoiMjAyNi0wNS0xNVQwMDoxMDowMCswMDowMCJ9.jRRbg07eP7U5RMEln2iTF5wuSb7xiGX25ekZlevrFhg")]
    [InlineData("旧实例a", "v1.eyJyZWdpc3RyYXRpb25JZCI6InJlZy0wMDEiLCJvcmdhbml6YXRpb25JZCI6Im9yZy0wMDEiLCJlbnZpcm9ubWVudElkIjoiZW52LWRldiIsImNvbm5lY3Rvckhvc3RJZCI6ImNvbm5lY3Rvci1ob3N0LTAwMSIsImluc3RhbmNlS2V5IjoiXHU2NUU3XHU1QjlFXHU0RjhCYSIsImlzc3VlZEF0VXRjIjoiMjAyNi0wNS0xNVQwMDowMDowMCswMDowMCIsImV4cGlyZXNBdFV0YyI6IjIwMjYtMDUtMTVUMDA6MTA6MDArMDA6MDAifQ.23UDVtUtTp4HZ6I2EHecPvzO8D_ppOo3FPblH_zWSWQ")]
    [InlineData("旧实例ab", "v1.eyJyZWdpc3RyYXRpb25JZCI6InJlZy0wMDEiLCJvcmdhbml6YXRpb25JZCI6Im9yZy0wMDEiLCJlbnZpcm9ubWVudElkIjoiZW52LWRldiIsImNvbm5lY3Rvckhvc3RJZCI6ImNvbm5lY3Rvci1ob3N0LTAwMSIsImluc3RhbmNlS2V5IjoiXHU2NUU3XHU1QjlFXHU0RjhCYWIiLCJpc3N1ZWRBdFV0YyI6IjIwMjYtMDUtMTVUMDA6MDA6MDArMDA6MDAiLCJleHBpcmVzQXRVdGMiOiIyMDI2LTA1LTE1VDAwOjEwOjAwKzAwOjAwIn0.7fTJIjRMmggUbReKKF7V70AykMHWlJ8So7Ns6OMvwHE")]
    public async Task Connector_v1_token_matches_independent_vector_and_accepts_legacy_token(string instanceKey, string legacyToken)
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-05-15T00:00:00Z"));
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectorIngestionToken:SigningKey", "test-connector-ingestion-signing-key-4169");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            });
        });
        using var client = host.CreateClient();
        var scenario = CreateScenario("legacy-token") with { InstanceKey = instanceKey };
        var service = host.Services.GetRequiredService<IConnectorIngestionTokenService>();

        Assert.Equal(legacyToken, service.CreateToken(CreateRegistration(scenario), "reg-001"));
        Assert.True(service.TryValidateToken(legacyToken, out var identity));
        Assert.Equal(instanceKey, identity.InstanceKey);
        await RegisterAndReadIngestionTokenAsync(client, scenario);

        using var heartbeat = await PostIngestionAsync(client, "/api/connectors/v1/heartbeats", CreateHeartbeat(scenario), legacyToken);
        using var snapshot = await PostIngestionAsync(client, "/api/connectors/v1/state-snapshots", CreateSnapshot(scenario), legacyToken);
        Assert.Equal(HttpStatusCode.NoContent, heartbeat.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, snapshot.StatusCode);

        foreach (var forged in new[]
        {
            scenario with { OrganizationId = "wrong-organization" },
            scenario with { EnvironmentId = "wrong-environment" },
            scenario with { ConnectorHostId = "wrong-host" },
            scenario with { InstanceKey = "wrong-instance" }
        })
        {
            using var rejectedHeartbeat = await PostIngestionAsync(client, "/api/connectors/v1/heartbeats", CreateHeartbeat(forged), legacyToken);
            using var rejectedSnapshot = await PostIngestionAsync(client, "/api/connectors/v1/state-snapshots", CreateSnapshot(forged), legacyToken);
            Assert.Equal(HttpStatusCode.Unauthorized, rejectedHeartbeat.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, rejectedSnapshot.StatusCode);
        }

        var signatureStart = legacyToken.LastIndexOf('.') + 1;
        var tamperedToken = legacyToken[..signatureStart] + (legacyToken[signatureStart] == 'A' ? 'B' : 'A') + legacyToken[(signatureStart + 1)..];
        using var rejected = await PostIngestionAsync(client, "/api/connectors/v1/heartbeats", CreateHeartbeat(scenario), tamperedToken);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.False(service.TryValidateToken("v1.bm90LWpzb24.ev5Tgf6hoDPrLIciKL__wJPFDW2pin7iHYEFwYger_g", out _));
    }
}
