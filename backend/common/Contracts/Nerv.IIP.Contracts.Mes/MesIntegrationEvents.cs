using System.Text.Json;
using System.Text.Json.Serialization;
using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.Mes;

public static class MesIntegrationEventTypes
{
    public const string WorkOrderReleased = "mes.WorkOrderReleased";

    /// <summary>
    /// 工单发布投影回填（#3000）：与 <see cref="WorkOrderReleased"/> 是同一份发布事实、同一份载荷，
    /// 但走独立事件类型与独立 topic，只投给 Quality 的回填消费组。
    /// </summary>
    public const string WorkOrderReleaseProjectionBackfilled = "mes.WorkOrderReleaseProjectionBackfilled";
    public const string WorkOrderCompleted = "mes.WorkOrderCompleted";
    public const string WorkOrderClosed = "mes.WorkOrderClosed";
    public const string ReworkWorkOrderCreated = "mes.ReworkWorkOrderCreated";
    public const string WorkOrderEngineeringChangeImpactDetected = "mes.WorkOrderEngineeringChangeImpactDetected";
    public const string OperationTaskCompleted = "mes.OperationTaskCompleted";
    public const string OperationTaskStarted = "mes.OperationTaskStarted";
    public const string OperationTaskPaused = "mes.OperationTaskPaused";
    public const string OperationTaskResumed = "mes.OperationTaskResumed";
    public const string DowntimeStarted = "mes.DowntimeStarted";
    public const string DowntimeRestored = "mes.DowntimeRestored";
    public const string OperationActualTimeSettled = "mes.OperationActualTimeSettled";
    public const string OperationActualTimeSettlementVoided = "mes.OperationActualTimeSettlementVoided";
    public const string OperationTaskManuallyDispatched = "mes.OperationTaskManuallyDispatched";
    public const string OperationTaskManualDispatchCleared = "mes.OperationTaskManualDispatchCleared";
    public const string FinishedGoodsReceiptRequested = "mes.FinishedGoodsReceiptRequested";
    public const string ProductionReportRecorded = "mes.ProductionReportRecorded";
    public const string MaterialIssueRequested = "mes.MaterialIssueRequested";
}

public static class MesSourceDocumentTypes
{
    /// <summary>Source document type WMS uses for outbound work created from a MES material issue request.</summary>
    public const string MaterialIssueRequest = "mes-material-issue-request";
}

public static class MesIntegrationEventVersions
{
    public const int V1 = 1;
    public const int V2 = 2;
}

/// <summary>
/// MES actual-time routing keys shared by the producer and its canonical-topic consumers.
/// </summary>
public static class MesActualTimeIntegrationEventTopics
{
    public const string DeploymentProfileToken = "{deployment-profile}";
    public const string SettledV1LegacyAlias = nameof(MesOperationActualTimeSettledIntegrationEvent);
    public const string VoidedV1LegacyAlias = nameof(MesOperationActualTimeSettlementVoidedIntegrationEvent);
    public const string SettledV2Template =
        "nerv-iip.{deployment-profile}.business-mes.mes.operation-actual-time-settled.v2";
    public const string VoidedV2Template =
        "nerv-iip.{deployment-profile}.business-mes.mes.operation-actual-time-settlement-voided.v2";

    public static string Settled(string deploymentProfile, int version) =>
        Build(deploymentProfile, "operation-actual-time-settled", version);

    public static string Voided(string deploymentProfile, int version) =>
        Build(deploymentProfile, "operation-actual-time-settlement-voided", version);

    public static string? CanonicalSubscriptionTemplate(Type integrationEventType)
    {
        ArgumentNullException.ThrowIfNull(integrationEventType);
        if (integrationEventType == typeof(MesOperationActualTimeSettledV2IntegrationEvent))
            return SettledV2Template;
        if (integrationEventType == typeof(MesOperationActualTimeSettlementVoidedV2IntegrationEvent))
            return VoidedV2Template;
        return null;
    }

