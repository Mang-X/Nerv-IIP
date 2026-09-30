using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Contracts.DemandPlanning;

public enum MaterialDeliveryStatus { Yellow, Green, Red }
public sealed record MaterialDeliveriesResponse(string RunId, string? PlanId, DateTimeOffset EvaluatedAtUtc,
    string SupplyCoverageScope, IReadOnlyCollection<MaterialDeliveryResponse> Items,
    IReadOnlyCollection<MaterialDeliveryUnknownRequirementSource> UnknownRequirementSuggestions);
public sealed record MaterialDeliveryUnknownRequirementSource(string Reason, string RunId, string SuggestionType,
    string SkuCode, string UomCode, string SiteCode, DateOnly RequiredDate, DateOnly ReleaseDate,
    MaterialDeliverySuggestionSource SuggestionSource, MaterialDeliveryNetRequirementSource RawNetRequirementSource,
    IReadOnlyCollection<MaterialDeliveryDemandSource> DemandSources);
public sealed record MaterialDeliveryDemandSource(string SourceReference, string? SourceLineReference,
    string SourceType, string ParentSkuCode, string? ComponentSkuCode, decimal GrossDemandQuantity,
    string? DemandSourceId, string? SourceDocumentId, int? SourceVersion, DateOnly? DueDate,
    string? ProductionVersionReference, string? ManufacturingBomReference, string? RoutingReference);
public sealed record MaterialDeliveryNetRequirementSource(decimal GrossDemandQuantity, decimal OnHandQuantity,
    decimal ReservedQuantity, decimal AvailableToNetQuantity, decimal ScheduledReceiptQuantity,
    decimal SafetyStockQuantity, decimal NetRequirementQuantity, decimal PlannedQuantity,
    decimal ScrapRate, decimal YieldRate, string Formula, string UomConversionSummary);
public sealed record MaterialDeliverySuggestionSource(string SuggestionId, string Status, decimal Quantity,
    decimal PlannedQuantity, string ReasonCode, string? DownstreamService, string? DownstreamDocumentType, string? DownstreamDocumentId);
public sealed record MaterialDeliveryResponse(string NetRequirementReference, string RunId, string SuggestionType,
    string SkuCode, string UomCode, string SiteCode, DateOnly RequiredDate, decimal NetRequirementQuantity,
    DateOnly LatestProcurementDate, DateTimeOffset LatestProcurementUtc, DateOnly? ExpectedArrivalDate,
    DateTimeOffset? ExpectedArrivalUtc, DateTimeOffset? ExpectedStartUtc, DateTimeOffset? LatestStartUtc,
    decimal CoveredQuantity, decimal UncoveredQuantity, MaterialDeliveryStatus Status, IReadOnlyCollection<string> Reasons,
    MaterialDeliveryNetRequirementSource NetRequirementSource, IReadOnlyCollection<MaterialDeliveryDemandSource> DemandSources,
    IReadOnlyCollection<MaterialDeliverySupplySource> SupplySources,
    IReadOnlyCollection<MaterialDeliveryOrderSourceContract> SchedulingSources,
    IReadOnlyCollection<MaterialDeliverySuggestionSource> SuggestionSources);

public sealed record MaterialDeliveryPurchaseSource(string PurchaseRequisitionNo, string PurchaseRequisitionLineNo, decimal Quantity, string? SuggestionId);
public sealed record MaterialDeliverySupplySource(string PurchaseOrderNo, string LineNo, string SiteCode, string SkuCode,
    string UomCode, DateOnly PromisedDate, decimal OpenQuantity, IReadOnlyCollection<MaterialDeliveryPurchaseSource> Sources);
