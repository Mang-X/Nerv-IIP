using System.Reflection;
using System.Text.Json;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.Contracts.BarcodeLabel;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.Contracts.Mes;
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
        typeof(MesOperationTaskManualDispatchClearedIntegrationEvent).Assembly,
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

    [Fact]
    public void Inventory_movement_requested_payload_serializes_unit_cost_as_v1_additive_field()
    {
        var integrationEvent = new InventoryMovementRequestedIntegrationEvent(
            "evt-inventory-unit-cost-001",
            InventoryIntegrationEventTypes.InventoryMovementRequested,
            InventoryIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-06-23T00:00:01Z"),
            InventoryIntegrationEventSources.BusinessMes,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "system:mes",
            "idem-unit-cost-001",
            new InventoryMovementRequestedPayload(
                "inbound",
                InventoryIntegrationEventSources.BusinessMes,
                "FGR-001",
                "WO-001",
                "idem-unit-cost-001",
                "SKU-FG",
                "PCS",
                "finished-goods",
                "receiving",
                "LOT-FG-001",
                null,
                "Unrestricted",
                "production",
                null,
                8m,
                DateTimeOffset.Parse("2026-06-23T00:00:00Z"),
                UnitCost: 12.34m));

        var json = JsonSerializer.Serialize(integrationEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(InventoryIntegrationEventVersions.V1, integrationEvent.EventVersion);
        Assert.Equal(12.34m, integrationEvent.Payload.UnitCost);
        Assert.Contains("\"unitCost\":12.34", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Inventory_movement_requested_payload_deserializes_legacy_v1_without_unit_cost()
    {
        var json = """
            {
              "eventId": "evt-legacy-inventory-001",
              "eventType": "inventory.InventoryMovementRequested",
              "eventVersion": 1,
              "occurredAtUtc": "2026-06-23T00:00:01Z",
              "sourceService": "business-mes",
              "correlationId": "corr-001",
              "causationId": "cause-001",
              "organizationId": "org-001",
              "environmentId": "env-dev",
              "actor": "system:mes",
              "idempotencyKey": "idem-legacy-inventory-001",
              "payload": {
                "movementType": "inbound",
                "sourceService": "business-mes",
                "sourceDocumentId": "FGR-001",
                "sourceDocumentLineId": "WO-001",
                "idempotencyKey": "idem-legacy-inventory-001",
                "skuCode": "SKU-FG",
                "uomCode": "PCS",
                "siteCode": "finished-goods",
                "locationCode": "receiving",
                "lotNo": "LOT-FG-001",
                "serialNo": null,
                "qualityStatus": "Unrestricted",
                "ownerType": "production",
                "ownerId": null,
                "quantity": 8,
                "requestedAtUtc": "2026-06-23T00:00:00Z"
              }
            }
            """;

        var integrationEvent = JsonSerializer.Deserialize<InventoryMovementRequestedIntegrationEvent>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(integrationEvent);
        Assert.Equal(InventoryIntegrationEventVersions.V1, integrationEvent.EventVersion);
        Assert.Null(integrationEvent.Payload.UnitCost);
        Assert.Null(integrationEvent.Payload.UnitCostAuthorityReference);
    }

    [Fact]
    public void Mes_production_report_v1_dimension_snapshot_has_a_fixed_bidirectional_json_wire()
    {
        const string json = """
            {"reportNo":"RPT-OEE-001","workOrderId":"WO-001","operationTaskId":"OP-010","workCenterId":"WC-CNC-01","deviceAssetId":"DEV-CNC-01","goodQuantity":80,"scrapQuantity":10,"reworkQuantity":5,"uomCode":"PCS","theoreticalRatePerHour":120,"reportedAtUtc":"2026-08-27T14:45:00+00:00","isReversal":false,"reversedReportNo":null,"materialMovementCount":3,"siteCode":"SITE-SH","workshopCode":"WS-MACH","lineCode":"LINE-CNC","shiftCode":"NIGHT","siteTimezone":"Asia/Shanghai","shiftStartsAt":"22:30:00","shiftEndsAt":"06:15:00","shiftCrossesMidnight":true,"shiftPaidMinutes":435,"shiftBreakMinutes":30}
            """;

        var payload = JsonSerializer.Deserialize<ProductionReportRecordedPayload>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(payload);
        Assert.Equal("SITE-SH", payload.SiteCode);
        Assert.Equal("WS-MACH", payload.WorkshopCode);
        Assert.Equal("LINE-CNC", payload.LineCode);
        Assert.Equal("NIGHT", payload.ShiftCode);
        Assert.Equal("Asia/Shanghai", payload.SiteTimezone);
        Assert.Equal(new TimeOnly(22, 30), payload.ShiftStartsAt);
        Assert.Equal(new TimeOnly(6, 15), payload.ShiftEndsAt);
        Assert.True(payload.ShiftCrossesMidnight);
        Assert.Equal(435, payload.ShiftPaidMinutes);
        Assert.Equal(30, payload.ShiftBreakMinutes);
        Assert.Equal(json, JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public void Mes_production_report_v1_explicit_null_dimension_snapshot_has_a_fixed_bidirectional_json_wire()
    {
        const string json = """
            {"reportNo":"RPT-OEE-NULL-001","workOrderId":"WO-001","operationTaskId":"OP-010","workCenterId":"WC-CNC-01","deviceAssetId":"DEV-CNC-01","goodQuantity":80,"scrapQuantity":10,"reworkQuantity":5,"uomCode":"PCS","theoreticalRatePerHour":120,"reportedAtUtc":"2026-08-27T14:45:00+00:00","isReversal":false,"reversedReportNo":null,"materialMovementCount":3,"siteCode":null,"workshopCode":null,"lineCode":null,"shiftCode":null,"siteTimezone":null,"shiftStartsAt":null,"shiftEndsAt":null,"shiftCrossesMidnight":null,"shiftPaidMinutes":null,"shiftBreakMinutes":null}
            """;

        var payload = JsonSerializer.Deserialize<ProductionReportRecordedPayload>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(payload);
        Assert.Null(payload.SiteCode);
        Assert.Null(payload.WorkshopCode);
        Assert.Null(payload.LineCode);
        Assert.Null(payload.ShiftCode);
        Assert.Null(payload.SiteTimezone);
        Assert.Null(payload.ShiftStartsAt);
        Assert.Null(payload.ShiftEndsAt);
        Assert.Null(payload.ShiftCrossesMidnight);
        Assert.Null(payload.ShiftPaidMinutes);
        Assert.Null(payload.ShiftBreakMinutes);
        Assert.Equal(json, JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public void Mes_production_report_legacy_v1_without_dimension_snapshot_remains_deserializable()
    {
        const string json = """
            {
              "eventId": "evt-mes-report-legacy-001",
              "eventType": "mes.ProductionReportRecorded",
              "eventVersion": 1,
              "occurredAtUtc": "2026-08-27T14:45:00Z",
              "sourceService": "business-mes",
              "correlationId": "corr-report-legacy-001",
              "causationId": "WO-001",
              "organizationId": "org-001",
              "environmentId": "env-dev",
              "actor": "system:mes",
              "idempotencyKey": "mes:production-report-recorded:org-001:env-dev:RPT-LEGACY-001",
              "payload": {
                "reportNo": "RPT-LEGACY-001",
                "workOrderId": "WO-001",
                "operationTaskId": "OP-010",
                "workCenterId": "WC-CNC-01",
                "deviceAssetId": "DEV-CNC-01",
                "goodQuantity": 80,
                "scrapQuantity": 10,
                "reworkQuantity": 5,
                "uomCode": "PCS",
                "theoreticalRatePerHour": 120,
                "reportedAtUtc": "2026-08-27T14:45:00Z",
                "isReversal": false,
                "reversedReportNo": null,
                "materialMovementCount": 3
              }
            }
            """;

        var integrationEvent = JsonSerializer.Deserialize<ProductionReportRecordedIntegrationEvent>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(integrationEvent);
        Assert.Equal(MesIntegrationEventVersions.V1, integrationEvent.EventVersion);
        Assert.Null(integrationEvent.Payload.SiteCode);
        Assert.Null(integrationEvent.Payload.WorkshopCode);
        Assert.Null(integrationEvent.Payload.LineCode);
        Assert.Null(integrationEvent.Payload.ShiftCode);
        Assert.Null(integrationEvent.Payload.SiteTimezone);
        Assert.Null(integrationEvent.Payload.ShiftStartsAt);
        Assert.Null(integrationEvent.Payload.ShiftEndsAt);
        Assert.Null(integrationEvent.Payload.ShiftCrossesMidnight);
        Assert.Null(integrationEvent.Payload.ShiftPaidMinutes);
        Assert.Null(integrationEvent.Payload.ShiftBreakMinutes);
    }

    [Fact]
    public void Mes_manual_dispatch_cleared_envelope_serializes_revision_and_prior_assignment_facts()
    {
        var clearedAtUtc = DateTimeOffset.Parse("2026-07-15T08:01:00Z");
        var integrationEvent = new MesOperationTaskManualDispatchClearedIntegrationEvent(
            "evt-mes-clear-2",
            MesIntegrationEventTypes.OperationTaskManualDispatchCleared,
            MesIntegrationEventVersions.V1,
            clearedAtUtc,
            MesIntegrationEventSources.BusinessMes,
            "corr-mes-clear-2",
            "evt-mes-dispatch-1",
            "org-001",
            "env-dev",
            "user:planner-1",
            "operation-task-manual-dispatch-cleared:org-001:env-dev:OP-10:2:device-cleared",
            new OperationTaskManualDispatchClearedPayload(
                "WO-001",
                "OP-10",
                10,
                "DEVICE-2",
                "WC-1",
                DateTimeOffset.Parse("2026-07-15T08:00:00Z"),
                DateTimeOffset.Parse("2026-07-15T09:00:00Z"),
                2,
                "device-cleared",
                clearedAtUtc));

        var json = JsonSerializer.Serialize(
            integrationEvent,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var roundTripped = JsonSerializer.Deserialize<MesOperationTaskManualDispatchClearedIntegrationEvent>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(roundTripped);
        Assert.Equal(MesIntegrationEventVersions.V1, roundTripped.EventVersion);
        Assert.Equal("evt-mes-dispatch-1", roundTripped.CausationId);
        Assert.Equal(2, roundTripped.Payload.DispatchRevision);
        Assert.Equal("DEVICE-2", roundTripped.Payload.ResourceId);
        Assert.Equal("device-cleared", roundTripped.Payload.ReasonCode);
        Assert.Equal(clearedAtUtc, roundTripped.Payload.ClearedAtUtc);
        Assert.Contains("\"dispatchRevision\":2", json, StringComparison.Ordinal);
        Assert.Contains("\"reasonCode\":\"device-cleared\"", json, StringComparison.Ordinal);
    }
}
