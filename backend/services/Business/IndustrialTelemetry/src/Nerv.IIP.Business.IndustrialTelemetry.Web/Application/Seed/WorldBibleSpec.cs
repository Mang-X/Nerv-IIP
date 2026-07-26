namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》§3 的 IndustrialTelemetry 侧固定形状：46 台设备的采集标签定义，
/// 以及 3 个采集连接器（<c>CONN-OPCUA-01</c> / <c>CONN-MODBUS-01</c> / <c>CONN-MQTT-01</c>）
/// 的标签清单——即「哪台设备的哪个点位由哪个连接器采集」。
///
/// 设备编码段按类别成段（<c>DEV-CNC-01..10</c> 等），与 MasterData 侧 <c>WorldBibleSpec</c>
/// 按同一字面量重复声明，两侧各有黄金向量测试防止漂移。
/// 本块只登记采集配置事实；连接器是否在线由 L3 常驻模拟/真实连接器心跳决定，种子不伪造。
/// </summary>
public static class WorldBibleSpec
{
    public const string OpcUaConnectorId = "CONN-OPCUA-01";
    public const string ModbusConnectorId = "CONN-MODBUS-01";
    public const string MqttConnectorId = "CONN-MQTT-01";

    /// <summary>清单登记时间锚定平台上线日（设定集 §1），保证重复执行结果确定。</summary>
    public static readonly DateTimeOffset ManifestObservedAtUtc = new(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 采集清单里的绑定一律登记为 <c>pending</c>：种子只声明「配置上该点位归属该连接器」，
    /// 真正的 active 由连接器自报激活结果驱动。
    /// </summary>
    public const string SeededActivationStatus = "pending";

    public static readonly WorldBibleConnector[] Connectors =
    [
        new(OpcUaConnectorId, "opcua", "机加车间 OPC UA 网关"),
        new(ModbusConnectorId, "modbus", "辅助设备 Modbus 网关"),
        new(MqttConnectorId, "mqtt", "装配/检测 MQTT 网关"),
    ];

    /// <summary>设定集 §3 的 8 个设备类别：编码段、台数、采集点位与所属连接器。</summary>
    public static readonly WorldBibleDeviceClass[] DeviceClasses =
    [
        new("DEV-CNC-", 10, OpcUaConnectorId,
            [new("spindle-temperature", "degC"), new("vibration", "mm/s"), new("spindle-speed", "rpm")]),
        new("DEV-GRD-", 4, OpcUaConnectorId,
            [new("vibration", "mm/s"), new("wheel-speed", "rpm")]),
        new("DEV-WLD-", 3, OpcUaConnectorId,
            [new("weld-current", "A"), new("temperature", "degC")]),
        new("DEV-ASM-", 12, MqttConnectorId,
            [new("press-force", "kN"), new("cycle-count", "count")]),
        new("DEV-TST-", 4, MqttConnectorId,
            [new("damping-force", "N")]),
        new("DEV-CTG-", 3, ModbusConnectorId,
            [new("bath-temperature", "degC"), new("bath-ph", "pH")]),
        new("DEV-PKG-", 2, ModbusConnectorId,
            [new("cycle-count", "count")]),
        new("DEV-AUX-", 8, ModbusConnectorId,
            [new("air-pressure", "bar"), new("temperature", "degC")]),
    ];

    public const string SamplingPolicy = "sample-2s";

    /// <summary>46 台设备 × 各自点位展开后的全量采集绑定。</summary>
    public static readonly IReadOnlyList<WorldBibleDeviceTag> DeviceTags = BuildDeviceTags();

    /// <summary>连接器的协议地址形式各不相同，确保采集健康页展示的是可辨识的真实地址形状。</summary>
    public static string ProtocolAddress(string connectorId, string deviceAssetId, string tagKey, int tagOrdinal) =>
        connectorId switch
        {
            OpcUaConnectorId => $"ns=2;s={deviceAssetId}.{tagKey}",
            ModbusConnectorId => $"40{tagOrdinal:D3}",
            _ => $"nerv/iip/{deviceAssetId}/{tagKey}",
        };

    private static IReadOnlyList<WorldBibleDeviceTag> BuildDeviceTags()
    {
        var tags = new List<WorldBibleDeviceTag>(100);
        var ordinal = 1;
        foreach (var deviceClass in DeviceClasses)
        {
            for (var index = 1; index <= deviceClass.DeviceCount; index++)
            {
                var deviceAssetId = $"{deviceClass.CodePrefix}{index:D2}";
                foreach (var tag in deviceClass.Tags)
                {
                    tags.Add(new WorldBibleDeviceTag(
                        deviceAssetId,
                        tag.TagKey,
                        tag.UnitCode,
                        deviceClass.CollectionConnectorId,
                        ProtocolAddress(deviceClass.CollectionConnectorId, deviceAssetId, tag.TagKey, ordinal)));
                    ordinal++;
                }
            }
        }

        return tags;
    }
}

public sealed record WorldBibleConnector(string ConnectorId, string SourceSystem, string DisplayName);

public sealed record WorldBibleTagPoint(string TagKey, string UnitCode);

public sealed record WorldBibleDeviceClass(
    string CodePrefix,
    int DeviceCount,
    string CollectionConnectorId,
    WorldBibleTagPoint[] Tags);

public sealed record WorldBibleDeviceTag(
    string DeviceAssetId,
    string TagKey,
    string UnitCode,
    string CollectionConnectorId,
    string ProtocolAddress);
