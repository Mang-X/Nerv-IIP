using System.Reflection;
using System.Text.Json;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.Contracts.BarcodeLabel;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Contracts.ProductEngineering;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Contracts.Wms;

namespace Nerv.IIP.Contracts.IntegrationEvents.Tests;

public sealed class IntegrationEventEnvelopeContractTests
{
    private static readonly string[] RequiredEnvelopeProperties =
    [
        "EventId",
        "EventType",
        "EventVersion",
        "OccurredAtUtc",
        "SourceService",
        "CorrelationId",
        "CausationId",
        "OrganizationId",
        "EnvironmentId",
        "Actor",
        "IdempotencyKey",
        "Payload"
    ];

    private static readonly Assembly[] PublicContractAssemblies =
    [
        typeof(OperationTaskCompletedIntegrationEvent).Assembly,
        typeof(InventoryMovementRequestedIntegrationEvent).Assembly,
        typeof(AssetUnavailableIntegrationEvent).Assembly,
        typeof(DeviceStateChangedIntegrationEvent).Assembly,
        typeof(WmsIntegrationEvent).Assembly,
        typeof(SkuChangedIntegrationEvent).Assembly,
        typeof(BomReleasedIntegrationEvent).Assembly,
        typeof(NcrOpenedIntegrationEvent).Assembly,
        typeof(ApprovalStartedIntegrationEvent).Assembly,
        typeof(BarcodeScanAcceptedIntegrationEvent).Assembly,
        typeof(SchedulePlanReleasedIntegrationEvent).Assembly
    ];

    public static TheoryData<Type> IntegrationEventTypes()
    {
        var data = new TheoryData<Type>();

        foreach (var eventType in PublicContractAssemblies
            .SelectMany(assembly => assembly.ExportedTypes)
            .Where(type =>
                type.IsClass &&
                !type.IsAbstract &&
                !type.ContainsGenericParameters &&
                type.Name.EndsWith("IntegrationEvent", StringComparison.Ordinal))
            .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            data.Add(eventType);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(IntegrationEventTypes))]
    public void Integration_events_expose_required_adr_0011_envelope_properties(Type eventType)
    {
        var properties = eventType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(x => x.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = RequiredEnvelopeProperties
            .Where(property => !properties.Contains(property))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"{eventType.FullName} is missing ADR 0011 envelope properties: {string.Join(", ", missing)}");
    }

    [Theory]
    [MemberData(nameof(IntegrationEventTypes))]
    public void Integration_events_implement_compile_time_envelope_contract(Type eventType)
    {
        Assert.True(
            typeof(IIntegrationEventEnvelope).IsAssignableFrom(eventType),
            $"{eventType.FullName} must implement {nameof(IIntegrationEventEnvelope)} for consumer guard validation.");
    }

    [Theory]
    [MemberData(nameof(IntegrationEventTypes))]
    public void Integration_event_payload_is_a_dedicated_contract_type(Type eventType)
    {
        var payloadProperty = eventType.GetProperty("Payload", BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(payloadProperty);
        Assert.False(payloadProperty.PropertyType == typeof(string), $"{eventType.FullName} payload must be structured.");
        Assert.True(payloadProperty.PropertyType.Namespace?.StartsWith("Nerv.IIP.Contracts.", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Quality_inspection_result_payload_exposes_optional_stock_locator_dimensions_in_v1()
    {
        var payload = new InspectionResultPayload(
            "QI-001",
            "PLAN-001",
            "receiving",
            "quality",
            "RCV-001",
            "SKU-FG-1000",
            3m,
            "passed",
            null,
            [],
            DateTimeOffset.Parse("2026-06-22T00:00:00Z"),
            StockRelease: null,
            ResultLines: null,
            LotNo: "LOT-002",
            SerialNo: "SER-002",
            SiteCode: "SITE-01",
            LocationCode: "IQC-HOLD",
            OwnerType: "company",
            OwnerId: "owner-001",
            UomCode: "kg");
        var integrationEvent = new InspectionResultIntegrationEvent(
            "evt-001",
            QualityIntegrationEventTypes.InspectionPassed,
            QualityIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-06-22T00:00:01Z"),
            QualityIntegrationEventSources.BusinessQuality,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "system:quality",
            "idem-001",
            payload);

        var json = JsonSerializer.Serialize(integrationEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(QualityIntegrationEventVersions.V1, integrationEvent.EventVersion);
        Assert.Equal("LOT-002", integrationEvent.Payload.LotNo);
        Assert.Equal("SER-002", integrationEvent.Payload.SerialNo);
        Assert.Equal("SITE-01", integrationEvent.Payload.SiteCode);
        Assert.Equal("IQC-HOLD", integrationEvent.Payload.LocationCode);
        Assert.Equal("company", integrationEvent.Payload.OwnerType);
        Assert.Equal("owner-001", integrationEvent.Payload.OwnerId);
        Assert.Equal("kg", integrationEvent.Payload.UomCode);
        Assert.Contains("\"lotNo\":\"LOT-002\"", json, StringComparison.Ordinal);
        Assert.Contains("\"uomCode\":\"kg\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Quality_inspection_result_payload_deserializes_legacy_v1_without_stock_locator_dimensions()
    {
        var json = """
            {
              "eventId": "evt-legacy-001",
              "eventType": "quality.InspectionPassed",
              "eventVersion": 1,
              "occurredAtUtc": "2026-06-22T00:00:01Z",
              "sourceService": "business-quality",
              "correlationId": "corr-001",
              "causationId": "cause-001",
              "organizationId": "org-001",
              "environmentId": "env-dev",
              "actor": "system:quality",
              "idempotencyKey": "idem-legacy-001",
              "payload": {
                "inspectionRecordId": "QI-001",
                "inspectionPlanId": "PLAN-001",
                "sourceType": "receiving",
                "sourceService": "quality",
                "sourceDocumentId": "RCV-001",
                "skuCode": "SKU-FG-1000",
                "inspectedQuantity": 3,
                "result": "passed",
                "dispositionReason": null,
                "dispositionAttachmentFileIds": [],
                "recordedAtUtc": "2026-06-22T00:00:00Z",
                "stockRelease": null,
                "resultLines": []
              }
            }
            """;

        var integrationEvent = JsonSerializer.Deserialize<InspectionResultIntegrationEvent>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(integrationEvent);
        Assert.Equal(QualityIntegrationEventVersions.V1, integrationEvent.EventVersion);
        Assert.Null(integrationEvent.Payload.LotNo);
        Assert.Null(integrationEvent.Payload.SerialNo);
        Assert.Null(integrationEvent.Payload.SiteCode);
        Assert.Null(integrationEvent.Payload.LocationCode);
        Assert.Null(integrationEvent.Payload.OwnerType);
        Assert.Null(integrationEvent.Payload.OwnerId);
        Assert.Null(integrationEvent.Payload.UomCode);
    }
}
