using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.ConnectorTagManifestAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》§3 的 IndustrialTelemetry 侧种子：为 46 台设备登记采集标签，
/// 并把每台设备的点位分配到 3 个采集连接器的标签清单上。
///
/// 只写采集配置，不写任何采样值、报警事件或在线状态；重复执行幂等（清单 revision 由内容
/// 决定，同一内容第二次执行只会被聚合判定为 idempotent）。
/// </summary>
public sealed class WorldBibleSeedService(ApplicationDbContext dbContext)
{
    public async Task SeedAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default)
    {
        foreach (var tag in WorldBibleSpec.DeviceTags)
        {
            var existing = await dbContext.TelemetryTags.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.DeviceAssetId == tag.DeviceAssetId && x.TagKey == tag.TagKey,
                cancellationToken);
            if (existing is null)
            {
                dbContext.TelemetryTags.Add(TelemetryTag.Create(
                    organizationId,
                    environmentId,
                    tag.DeviceAssetId,
                    tag.TagKey,
                    "decimal",
                    tag.UnitCode,
                    WorldBibleSpec.SamplingPolicy));
                continue;
            }

            if (existing.UnitCode != tag.UnitCode || existing.SamplingPolicy != WorldBibleSpec.SamplingPolicy)
            {
                throw Collision($"{tag.DeviceAssetId}/{tag.TagKey}");
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var connector in WorldBibleSpec.Connectors)
        {
            await SeedManifestAsync(organizationId, environmentId, connector, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task SeedManifestAsync(
        string organizationId,
        string environmentId,
        WorldBibleConnector connector,
        CancellationToken cancellationToken)
    {
        var entries = WorldBibleSpec.DeviceTags
            .Where(tag => tag.CollectionConnectorId == connector.ConnectorId)
            .Select(tag => new ConnectorTagManifestEntry(
                tag.DeviceAssetId,
                tag.TagKey,
                Enabled: true,
                tag.ProtocolAddress,
                WorldBibleSpec.SeededActivationStatus,
                WorldBibleSpec.ManifestObservedAtUtc))
            .ToArray();
        var revision = ComputeRevision(connector, entries);

        var existing = await dbContext.ConnectorTagManifests
            .Include(x => x.Bindings)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.CollectionConnectorId == connector.ConnectorId,
                cancellationToken);
        if (existing is null)
        {
            dbContext.ConnectorTagManifests.Add(ConnectorTagManifest.Create(
                organizationId,
                environmentId,
                connector.ConnectorId,
                connector.SourceSystem,
                revision,
                WorldBibleSpec.ManifestObservedAtUtc,
                entries));
            return;
        }

        // 已经有清单：若不是本块登记的同一 revision，说明连接器/租户已经接管，绝不覆盖。
        if (existing.ManifestRevision != revision)
        {
            return;
        }

        var result = existing.Apply(connector.SourceSystem, revision, WorldBibleSpec.ManifestObservedAtUtc, entries);
        if (result.Disposition == ManifestApplyDisposition.Conflict)
        {
            throw Collision(connector.ConnectorId);
        }
    }

    /// <summary>
    /// 清单 revision 必须是 64 位小写 SHA-256；用「连接器 + 排序后的设备/点位/地址」计算，
    /// 保证同一世界观内容每次执行得到同一 revision（幂等的前提）。
    /// </summary>
    private static string ComputeRevision(WorldBibleConnector connector, IReadOnlyCollection<ConnectorTagManifestEntry> entries)
    {
        var payload = new StringBuilder();
        payload.Append(connector.ConnectorId).Append('|').Append(connector.SourceSystem).Append('\n');
        foreach (var entry in entries
            .OrderBy(x => x.DeviceAssetId, StringComparer.Ordinal)
            .ThenBy(x => x.TagKey, StringComparer.Ordinal))
        {
            payload
                .Append(entry.DeviceAssetId).Append('|')
                .Append(entry.TagKey).Append('|')
                .Append(entry.Enabled ? '1' : '0').Append('|')
                .Append(entry.ProtocolAddress).Append('\n');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString()));
        return Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture);
    }

    private static InvalidOperationException Collision(string key) =>
        new($"Reserved world-bible telemetry fact '{key}' exists with incompatible tenant facts; the seed will not overwrite it.");
}
