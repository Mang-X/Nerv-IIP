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

    public static T Read<T>(ref Utf8JsonReader reader, JsonSerializerOptions options, int expectedVersion, bool allowMachineFacts)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (!TryGetProperty(root, "eventVersion", "EventVersion", out var versionElement)
            || versionElement.ValueKind != JsonValueKind.Number
            || versionElement.GetInt32() != expectedVersion)
            throw new JsonException($"MES actual-time envelope requires eventVersion {expectedVersion}.");
        if (!TryGetProperty(root, "payload", "Payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            throw new JsonException("MES actual-time payload is required.");
        if (!allowMachineFacts)
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
        var dto = MesActualTimeWireContract.Read<SettledV1Dto>(ref reader, options, MesIntegrationEventVersions.V1, false);
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
        var dto = MesActualTimeWireContract.Read<SettledV2Dto>(ref reader, options, MesIntegrationEventVersions.V2, true);
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
        var dto = MesActualTimeWireContract.Read<VoidedV1Dto>(ref reader, options, MesIntegrationEventVersions.V1, false);
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
        var dto = MesActualTimeWireContract.Read<VoidedV2Dto>(ref reader, options, MesIntegrationEventVersions.V2, true);
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
