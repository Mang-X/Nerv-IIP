using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
        public IReadOnlyList<CreateOperationTaskRequest> CreatedRequests => createdRequests;

        public Task<OperationTaskResponse> CreateDeviceControlTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken)
        {
            createdRequests.Add(request);
            return Task.FromResult(new OperationTaskResponse(
                "op-device-control-001",
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
                []));
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
