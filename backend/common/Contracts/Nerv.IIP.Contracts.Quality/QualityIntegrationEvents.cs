using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.Quality;

public static class QualityIntegrationEventTypes
{
    public const string InspectionPassed = "quality.InspectionPassed";
    public const string InspectionConditionalReleased = "quality.InspectionConditionalReleased";
    public const string InspectionRejected = "quality.InspectionRejected";
    public const string DefectRaised = "quality.DefectRaised";
    public const string NcrOpened = "quality.NcrOpened";
    public const string NcrReworkRequested = "quality.NcrReworkRequested";
    public const string DispositionDecided = "quality.DispositionDecided";
    public const string NcrClosed = "quality.NcrClosed";
    public const string InspectionTaskOverdue = "quality.InspectionTaskOverdue";
    public const string CapaOpened = "quality.CapaOpened";
    public const string CapaEffectivenessVerified = "quality.CapaEffectivenessVerified";
    public const string CapaClosed = "quality.CapaClosed";
    public const string SpcAlertRaised = "quality.SpcAlertRaised";
    public const string MeasuringDeviceCalibrationDue = "quality.MeasuringDeviceCalibrationDue";
}

public static class QualityIntegrationEventVersions
{
    public const int V1 = 1;
}

public static class QualityIntegrationEventSources
{
    public const string BusinessQuality = "business-quality";

    /// <summary>
    /// MES 侧**事件信封来源**服务短名（短横线小写），与可观测性服务名、CAP 消费者名同一口径面；
    /// 与 DP 接受面的 <c>Nerv.IIP.Contracts.DemandPlanning.DemandPlanningDownstreamReferences.BusinessMes</c>
    /// （<c>"BusinessMes"</c>，PascalCase）是**两个面**，取值不同、不可互相引用
    /// （#1370 ③ 批次 D 裁决：两面并存，各自面内保持单一取值）。
    /// </summary>
    public const string BusinessMes = "business-mes";
}

/// <summary>
/// 检验来源环节词表。取值域由 <c>InspectionRecord.SourceTypes</c> 锁死，而
/// <c>InspectionResultIntegrationEvent</c> 的 <c>payload.SourceType</c> 直接取自
/// <c>record.SourceType</c>，所以这里就是跨服务消费者按来源环节分流时的**唯一**取值来源；
/// 消费侧不得再写裸字面量（#2976）。
///
/// <c>Wms</c> 是来源**服务**取值，不是来源环节，历史上落在本类里；WMS/ERP 两个消费者用它匹配
/// <c>payload.SourceService</c>。保留原位不动，但由 <c>QualityInspectionSourceTypeContractTests</c>
/// 明写为「服务轴遗留项」，避免后来人把它当成第七个来源环节。
/// </summary>
public static class QualityInspectionSourceTypes
{
    public const string Wms = "wms";
    public const string Receiving = "receiving";
    public const string Operation = "operation";
    public const string Final = "final";
    public const string FirstArticle = "first-article";
    public const string Maintenance = "maintenance";
    public const string CustomerReturn = "customer-return";

    /// <summary>六个来源环节取值（不含服务轴的 <see cref="Wms"/>）。</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Receiving,
        Operation,
        Final,
        FirstArticle,
        Maintenance,
        CustomerReturn,
    ];
}

/// <summary>
/// 检验来源**服务**词表。取值域由 <c>InspectionRecord.SourceServices</c> 锁死，而
/// <c>InspectionResultIntegrationEvent</c> 的 <c>payload.SourceService</c> 直接取自
/// <c>record.SourceService</c>，所以这里就是跨服务消费者按来源服务分流时的**唯一**取值来源
/// （#3191，姿势同 #2976 对来源环节轴做过的那次）。
///
/// 注意与 <see cref="QualityIntegrationEventSources"/> 的区别：后者是**事件信封**来源面
/// （<c>"business-quality"</c> / <c>"business-mes"</c>），本类是 **payload 来源面**，两个轴取值不相交。
/// 拿信封面常量去比 payload 面取值必然恒不相等——#3191 的缺陷就是这么来的。
/// </summary>
public static class QualityInspectionSourceServices
{
    public const string Inventory = "inventory";
    public const string Wms = "wms";
    public const string Mes = "mes";
    public const string Erp = "erp";
    public const string Maintenance = "maintenance";
    public const string PurchaseReceipt = "purchase-receipt";
    public const string MesOperation = "mes-operation";
    public const string CustomerReturn = "customer-return";

