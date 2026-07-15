using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.Contracts.AppHubQueries;
using Nerv.IIP.Contracts.ConnectorProtocol;

namespace Nerv.IIP.AppHub.Web.Tests;

public sealed class AppHubConnectorEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string InternalServiceBearerToken = "local-internal-service-token";

    [Fact]
    public async Task Authorized_collection_health_query_returns_unknown_for_missing_connector()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", InternalServiceBearerToken);

        using var response = await client.GetAsync("/internal/apphub/v1/connectors/missing/collection-health?organizationId=org&environmentId=env");
        var body = await ReadResponseDataAsync<ConnectorCollectionHealthResponse>(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("unknown", body.Status);
        Assert.Null(body.LastHeartbeatAtUtc);
        Assert.Null(body.ReceivedCount);
    }

    [Fact]
    public async Task Authorized_collection_health_query_marks_old_heartbeat_stale_and_returns_persisted_metrics()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-13T01:10:00Z"));
        var databaseName = $"health-endpoint-{Guid.CreateVersion7():N}";
        var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ApplicationDbContext>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var instance = new ApplicationInstance("org", "env", "host", "collector", "1", "node", "opcua-main", "OPC", new Dictionary<string, string>(), []);
        instance.RecordHeartbeat(DateTimeOffset.Parse("2026-07-13T01:00:00Z"), true, 3);
        instance.RecordCollectionHealth(new ConnectorCollectionHealth("opcua-main", "opcua", Guid.Parse("11111111-1111-1111-1111-111111111111"), DateTimeOffset.Parse("2026-07-13T01:01:00Z"), 12, 2, 1, DateTimeOffset.Parse("2026-07-13T01:00:59Z")));
        db.ApplicationInstances.Add(instance);
        await db.SaveChangesAsync();

        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", InternalServiceBearerToken);
        using var response = await client.GetAsync("/internal/apphub/v1/connectors/opcua-main/collection-health?organizationId=org&environmentId=env");
        var body = await ReadResponseDataAsync<ConnectorCollectionHealthResponse>(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("stale", body.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-07-13T01:00:00Z"), body.LastHeartbeatAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-07-13T01:01:00Z"), body.MetricsReportedAtUtc);
        Assert.Equal(12, body.ReceivedCount);
        Assert.Equal(2, body.DroppedCount);
        Assert.Equal(1, body.ErrorCount);
        Assert.Equal("opcua", body.SourceSystem);
    }

    [Fact]
    public async Task Authorized_collection_health_query_marks_old_metrics_stale_while_heartbeat_remains_current()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-13T01:10:00Z"));
        var databaseName = $"health-endpoint-{Guid.CreateVersion7():N}";
        var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ApplicationDbContext>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var instance = new ApplicationInstance("org", "env", "host", "collector", "1", "node", "opcua-main", "OPC", new Dictionary<string, string>(), []);
        instance.RecordHeartbeat(DateTimeOffset.Parse("2026-07-13T01:09:00Z"), true, 3);
        instance.RecordCollectionHealth(new ConnectorCollectionHealth("opcua-main", "opcua", Guid.Parse("11111111-1111-1111-1111-111111111111"), DateTimeOffset.Parse("2026-07-13T01:01:00Z"), 12, 2, 1, DateTimeOffset.Parse("2026-07-13T01:00:59Z")));
        db.ApplicationInstances.Add(instance);
        await db.SaveChangesAsync();

        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", InternalServiceBearerToken);
        using var response = await client.GetAsync("/internal/apphub/v1/connectors/opcua-main/collection-health?organizationId=org&environmentId=env");
        var body = await ReadResponseDataAsync<ConnectorCollectionHealthResponse>(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("stale", body.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-07-13T01:09:00Z"), body.LastHeartbeatAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-07-13T01:01:00Z"), body.MetricsReportedAtUtc);
    }

    [Fact]
    public async Task Authorized_collection_health_query_returns_unknown_when_heartbeat_exists_without_metrics()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-13T01:10:00Z"));
        var scenario = CreateScenario("heartbeat-without-metrics");
        var client = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        })).CreateClient();
        client.DefaultRequestHeaders.Add("X-Connector-Host-Id", scenario.ConnectorHostId);
        client.DefaultRequestHeaders.Add("X-Connector-Secret", "local-connector-secret");
        client.DefaultRequestHeaders.Add("X-Organization-Id", scenario.OrganizationId);
        client.DefaultRequestHeaders.Add("X-Environment-Id", scenario.EnvironmentId);

        using var registration = await client.PostAsJsonAsync("/api/connectors/v1/registrations", CreateRegistration(scenario));
        var ingestionToken = await ReadRegistrationIngestionTokenAsync(registration);
        client.DefaultRequestHeaders.Remove("X-Connector-Secret");
        client.DefaultRequestHeaders.Add("X-Connector-Ingestion-Token", ingestionToken);
        using var heartbeat = await client.PostAsJsonAsync("/api/connectors/v1/heartbeats", CreateHeartbeat(scenario));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", InternalServiceBearerToken);

        using var response = await client.GetAsync($"/internal/apphub/v1/connectors/{scenario.InstanceKey}/collection-health?organizationId={scenario.OrganizationId}&environmentId={scenario.EnvironmentId}");
        var body = await ReadResponseDataAsync<ConnectorCollectionHealthResponse>(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("unknown", body.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-05-15T00:00:05Z"), body.LastHeartbeatAtUtc);
        Assert.Null(body.MetricsReportedAtUtc);
    }

    [Fact]
    public async Task Authorized_collection_health_list_returns_reporting_connectors_with_stale_sorted_first()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-13T01:10:00Z"));
        var databaseName = $"health-list-{Guid.CreateVersion7():N}";
        var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ApplicationDbContext>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Fresh OPC UA collector -> current.
        var current = new ApplicationInstance("org", "env", "host", "collector", "1", "node", "opcua-main", "OPC UA Main", new Dictionary<string, string>(), []);
        current.RecordHeartbeat(DateTimeOffset.Parse("2026-07-13T01:09:30Z"), true, 3);
        current.RecordCollectionHealth(new ConnectorCollectionHealth("opcua-main", "opcua", Guid.Parse("11111111-1111-1111-1111-111111111111"), DateTimeOffset.Parse("2026-07-13T01:09:40Z"), 100, 4, 1, DateTimeOffset.Parse("2026-07-13T01:09:39Z")));

        // Disconnected Modbus collector (old heartbeat) -> stale, must sort first.
        var stale = new ApplicationInstance("org", "env", "host", "collector", "1", "node", "modbus-main", "Modbus Main", new Dictionary<string, string>(), []);
        stale.RecordHeartbeat(DateTimeOffset.Parse("2026-07-13T01:00:00Z"), true, 3);
        stale.RecordCollectionHealth(new ConnectorCollectionHealth("modbus-main", "modbus", Guid.Parse("22222222-2222-2222-2222-222222222222"), DateTimeOffset.Parse("2026-07-13T01:01:00Z"), 50, 9, 2, DateTimeOffset.Parse("2026-07-13T01:00:58Z")));

        // Registered but never reported collection health -> excluded from the wall.
        var withoutHealth = new ApplicationInstance("org", "env", "host", "collector", "1", "node", "docker-x", "Docker X", new Dictionary<string, string>(), []);
        withoutHealth.RecordHeartbeat(DateTimeOffset.Parse("2026-07-13T01:09:00Z"), true, 3);

        // Different environment must not leak into the org/env-scoped list.
        var otherEnv = new ApplicationInstance("org", "env-other", "host", "collector", "1", "node", "opcua-other", "OPC UA Other", new Dictionary<string, string>(), []);
        otherEnv.RecordHeartbeat(DateTimeOffset.Parse("2026-07-13T01:09:30Z"), true, 3);
        otherEnv.RecordCollectionHealth(new ConnectorCollectionHealth("opcua-other", "opcua", Guid.Parse("33333333-3333-3333-3333-333333333333"), DateTimeOffset.Parse("2026-07-13T01:09:40Z"), 7, 0, 0, DateTimeOffset.Parse("2026-07-13T01:09:39Z")));

        db.ApplicationInstances.AddRange(current, stale, withoutHealth, otherEnv);
        await db.SaveChangesAsync();

        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", InternalServiceBearerToken);
        using var response = await client.GetAsync("/internal/apphub/v1/connectors/collection-health?organizationId=org&environmentId=env");
        var body = await ReadResponseDataAsync<ConnectorCollectionHealthListResponse>(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, body.Total);
        Assert.Equal(2, body.Items.Count);

        var first = body.Items[0];
        Assert.Equal("modbus-main", first.ConnectorId);
        Assert.Equal("Modbus Main", first.ConnectorName);
        Assert.Equal("stale", first.Status);
        Assert.Equal("modbus", first.SourceSystem);
        Assert.Equal(50, first.ReceivedCount);
        Assert.Equal(9, first.DroppedCount);
        Assert.Equal(2, first.ErrorCount);
        Assert.Equal(DateTimeOffset.Parse("2026-07-13T01:00:00Z"), first.LastHeartbeatAtUtc);

        var second = body.Items[1];
        Assert.Equal("opcua-main", second.ConnectorId);
        Assert.Equal("current", second.Status);
        Assert.Equal("opcua", second.SourceSystem);
        Assert.Equal(100, second.ReceivedCount);
    }

    [Fact]
    public async Task Collection_health_list_requires_internal_service_authorization()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/internal/apphub/v1/connectors/collection-health?organizationId=org-unauthorized&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Instance_query_endpoints_require_internal_service_authorization()
    {
        var client = factory.CreateClient();
        var query = new InstanceListQuery("org-unauthorized", "env-dev", 1, 20, "instanceName", "asc", null);

        using var list = await client.PostAsJsonAsync("/internal/apphub/v1/instances/query", query);
        using var detail = await client.GetAsync("/internal/apphub/v1/instances/instance-missing?organizationId=org-unauthorized&environmentId=env-dev");
        using var collectionHealth = await client.GetAsync("/internal/apphub/v1/connectors/connector-missing/collection-health?organizationId=org-unauthorized&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, detail.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, collectionHealth.StatusCode);
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
        client.DefaultRequestHeaders.Add("X-Organization-Id", scenario.OrganizationId);
        client.DefaultRequestHeaders.Add("X-Environment-Id", scenario.EnvironmentId);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "corr-apphub-web-test");

        using var registration = await client.PostAsJsonAsync("/api/connectors/v1/registrations", CreateRegistration(scenario));
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        var ingestionToken = await ReadRegistrationIngestionTokenAsync(registration);

        client.DefaultRequestHeaders.Remove("X-Connector-Secret");
        client.DefaultRequestHeaders.Add("X-Connector-Ingestion-Token", ingestionToken);
        using var heartbeat = await client.PostAsJsonAsync("/api/connectors/v1/heartbeats", CreateHeartbeat(scenario));
        Assert.Equal(HttpStatusCode.NoContent, heartbeat.StatusCode);

        using var snapshot = await client.PostAsJsonAsync("/api/connectors/v1/state-snapshots", CreateSnapshot(scenario));
        Assert.Equal(HttpStatusCode.NoContent, snapshot.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", InternalServiceBearerToken);
        var query = new InstanceListQuery(scenario.OrganizationId, scenario.EnvironmentId, 1, 20, "instanceName", "asc", scenario.InstanceKey);
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
        Assert.Equal(scenario.InstanceKey, item.InstanceName);
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
        client.DefaultRequestHeaders.Add("X-Organization-Id", scenario.OrganizationId);
        client.DefaultRequestHeaders.Add("X-Environment-Id", scenario.EnvironmentId);

        using var registration = await client.PostAsJsonAsync("/api/connectors/v1/registrations", CreateRegistration(scenario));
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", InternalServiceBearerToken);
        var query = new InstanceListQuery(scenario.OrganizationId, scenario.EnvironmentId, 0, 0, "instanceName", "asc", scenario.InstanceKey);
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
    public async Task Connector_ingestion_token_is_bound_to_registered_instance_identity()
    {
        var scenarioA = CreateScenario("bound-token-a");
        var scenarioB = CreateScenario("bound-token-b");
        var client = factory.CreateClient();

        var tokenA = await RegisterAndReadIngestionTokenAsync(client, scenarioA);
        var tokenB = await RegisterAndReadIngestionTokenAsync(client, scenarioB);

        using var spoofedHeartbeat = await PostIngestionAsync(client, "/api/connectors/v1/heartbeats", CreateHeartbeat(scenarioB), tokenA);
        using var spoofedSnapshot = await PostIngestionAsync(client, "/api/connectors/v1/state-snapshots", CreateSnapshot(scenarioB), tokenA);

        Assert.Equal(HttpStatusCode.Unauthorized, spoofedHeartbeat.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, spoofedSnapshot.StatusCode);

        using var allowedHeartbeat = await PostIngestionAsync(client, "/api/connectors/v1/heartbeats", CreateHeartbeat(scenarioB), tokenB);
        using var allowedSnapshot = await PostIngestionAsync(client, "/api/connectors/v1/state-snapshots", CreateSnapshot(scenarioB), tokenB);

        Assert.Equal(HttpStatusCode.NoContent, allowedHeartbeat.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, allowedSnapshot.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", InternalServiceBearerToken);
        var query = new InstanceListQuery(scenarioB.OrganizationId, scenarioB.EnvironmentId, 1, 20, "instanceName", "asc", scenarioB.InstanceKey);
        using var list = await client.PostAsJsonAsync("/internal/apphub/v1/instances/query", query);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listBody = await ReadResponseDataAsync<InstanceListResponse>(list);

        var item = Assert.Single(listBody.Items);
        Assert.Equal(scenarioB.InstanceKey, item.InstanceKey);
        Assert.Equal(DateTimeOffset.Parse("2026-05-15T00:00:05Z"), item.LastHeartbeatAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-05-15T00:00:10Z"), item.LastStateObservedAtUtc);
        Assert.Equal("running", item.ReportedStatus);
        Assert.Equal("healthy", item.HealthStatus);
    }

    [Fact]
    public async Task Connector_ingestion_token_rejects_body_scope_mismatch()
    {
        var registered = CreateScenario("scope-token");
        var forged = registered with
        {
            OrganizationId = $"org-forged-{Guid.NewGuid():N}",
            EnvironmentId = $"env-forged-{Guid.NewGuid():N}"
        };
        var client = factory.CreateClient();
        var token = await RegisterAndReadIngestionTokenAsync(client, registered);

        using var response = await PostIngestionAsync(client, "/api/connectors/v1/heartbeats", CreateHeartbeat(forged), token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Connector_registration_rejects_scope_mismatch_between_credential_and_body()
    {
        var authorized = CreateScenario("registration-scope-authorized");
        var forged = authorized with
        {
            OrganizationId = $"org-forged-{Guid.NewGuid():N}",
            EnvironmentId = $"env-forged-{Guid.NewGuid():N}"
        };
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/connectors/v1/registrations")
        {
            Content = JsonContent.Create(CreateRegistration(forged))
        };
        request.Headers.Add("X-Connector-Host-Id", authorized.ConnectorHostId);
        request.Headers.Add("X-Connector-Secret", "local-connector-secret");
        request.Headers.Add("X-Organization-Id", authorized.OrganizationId);
        request.Headers.Add("X-Environment-Id", authorized.EnvironmentId);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Connector_registration_rejects_unconfigured_scope_even_when_headers_match_body()
    {
        var authorized = CreateScenario("registration-configured-scope");
        var forged = authorized with
        {
            OrganizationId = $"org-forged-{Guid.NewGuid():N}",
            EnvironmentId = $"env-forged-{Guid.NewGuid():N}"
        };
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/connectors/v1/registrations")
        {
            Content = JsonContent.Create(CreateRegistration(forged))
        };
        request.Headers.Add("X-Connector-Host-Id", forged.ConnectorHostId);
        request.Headers.Add("X-Connector-Secret", "local-connector-secret");
        request.Headers.Add("X-Organization-Id", forged.OrganizationId);
        request.Headers.Add("X-Environment-Id", forged.EnvironmentId);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Connector_ingestion_token_rejects_expired_token()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-05-15T00:00:00Z"));
        var scenario = CreateScenario("expired-token");
        var client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectorIngestionToken:LifetimeMinutes", "5");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            });
        }).CreateClient();

        var token = await RegisterAndReadIngestionTokenAsync(client, scenario);
        clock.Advance(TimeSpan.FromMinutes(6));

        using var response = await PostIngestionAsync(client, "/api/connectors/v1/heartbeats", CreateHeartbeat(scenario), token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
        client.DefaultRequestHeaders.Add("X-Organization-Id", scenario.OrganizationId);
        client.DefaultRequestHeaders.Add("X-Environment-Id", scenario.EnvironmentId);

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
        client.DefaultRequestHeaders.Add("X-Organization-Id", scenario.OrganizationId);
        client.DefaultRequestHeaders.Add("X-Environment-Id", scenario.EnvironmentId);

        using var response = await client.PostAsJsonAsync("/api/connectors/v1/registrations", CreateRegistration(scenario));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static TestScenario CreateScenario(string label)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new TestScenario(
            "org-001",
            "env-dev",
            "connector-host-001",
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
            scenario.InstanceKey,
            [new CapabilityDescriptor("lifecycle.restart", "1.0", "lifecycle", ["restart"], new Dictionary<string, string>())],
            new Dictionary<string, string> { ["containerId"] = "local-demo-001" });

    private static ApplicationHeartbeat CreateHeartbeat(TestScenario scenario) =>
        new(Context(scenario), scenario.InstanceKey, DateTimeOffset.Parse("2026-05-15T00:00:05Z"), true, DateTimeOffset.Parse("2026-05-15T00:00:00Z"), 7, new Dictionary<string, string>());

    private static InstanceStateSnapshot CreateSnapshot(TestScenario scenario) =>
        new(Context(scenario), scenario.InstanceKey, DateTimeOffset.Parse("2026-05-15T00:00:10Z"), "running", "healthy", "demo-api is running", new Dictionary<string, string>(), new Dictionary<string, decimal>(), new Dictionary<string, string> { ["containerId"] = "local-demo-001" });

    private sealed record TestScenario(string OrganizationId, string EnvironmentId, string ConnectorHostId, string InstanceKey, string IdempotencyKey);
    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta)
        {
            _utcNow = _utcNow.Add(delta);
        }
    }

    private static async Task<string> RegisterAndReadIngestionTokenAsync(HttpClient client, TestScenario scenario)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/connectors/v1/registrations")
        {
            Content = JsonContent.Create(CreateRegistration(scenario))
        };
        request.Headers.Add("X-Connector-Host-Id", scenario.ConnectorHostId);
        request.Headers.Add("X-Connector-Secret", "local-connector-secret");
        request.Headers.Add("X-Organization-Id", scenario.OrganizationId);
        request.Headers.Add("X-Environment-Id", scenario.EnvironmentId);

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadRegistrationIngestionTokenAsync(response);
    }

    private static async Task<HttpResponseMessage> PostIngestionAsync<T>(HttpClient client, string path, T payload, string ingestionToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-Connector-Ingestion-Token", ingestionToken);
        return await client.SendAsync(request);
    }

    private static async Task<string> ReadRegistrationIngestionTokenAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(body);
        var token = body["data"]?["ingestionToken"]?.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(token), "Registration response must include a per-instance ingestion token.");
        return token;
    }

    private static async Task<T> ReadResponseDataAsync<T>(HttpResponseMessage response)
    {
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>();
        Assert.NotNull(envelope);
        Assert.True(envelope.Success, envelope.Message);
        Assert.NotNull(envelope.Data);
        return envelope.Data;
    }
}