    public static string ResolveSubscriptionTemplate(string topic, string deploymentProfile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentProfile);
        return topic.Contains(DeploymentProfileToken, StringComparison.Ordinal)
            ? topic.Replace(DeploymentProfileToken, NormalizeDeploymentProfile(deploymentProfile), StringComparison.Ordinal)
            : topic;
    }

    private static string Build(string deploymentProfile, string eventName, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentProfile);
        return $"nerv-iip.{NormalizeDeploymentProfile(deploymentProfile)}.business-mes.mes.{eventName}.v{version}";
    }

    private static string NormalizeDeploymentProfile(string deploymentProfile) =>
        deploymentProfile.Trim().ToLowerInvariant();
}

public enum MesMachineTimeFactStatus
{
    Available,
    NotApplicable,
    Unavailable,
}

public sealed class MesMachineTimeFactStatusJsonConverter()
    : JsonStringEnumConverter<MesMachineTimeFactStatus>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

public static class MesMachineTimeBasisCodes
{
    public const string SingleDeviceActiveMinusExplicitPauseV1 = "single-device-active-minus-explicit-pause-v1";
}

public static class MesIntegrationEventSources
{
    public const string BusinessMes = "business-mes";
}

public static class MesManualDispatchClearReasonCodes
{
    public const string DeviceCleared = "device-cleared";
    public const string OperationCancelled = "operation-cancelled";
}

