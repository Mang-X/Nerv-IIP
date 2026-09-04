using System.Text.Json;
using DotNetCore.CAP;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Maintenance.Domain.DomainEvents;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventConverters;
using Nerv.IIP.DistributedLocking;
using NetCorePal.Context.CAP;
using NetCorePal.Extensions.Primitives;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

/// <summary>
/// #2968 验收条件「生产 DI/outbox 发现边界有可失败证据」：直接构建生产分支（<c>isTesting:false</c>）的注册，
/// 断言 v2 双发链每一环都被生产容器发现；删掉 <c>AddMaintenanceCapIntegrationEvents</c> 里任一注册或
/// <c>Program.cs</c> 里的 v2 锁注册，对应断言立即变红。
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MaintenanceAssetUnavailableV2WiringTests
{
    [Fact]
    public void Production_cap_registration_discovers_the_v2_dual_publisher_and_its_outbox_and_topic_dependencies()
    {
        using var provider = BuildProvider("Development", isTesting: false);

        var outbox = provider.GetRequiredService<IMaintenanceIntegrationEventOutboxPublisher>();
        Assert.IsType<CapMaintenanceIntegrationEventOutboxPublisher>(outbox);
        Assert.NotNull(provider.GetService<ICapPublisher>());
        Assert.Equal("Development", provider.GetRequiredService<MaintenanceAssetUnavailableTopicOptions>().DeploymentProfile);

        var handlers = provider.GetServices<INotificationHandler<AssetUnavailableByReasonCodeDomainEvent>>().ToArray();
        Assert.Single(handlers.OfType<AssetUnavailableV2IntegrationEventPublisher>());

        // v1 路径保留：自由文本领域事件仍由 netcorepal 转换器发布链承接，而不是被 v2 publisher 抢走或删除。
        Assert.NotEmpty(provider.GetServices<INotificationHandler<AssetUnavailableDomainEvent>>());
        Assert.Empty(provider.GetServices<INotificationHandler<AssetUnavailableDomainEvent>>().OfType<AssetUnavailableV2IntegrationEventPublisher>());
    }

    [Fact]
    public void Testing_cap_registration_keeps_the_existing_no_outbox_behaviour()
    {
        using var provider = BuildProvider("Testing", isTesting: true);

        Assert.Null(provider.GetService<IMaintenanceIntegrationEventOutboxPublisher>());
        Assert.Null(provider.GetService<ICapPublisher>());
        Assert.Null(provider.GetService<MaintenanceAssetUnavailableTopicOptions>());

        // 与 v1 完全对齐：Testing 分支不接任何 outbox，v1 转换器 handler（缺 IIntegrationEventPublisher）和 v2 双发 publisher
        // （缺 IMaintenanceIntegrationEventOutboxPublisher）在这里同样不可激活；v2 没有偷偷多一条只在测试里成立的发布路径。
        var v1Activation = Record.Exception(() => provider.GetServices<INotificationHandler<AssetUnavailableDomainEvent>>().ToArray());
        var v2Activation = Record.Exception(() => provider.GetServices<INotificationHandler<AssetUnavailableByReasonCodeDomainEvent>>().ToArray());
        Assert.IsType<InvalidOperationException>(v1Activation);
        Assert.Contains(nameof(IIntegrationEventPublisher), v1Activation.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(v2Activation);
        Assert.Contains(nameof(IMaintenanceIntegrationEventOutboxPublisher), v2Activation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Program_registers_the_v2_command_lock_and_exposes_v2_next_to_an_unchanged_v1_contract()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("IndustrialTelemetry:BaseUrl", "http://industrial-telemetry.local");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            });

        using (var scope = factory.Services.CreateScope())
        {
            var locks = scope.ServiceProvider.GetServices<ICommandLock<CreateMaintenanceWorkOrderV2Command>>().ToArray();
            Assert.Single(locks.OfType<CreateMaintenanceWorkOrderV2CommandLock>());
            Assert.Single(scope.ServiceProvider.GetServices<ICommandLock<CreateMaintenanceWorkOrderCommand>>().OfType<CreateMaintenanceWorkOrderCommandLock>());
            Assert.NotNull(scope.ServiceProvider.GetService<IRequestHandler<CreateMaintenanceWorkOrderV2Command, MaintenanceWorkOrderCommandResult>>());
        }

        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = document.RootElement.GetProperty("paths");

        var v2 = paths.GetProperty("/api/business/v2/maintenance/work-orders").GetProperty("post");
        Assert.Equal("createMaintenanceWorkOrderV2", v2.GetProperty("operationId").GetString());
        var v2Schema = ResolveRequestSchema(document, v2);
        Assert.True(v2Schema.TryGetProperty("assetUnavailableReasonCode", out var reasonCode));
        Assert.True(reasonCode.TryGetProperty("nullable", out var nullable) && nullable.GetBoolean());
        Assert.False(v2Schema.TryGetProperty("assetUnavailableReason", out _));

        // v1 零漂移：wire property、operationId 不变，也没有把 v2 字段偷偷挂到 v1 上。
        var v1 = paths.GetProperty("/api/business/v1/maintenance/work-orders").GetProperty("post");
        Assert.Equal("createMaintenanceWorkOrder", v1.GetProperty("operationId").GetString());
        var v1Schema = ResolveRequestSchema(document, v1);
        Assert.True(v1Schema.TryGetProperty("assetUnavailableReason", out _));
        Assert.False(v1Schema.TryGetProperty("assetUnavailableReasonCode", out _));
    }

    private static JsonElement ResolveRequestSchema(JsonDocument document, JsonElement operation)
    {
        var reference = operation.GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString()!;
        var schemaName = reference[(reference.LastIndexOf('/') + 1)..];
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(schemaName).GetProperty("properties");
    }

    private static ServiceProvider BuildProvider(string environmentName, bool isTesting)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:Provider"] = "InMemory",
                ["Cap:Version"] = "test-maintenance-v2",
            })
            .Build();
        services.AddLogging();
        services.AddMediatR(options => options.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase($"maintenance-v2-wiring-{environmentName}"));
        services.AddContext().AddEnvContext().AddCapContextProcessor();
        services.AddMaintenanceCapIntegrationEvents(configuration, environmentName, isTesting);
        return services.BuildServiceProvider();
    }
}
