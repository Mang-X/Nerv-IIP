using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryConnectorConfigAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;
using Nerv.IIP.Contracts.IndustrialTelemetry;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application;

public sealed record ListTelemetryConnectorConfigsQuery(string OrganizationId, string EnvironmentId, bool EnabledOnly = false)
    : IQuery<TelemetryConnectorConfigItem[]>;

public sealed record SaveTelemetryConnectorConfigCommand(string OrganizationId, string EnvironmentId, TelemetryConnectorConfigInput Config)
    : ICommand<TelemetryConnectorConfigItem>;

public sealed record DeleteTelemetryConnectorConfigCommand(string OrganizationId, string EnvironmentId, string ConnectorId)
    : ICommand<bool>;

public sealed class TelemetryConnectorConfigHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListTelemetryConnectorConfigsQuery, TelemetryConnectorConfigItem[]>,
        ICommandHandler<SaveTelemetryConnectorConfigCommand, TelemetryConnectorConfigItem>,
        ICommandHandler<DeleteTelemetryConnectorConfigCommand, bool>
{
    public async Task<TelemetryConnectorConfigItem[]> Handle(ListTelemetryConnectorConfigsQuery request, CancellationToken cancellationToken)
    {
        var rows = await dbContext.TelemetryConnectorConfigs.AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => !request.EnabledOnly || x.IsEnabled)
            .OrderBy(x => x.ConnectorId)
            .ToArrayAsync(cancellationToken);
        return rows.Select(ToItem).ToArray();
    }

    public async Task<TelemetryConnectorConfigItem> Handle(SaveTelemetryConnectorConfigCommand request, CancellationToken cancellationToken)
    {
        var input = request.Config;
        var protocol = input.Protocol.Trim().ToLowerInvariant();
        if (protocol is not ("modbus" or "mqtt" or "opcua") || input.PollIntervalSeconds < 1
            || string.IsNullOrWhiteSpace(input.ConnectorId) || string.IsNullOrWhiteSpace(input.DisplayName)
            || input.Mappings is not { Count: > 0 and <= 500 })
        {
            throw new KnownException("请填写有效的连接器标识、名称、协议、采集周期和测点映射。");
        }

        var endpoint = input.Endpoint.Trim();
        if (protocol == "opcua" && !endpoint.Contains("://", StringComparison.Ordinal))
        {
            endpoint = $"opc.tcp://{endpoint}";
        }
        _ = TelemetryConnectionProbe.ParseEndpoint(protocol, endpoint);
        var row = await dbContext.TelemetryConnectorConfigs.FindAsync(
            [request.OrganizationId, request.EnvironmentId, input.ConnectorId], cancellationToken);
        if (row is null)
        {
            row = new TelemetryConnectorConfig(request.OrganizationId, request.EnvironmentId, input.ConnectorId);
            dbContext.TelemetryConnectorConfigs.Add(row);
        }

        var mappings = new List<TelemetryConnectorMapping>(input.Mappings.Count);
        var keys = new HashSet<(string, string)>();
        foreach (var mapping in input.Mappings)
        {
            if (mapping.BucketSeconds < 1 || string.IsNullOrWhiteSpace(mapping.TagKey)
                || string.IsNullOrWhiteSpace(mapping.DeviceAssetId) || string.IsNullOrWhiteSpace(mapping.UnitCode)
                || protocol == "modbus" && (mapping.Address is < 40001 or > 49999 || mapping.DataType is not ("Float32" or "UInt16"))
                || protocol == "mqtt" && (string.IsNullOrWhiteSpace(mapping.TopicFilter) || string.IsNullOrWhiteSpace(mapping.ValueJsonPath))
                || protocol == "opcua" && string.IsNullOrWhiteSpace(mapping.NodeId))
            {
                throw new KnownException("请填写与协议匹配的测点映射。");
            }

            var deviceId = mapping.DeviceAssetId.Trim();
            var tagKey = mapping.TagKey.Trim().ToLowerInvariant();
            if (!keys.Add((deviceId, tagKey)))
            {
                throw new KnownException($"设备测点 {tagKey} 重复。");
            }

            var existing = await dbContext.TelemetryTags.SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId
                && x.DeviceAssetId == deviceId && x.TagKey == tagKey, cancellationToken);
            if (existing is { IsEnabled: false })
            {
                throw new KnownException($"点位编码 {tagKey} 已停用，请换一个点位编码。");
            }
            if (existing is not null && Domain.TelemetrySamplingPolicy.Parse(existing.SamplingPolicy).BucketSeconds != mapping.BucketSeconds)
            {
                throw new KnownException($"点位 {tagKey} 的采样分桶周期与映射不一致。");
            }
            if (existing is null)
            {
                dbContext.TelemetryTags.Add(TelemetryTag.Create(request.OrganizationId, request.EnvironmentId,
                    deviceId, tagKey, "number", mapping.UnitCode, $"bucket={mapping.BucketSeconds}s"));
            }

            mappings.Add(mapping with { DeviceAssetId = deviceId, TagKey = tagKey });
        }

        row.Configure(input.DisplayName, protocol, endpoint, input.IsEnabled, input.PollIntervalSeconds,
            JsonSerializer.Serialize(mappings), DateTimeOffset.UtcNow);
        return ToItem(row);
    }

    public async Task<bool> Handle(DeleteTelemetryConnectorConfigCommand request, CancellationToken cancellationToken)
    {
        var row = await dbContext.TelemetryConnectorConfigs.FindAsync(
            [request.OrganizationId, request.EnvironmentId, request.ConnectorId], cancellationToken);
        if (row is null)
        {
            return false;
        }

        dbContext.TelemetryConnectorConfigs.Remove(row);
        return true;
    }

    private static TelemetryConnectorConfigItem ToItem(TelemetryConnectorConfig row) =>
        new(row.ConnectorId, row.DisplayName, row.Protocol, row.Endpoint, row.IsEnabled, row.PollIntervalSeconds,
            JsonSerializer.Deserialize<TelemetryConnectorMapping[]>(row.MappingsJson)!, row.Revision, row.UpdatedAtUtc);
}
