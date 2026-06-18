namespace Nerv.IIP.Business.DemandPlanning.Web.Application.IntegrationEvents;

public static class DemandPlanningIntegrationEventTypes
{
    public const string MrpRunCompleted = "demandPlanning.MrpRunCompleted";
    public const string PlannedPurchaseSuggested = "demandPlanning.PlannedPurchaseSuggested";
    public const string PlannedWorkOrderSuggested = "demandPlanning.PlannedWorkOrderSuggested";
    public const string PlanningSuggestionAccepted = "demandPlanning.PlanningSuggestionAccepted";
}

public static class DemandPlanningIntegrationEventSources
{
    public const string BusinessDemandPlanning = "business-demand-planning";
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
    TPayload Payload);

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
    string DownstreamService,
    string DownstreamDocumentType,
    string DownstreamDocumentId);
