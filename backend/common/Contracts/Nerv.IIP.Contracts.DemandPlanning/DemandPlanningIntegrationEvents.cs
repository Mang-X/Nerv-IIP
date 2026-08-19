using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.DemandPlanning;

public static class DemandPlanningIntegrationEventTypes
{
    public const string MrpRunCompleted = "demandPlanning.MrpRunCompleted";
    public const string PlannedPurchaseSuggested = "demandPlanning.PlannedPurchaseSuggested";
    public const string PlannedWorkOrderSuggested = "demandPlanning.PlannedWorkOrderSuggested";
    public const string PlanningSuggestionAccepted = "demandPlanning.PlanningSuggestionAccepted";
}

public static class DemandPlanningIntegrationEventVersions
{
    public const int V1 = 1;
}

public static class DemandPlanningIntegrationEventSources
{
    public const string BusinessDemandPlanning = "business-demand-planning";
}

public static class DemandPlanningSuggestionTypes
{
    public const string PlannedPurchase = "planned-purchase";
    public const string PlannedWorkOrder = "planned-work-order";
}

public static class DemandPlanningDownstreamReferences
{
    public const string BusinessErp = "BusinessErp";
    public const string PurchaseRequisition = "PurchaseRequisition";

    /// <summary>
    /// 已接受建议的**下游服务引用**（PascalCase）。这是 DP 接受面的对外口径：
    /// 网关下游服务表、前端排产工作台的精确等值匹配与 <c>accepted_downstream_service</c> 落库列都用这个取值，
    /// 与事件信封来源面的 <c>Nerv.IIP.Contracts.Quality.QualityIntegrationEventSources.BusinessMes</c>
    /// （<c>"business-mes"</c>，短横线小写）是**两个面**：一个是「谁接单」，一个是「谁发的事件」，
    /// 取值不同、不可互相引用（#1370 ③ 批次 D 裁决：两面并存，改的是种子不是契约取值）。
    /// </summary>
    public const string BusinessMes = "BusinessMes";

    /// <summary>已接受建议的下游单据类型（MES 工单），与 <see cref="BusinessMes"/> 同一口径面。</summary>
    public const string WorkOrder = "WorkOrder";
}

public static class DemandPlanningSourceReferences
{
    public const string DemandPlanning = "DemandPlanning";
    public const string PlanningSuggestion = "PlanningSuggestion";
}

public static class PlanningSuggestionAcceptedIntegrationEventTopic
{
    public const string TopicName = "PlanningSuggestionAcceptedIntegrationEvent";
}

public sealed record DemandPlanningIntegrationEvent<TPayload>(
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
    TPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record PlanningSuggestionAcceptedIntegrationEvent(
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
    PlanningSuggestionAcceptedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record MrpRunCompletedPayload(
    string MrpRunId,
    DateOnly HorizonStart,
    DateOnly HorizonEnd,
    int DemandCount,
    int AvailabilityCount,
    int SuggestionCount,
    string ProductionEngineeringSnapshotSource,
    string InventorySnapshotSource);

public sealed record PlanningSuggestionPayload(
    string SuggestionId,
    string MrpRunId,
    string SuggestionType,
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal Quantity,
    DateOnly RequiredDate,
    DateOnly ReleaseDate,
    IReadOnlyCollection<PlanningSuggestionPeggingPayload> Pegging);

public sealed record PlanningSuggestionPeggingPayload(
    string DemandSourceReference,
    string ParentSkuCode,
    string? ComponentSkuCode,
    decimal Quantity,
    string? ProductionVersionReference,
    string? ManufacturingBomReference,
    string? RoutingReference);

public sealed record PlanningSuggestionAcceptedPayload(
    string SuggestionId,
    string MrpRunId,
    string SuggestionType,
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal Quantity,
    DateOnly RequiredDate,
    DateOnly ReleaseDate,
    string? DemandSourceReference,
    string? ProductionVersionReference,
    string DownstreamService,
    string DownstreamDocumentType,
    string? DownstreamDocumentId,
    IReadOnlyCollection<string>? DemandSourceReferences = null);
