using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Seed;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

/// <summary>
/// 《工厂世界观设定集》§3 采集侧黄金向量：46 台设备的点位定义与 3 个采集连接器的标签清单。
/// 种子只登记采集配置，不写采样值/报警/在线状态。
/// </summary>
public sealed class WorldBibleSeedServiceTests
{
    [Fact]
    public void Spec_covers_forty_six_devices_across_three_connectors()
    {
        Assert.Equal(3, WorldBibleSpec.Connectors.Length);
        Assert.Equal(46, WorldBibleSpec.DeviceClasses.Sum(x => x.DeviceCount));
        Assert.Equal(46, WorldBibleSpec.DeviceTags.Select(x => x.DeviceAssetId).Distinct(StringComparer.Ordinal).Count());

        var connectorIds = WorldBibleSpec.Connectors.Select(x => x.ConnectorId).ToArray();
        Assert.All(WorldBibleSpec.DeviceTags, tag => Assert.Contains(tag.CollectionConnectorId, connectorIds));

        // 设定集 §3：OPC UA 覆盖机加（CNC/磨床/焊接机器人），MQTT 覆盖装配/检测，Modbus 覆盖辅助与涂装/包装。
        Assert.Equal(
            17,
            WorldBibleSpec.DeviceTags
                .Where(x => x.CollectionConnectorId == WorldBibleSpec.OpcUaConnectorId)
                .Select(x => x.DeviceAssetId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            16,
            WorldBibleSpec.DeviceTags
                .Where(x => x.CollectionConnectorId == WorldBibleSpec.MqttConnectorId)
                .Select(x => x.DeviceAssetId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            13,
            WorldBibleSpec.DeviceTags
                .Where(x => x.CollectionConnectorId == WorldBibleSpec.ModbusConnectorId)
                .Select(x => x.DeviceAssetId).Distinct(StringComparer.Ordinal).Count());

        // 设备/点位组合唯一，且不与固定演示设备号段重叠。
        Assert.Equal(
            WorldBibleSpec.DeviceTags.Count,
            WorldBibleSpec.DeviceTags.Select(x => $"{x.DeviceAssetId}/{x.TagKey}").Distinct(StringComparer.Ordinal).Count());
        Assert.All(WorldBibleSpec.DeviceTags, tag => Assert.DoesNotContain("DEMO", tag.DeviceAssetId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Seed_registers_tags_and_three_connector_manifests_once()
    {
        await using var db = CreateDbContext();
        var seed = new WorldBibleSeedService(db);

        await seed.SeedAsync("org-001", "env-dev");
        await seed.SeedAsync("org-001", "env-dev");

        Assert.Equal(WorldBibleSpec.DeviceTags.Count, await db.TelemetryTags.CountAsync());
        Assert.Equal(3, await db.ConnectorTagManifests.CountAsync());
        Assert.Equal(WorldBibleSpec.DeviceTags.Count, await db.ConnectorTagBindings.CountAsync());

        var manifests = await db.ConnectorTagManifests.Include(x => x.Bindings).ToArrayAsync();
        Assert.All(manifests, manifest =>
        {
            Assert.Equal(64, manifest.ManifestRevision.Length);
            Assert.All(manifest.Bindings, binding =>
            {
                Assert.True(binding.IsCurrent);
                Assert.True(binding.Enabled);
                // 种子不伪造激活成功：一律 pending，由连接器自报激活。
                Assert.Equal("pending", binding.ActivationStatus);
            });
        });

        Assert.Equal(
            ["modbus", "mqtt", "opcua"],
            manifests.Select(x => x.SourceSystem).OrderBy(x => x, StringComparer.Ordinal));

        var cncTag = await db.TelemetryTags.SingleAsync(x => x.DeviceAssetId == "DEV-CNC-01" && x.TagKey == "vibration");
        Assert.Equal("mm/s", cncTag.UnitCode);
        Assert.Equal("sample-2s", cncTag.SamplingPolicy);
    }

    [Fact]
    public async Task Seed_leaves_the_frozen_demo_telemetry_facts_untouched()
    {
        await using var db = CreateDbContext();
        await new LeaderDemoSeedService(db).SeedAsync("org-001", "env-dev");

        await new WorldBibleSeedService(db).SeedAsync("org-001", "env-dev");

        Assert.Equal(2, await db.TelemetryTags.CountAsync(x => x.DeviceAssetId == "DEV-CNC-DEMO"));
        Assert.Single(await db.AlarmRules.Where(x => x.RuleCode == "ALARM-DEMO-001").ToArrayAsync());
        Assert.Empty(await db.AlarmEvents.ToArrayAsync());
        Assert.Empty(await db.TelemetryRawSamples.ToArrayAsync());
        Assert.Empty(await db.DeviceStateSnapshots.ToArrayAsync());
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"industrial-telemetry-world-bible-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new WorldBibleSeedTestMediator());
    }

    private sealed class WorldBibleSeedTestMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
