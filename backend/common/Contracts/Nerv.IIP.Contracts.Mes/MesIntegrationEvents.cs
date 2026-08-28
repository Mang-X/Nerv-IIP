using System.Text.Json;
using System.Text.Json.Serialization;
using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.Mes;

public static class MesIntegrationEventTypes
{
    public const string WorkOrderReleased = "mes.WorkOrderReleased";
    public const string WorkOrderCompleted = "mes.WorkOrderCompleted";
    public const string WorkOrderClosed = "mes.WorkOrderClosed";
    public const string WorkOrderEngineeringChangeImpactDetected = "mes.WorkOrderEngineeringChangeImpactDetected";
    public const string OperationTaskCompleted = "mes.OperationTaskCompleted";
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

public sealed record ReleasedOperationPayload(
    string OperationId,
    int OperationSequence,
    string WorkCenterId);

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

[JsonConverter(typeof(MesOperationActualTimeSettledIntegrationEventJsonConverter))]
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
    IReadOnlyCollection<string> CoveredProductionReportNos,
    string? DeviceAssetId = null,
    [property: JsonConverter(typeof(MesMachineTimeFactStatusJsonConverter))]
    MesMachineTimeFactStatus? MachineTimeStatus = null,
    long? BillableMachineTicks = null,
    string? MachineTimeBasisCode = null);

[JsonConverter(typeof(MesOperationActualTimeSettlementVoidedIntegrationEventJsonConverter))]
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
    IReadOnlyCollection<string> CoveredProductionReportNos,
    string? DeviceAssetId = null,
    [property: JsonConverter(typeof(MesMachineTimeFactStatusJsonConverter))]
    MesMachineTimeFactStatus? MachineTimeStatus = null,
    long? BillableMachineTicks = null,
    string? MachineTimeBasisCode = null);

internal static class MesActualTimeContractInvariant
{
    public static void Validate(
        int eventVersion,
        string? deviceAssetId,
        MesMachineTimeFactStatus? status,
        long? billableMachineTicks,
        string? basisCode)
    {
        if (eventVersion == MesIntegrationEventVersions.V1)
        {
            if (deviceAssetId is not null || status is not null || billableMachineTicks is not null || basisCode is not null)
                throw new JsonException("MES actual-time V1 must not contain V2 machine-time facts.");
            return;
        }

        if (eventVersion != MesIntegrationEventVersions.V2 || status is null)
            throw new JsonException("MES actual-time V2 requires a complete machine-time fact.");

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

public sealed class MesOperationActualTimeSettledIntegrationEventJsonConverter
    : JsonConverter<MesOperationActualTimeSettledIntegrationEvent>
{
    public override MesOperationActualTimeSettledIntegrationEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dto = JsonSerializer.Deserialize<SettledDto>(ref reader, options)
            ?? throw new JsonException("MES actual-time settled envelope is required.");
        MesActualTimeContractInvariant.Validate(dto.EventVersion, dto.Payload.DeviceAssetId,
            dto.Payload.MachineTimeStatus, dto.Payload.BillableMachineTicks, dto.Payload.MachineTimeBasisCode);
        return new(dto.EventId, dto.EventType, dto.EventVersion, dto.OccurredAtUtc, dto.SourceService,
            dto.CorrelationId, dto.CausationId, dto.OrganizationId, dto.EnvironmentId, dto.Actor,
            dto.IdempotencyKey, dto.Payload);
    }

    public override void Write(Utf8JsonWriter writer, MesOperationActualTimeSettledIntegrationEvent value, JsonSerializerOptions options)
    {
        MesActualTimeContractInvariant.Validate(value.EventVersion, value.Payload.DeviceAssetId,
            value.Payload.MachineTimeStatus, value.Payload.BillableMachineTicks, value.Payload.MachineTimeBasisCode);
        JsonSerializer.Serialize(writer, new SettledDto(value.EventId, value.EventType, value.EventVersion,
            value.OccurredAtUtc, value.SourceService, value.CorrelationId, value.CausationId,
            value.OrganizationId, value.EnvironmentId, value.Actor, value.IdempotencyKey, value.Payload), options);
    }

    private sealed record SettledDto(string EventId, string EventType, int EventVersion,
        DateTimeOffset OccurredAtUtc, string SourceService, string CorrelationId, string CausationId,
        string OrganizationId, string EnvironmentId, string Actor, string IdempotencyKey,
        OperationActualTimeSettledPayload Payload);
}

public sealed class MesOperationActualTimeSettlementVoidedIntegrationEventJsonConverter
    : JsonConverter<MesOperationActualTimeSettlementVoidedIntegrationEvent>
{
    public override MesOperationActualTimeSettlementVoidedIntegrationEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dto = JsonSerializer.Deserialize<VoidedDto>(ref reader, options)
            ?? throw new JsonException("MES actual-time voided envelope is required.");
        MesActualTimeContractInvariant.Validate(dto.EventVersion, dto.Payload.DeviceAssetId,
            dto.Payload.MachineTimeStatus, dto.Payload.BillableMachineTicks, dto.Payload.MachineTimeBasisCode);
        return new(dto.EventId, dto.EventType, dto.EventVersion, dto.OccurredAtUtc, dto.SourceService,
            dto.CorrelationId, dto.CausationId, dto.OrganizationId, dto.EnvironmentId, dto.Actor,
            dto.IdempotencyKey, dto.Payload);
    }

    public override void Write(Utf8JsonWriter writer, MesOperationActualTimeSettlementVoidedIntegrationEvent value, JsonSerializerOptions options)
    {
        MesActualTimeContractInvariant.Validate(value.EventVersion, value.Payload.DeviceAssetId,
            value.Payload.MachineTimeStatus, value.Payload.BillableMachineTicks, value.Payload.MachineTimeBasisCode);
        JsonSerializer.Serialize(writer, new VoidedDto(value.EventId, value.EventType, value.EventVersion,
            value.OccurredAtUtc, value.SourceService, value.CorrelationId, value.CausationId,
            value.OrganizationId, value.EnvironmentId, value.Actor, value.IdempotencyKey, value.Payload), options);
    }

    private sealed record VoidedDto(string EventId, string EventType, int EventVersion,
        DateTimeOffset OccurredAtUtc, string SourceService, string CorrelationId, string CausationId,
        string OrganizationId, string EnvironmentId, string Actor, string IdempotencyKey,
        OperationActualTimeSettlementVoidedPayload Payload);
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
