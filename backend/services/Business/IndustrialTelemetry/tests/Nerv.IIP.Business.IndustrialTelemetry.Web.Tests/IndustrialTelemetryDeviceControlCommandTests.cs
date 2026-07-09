using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.DistributedLocks;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

public sealed class IndustrialTelemetryDeviceControlCommandTests
{
    [Fact]
    public async Task Device_control_command_rejects_out_of_range_write_before_creating_ops_task()
    {
        await using var factory = new DeviceControlHttpTestFactory();
        await factory.SeedWritableTagAsync("DEV-CNC-01", "spindle.speed", "number", minValue: 0m, maxValue: 100m);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        var response = await client.PostAsJsonAsync("/api/business/v1/iiot/device-control-commands", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            connectorHostId = "connector-host-001",
            instanceKey = "opcua-cell-01",
            deviceAssetId = "DEV-CNC-01",
            commandType = "write-tag",
            tagKey = "spindle.speed",
            value = "120",
            requestedBy = "user:operator-001",
            reason = "speed adjustment",
            idempotencyKey = "idem-device-control-out-of-range-001",
            correlationId = "corr-device-control-out-of-range-001",
        });

        Assert.Empty(factory.OpsClient.CreatedRequests);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Device_control_command_rejects_unsupported_command_type_before_creating_ops_task()
    {
        await using var factory = new DeviceControlHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        var response = await client.PostAsJsonAsync("/api/business/v1/iiot/device-control-commands", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            connectorHostId = "connector-host-001",
            instanceKey = "opcua-cell-01",
            deviceAssetId = "DEV-CNC-01",
            commandType = "calibrate",
            requestedBy = "user:operator-001",
            reason = "unsupported",
            idempotencyKey = "idem-device-control-unsupported-001",
            correlationId = "corr-device-control-unsupported-001",
        });

        Assert.Empty(factory.OpsClient.CreatedRequests);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Device_control_command_rejects_write_tag_without_tag_or_value_before_creating_ops_task()
    {
        await using var factory = new DeviceControlHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        var response = await client.PostAsJsonAsync("/api/business/v1/iiot/device-control-commands", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            connectorHostId = "connector-host-001",
            instanceKey = "opcua-cell-01",
            deviceAssetId = "DEV-CNC-01",
            commandType = "write-tag",
            requestedBy = "user:operator-001",
            reason = "missing tag and value",
            idempotencyKey = "idem-device-control-missing-tag-value-001",
            correlationId = "corr-device-control-missing-tag-value-001",
        });

