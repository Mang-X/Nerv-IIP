using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Nerv.IIP.Contracts.AppHubQueries;
using Nerv.IIP.Contracts.ConnectorProtocol;

namespace Nerv.IIP.AppHub.Web.Tests;

public sealed class AppHubConnectorEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string InternalServiceBearerToken = "local-internal-service-token";

    [Fact]
    public async Task Instance_query_endpoints_require_internal_service_authorization()
    {
        var client = factory.CreateClient();
        var query = new InstanceListQuery("org-unauthorized", "env-dev", 1, 20, "instanceName", "asc", null);

        using var list = await client.PostAsJsonAsync("/internal/apphub/v1/instances/query", query);
        using var detail = await client.GetAsync("/internal/apphub/v1/instances/instance-missing?organizationId=org-unauthorized&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, detail.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_remains_anonymous()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Connector_ingestion_requires_local_connector_credential()
    {
        var client = factory.CreateClient();
        var scenario = CreateScenario("missing-auth");
        var registration = CreateRegistration(scenario);

        using var response = await client.PostAsJsonAsync("/api/connectors/v1/registrations", registration);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Connector_registration_heartbeat_and_state_are_queryable()
    {
        var scenario = CreateScenario("positive-flow");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Connector-Host-Id", scenario.ConnectorHostId);
        client.DefaultRequestHeaders.Add("X-Connector-Secret", "local-connector-secret");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "corr-apphub-web-test");

        using var registration = await client.PostAsJsonAsync("/api/connectors/v1/registrations", CreateRegistration(scenario));
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);

        using var heartbeat = await client.PostAsJsonAsync("/api/connectors/v1/heartbeats", CreateHeartbeat(scenario));
        Assert.Equal(HttpStatusCode.NoContent, heartbeat.StatusCode);

        using var snapshot = await client.PostAsJsonAsync("/api/connectors/v1/state-snapshots", CreateSnapshot(scenario));
        Assert.Equal(HttpStatusCode.NoContent, snapshot.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", InternalServiceBearerToken);
        var query = new InstanceListQuery(scenario.OrganizationId, scenario.EnvironmentId, 1, 20, "instanceName", "asc", null);
        using var list = await client.PostAsJsonAsync("/internal/apphub/v1/instances/query", query);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listBody = await ReadResponseDataAsync<InstanceListResponse>(list);

        using var detailResponse = await client.GetAsync($"/internal/apphub/v1/instances/{scenario.InstanceKey}?organizationId={scenario.OrganizationId}&environmentId={scenario.EnvironmentId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await ReadResponseDataAsync<InstanceDetailResponse>(detailResponse);

        Assert.NotNull(listBody);
        Assert.Equal(1, listBody.PageIndex);
        Assert.Equal(20, listBody.PageSize);
        Assert.Equal(1, listBody.TotalCount);
        var item = Assert.Single(listBody.Items);
        Assert.Equal("demo-api", item.ApplicationKey);
        Assert.Equal("Demo API", item.ApplicationName);
        Assert.Equal("1.0.0", item.Version);
        Assert.Equal("node-001", item.NodeKey);
        Assert.Equal("local-docker", item.NodeName);
        Assert.Equal(scenario.InstanceKey, item.InstanceKey);
        Assert.Equal("demo-api", item.InstanceName);
        Assert.Equal("running", item.ReportedStatus);
        Assert.Equal("healthy", item.HealthStatus);
        Assert.Equal(DateTimeOffset.Parse("2026-05-15T00:00:05Z"), item.LastHeartbeatAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-05-15T00:00:10Z"), item.LastStateObservedAtUtc);
        Assert.NotNull(detail);
        Assert.Equal(scenario.InstanceKey, detail.InstanceKey);
        Assert.Equal("running", detail.ReportedStatus);
        Assert.Equal("healthy", detail.HealthStatus);
    }

    [Fact]
    public async Task Instance_list_query_returns_effective_page_metadata()
    {
        var scenario = CreateScenario("effective-page");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Connector-Host-Id", scenario.ConnectorHostId);
        client.DefaultRequestHeaders.Add("X-Connector-Secret", "local-connector-secret");

        using var registration = await client.PostAsJsonAsync("/api/connectors/v1/registrations", CreateRegistration(scenario));
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", InternalServiceBearerToken);
        var query = new InstanceListQuery(scenario.OrganizationId, scenario.EnvironmentId, 0, 0, "instanceName", "asc", null);
        using var list = await client.PostAsJsonAsync("/internal/apphub/v1/instances/query", query);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listBody = await ReadResponseDataAsync<InstanceListResponse>(list);

        Assert.NotNull(listBody);
        Assert.Equal(1, listBody.PageIndex);
        Assert.Equal(1, listBody.PageSize);
        Assert.Equal(1, listBody.TotalCount);
        Assert.Single(listBody.Items);
    }

    [Fact]
    public async Task Connector_ingestion_uses_configured_connector_secret()
    {
        var scenario = CreateScenario("configured-secret");
        var client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectorHostCredential:Secret", "configured-connector-secret");
        }).CreateClient();
        client.DefaultRequestHeaders.Add("X-Connector-Host-Id", scenario.ConnectorHostId);
        client.DefaultRequestHeaders.Add("X-Connector-Secret", "configured-connector-secret");

        using var response = await client.PostAsJsonAsync("/api/connectors/v1/registrations", CreateRegistration(scenario));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Connector_ingestion_rejects_repo_default_secret_when_configuration_overrides_it()
    {
        var scenario = CreateScenario("reject-default-secret");
        var client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectorHostCredential:Secret", "configured-connector-secret");
        }).CreateClient();
        client.DefaultRequestHeaders.Add("X-Connector-Host-Id", scenario.ConnectorHostId);
        client.DefaultRequestHeaders.Add("X-Connector-Secret", "local-connector-secret");

        using var response = await client.PostAsJsonAsync("/api/connectors/v1/registrations", CreateRegistration(scenario));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static TestScenario CreateScenario(string label)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new TestScenario(
            $"org-{label}-{suffix}",
            $"env-{label}-{suffix}",
            $"connector-host-{label}-{suffix}",
            $"demo-api-{label}-{suffix}",
            $"web-test-{label}-{suffix}");
    }

    private static ConnectorRequestContext Context(TestScenario scenario) => new("1.0", "1.0", "corr-apphub-web-test", DateTimeOffset.Parse("2026-05-15T00:00:00Z"), scenario.OrganizationId, scenario.EnvironmentId, scenario.ConnectorHostId);

    private static ApplicationRegistration CreateRegistration(TestScenario scenario) =>
        new(
            Context(scenario),
            scenario.IdempotencyKey,
            "node-001",
            "local-docker",
            "docker",
            "demo-api",
            "Demo API",
            "1.0.0",
            scenario.InstanceKey,
            "demo-api",
            [new CapabilityDescriptor("lifecycle.restart", "1.0", "lifecycle", ["restart"], new Dictionary<string, string>())],
            new Dictionary<string, string> { ["containerId"] = "local-demo-001" });

    private static ApplicationHeartbeat CreateHeartbeat(TestScenario scenario) =>
        new(Context(scenario), scenario.InstanceKey, DateTimeOffset.Parse("2026-05-15T00:00:05Z"), true, DateTimeOffset.Parse("2026-05-15T00:00:00Z"), 7, new Dictionary<string, string>());

    private static InstanceStateSnapshot CreateSnapshot(TestScenario scenario) =>
        new(Context(scenario), scenario.InstanceKey, DateTimeOffset.Parse("2026-05-15T00:00:10Z"), "running", "healthy", "demo-api is running", new Dictionary<string, string>(), new Dictionary<string, decimal>(), new Dictionary<string, string> { ["containerId"] = "local-demo-001" });

    private sealed record TestScenario(string OrganizationId, string EnvironmentId, string ConnectorHostId, string InstanceKey, string IdempotencyKey);
    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);

    private static async Task<T> ReadResponseDataAsync<T>(HttpResponseMessage response)
    {
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>();
        Assert.NotNull(envelope);
        Assert.True(envelope.Success, envelope.Message);
        Assert.NotNull(envelope.Data);
        return envelope.Data;
    }
}