    /// <summary>八个来源服务取值。</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Inventory,
        Wms,
        Mes,
        Erp,
        Maintenance,
        PurchaseReceipt,
        MesOperation,
        CustomerReturn,
    ];

    /// <summary>
    /// 检验对象归属 MES 工单/工序的两个取值。MES 与 Scheduling 的入站门都引这一份，
    /// 不再各写一份字面量。<c>QualityIntegrationEventSources.BusinessMes</c> 是历史入站别名，
    /// 不属于本词表，只在 MES 入站侧额外接受（#1370 ③ 批次 D）。
    /// </summary>
    public static readonly IReadOnlyList<string> MesOwned = [Mes, MesOperation];
}

public static class QualityStockReleaseTargetStatuses
{
    public const string Unrestricted = "unrestricted";
    public const string Restricted = "restricted";
    public const string Blocked = "blocked";
}

public static class QualityNcrDispositionTypes
{
    public const string Rework = "rework";
    public const string Scrap = "scrap";
    public const string ReturnToSupplier = "return-to-supplier";
    public const string ConditionalRelease = "conditional-release";
    public const string SortAndScreen = "sort-and-screen";
}

public static class QualitySpcRuleCodes
{
    public const string BeyondControlLimit = "beyond-control-limit";
    public const string ConsecutiveShiftAboveCenter = "consecutive-shift-above-center";
    public const string ConsecutiveShiftBelowCenter = "consecutive-shift-below-center";
    public const string TrendIncreasing = "trend-increasing";
    public const string TrendDecreasing = "trend-decreasing";
}

