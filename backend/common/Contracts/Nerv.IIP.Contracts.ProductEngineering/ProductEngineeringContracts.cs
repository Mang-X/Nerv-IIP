namespace Nerv.IIP.Contracts.ProductEngineering;

public static class ProductionEngineeringContractStatuses
{
    public const string Active = "active";
    public const string Archived = "archived";
}

public sealed record ResolveProductionVersionRequest(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    DateOnly EffectiveDate,
    decimal LotSize);

public sealed record ResolveProductionVersionResponse(
    string ProductionVersionId,
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string MbomVersionId,
    string RoutingVersionId,
    DateOnly EffectiveDate,
    decimal LotSize,
    string Status);

public sealed record ProductionVersionListItem(
    string ProductionVersionId,
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string MbomVersionId,
    string RoutingVersionId,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    decimal? LotSizeMin,
    decimal? LotSizeMax,
    int Priority,
    bool IsDefault,
    string Status);

public sealed record ListProductionVersionsResponse(IReadOnlyCollection<ProductionVersionListItem> Items, int Total);

public static class ProductEngineeringIntegrationEventTypes
{
    public const string BomReleased = "productEngineering.BomReleased";
    public const string RoutingReleased = "productEngineering.RoutingReleased";
    public const string ProductionVersionCreated = "productEngineering.ProductionVersionCreated";
    public const string EngineeringChangeReleased = "productEngineering.EngineeringChangeReleased";
}

public static class ProductEngineeringIntegrationEventVersions
{
    public const int V1 = 1;
}

public static class ProductEngineeringIntegrationEventSources
{
    public const string BusinessProductEngineering = "business-product-engineering";
}

public sealed record BomReleasedIntegrationEvent(
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
    BomReleasedPayload Payload);

public sealed record RoutingReleasedIntegrationEvent(
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
    RoutingReleasedPayload Payload);

public sealed record ProductionVersionCreatedIntegrationEvent(
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
    ProductionVersionCreatedPayload Payload);

public sealed record EngineeringChangeReleasedIntegrationEvent(
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
    EngineeringChangeReleasedPayload Payload);

public sealed record BomReleasedPayload(
    string BomVersionId,
    string BomType,
    string ItemOrSkuCode,
    IReadOnlyCollection<BomReleasedLine> Lines,
    DateOnly EffectiveDate);

public sealed record BomReleasedLine(
    string ComponentCode,
    decimal Quantity,
    string UnitOfMeasureCode);

public sealed record RoutingReleasedPayload(
    string RoutingVersionId,
    string SkuCode,
    IReadOnlyCollection<RoutingReleasedOperation> Operations,
    DateOnly EffectiveDate);

public sealed record RoutingReleasedOperation(
    int Sequence,
    string WorkCenterCode,
    string OperationName,
    int StandardMinutes);

public sealed record ProductionVersionCreatedPayload(
    string ProductionVersionId,
    string SkuCode,
    string MbomVersionId,
    string RoutingVersionId,
    DateOnly ValidFrom,
    DateOnly? ValidTo);

public sealed record EngineeringChangeReleasedPayload(
    string ChangeId,
    string ChangeNumber,
    IReadOnlyCollection<string> AffectedVersionIds,
    DateOnly EffectiveDate);
