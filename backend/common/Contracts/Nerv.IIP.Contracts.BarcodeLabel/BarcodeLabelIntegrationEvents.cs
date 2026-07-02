using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.BarcodeLabel;

public static class BarcodeLabelIntegrationEventTypes
{
    public const string BarcodeScanAccepted = "barcode.BarcodeScanAccepted";
}

public static class BarcodeLabelIntegrationEventVersions
{
    public const int V1 = 1;
}

public static class BarcodeLabelIntegrationEventSources
{
    public const string BusinessBarcodeLabel = "business-barcode-label";
}

public sealed record BarcodeScanAcceptedIntegrationEvent(
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
    BarcodeScanAcceptedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record BarcodeScanAcceptedPayload(
    string ScanRecordId,
    string DeviceCode,
    string ScannedValue,
    string SourceWorkflow,
    string SourceDocumentId,
    string? Gtin,
    string? LotNo,
    string? SerialNumber,
    decimal? Quantity,
    DateTimeOffset ScannedAtUtc);