public sealed record DefectRaisedIntegrationEvent(
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
    DefectRaisedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record DefectRaisedPayload(
    string DefectNo,
    string WorkOrderId,
    string? OperationTaskId,
    string DefectCode,
    decimal Quantity,
    DateTimeOffset RecordedAtUtc);

public sealed record NcrOpenedIntegrationEvent(
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
    NcrOpenedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record NcrDispositionDecidedIntegrationEvent(
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
    NcrDispositionDecidedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record NcrReworkRequestedIntegrationEvent(
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
    NcrReworkRequestedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record NcrClosedIntegrationEvent(
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
    NcrClosedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record CapaOpenedIntegrationEvent(
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
    CapaOpenedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record CapaEffectivenessVerifiedIntegrationEvent(
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
    CapaEffectivenessVerifiedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record CapaClosedIntegrationEvent(
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
    CapaClosedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record InspectionResultIntegrationEvent(
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
    InspectionResultPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record InspectionTaskOverdueIntegrationEvent(
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
    InspectionTaskOverduePayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record MeasuringDeviceCalibrationDueIntegrationEvent(
    string EventId, string EventType, int EventVersion, DateTimeOffset OccurredAtUtc,
    string SourceService, string CorrelationId, string CausationId, string OrganizationId,
    string EnvironmentId, string Actor, string IdempotencyKey, MeasuringDeviceCalibrationDuePayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record MeasuringDeviceCalibrationDuePayload(
    string MeasuringDeviceId, string DeviceCode, string DeviceType, string CalibrationState,
    DateTimeOffset CalibrationDueAtUtc, DateTimeOffset EvaluatedAtUtc);

public sealed record SpcAlertRaisedIntegrationEvent(
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
    SpcAlertRaisedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record InspectionTaskOverduePayload(
    string InspectionTaskId,
    string SourceType,
    string SourceService,
    string SourceDocumentId,
    string? SourceDocumentLineId,
    string SkuCode,
    DateTimeOffset DueAtUtc,
    DateTimeOffset RemindedAtUtc);

public sealed record SpcAlertRaisedPayload(
    string AlertKey,
    string ResourceType,
    string SkuCode,
    string CharacteristicCode,
    string WorkCenterId,
    IReadOnlyCollection<string> RuleCodes,
    string Severity,
    DateTimeOffset LatestMeasuredAtUtc,
    string Summary);

public sealed record InspectionResultPayload(
    string InspectionRecordId,
    string? InspectionPlanId,
    string SourceType,
    string SourceService,
    string SourceDocumentId,
    string SkuCode,
    decimal InspectedQuantity,
    string Result,
    string? DispositionReason,
    IReadOnlyCollection<string> DispositionAttachmentFileIds,
    DateTimeOffset RecordedAtUtc,
    StockReleaseDimensionPayload? StockRelease = null,
    IReadOnlyCollection<InspectionResultLinePayload>? ResultLines = null,
    string? LotNo = null,
    string? SerialNo = null,
    string? SiteCode = null,
    string? LocationCode = null,
    string? OwnerType = null,
    string? OwnerId = null,
    string? UomCode = null,
    /// <summary>
    /// 检验对象所属 MES 工单公开 id；非 MES 归属的检验（收货检、终检、维修检、退货检）为 null。
    /// 由 Quality 侧解出并结构化发布，消费者不得再自行拆 <see cref="SourceDocumentId"/> 复合串（#3191）。
    /// </summary>
    string? WorkOrderId = null,
    /// <summary>
    /// 检验对象所属 MES 工序任务公开 id；来源为工单级检验或非 MES 归属检验时为 null。
    /// </summary>
    string? OperationTaskId = null);

public sealed record StockReleaseDimensionPayload(
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string SourceQualityStatus,
    string OwnerType,
    string? OwnerId,
    string? TargetQualityStatus = null);

public sealed record InspectionResultLinePayload(
    string CharacteristicCode,
    decimal? MeasuredValue,
    string? ObservedText,
    string? UnitCode,
    string Result,
    string? DefectReason,
    decimal? DefectQuantity);

public sealed record NcrOpenedPayload(
    string NcrId,
    string NcrCode,
    string SourceType,
    string SourceDocumentId,
    string SkuCode,
    decimal DefectQuantity,
    string DefectReason,
    string? BatchNo,
    string? SerialNo,
    string Status,
    DateTimeOffset OpenedAtUtc);

public sealed record NcrDispositionDecidedPayload(
    string NcrId,
    string NcrCode,
    string SkuCode,
    decimal DefectQuantity,
    string DispositionType,
    string? DispositionApprovalChainId,
    string? ReworkWorkOrderId,
    string? ScrapMovementId,
    string? ReturnDocumentId,
    DateTimeOffset ChangedAtUtc,
    IReadOnlyCollection<MrbReviewPayload>? MrbReviews = null)
{
    public string? SourceDocumentId { get; init; }
    public string? LotNo { get; init; }
    public string? SerialNo { get; init; }
    public string? UomCode { get; init; }
    public string? SiteCode { get; init; }
    public string? LocationCode { get; init; }
    public string? OwnerType { get; init; }
    public string? OwnerId { get; init; }
}

public sealed record NcrReworkRequestedPayload(
    string NcrId,
    string NcrCode,
    string SourceDefectNo,
    string SkuCode,
    decimal Quantity,
    string? LotNo,
    string? SerialNo,
    DateTimeOffset RequestedAtUtc)
{
    public DateTimeOffset RequestedAtUtc { get; } = new(
        RequestedAtUtc.UtcTicks - (RequestedAtUtc.UtcTicks % TimeSpan.TicksPerMicrosecond),
        TimeSpan.Zero);
}

public sealed record MrbReviewPayload(
    string ReviewerId,
    string Decision,
    string? Comment,
    DateTimeOffset ReviewedAtUtc);

public sealed record NcrClosedPayload(
    string NcrId,
    string NcrCode,
    string SkuCode,
    decimal DefectQuantity,
    string DispositionType,
    string? ReworkWorkOrderId,
    string? ScrapMovementId,
    string? ReturnDocumentId,
    string Reason,
    DateTimeOffset ClosedAtUtc);

public sealed record CapaOpenedPayload(
    string CorrectiveActionId,
    string CapaCode,
    string? SourceNcrId,
    string OwnerUserId,
    string Status,
    DateTimeOffset DueAtUtc,
    DateTimeOffset OpenedAtUtc);

public sealed record CapaEffectivenessVerifiedPayload(
    string CorrectiveActionId,
    string CapaCode,
    string? SourceNcrId,
    string VerificationInspectionRecordId,
    string VerifiedByUserId,
    string Result,
    DateTimeOffset VerifiedAtUtc);

public sealed record CapaClosedPayload(
    string CorrectiveActionId,
    string CapaCode,
    string? SourceNcrId,
    string? CloseApprovalChainId,
    string ClosedByUserId,
    DateTimeOffset ClosedAtUtc);