        Assert.Empty(factory.OpsClient.CreatedRequests);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Device_control_command_creates_approval_gated_ops_task_with_auditable_parameters()
    {
        await using var factory = new DeviceControlHttpTestFactory();
        await factory.SeedWritableTagAsync("DEV-CNC-01", "spindle.speed", "number", minValue: 0m, maxValue: 100m);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        var response = await client.PostAsJsonAsync("/api/business/v1/iiot/device-control-commands", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            connectorHostId = "connector-host-001",
            instanceKey = "opcua-cell-01",
            deviceAssetId = "DEV-CNC-01",
            commandType = "write-tag",
            tagKey = "spindle.speed",
            value = "80",
            requestedBy = "user:operator-001",
            reason = "speed adjustment",
            idempotencyKey = "idem-device-control-valid-001",
            correlationId = "corr-device-control-valid-001",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = Assert.Single(factory.OpsClient.CreatedRequests);
        Assert.Equal("device.control.command", request.OperationCode);
        Assert.Equal("opcua-cell-01", request.InstanceKey);
        Assert.Equal("user:operator-001", request.RequestedBy);
        Assert.Equal("write-tag", request.Parameters["commandType"]);
        Assert.Equal("DEV-CNC-01", request.Parameters["deviceAssetId"]);
        Assert.Equal("spindle.speed", request.Parameters["tagKey"]);
        Assert.Equal("80", request.Parameters["value"]);
        Assert.Equal("connector-host-001", request.Parameters["connectorHostId"]);
    }

    [Fact]
    public async Task Device_control_command_is_recorded_in_ledger_and_result_reflects_live_ops_status()
    {
        await using var factory = new DeviceControlHttpTestFactory();
        await factory.SeedWritableTagAsync("DEV-CNC-01", "spindle.speed", "number", minValue: 0m, maxValue: 100m);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        var commandId = await DispatchWriteTagAsync(client, "DEV-CNC-01", "spindle.speed", "80", "idem-result-001", "corr-result-001");
        Assert.Equal("op-idem-result-001", commandId);

        // Simulate the connector host executing and completing the task in Ops.
        factory.OpsClient.SetTaskState(commandId, CompletedTask(commandId));

        using var response = await client.GetAsync($"/api/business/v1/iiot/device-control-commands/{Uri.EscapeDataString(commandId)}?organizationId=org-001&environmentId=env-dev");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(commandId, data.GetProperty("commandId").GetString());
        Assert.Equal("DEV-CNC-01", data.GetProperty("deviceAssetId").GetString());
        Assert.Equal("write-tag", data.GetProperty("commandType").GetString());
        Assert.Equal("spindle.speed", data.GetProperty("tagKey").GetString());
        Assert.Equal("80", data.GetProperty("value").GetString());
        Assert.Equal("user:operator-001", data.GetProperty("requestedBy").GetString());
        Assert.Equal("succeeded", data.GetProperty("status").GetString());
        Assert.True(data.GetProperty("statusFromLiveOps").GetBoolean());
        Assert.Equal("approved", data.GetProperty("approval").GetProperty("status").GetString());
        var attempt = Assert.Single(data.GetProperty("attempts").EnumerateArray());
        Assert.Equal("succeeded", attempt.GetProperty("status").GetString());
        Assert.Equal("ok", attempt.GetProperty("output").GetProperty("result").GetString());
    }

    [Fact]
    public async Task Device_control_command_result_falls_back_to_ledger_snapshot_when_ops_unavailable()
    {
        await using var factory = new DeviceControlHttpTestFactory();
        await factory.SeedWritableTagAsync("DEV-CNC-01", "spindle.speed", "number", minValue: 0m, maxValue: 100m);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        var commandId = await DispatchWriteTagAsync(client, "DEV-CNC-01", "spindle.speed", "60", "idem-fallback-001", "corr-fallback-001");
        factory.OpsClient.SetTaskState(commandId, CompletedTask(commandId));
        factory.OpsClient.FailGet = true;

        using var response = await client.GetAsync($"/api/business/v1/iiot/device-control-commands/{Uri.EscapeDataString(commandId)}?organizationId=org-001&environmentId=env-dev");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("approval-pending", data.GetProperty("status").GetString());
        Assert.False(data.GetProperty("statusFromLiveOps").GetBoolean());
        Assert.Empty(data.GetProperty("attempts").EnumerateArray());
        Assert.Equal("pending", data.GetProperty("approval").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Device_control_command_history_lists_dispatched_commands_filtered_by_device()
    {
        await using var factory = new DeviceControlHttpTestFactory();
        await factory.SeedWritableTagAsync("DEV-CNC-01", "spindle.speed", "number", minValue: 0m, maxValue: 100m);
        await factory.SeedWritableTagAsync("DEV-CNC-02", "feed.rate", "number", minValue: 0m, maxValue: 500m);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        await DispatchWriteTagAsync(client, "DEV-CNC-01", "spindle.speed", "50", "idem-history-001", "corr-history-001");
        await DispatchWriteTagAsync(client, "DEV-CNC-01", "spindle.speed", "60", "idem-history-002", "corr-history-002");
        await DispatchWriteTagAsync(client, "DEV-CNC-02", "feed.rate", "120", "idem-history-003", "corr-history-003");

        using var deviceScoped = await client.GetAsync("/api/business/v1/iiot/device-control-commands?organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-CNC-01");
        using var allScoped = await client.GetAsync("/api/business/v1/iiot/device-control-commands?organizationId=org-001&environmentId=env-dev");

        var deviceBody = await deviceScoped.Content.ReadAsStringAsync();
        Assert.True(deviceScoped.IsSuccessStatusCode, $"Expected history to succeed, got {(int)deviceScoped.StatusCode}: {deviceBody}");
        using var deviceDocument = JsonDocument.Parse(deviceBody);
        var deviceData = deviceDocument.RootElement.GetProperty("data");
        Assert.Equal(2, deviceData.GetProperty("total").GetInt32());
        Assert.All(deviceData.GetProperty("items").EnumerateArray(), x => Assert.Equal("DEV-CNC-01", x.GetProperty("deviceAssetId").GetString()));
        Assert.All(deviceData.GetProperty("items").EnumerateArray(), x => Assert.Equal("write-tag", x.GetProperty("commandType").GetString()));

        using var allDocument = JsonDocument.Parse(await allScoped.Content.ReadAsStringAsync());
        Assert.Equal(3, allDocument.RootElement.GetProperty("data").GetProperty("total").GetInt32());
    }

    private static async Task<string> DispatchWriteTagAsync(HttpClient client, string deviceAssetId, string tagKey, string value, string idempotencyKey, string correlationId)
    {
        using var response = await client.PostAsJsonAsync("/api/business/v1/iiot/device-control-commands", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            connectorHostId = "connector-host-001",
            instanceKey = "opcua-cell-01",
            deviceAssetId,
            commandType = "write-tag",
            tagKey,
            value,
            requestedBy = "user:operator-001",
            reason = "speed adjustment",
            idempotencyKey,
            correlationId,
        });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected dispatch to succeed, got {(int)response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").GetProperty("operationTaskId").GetString()
            ?? throw new InvalidOperationException("Dispatch response did not contain operationTaskId.");
    }

    private static OperationTaskResponse CompletedTask(string operationTaskId)
    {
        return new OperationTaskResponse(
            operationTaskId,
            "org-001",
            "env-dev",
            "opcua-cell-01",
            "device.control.command",
            "succeeded",
            "user:operator-001",
            DateTimeOffset.Parse("2026-07-07T00:00:00Z"),
            new OperationApprovalSummary("approved", "user:operator-001", DateTimeOffset.Parse("2026-07-07T00:00:00Z"), "user:supervisor-001", DateTimeOffset.Parse("2026-07-07T00:02:00Z"), "approved for maintenance"),
            "attempt-001",
            [
                new OperationAttemptSummary(
                    "attempt-001",
                    "succeeded",
                    DateTimeOffset.Parse("2026-07-07T00:03:00Z"),
                    DateTimeOffset.Parse("2026-07-07T00:05:00Z"),
                    null,
                    "lease-001",
                    DateTimeOffset.Parse("2026-07-07T00:03:00Z"),
                    DateTimeOffset.Parse("2026-07-07T00:08:00Z"),
                    1,
                    300,
                    3,
                    null,
                    new Dictionary<string, string> { ["result"] = "ok" }),
            ],
            []);
    }

    private sealed class DeviceControlHttpTestFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"industrial-telemetry-device-control-{Guid.NewGuid():N}";
        private readonly ServiceProvider efServices = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();

        public RecordingDeviceControlOpsClient OpsClient { get; } = new();

        public async Task SeedWritableTagAsync(string deviceAssetId, string tagKey, string valueType, decimal minValue, decimal maxValue)
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tag = TelemetryTag.Create("org-001", "env-dev", deviceAssetId, tagKey, valueType, "rpm", "sample-10s");
            tag.ConfigureControl(isWritable: true, minValue, maxValue, allowedValues: []);
            dbContext.TelemetryTags.Add(tag);
            await dbContext.SaveChangesAsync();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("environment", "Testing");
            builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ApplicationDbContext>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDistributedLock>();
                services.RemoveAll<IIntegrationEventPublisher>();
                services.RemoveAll<IDeviceControlOpsClient>();
                services.AddInMemoryDistributedLock();
                services.AddSingleton<IIntegrationEventPublisher, NoopIntegrationEventPublisher>();
                services.AddSingleton<IDeviceControlOpsClient>(OpsClient);
                services.AddDbContext<ApplicationDbContext>(options =>
                    options
                        .UseInMemoryDatabase(databaseName)
                        .UseInternalServiceProvider(efServices)
                        .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                efServices.Dispose();
            }
        }
    }

    private sealed class RecordingDeviceControlOpsClient : IDeviceControlOpsClient
    {
        private readonly List<CreateOperationTaskRequest> createdRequests = [];
        private readonly Dictionary<string, OperationTaskResponse> tasks = new(StringComparer.Ordinal);

        public IReadOnlyList<CreateOperationTaskRequest> CreatedRequests => createdRequests;

        // When set, GetDeviceControlTaskAsync throws like the Ops SDK does when Ops is unavailable.
        public bool FailGet { get; set; }

        public void SetTaskState(string operationTaskId, OperationTaskResponse response) => tasks[operationTaskId] = response;

        public Task<OperationTaskResponse> CreateDeviceControlTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken)
        {
            createdRequests.Add(request);
            var operationTaskId = $"op-{request.IdempotencyKey}";
            var response = new OperationTaskResponse(
                operationTaskId,
                request.OrganizationId,
                request.EnvironmentId,
                request.InstanceKey,
                request.OperationCode,
                "approval-pending",
                request.RequestedBy,
                DateTimeOffset.Parse("2026-07-07T00:00:00Z"),
                new OperationApprovalSummary("pending", request.RequestedBy, DateTimeOffset.Parse("2026-07-07T00:00:00Z"), null, null, null),
                null,
                [],
                []);
            tasks[operationTaskId] = response;
            return Task.FromResult(response);
        }

        public Task<OperationTaskResponse> GetDeviceControlTaskAsync(string operationTaskId, CancellationToken cancellationToken)
        {
            if (FailGet || !tasks.TryGetValue(operationTaskId, out var response))
            {
                throw new HttpRequestException($"Operation task unavailable: {operationTaskId}");
            }

            return Task.FromResult(response);
        }
    }

    private sealed class NoopIntegrationEventPublisher : IIntegrationEventPublisher
    {
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
