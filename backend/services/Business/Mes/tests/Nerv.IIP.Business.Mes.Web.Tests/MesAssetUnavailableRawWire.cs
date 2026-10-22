namespace Nerv.IIP.Business.Mes.Web.Tests.RawWire;

/// <summary>
/// 与真实 Maintenance v2 Dto 同形、同名（CAP 的 cap-msg-type 头按类型名解析回真实契约类型）但不带契约校验的 wire 信封：
/// 真实类型的 JSON converter 在 Write 时也会 Validate，根本发不出非法信封；这里模拟的是 producer 侧绕过契约或契约漂移
/// 后落到 transport 上的原始消息，消费者仍按真实契约类型反序列化并在 converter 阶段拒收。
/// </summary>
public sealed record AssetUnavailableV2IntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    AssetUnavailableV2Payload Payload);

public sealed record AssetUnavailableV2Payload(string DeviceAssetId, string ReasonCode, DateTimeOffset FromUtc);