public sealed record WorkOrderReleasedIntegrationEvent(
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
    WorkOrderReleasedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record WorkOrderReleasedPayload(
    string WorkOrderId,
    string SkuCode,
    decimal PlannedQuantity,
    DateTimeOffset ReleasedAtUtc,
    IReadOnlyCollection<ReleasedOperationPayload> Operations);

/// <param name="OperationId">工序任务标识。</param>
/// <param name="OperationSequence">工序号。</param>
/// <param name="WorkCenterId">工作中心标识。</param>
/// <param name="PreReleaseGoodQuantity">
/// 本道工序在**下达动作发生的那一刻**就已经存在的净良品量（口径：该工序全部非冲销报工行的
/// <c>GoodQuantity</c> 之和，与 Quality 侧 <c>PeriodicInspectionRuntimeContext.QuantityHighWater</c>
/// 逐字同一个口径）。工单在 <c>created</c> 状态就能开工、报工（#3113），下达因此可能发生在已有产量之后；
/// owner 已裁定「**下达之前已产出的数量不补开巡检任务**」，而做这个判断需要
/// 「哪些产量在下达动作之前就已存在、且**按工序分辨**」——这个事实只有 MES 在下达那一刻掌握。
/// 载荷里那个工单级 <see cref="WorkOrderReleasedPayload.ReleasedAtUtc"/> 承担不了它：
/// 它是一个被夹到「不晚于最早既有活动」的**标量**，既分不出工序，也不告诉消费侧「当时已经有多少」。
/// 消费侧照它推断，在「多工序」与「发布事件先于报工事件到达」两种形态下必然失效（#3129）。
///
/// <para><b>为什么是数量、而不是「最早活动时刻」。</b>owner 的裁定只落在**数量**这一维：
/// 时间型巡检该不该开与「下达前后」没有业务关系（它由 <c>FirstActivityAtUtc</c> 起算、
/// 由定时任务生成，与本字段两条独立的路）。只带数量，是为了不把一条数量维的裁定外溢到时间维；
/// 需要时间维时请另立字段与另一条裁定，**不要把本字段当通用的「下达前活动事实」读**。</para>
///
/// <para><b>为什么可空、null 意味着什么（本票的设计决定，不是兼容细节）。</b>
/// 按 ADR 0011 §4「同一 <c>eventType</c> 下新增可选字段不提升版本」，本字段是**可空可选**的，
/// 因此 <c>eventVersion</c> 不升。代价必须写明：
/// <list type="number">
/// <item><b>哪些消息会是 null。</b>① 本次发布上线**之前**由旧生产者序列化、此刻仍躺在
/// 消息中间件在途队列或死信（DLQ）里、上线后才被消费或重投的 <c>mes.WorkOrderReleased</c>；
/// ② <c>mes.WorkOrderReleaseProjectionBackfilled</c>（#3000 存量回填）——它的消费分支
/// 无条件跳过到 <c>OccurredAtUtc</c> 为止的全部累计，本字段在那条分支上没有作用，故生产者不填（见
/// <c>WorkOrderReleaseProjectionBackfill</c> 构造点的说明）。
/// **上线后由 MES 直投路径新发出的发布事实一律带值**（没有既有产量时带 <c>0</c>，不是 null）。</item>
/// <item><b>null 时的行为。</b>消费侧按**本字段出现之前的老行为**处理：不跳过任何已累计的产量窗口，
/// 即把下达前的产量也补开成巡检任务。这正是 #3129 要修的那个行为——
/// 也就是说 <b>null 是一个明确的、已知的不生效面，不是「安全默认值」</b>。</item>
/// <item><b>为什么可接受。</b>null 时的行为与本次改动之前的 main **逐字相同**，
/// 不引入任何相对 main 的回归；多开出的巡检任务是可人工关闭的待办，不是数据损坏、不进死信。
/// 相对的，若把本字段做成必填，全部在途与 DLQ 中的旧消息会在反序列化时落到 <c>default</c>（<c>0</c>）
/// 或整封失败——前者是**静默**按「下达前零产量」处理、后者直接丢事实，两者都比多开几张任务坏。</item>
/// <item><b>这个不生效面什么时候消失。</b>当「本次发布之前入队的 <c>mes.WorkOrderReleased</c>」
/// 被消费干净、且 DLQ 中同批旧消息被重投或清理之后即自然消失；它不随时间无限存在，
/// 也**不需要**后续代码改动来收口。等 DLQ 清空后若要把它彻底关掉，做法是把本字段改成必填并升
/// <c>eventVersion</c>——那是一次独立的决定，本票不做。</item>
/// </list>
/// </para>
/// </param>
public sealed record ReleasedOperationPayload(
    string OperationId,
    int OperationSequence,
    string WorkCenterId,
    decimal? PreReleaseGoodQuantity = null);

/// <summary>
/// 存量在制工单的发布事实补投（#3000）。载荷与 <see cref="WorkOrderReleasedIntegrationEvent"/> 完全相同，
/// 但**不能**复用发布事件的 topic 重放：
/// 1) Scheduling 的发布事件消费者会让全部已生成排程计划失效，重放会把这一副作用扩到每一张在制工单；
/// 2) Quality 的发布事件消费组对已同步过的工单会拿重建的发布时刻与库里那一个比对，判为冲突事实进死信。
/// 因此补投是独立通道，接收方按「只补空缺、不覆盖既有发布事实」处理。
/// </summary>
public sealed record WorkOrderReleaseProjectionBackfilledIntegrationEvent(
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
    WorkOrderReleasedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record WorkOrderCompletedIntegrationEvent(
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
    WorkOrderCompletedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record WorkOrderCompletedPayload(
    string WorkOrderId,
    string SkuCode,
    decimal PlannedQuantity,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    DateTimeOffset CompletedAtUtc,
    int ExpectedCostReportCount = 0,
    int ExpectedMaterialMovementCount = 0);

public sealed record WorkOrderClosedIntegrationEvent(
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
    WorkOrderClosedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record WorkOrderClosedPayload(
    string WorkOrderId,
    string SkuCode,
    decimal PlannedQuantity,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    DateTimeOffset ClosedAtUtc);

public sealed record ReworkWorkOrderCreatedIntegrationEvent(
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
    ReworkWorkOrderCreatedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record ReworkWorkOrderCreatedPayload(
    string SourceNcrId,
    string SourceNcrCode,
    string ReworkWorkOrderId,
    string SourceWorkOrderId,
    string? SourceOperationTaskId,
    string SkuCode,
    decimal Quantity,
    string? SourceLotNo,
    string? SourceSerialNo,
    DateTimeOffset CreatedAtUtc);

public static class MesEngineeringChangeImpactContractStatuses
{
    public const string PendingDecision = "pending-decision";
    public const string AutoRebound = "auto-rebound";
    public const string BlockedForManualConfirmation = "blocked-for-manual-confirmation";
    public const string Decided = "decided";
}

public sealed record WorkOrderEngineeringChangeImpactDetectedIntegrationEvent(
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
    WorkOrderEngineeringChangeImpactDetectedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record WorkOrderEngineeringChangeImpactDetectedPayload(
    string WorkOrderId,
    string SkuCode,
    string ChangeNumber,
    string ArchivedProductionVersionId,
    string? SupersededByProductionVersionId,
    string ImpactStatus,
    DateOnly EffectiveDate);

public sealed record MesOperationTaskCompletedIntegrationEvent(
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
    OperationTaskCompletedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record OperationTaskCompletedPayload(
    string WorkOrderId,
    string OperationTaskId,
    string SkuCode,
    int OperationSequence,
    string WorkCenterId,
    decimal PlannedQuantity,
    string UomCode,
    bool RequiresQualityInspection,
    DateTimeOffset CompletedAtUtc);

public sealed record MesOperationTaskStartedIntegrationEvent(
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
    OperationTaskLifecyclePayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record MesOperationTaskPausedIntegrationEvent(
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
    OperationTaskLifecyclePayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record MesOperationTaskResumedIntegrationEvent(
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
    OperationTaskLifecyclePayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record OperationTaskLifecyclePayload(
    string WorkOrderId,
    string OperationTaskId,
    int OperationSequence,
    string WorkCenterId,
    DateTimeOffset ChangedAtUtc);

public sealed record MesDowntimeStartedIntegrationEvent(
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
    DowntimeStartedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record DowntimeStartedPayload(
    string DowntimeEventNo,
    string? WorkOrderId,
    string? OperationTaskId,
    string WorkCenterId,
    string? DeviceAssetId,
    string Reason,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? PlannedEndUtc);

public sealed record MesDowntimeRestoredIntegrationEvent(
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
    DowntimeRestoredPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record DowntimeRestoredPayload(
    string DowntimeEventNo,
    string? WorkOrderId,
    string? OperationTaskId,
    string WorkCenterId,
    string? DeviceAssetId,
    string Reason,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset RestoredAtUtc);

[JsonConverter(typeof(MesOperationActualTimeSettledV1IntegrationEventJsonConverter))]
public sealed record MesOperationActualTimeSettledIntegrationEvent(
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
    OperationActualTimeSettledPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record OperationActualTimeSettledPayload(
    string WorkOrderId,
    string OperationTaskId,
    string WorkCenterId,
    long SettlementRevision,
    DateTimeOffset CompletedAtUtc,
    long ActualLaborTicks,
    long ActualMachineTicks,
    IReadOnlyCollection<string> CoveredProductionReportNos);

[JsonConverter(typeof(MesOperationActualTimeSettledV2IntegrationEventJsonConverter))]
public sealed record MesOperationActualTimeSettledV2IntegrationEvent(
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
    OperationActualTimeSettledV2Payload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record OperationActualTimeSettledV2Payload(
    string WorkOrderId,
    string OperationTaskId,
    string WorkCenterId,
    long SettlementRevision,
    DateTimeOffset CompletedAtUtc,
    long ActualLaborTicks,
    long ActualMachineTicks,
    IReadOnlyCollection<string> CoveredProductionReportNos,
    string? DeviceAssetId,
    [property: JsonConverter(typeof(MesMachineTimeFactStatusJsonConverter))]
    MesMachineTimeFactStatus MachineTimeStatus,
    long? BillableMachineTicks,
    string? MachineTimeBasisCode);

[JsonConverter(typeof(MesOperationActualTimeSettlementVoidedV1IntegrationEventJsonConverter))]
public sealed record MesOperationActualTimeSettlementVoidedIntegrationEvent(
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
    OperationActualTimeSettlementVoidedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record OperationActualTimeSettlementVoidedPayload(
    string WorkOrderId,
    string OperationTaskId,
    string WorkCenterId,
    long SettlementRevision,
    DateTimeOffset CompletedAtUtc,
    DateTimeOffset VoidedAtUtc,
    long ActualLaborTicks,
    long ActualMachineTicks,
    IReadOnlyCollection<string> CoveredProductionReportNos);

[JsonConverter(typeof(MesOperationActualTimeSettlementVoidedV2IntegrationEventJsonConverter))]
public sealed record MesOperationActualTimeSettlementVoidedV2IntegrationEvent(
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
    OperationActualTimeSettlementVoidedV2Payload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record OperationActualTimeSettlementVoidedV2Payload(
    string WorkOrderId,
    string OperationTaskId,
    string WorkCenterId,
    long SettlementRevision,
    DateTimeOffset CompletedAtUtc,
    DateTimeOffset VoidedAtUtc,
    long ActualLaborTicks,
    long ActualMachineTicks,
    IReadOnlyCollection<string> CoveredProductionReportNos,
    string? DeviceAssetId,
    [property: JsonConverter(typeof(MesMachineTimeFactStatusJsonConverter))]
    MesMachineTimeFactStatus MachineTimeStatus,
    long? BillableMachineTicks,
    string? MachineTimeBasisCode);

internal static class MesActualTimeContractInvariant
{
    public static void Validate(
        string? deviceAssetId,
        MesMachineTimeFactStatus status,
        long? billableMachineTicks,
        string? basisCode)
    {
        if (status == MesMachineTimeFactStatus.Available)
        {
            if (string.IsNullOrWhiteSpace(deviceAssetId)
                || billableMachineTicks is null or < 0
                || basisCode != MesMachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1)
                throw new JsonException("Available machine time requires device, non-negative ticks, and the canonical basis.");
            return;
        }

        if (deviceAssetId is not null || billableMachineTicks is not null || basisCode is not null)
            throw new JsonException("Unavailable or not-applicable machine time must not contain evidence values.");
    }
}

internal static class MesActualTimeWireContract
{
    private static readonly string[] MachineFactPropertyNames =
        ["deviceAssetId", "machineTimeStatus", "billableMachineTicks", "machineTimeBasisCode"];

    public static T ReadV1<T>(ref Utf8JsonReader reader, JsonSerializerOptions options)
        => Read<T>(ref reader, options, MesIntegrationEventVersions.V1, MachineFactShape.Forbidden);

    public static T ReadV2<T>(ref Utf8JsonReader reader, JsonSerializerOptions options)
        => Read<T>(ref reader, options, MesIntegrationEventVersions.V2, MachineFactShape.Required);

    private static T Read<T>(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options,
        int expectedVersion,
        MachineFactShape machineFactShape)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (!TryGetProperty(root, "eventVersion", "EventVersion", out var versionElement)
            || versionElement.ValueKind != JsonValueKind.Number
            || versionElement.GetInt32() != expectedVersion)
            throw new JsonException($"MES actual-time envelope requires eventVersion {expectedVersion}.");
        if (!TryGetProperty(root, "payload", "Payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            throw new JsonException("MES actual-time payload is required.");
        if (machineFactShape == MachineFactShape.Forbidden)
        {
            if (MachineFactPropertyNames.Any(name =>
                    payload.TryGetProperty(name, out _)
                    || payload.TryGetProperty(char.ToUpperInvariant(name[0]) + name[1..], out _)))
                throw new JsonException("MES actual-time V1 payload must not contain V2 machine-time properties.");
        }
        else if (MachineFactPropertyNames.Any(name =>
                     !payload.TryGetProperty(name, out _)
                     && !payload.TryGetProperty(char.ToUpperInvariant(name[0]) + name[1..], out _)))
        {
            throw new JsonException("MES actual-time V2 payload requires every machine-time property.");
        }
        return JsonSerializer.Deserialize<T>(root.GetRawText(), options)
            ?? throw new JsonException("MES actual-time envelope is required.");
    }

    private enum MachineFactShape
    {
        Forbidden,
        Required,
    }

    public static void RequireVersion(int actualVersion, int expectedVersion)
    {
        if (actualVersion != expectedVersion)
            throw new JsonException($"MES actual-time envelope requires eventVersion {expectedVersion}.");
    }

    private static bool TryGetProperty(
        JsonElement element,
        string camelName,
        string pascalName,
        out JsonElement value) =>
        element.TryGetProperty(camelName, out value) || element.TryGetProperty(pascalName, out value);
}

public sealed class MesOperationActualTimeSettledV1IntegrationEventJsonConverter
    : JsonConverter<MesOperationActualTimeSettledIntegrationEvent>
{
    public override MesOperationActualTimeSettledIntegrationEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dto = MesActualTimeWireContract.ReadV1<SettledV1Dto>(ref reader, options);
        return new(dto.EventId, dto.EventType, dto.EventVersion, dto.OccurredAtUtc, dto.SourceService,
            dto.CorrelationId, dto.CausationId, dto.OrganizationId, dto.EnvironmentId, dto.Actor,
            dto.IdempotencyKey, dto.Payload);
    }

    public override void Write(Utf8JsonWriter writer, MesOperationActualTimeSettledIntegrationEvent value, JsonSerializerOptions options)
    {
        MesActualTimeWireContract.RequireVersion(value.EventVersion, MesIntegrationEventVersions.V1);
        JsonSerializer.Serialize(writer, new SettledV1Dto(value.EventId, value.EventType, value.EventVersion,
            value.OccurredAtUtc, value.SourceService, value.CorrelationId, value.CausationId,
            value.OrganizationId, value.EnvironmentId, value.Actor, value.IdempotencyKey, value.Payload), options);
    }

    private sealed record SettledV1Dto(string EventId, string EventType, int EventVersion,
        DateTimeOffset OccurredAtUtc, string SourceService, string CorrelationId, string CausationId,
        string OrganizationId, string EnvironmentId, string Actor, string IdempotencyKey,
        OperationActualTimeSettledPayload Payload);
}

public sealed class MesOperationActualTimeSettledV2IntegrationEventJsonConverter
    : JsonConverter<MesOperationActualTimeSettledV2IntegrationEvent>
{
    public override MesOperationActualTimeSettledV2IntegrationEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dto = MesActualTimeWireContract.ReadV2<SettledV2Dto>(ref reader, options);
        MesActualTimeContractInvariant.Validate(dto.Payload.DeviceAssetId, dto.Payload.MachineTimeStatus,
            dto.Payload.BillableMachineTicks, dto.Payload.MachineTimeBasisCode);
        return new(dto.EventId, dto.EventType, dto.EventVersion, dto.OccurredAtUtc, dto.SourceService,
            dto.CorrelationId, dto.CausationId, dto.OrganizationId, dto.EnvironmentId, dto.Actor,
            dto.IdempotencyKey, dto.Payload);
    }

    public override void Write(Utf8JsonWriter writer, MesOperationActualTimeSettledV2IntegrationEvent value, JsonSerializerOptions options)
    {
        MesActualTimeWireContract.RequireVersion(value.EventVersion, MesIntegrationEventVersions.V2);
        MesActualTimeContractInvariant.Validate(value.Payload.DeviceAssetId, value.Payload.MachineTimeStatus,
            value.Payload.BillableMachineTicks, value.Payload.MachineTimeBasisCode);
        JsonSerializer.Serialize(writer, new SettledV2Dto(value.EventId, value.EventType, value.EventVersion,
            value.OccurredAtUtc, value.SourceService, value.CorrelationId, value.CausationId,
            value.OrganizationId, value.EnvironmentId, value.Actor, value.IdempotencyKey, value.Payload), options);
    }

    private sealed record SettledV2Dto(string EventId, string EventType, int EventVersion,
        DateTimeOffset OccurredAtUtc, string SourceService, string CorrelationId, string CausationId,
        string OrganizationId, string EnvironmentId, string Actor, string IdempotencyKey,
        OperationActualTimeSettledV2Payload Payload);
}

public sealed class MesOperationActualTimeSettlementVoidedV1IntegrationEventJsonConverter
    : JsonConverter<MesOperationActualTimeSettlementVoidedIntegrationEvent>
{
    public override MesOperationActualTimeSettlementVoidedIntegrationEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dto = MesActualTimeWireContract.ReadV1<VoidedV1Dto>(ref reader, options);
        return new(dto.EventId, dto.EventType, dto.EventVersion, dto.OccurredAtUtc, dto.SourceService,
            dto.CorrelationId, dto.CausationId, dto.OrganizationId, dto.EnvironmentId, dto.Actor,
            dto.IdempotencyKey, dto.Payload);
    }

    public override void Write(Utf8JsonWriter writer, MesOperationActualTimeSettlementVoidedIntegrationEvent value, JsonSerializerOptions options)
    {
        MesActualTimeWireContract.RequireVersion(value.EventVersion, MesIntegrationEventVersions.V1);
        JsonSerializer.Serialize(writer, new VoidedV1Dto(value.EventId, value.EventType, value.EventVersion,
            value.OccurredAtUtc, value.SourceService, value.CorrelationId, value.CausationId,
            value.OrganizationId, value.EnvironmentId, value.Actor, value.IdempotencyKey, value.Payload), options);
    }

    private sealed record VoidedV1Dto(string EventId, string EventType, int EventVersion,
        DateTimeOffset OccurredAtUtc, string SourceService, string CorrelationId, string CausationId,
        string OrganizationId, string EnvironmentId, string Actor, string IdempotencyKey,
        OperationActualTimeSettlementVoidedPayload Payload);
}

public sealed class MesOperationActualTimeSettlementVoidedV2IntegrationEventJsonConverter
    : JsonConverter<MesOperationActualTimeSettlementVoidedV2IntegrationEvent>
{
    public override MesOperationActualTimeSettlementVoidedV2IntegrationEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dto = MesActualTimeWireContract.ReadV2<VoidedV2Dto>(ref reader, options);
        MesActualTimeContractInvariant.Validate(dto.Payload.DeviceAssetId, dto.Payload.MachineTimeStatus,
            dto.Payload.BillableMachineTicks, dto.Payload.MachineTimeBasisCode);
        return new(dto.EventId, dto.EventType, dto.EventVersion, dto.OccurredAtUtc, dto.SourceService,
            dto.CorrelationId, dto.CausationId, dto.OrganizationId, dto.EnvironmentId, dto.Actor,
            dto.IdempotencyKey, dto.Payload);
    }

    public override void Write(Utf8JsonWriter writer, MesOperationActualTimeSettlementVoidedV2IntegrationEvent value, JsonSerializerOptions options)
    {
        MesActualTimeWireContract.RequireVersion(value.EventVersion, MesIntegrationEventVersions.V2);
        MesActualTimeContractInvariant.Validate(value.Payload.DeviceAssetId, value.Payload.MachineTimeStatus,
            value.Payload.BillableMachineTicks, value.Payload.MachineTimeBasisCode);
        JsonSerializer.Serialize(writer, new VoidedV2Dto(value.EventId, value.EventType, value.EventVersion,
            value.OccurredAtUtc, value.SourceService, value.CorrelationId, value.CausationId,
            value.OrganizationId, value.EnvironmentId, value.Actor, value.IdempotencyKey, value.Payload), options);
    }

    private sealed record VoidedV2Dto(string EventId, string EventType, int EventVersion,
        DateTimeOffset OccurredAtUtc, string SourceService, string CorrelationId, string CausationId,
        string OrganizationId, string EnvironmentId, string Actor, string IdempotencyKey,
        OperationActualTimeSettlementVoidedV2Payload Payload);
}

public sealed record MesOperationTaskManuallyDispatchedIntegrationEvent(
    string EventId, string EventType, int EventVersion, DateTimeOffset OccurredAtUtc,
    string SourceService, string CorrelationId, string CausationId,
    string OrganizationId, string EnvironmentId, string Actor, string IdempotencyKey,
    OperationTaskManuallyDispatchedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record OperationTaskManuallyDispatchedPayload(
    string WorkOrderId, string OperationTaskId, int OperationSequence,
    string ResourceId, string WorkCenterId, DateTimeOffset StartUtc,
    DateTimeOffset EndUtc, DateTimeOffset AssignedAtUtc,
    long DispatchRevision = 0);

public sealed record MesOperationTaskManualDispatchClearedIntegrationEvent(
    string EventId, string EventType, int EventVersion, DateTimeOffset OccurredAtUtc,
    string SourceService, string CorrelationId, string CausationId,
    string OrganizationId, string EnvironmentId, string Actor, string IdempotencyKey,
    OperationTaskManualDispatchClearedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record OperationTaskManualDispatchClearedPayload(
    string WorkOrderId, string OperationTaskId, int OperationSequence,
    string ResourceId, string WorkCenterId, DateTimeOffset StartUtc,
    DateTimeOffset EndUtc, long DispatchRevision,
    string ReasonCode, DateTimeOffset ClearedAtUtc);

public sealed record ProductionReportRecordedIntegrationEvent(
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
    ProductionReportRecordedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record ProductionReportRecordedPayload(
    string ReportNo,
    string WorkOrderId,
    string OperationTaskId,
    string WorkCenterId,
    string? DeviceAssetId,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    decimal ReworkQuantity,
    string UomCode,
    decimal? TheoreticalRatePerHour,
    DateTimeOffset ReportedAtUtc,
    bool IsReversal,
    string? ReversedReportNo = null,
    int MaterialMovementCount = 0,
    string? SiteCode = null,
    string? WorkshopCode = null,
    string? LineCode = null,
    string? ShiftCode = null,
    string? SiteTimezone = null,
    TimeOnly? ShiftStartsAt = null,
    TimeOnly? ShiftEndsAt = null,
    bool? ShiftCrossesMidnight = null,
    int? ShiftPaidMinutes = null,
    int? ShiftBreakMinutes = null);

public sealed record FinishedGoodsReceiptRequestedIntegrationEvent(
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
    FinishedGoodsReceiptRequestedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record FinishedGoodsReceiptRequestedPayload(
    string RequestNo,
    string WorkOrderId,
    string SkuCode,
    decimal Quantity,
    string UomCode,
    string? ProducedLotNo,
    string? SerialNo,
    DateTimeOffset RequestedAtUtc);

public sealed record MesMaterialIssueRequestedIntegrationEvent(
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
    MesMaterialIssueRequestedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record MesMaterialIssueRequestedPayload(
    string RequestNo,
    string WorkOrderId,
    string? OperationTaskId,
    string MaterialId,
    string UomCode,
    decimal RequestedQuantity,
    DateTimeOffset RequestedAtUtc,
    string? SiteCode = null,
    string? SourceLocationCode = null,
    string? LineSideLocationCode = null);
