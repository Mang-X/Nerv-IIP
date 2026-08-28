using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesIntegrationEventTests
{
    /// <summary>对齐库存世界观种子的成品仓事实（SITE-001 / WH-WB-FG-01，#1331）。</summary>
    private static readonly IMesFinishedGoodsReceiptLocationResolver FinishedGoodsLocationResolver =
        new ConfiguredMesFinishedGoodsReceiptLocationResolver(new MesFinishedGoodsReceiptLocationOptions
        {
            SiteCode = "SITE-001",
            LocationCode = "WH-WB-FG-01",
        });

    [Fact]
    public void Actual_time_settlement_converter_preserves_revision_snapshot_and_request_lineage()
    {
        var completedAtUtc = DateTimeOffset.Parse("2026-08-26T03:00:00Z");
        var settlement = new OperationActualTimeSettlementSnapshot(
            "org-001", "env-dev", "WO-001", "OP-001", "WC-001", 2,
            completedAtUtc, 72_000_000_000, 36_000_000_000, ["PR-001", "PR-002"],
            "DEVICE-001", MachineTimeFactStatus.Available, 24_000_000_000,
            MachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1);

        var integrationEvent = new OperationActualTimeSettledIntegrationEventConverter(
                new StubMesIntegrationEventContextAccessor(
                    new MesIntegrationEventContext("corr-settled", "cause-report-completed")))
            .Convert(new OperationActualTimeSettledDomainEvent(settlement));
        var legacyEvent = new OperationActualTimeSettledV1IntegrationEventConverter(
                new StubMesIntegrationEventContextAccessor(
                    new MesIntegrationEventContext("corr-settled", "cause-report-completed")))
            .Convert(new OperationActualTimeSettledDomainEvent(settlement));

        Assert.Equal(MesIntegrationEventVersions.V1, legacyEvent.EventVersion);
        Assert.Equal(legacyEvent.IdempotencyKey, integrationEvent.IdempotencyKey);
        Assert.Equal(
            legacyEvent.IdempotencyKey,
            new OperationActualTimeSettledV1IntegrationEventConverter(
                    new StubMesIntegrationEventContextAccessor(
                        new MesIntegrationEventContext("corr-settled", "cause-report-completed")))
                .Convert(new OperationActualTimeSettledDomainEvent(settlement)).IdempotencyKey);
        Assert.Equal(
            integrationEvent.IdempotencyKey,
            new OperationActualTimeSettledIntegrationEventConverter(
                    new StubMesIntegrationEventContextAccessor(
                        new MesIntegrationEventContext("corr-settled", "cause-report-completed")))
                .Convert(new OperationActualTimeSettledDomainEvent(settlement)).IdempotencyKey);
        Assert.NotEqual(legacyEvent.GetType(), integrationEvent.GetType());
        Assert.Equal(MesIntegrationEventTypes.OperationActualTimeSettled, integrationEvent.EventType);
        Assert.Equal(MesIntegrationEventVersions.V2, integrationEvent.EventVersion);
        Assert.Equal("corr-settled", integrationEvent.CorrelationId);
        Assert.Equal("cause-report-completed", integrationEvent.CausationId);
        Assert.Equal("mes:operation-actual-time-settled:org-001:env-dev:OP-001:2", integrationEvent.IdempotencyKey);
        Assert.Equal(2, integrationEvent.Payload.SettlementRevision);
        Assert.Equal(72_000_000_000, integrationEvent.Payload.ActualLaborTicks);
        Assert.Equal(36_000_000_000, integrationEvent.Payload.ActualMachineTicks);
        Assert.Equal("DEVICE-001", integrationEvent.Payload.DeviceAssetId);
        Assert.Equal(MesMachineTimeFactStatus.Available, integrationEvent.Payload.MachineTimeStatus);
        Assert.Equal(24_000_000_000, integrationEvent.Payload.BillableMachineTicks);
        Assert.Equal(MesMachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1, integrationEvent.Payload.MachineTimeBasisCode);
        Assert.Equal(["PR-001", "PR-002"], integrationEvent.Payload.CoveredProductionReportNos);
    }

    [Fact]
    public void Actual_time_void_converter_references_the_same_settlement_revision_and_snapshot()
    {
        var completedAtUtc = DateTimeOffset.Parse("2026-08-26T03:00:00Z");
        var voidedAtUtc = completedAtUtc.AddMinutes(10);
        var settlement = new OperationActualTimeSettlementSnapshot(
            "org-001", "env-dev", "WO-001", "OP-001", "WC-001", 2,
            completedAtUtc, 72_000_000_000, 36_000_000_000, ["PR-001", "PR-002"],
            "DEVICE-SETTLED", MachineTimeFactStatus.Available, 24_000_000_000,
            MachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1);

        var integrationEvent = new OperationActualTimeSettlementVoidedIntegrationEventConverter(
                new StubMesIntegrationEventContextAccessor(
                    new MesIntegrationEventContext("corr-voided", "cause-report-reversed")))
            .Convert(new OperationActualTimeSettlementVoidedDomainEvent(settlement, voidedAtUtc));
        var legacyEvent = new OperationActualTimeSettlementVoidedV1IntegrationEventConverter(
                new StubMesIntegrationEventContextAccessor(
                    new MesIntegrationEventContext("corr-voided", "cause-report-reversed")))
            .Convert(new OperationActualTimeSettlementVoidedDomainEvent(settlement, voidedAtUtc));

        Assert.Equal(MesIntegrationEventVersions.V1, legacyEvent.EventVersion);
        Assert.Equal(legacyEvent.IdempotencyKey, integrationEvent.IdempotencyKey);
        Assert.Equal(
            legacyEvent.IdempotencyKey,
            new OperationActualTimeSettlementVoidedV1IntegrationEventConverter(
                    new StubMesIntegrationEventContextAccessor(
                        new MesIntegrationEventContext("corr-voided", "cause-report-reversed")))
                .Convert(new OperationActualTimeSettlementVoidedDomainEvent(settlement, voidedAtUtc)).IdempotencyKey);
        Assert.Equal(
            integrationEvent.IdempotencyKey,
            new OperationActualTimeSettlementVoidedIntegrationEventConverter(
                    new StubMesIntegrationEventContextAccessor(
                        new MesIntegrationEventContext("corr-voided", "cause-report-reversed")))
                .Convert(new OperationActualTimeSettlementVoidedDomainEvent(settlement, voidedAtUtc)).IdempotencyKey);
        Assert.NotEqual(legacyEvent.GetType(), integrationEvent.GetType());
        Assert.Equal(MesIntegrationEventTypes.OperationActualTimeSettlementVoided, integrationEvent.EventType);
        Assert.Equal("corr-voided", integrationEvent.CorrelationId);
        Assert.Equal("cause-report-reversed", integrationEvent.CausationId);
        Assert.Equal("mes:operation-actual-time-settlement-voided:org-001:env-dev:OP-001:2", integrationEvent.IdempotencyKey);
        Assert.Equal(2, integrationEvent.Payload.SettlementRevision);
        Assert.Equal(completedAtUtc, integrationEvent.Payload.CompletedAtUtc);
        Assert.Equal(voidedAtUtc, integrationEvent.Payload.VoidedAtUtc);
        Assert.Equal(72_000_000_000, integrationEvent.Payload.ActualLaborTicks);
        Assert.Equal(36_000_000_000, integrationEvent.Payload.ActualMachineTicks);
        Assert.Equal(MesIntegrationEventVersions.V2, integrationEvent.EventVersion);
        Assert.Equal("DEVICE-SETTLED", integrationEvent.Payload.DeviceAssetId);
        Assert.Equal(MesMachineTimeFactStatus.Available, integrationEvent.Payload.MachineTimeStatus);
        Assert.Equal(24_000_000_000, integrationEvent.Payload.BillableMachineTicks);
        Assert.Equal(MesMachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1, integrationEvent.Payload.MachineTimeBasisCode);
        Assert.Equal(["PR-001", "PR-002"], integrationEvent.Payload.CoveredProductionReportNos);
    }

    [Fact]
    public void Actual_time_topics_encode_deployment_source_context_event_and_envelope_version()
    {
        Assert.Equal(
            "nerv-iip.production.business-mes.mes.operation-actual-time-settled.v1",
            MesActualTimeIntegrationEventTopics.Settled("Production", MesIntegrationEventVersions.V1));
        Assert.Equal(
            "nerv-iip.production.business-mes.mes.operation-actual-time-settled.v2",
            MesActualTimeIntegrationEventTopics.Settled("Production", MesIntegrationEventVersions.V2));
        Assert.Equal(
            "nerv-iip.production.business-mes.mes.operation-actual-time-settlement-voided.v1",
            MesActualTimeIntegrationEventTopics.Voided("Production", MesIntegrationEventVersions.V1));
        Assert.Equal(
            "nerv-iip.production.business-mes.mes.operation-actual-time-settlement-voided.v2",
            MesActualTimeIntegrationEventTopics.Voided("Production", MesIntegrationEventVersions.V2));
    }

    [Fact]
    public void Manual_dispatch_clear_reason_converter_maps_every_domain_reason_to_wire_contract()
    {
        var expectedCodes = new Dictionary<OperationTaskManualDispatchClearReason, string>
        {
            [OperationTaskManualDispatchClearReason.DeviceCleared] =
                MesManualDispatchClearReasonCodes.DeviceCleared,
            [OperationTaskManualDispatchClearReason.OperationCancelled] =
                MesManualDispatchClearReasonCodes.OperationCancelled
        };
        Assert.Equal(Enum.GetValues<OperationTaskManualDispatchClearReason>(), expectedCodes.Keys);

        var occurredAtUtc = DateTimeOffset.Parse("2026-07-15T08:00:00Z");
        var dispatch = new OperationTaskManualDispatchSnapshot(
            "org-001", "env-dev", "WO-001", "OP-10", 10,
            "DEVICE-2", "WC-1", occurredAtUtc, occurredAtUtc.AddHours(1),
            occurredAtUtc, 2);
        var converter = new OperationTaskManualDispatchClearedIntegrationEventConverter(
            new StubMesIntegrationEventContextAccessor(
                new MesIntegrationEventContext("corr-clear", "cause-clear")));

        foreach (var (reason, expectedCode) in expectedCodes)
        {
            var integrationEvent = converter.Convert(
                new OperationTaskManualDispatchClearedDomainEvent(
                    dispatch, reason, occurredAtUtc, "user:planner-1"));

            Assert.Equal(expectedCode, integrationEvent.Payload.ReasonCode);
        }
    }

    [Fact]
    public void Manual_dispatch_lifecycle_converters_preserve_real_snapshot_revision_actor_and_lineage()
    {
        var start = DateTimeOffset.Parse("2026-07-15T08:00:00Z");
        var task = OperationTask.Queue(
            "org-001", "env-dev", "WO-001", "OP-10", 10, "WC-1", [],
            start, TimeSpan.FromHours(1));
        task.Assign("operator-1", "DEVICE-2", "SHIFT-1", start.AddMinutes(-5), "user:planner-1");
        var dispatchedDomainEvent = Assert.IsType<OperationTaskManuallyDispatchedDomainEvent>(
            Assert.Single(task.GetDomainEvents()));
        var dispatched = new OperationTaskManuallyDispatchedIntegrationEventConverter()
            .Convert(dispatchedDomainEvent);

        task.ClearDomainEvents();
        task.Assign("operator-1", null, "SHIFT-1", start.AddMinutes(-4), "user:planner-1");
        var clearedDomainEvent = Assert.IsType<OperationTaskManualDispatchClearedDomainEvent>(
            Assert.Single(task.GetDomainEvents()));
        var cleared = new OperationTaskManualDispatchClearedIntegrationEventConverter(
                new StubMesIntegrationEventContextAccessor(
                    new MesIntegrationEventContext("corr-clear-2", dispatched.EventId)))
            .Convert(clearedDomainEvent);

        Assert.Equal(MesIntegrationEventTypes.OperationTaskManuallyDispatched, dispatched.EventType);
        Assert.Equal(1, dispatched.Payload.DispatchRevision);
        Assert.Equal("DEVICE-2", dispatched.Payload.ResourceId);
        Assert.Equal("user:planner-1", dispatched.Actor);
        Assert.Equal(MesIntegrationEventTypes.OperationTaskManualDispatchCleared, cleared.EventType);
        Assert.Equal(2, cleared.Payload.DispatchRevision);
        Assert.Equal("DEVICE-2", cleared.Payload.ResourceId);
        Assert.Equal(MesManualDispatchClearReasonCodes.DeviceCleared, cleared.Payload.ReasonCode);
        Assert.Equal("corr-clear-2", cleared.CorrelationId);
        Assert.Equal(dispatched.EventId, cleared.CausationId);
        Assert.Equal("user:planner-1", cleared.Actor);
        Assert.NotEqual(dispatched.IdempotencyKey, cleared.IdempotencyKey);
    }

    [Fact]
    public void Production_report_converter_emits_inventory_outbound_requests_from_production_line_side_account()
    {
        var reportedAtUtc = DateTimeOffset.Parse("2026-06-15T08:00:00Z");
        var consumption = ProductionReportMaterialConsumption.Record(
            "org-001",
            "env-dev",
            "PRPT-001",
            "WO-001",
            "OP-10",
            "MAT-OIL",
            "LOT-OIL-A",
            "L",
            2.5m,
            "MIR-001",
            MaterialSupplyTestFixtures.Locations.TargetSiteCode,
            MaterialSupplyTestFixtures.Locations.TargetLocationCode);

        var integrationEvent = new ProductionMaterialConsumedIntegrationEventConverter()
            .Convert(new ProductionMaterialConsumedDomainEvent(consumption));

        Assert.Equal(InventoryIntegrationEventTypes.InventoryMovementRequested, integrationEvent.EventType);
        Assert.Equal("business-mes", integrationEvent.SourceService);
        Assert.Equal("outbound", integrationEvent.Payload.MovementType);
        Assert.Equal("business-mes", integrationEvent.Payload.SourceService);
        Assert.Equal("PRPT-001", integrationEvent.Payload.SourceDocumentId);
        Assert.Equal("MAT-OIL", integrationEvent.Payload.SkuCode);
        Assert.Equal(MaterialSupplyTestFixtures.Locations.TargetSiteCode, integrationEvent.Payload.SiteCode);
        Assert.Equal(MaterialSupplyTestFixtures.Locations.TargetLocationCode, integrationEvent.Payload.LocationCode);
        Assert.Equal("LOT-OIL-A", integrationEvent.Payload.LotNo);
        Assert.Equal(-2.5m, integrationEvent.Payload.Quantity);
        Assert.Equal("PRPT-001", integrationEvent.CorrelationId);
        Assert.Contains("MIR-001", integrationEvent.IdempotencyKey, StringComparison.Ordinal);
    }

    [Fact]
    public void Finished_goods_receipt_converter_emits_inventory_inbound_request()
    {
        var request = FinishedGoodsReceiptRequest.Create(
            "org-001",
            "env-dev",
            "FGR-001",
            "WO-001",
            "SKU-FG",
            8m,
            "PCS",
            DateTimeOffset.Parse("2026-06-15T09:00:00Z"),
            "LOT-FG-001",
            null,
            12.34m);

        var domainEvent = Assert.IsType<FinishedGoodsReceiptRequestedDomainEvent>(request.GetDomainEvents().Single());
        var integrationEvent = new FinishedGoodsReceiptRequestedIntegrationEventConverter(FinishedGoodsLocationResolver)
            .Convert(domainEvent);

        Assert.Equal(InventoryIntegrationEventTypes.InventoryMovementRequested, integrationEvent.EventType);
        Assert.Equal("inbound", integrationEvent.Payload.MovementType);
        Assert.Equal("FGR-001", integrationEvent.Payload.SourceDocumentId);
        Assert.Equal("SKU-FG", integrationEvent.Payload.SkuCode);
        Assert.Equal("SITE-001", integrationEvent.Payload.SiteCode);
        Assert.Equal("WH-WB-FG-01", integrationEvent.Payload.LocationCode);
        Assert.Equal("LOT-FG-001", integrationEvent.Payload.LotNo);
        Assert.Equal(8m, integrationEvent.Payload.Quantity);
        Assert.Equal(12.34m, integrationEvent.Payload.UnitCost);
        Assert.Equal(
            InventoryMovementUnitCostAuthorityReferences.MesFinishedGoodsReceipt,
            integrationEvent.Payload.UnitCostAuthorityReference);
        Assert.Equal("WO-001", integrationEvent.CorrelationId);
    }

    [Fact]
    public void Finished_goods_receipt_converter_fails_explicitly_when_location_unconfigured()
    {
        var request = FinishedGoodsReceiptRequest.Create(
            "org-001",
            "env-dev",
            "FGR-002",
            "WO-001",
            "SKU-FG",
            8m,
            "PCS",
            DateTimeOffset.Parse("2026-06-15T09:00:00Z"),
            "LOT-FG-001",
            null,
            12.34m);
        var domainEvent = Assert.IsType<FinishedGoodsReceiptRequestedDomainEvent>(request.GetDomainEvents().Single());
        var converter = new FinishedGoodsReceiptRequestedIntegrationEventConverter(
            new ConfiguredMesFinishedGoodsReceiptLocationResolver(new MesFinishedGoodsReceiptLocationOptions()));

        var exception = Assert.Throws<KnownException>(() => converter.Convert(domainEvent));

        Assert.Contains("FINISHED_GOODS_LOCATION_UNCONFIGURED", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Finished_goods_receipt_waits_for_erp_cost_before_emitting_inventory_request()
    {
        var request = FinishedGoodsReceiptRequest.Create(
            "org-001",
            "env-dev",
            "FGR-LEGACY",
            "WO-001",
            "SKU-FG",
            8m,
            "PCS",
            DateTimeOffset.Parse("2026-06-15T09:00:00Z"),
            "LOT-FG-001");

        Assert.Empty(request.GetDomainEvents());

        request.ApplyCapitalizedUnitCost(12.34m);

        var domainEvent = Assert.IsType<FinishedGoodsReceiptRequestedDomainEvent>(request.GetDomainEvents().Single());
        var integrationEvent = new FinishedGoodsReceiptRequestedIntegrationEventConverter(FinishedGoodsLocationResolver)
            .Convert(domainEvent);

        Assert.Equal(InventoryIntegrationEventTypes.InventoryMovementRequested, integrationEvent.EventType);
        Assert.Equal("inbound", integrationEvent.Payload.MovementType);
        Assert.Equal("FGR-LEGACY", integrationEvent.Payload.SourceDocumentId);
        Assert.Equal(12.34m, integrationEvent.Payload.UnitCost);
        Assert.Equal(
            InventoryMovementUnitCostAuthorityReferences.MesFinishedGoodsReceipt,
            integrationEvent.Payload.UnitCostAuthorityReference);
    }

    [Fact]
    public void Material_issue_converter_emits_inventory_outbound_request_for_confirmed_pick()
    {
        var request = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-001",
            "WO-001",
            "OP-10",
            "MAT-OIL",
            "L",
            3m,
            DateTimeOffset.Parse("2026-06-15T07:45:00Z"));
        request.ConfirmLineSideReceipt(
            MaterialSupplyTestFixtures.Locations,
            DateTimeOffset.Parse("2026-06-15T08:15:00Z"),
            3m,
            "LOT-OIL-A");

        var integrationEvent = new MaterialIssueRequestedIntegrationEventConverter()
            .Convert(new MaterialIssueRequestedDomainEvent(request, 3m));

        Assert.Equal(InventoryIntegrationEventTypes.InventoryMovementRequested, integrationEvent.EventType);
        Assert.Equal("outbound", integrationEvent.Payload.MovementType);
        Assert.Equal("MIR-001", integrationEvent.Payload.SourceDocumentId);
        Assert.Equal("MAT-OIL", integrationEvent.Payload.SkuCode);
        Assert.Equal("L", integrationEvent.Payload.UomCode);
        Assert.Equal(MaterialSupplyTestFixtures.Locations.SourceSiteCode, integrationEvent.Payload.SiteCode);
        Assert.Equal(MaterialSupplyTestFixtures.Locations.SourceLocationCode, integrationEvent.Payload.LocationCode);
        Assert.Equal("LOT-OIL-A", integrationEvent.Payload.LotNo);
        Assert.Equal(-3m, integrationEvent.Payload.Quantity);
        Assert.Equal("WO-001", integrationEvent.CorrelationId);
    }

    [Fact]
    public void Material_issue_converter_emits_one_inventory_detail_per_source_allocation()
    {
        var request = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-SPLIT",
            "WO-001",
            "OP-10",
            "MAT-OIL",
            "L",
            5m,
            DateTimeOffset.Parse("2026-06-15T07:45:00Z"));
        request.ConfirmLineSideReceipt(
            new MaterialTransferLocations(
                "SITE-001",
                "WH-WB-RM-01",
                "SITE-001",
                "WH-WB-LINE-01",
                [
                    new MaterialTransferAllocation("SITE-001", "WH-WB-RM-01", "LOT-OPENING-MAT-OIL", 3m),
                    new MaterialTransferAllocation("SITE-001", "WH-WB-SF-01", "LOT-PO-001", 2m),
                ]),
            DateTimeOffset.Parse("2026-06-15T08:15:00Z"),
            5m,
            "LOT-WO-001");

        var issueEvents = request.GetDomainEvents()
            .OfType<MaterialIssueRequestedDomainEvent>()
            .ToArray();
        var inventoryEvents = issueEvents
            .Select(new MaterialIssueRequestedIntegrationEventConverter().Convert)
            .ToArray();

        Assert.Equal(2, inventoryEvents.Length);
        Assert.Collection(
            inventoryEvents,
            first =>
            {
                Assert.Equal("WH-WB-RM-01", first.Payload.LocationCode);
                Assert.Equal("LOT-OPENING-MAT-OIL", first.Payload.LotNo);
                Assert.Equal(-3m, first.Payload.Quantity);
            },
            second =>
            {
                Assert.Equal("WH-WB-SF-01", second.Payload.LocationCode);
                Assert.Equal("LOT-PO-001", second.Payload.LotNo);
                Assert.Equal(-2m, second.Payload.Quantity);
            });
        Assert.NotEqual(inventoryEvents[0].Payload.IdempotencyKey, inventoryEvents[1].Payload.IdempotencyKey);
    }

    [Fact]
    public void Line_side_receipt_converter_emits_inventory_inbound_request_to_production_line_side_account()
    {
        var request = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-001",
            "WO-001",
            "OP-10",
            "MAT-OIL",
            "L",
            3m,
            DateTimeOffset.Parse("2026-06-15T07:45:00Z"));
        request.ConfirmLineSideReceipt(
            MaterialSupplyTestFixtures.Locations,
            DateTimeOffset.Parse("2026-06-15T08:15:00Z"),
            3m,
            "LOT-OIL-A");

        var integrationEvent = new MaterialLineSideReceiptConfirmedIntegrationEventConverter()
            .Convert(new MaterialLineSideReceiptConfirmedDomainEvent(request, 3m));

        Assert.Equal(InventoryIntegrationEventTypes.InventoryMovementRequested, integrationEvent.EventType);
        Assert.Equal("inbound", integrationEvent.Payload.MovementType);
        Assert.Equal("MIR-001", integrationEvent.Payload.SourceDocumentId);
        Assert.Equal("OP-10", integrationEvent.Payload.SourceDocumentLineId);
        Assert.Equal("MAT-OIL", integrationEvent.Payload.SkuCode);
        Assert.Equal("L", integrationEvent.Payload.UomCode);
        Assert.Equal(MaterialSupplyTestFixtures.Locations.TargetSiteCode, integrationEvent.Payload.SiteCode);
        Assert.Equal(MaterialSupplyTestFixtures.Locations.TargetLocationCode, integrationEvent.Payload.LocationCode);
        Assert.Equal("LOT-OIL-A", integrationEvent.Payload.LotNo);
        Assert.Equal(3m, integrationEvent.Payload.Quantity);
        Assert.Equal("WO-001", integrationEvent.CorrelationId);
        Assert.Contains("line-side-receipt", integrationEvent.IdempotencyKey, StringComparison.Ordinal);
    }

    [Fact]
    public void Line_side_return_converters_emit_inventory_reversal_requests()
    {
        var request = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-001",
            "WO-001",
            "OP-10",
            "MAT-OIL",
            "L",
            3m,
            DateTimeOffset.Parse("2026-06-15T07:45:00Z"));
        request.ConfirmAndPostLineSideReceipt(
            MaterialSupplyTestFixtures.Locations,
            DateTimeOffset.Parse("2026-06-15T08:15:00Z"),
            3m,
            "LOT-OIL-A");
        request.ReturnLineSideMaterial(DateTimeOffset.Parse("2026-06-15T10:00:00Z"), 1m);

        var productionOutbound = new MaterialLineSideReturnRequestedIntegrationEventConverter()
            .Convert(new MaterialLineSideReturnRequestedDomainEvent(request, 1m, "LOT-MAT-001", DateTimeOffset.Parse("2026-06-01T08:30:00Z")));
        var warehouseInbound = new MaterialReturnedToWarehouseIntegrationEventConverter()
            .Convert(new MaterialReturnedToWarehouseDomainEvent(request, 1m, "LOT-MAT-001", DateTimeOffset.Parse("2026-06-01T08:30:00Z")));

        Assert.Equal("outbound", productionOutbound.Payload.MovementType);
        Assert.Equal(MaterialSupplyTestFixtures.Locations.TargetSiteCode, productionOutbound.Payload.SiteCode);
        Assert.Equal(MaterialSupplyTestFixtures.Locations.TargetLocationCode, productionOutbound.Payload.LocationCode);
        Assert.Equal(-1m, productionOutbound.Payload.Quantity);
        Assert.Equal("inbound", warehouseInbound.Payload.MovementType);
        Assert.Equal(MaterialSupplyTestFixtures.Locations.SourceSiteCode, warehouseInbound.Payload.SiteCode);
        Assert.Equal(MaterialSupplyTestFixtures.Locations.SourceLocationCode, warehouseInbound.Payload.LocationCode);
        Assert.Equal(1m, warehouseInbound.Payload.Quantity);
    }

    [Fact]
    public void Defect_converter_emits_quality_defect_raised_event()
    {
        var defect = DefectRecord.Create(
            "org-001",
            "env-dev",
            "DEF-001",
            "WO-001",
            "OP-10",
            "SURFACE",
            1m,
            DateTimeOffset.Parse("2026-06-15T10:00:00Z"));

        var integrationEvent = new DefectRaisedIntegrationEventConverter()
            .Convert(new DefectRaisedDomainEvent(defect));

        Assert.Equal(QualityIntegrationEventTypes.DefectRaised, integrationEvent.EventType);
        Assert.Equal("business-mes", integrationEvent.SourceService);
        Assert.Equal("DEF-001", integrationEvent.Payload.DefectNo);
        Assert.Equal("WO-001", integrationEvent.Payload.WorkOrderId);
        Assert.Equal("SURFACE", integrationEvent.Payload.DefectCode);
        Assert.Equal(1m, integrationEvent.Payload.Quantity);
    }

    [Fact]
    public void Production_report_converter_emits_oee_projection_fact_with_standard_rate_snapshot()
    {
        var reportedAtUtc = DateTimeOffset.Parse("2026-07-10T08:45:00Z");
        var dimensionSnapshot = ProductionReportOeeDimensionSnapshot.Resolved(
            "DEV-CNC-01",
            "WC-CNC-01",
            "SITE-SH",
            "WS-MACH",
            "LINE-CNC",
            "Asia/Shanghai",
            "NIGHT",
            new TimeOnly(22, 30),
            new TimeOnly(6, 15),
            true,
            435,
            30);
        var report = ProductionReport.Record(
            "org-001",
            "env-dev",
            "PRPT-OEE-001",
            "WO-001",
            "OP-10",
            80m,
            10m,
            false,
            reportedAtUtc,
            10m,
            oeeProjection: new ProductionReportOeeProjection("WC-PACK-01", "DEV-PACK-01", "PCS", 100m),
            oeeDimensionSnapshot: dimensionSnapshot);

        var domainEvent = Assert.IsType<ProductionReportRecordedDomainEvent>(report.GetDomainEvents().Single());
        var integrationEvent = new ProductionReportRecordedIntegrationEventConverter().Convert(domainEvent);

        Assert.Equal(MesIntegrationEventTypes.ProductionReportRecorded, integrationEvent.EventType);
        Assert.Equal(MesIntegrationEventVersions.V1, integrationEvent.EventVersion);
        Assert.Equal("DEV-CNC-01", integrationEvent.Payload.DeviceAssetId);
        Assert.Equal("WC-CNC-01", integrationEvent.Payload.WorkCenterId);
        Assert.Equal(100m, integrationEvent.Payload.TheoreticalRatePerHour);
        Assert.Equal(80m, integrationEvent.Payload.GoodQuantity);
        Assert.Equal(10m, integrationEvent.Payload.ScrapQuantity);
        Assert.Equal(10m, integrationEvent.Payload.ReworkQuantity);
        Assert.Equal("SITE-SH", integrationEvent.Payload.SiteCode);
        Assert.Equal("WS-MACH", integrationEvent.Payload.WorkshopCode);
        Assert.Equal("LINE-CNC", integrationEvent.Payload.LineCode);
        Assert.Equal("NIGHT", integrationEvent.Payload.ShiftCode);
        Assert.Equal("Asia/Shanghai", integrationEvent.Payload.SiteTimezone);
        Assert.Equal(new TimeOnly(22, 30), integrationEvent.Payload.ShiftStartsAt);
        Assert.Equal(new TimeOnly(6, 15), integrationEvent.Payload.ShiftEndsAt);
        Assert.True(integrationEvent.Payload.ShiftCrossesMidnight);
        Assert.Equal(435, integrationEvent.Payload.ShiftPaidMinutes);
        Assert.Equal(30, integrationEvent.Payload.ShiftBreakMinutes);
    }

    [Fact]
    public void Production_report_reversal_converter_reuses_the_original_oee_snapshot()
    {
        var dimensionSnapshot = ProductionReportOeeDimensionSnapshot.Resolved(
            "DEV-CNC-01",
            "WC-CNC-01",
            "SITE-SH",
            "WS-MACH",
            "LINE-CNC",
            "Asia/Shanghai",
            "NIGHT",
            new TimeOnly(22, 30),
            new TimeOnly(6, 15),
            true,
            435,
            30);
        var original = ProductionReport.Record(
            "org-001",
            "env-dev",
            "PRPT-OEE-ORIGINAL-001",
            "WO-001",
            "OP-10",
            80m,
            10m,
            false,
            DateTimeOffset.Parse("2026-07-10T08:45:00Z"),
            10m,
            oeeProjection: new ProductionReportOeeProjection("WC-PACK-01", "DEV-PACK-01", "PCS", 100m),
            oeeDimensionSnapshot: dimensionSnapshot);
        var reversal = ProductionReport.Reverse(
            original,
            "PRPT-OEE-REVERSAL-001",
            DateTimeOffset.Parse("2026-07-10T09:00:00Z"),
            "operator correction",
            "operator-1");

        var domainEvent = Assert.IsType<ProductionReportRecordedDomainEvent>(reversal.GetDomainEvents().Single());
        var integrationEvent = new ProductionReportRecordedIntegrationEventConverter().Convert(domainEvent);

        Assert.True(integrationEvent.Payload.IsReversal);
        Assert.Equal(original.ReportNo, integrationEvent.Payload.ReversedReportNo);
        Assert.Equal("WC-CNC-01", integrationEvent.Payload.WorkCenterId);
        Assert.Equal("DEV-CNC-01", integrationEvent.Payload.DeviceAssetId);
        Assert.Equal("PCS", integrationEvent.Payload.UomCode);
        Assert.Equal(100m, integrationEvent.Payload.TheoreticalRatePerHour);
        Assert.Equal("SITE-SH", integrationEvent.Payload.SiteCode);
        Assert.Equal("WS-MACH", integrationEvent.Payload.WorkshopCode);
        Assert.Equal("LINE-CNC", integrationEvent.Payload.LineCode);
        Assert.Equal("NIGHT", integrationEvent.Payload.ShiftCode);
        Assert.Equal("Asia/Shanghai", integrationEvent.Payload.SiteTimezone);
        Assert.Equal(new TimeOnly(22, 30), integrationEvent.Payload.ShiftStartsAt);
        Assert.Equal(new TimeOnly(6, 15), integrationEvent.Payload.ShiftEndsAt);
        Assert.True(integrationEvent.Payload.ShiftCrossesMidnight);
        Assert.Equal(435, integrationEvent.Payload.ShiftPaidMinutes);
        Assert.Equal(30, integrationEvent.Payload.ShiftBreakMinutes);
    }

    [Fact]
    public void Creating_a_material_issue_request_publishes_the_warehouse_leg_of_the_chain()
    {
        var request = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-001",
            "WO-001",
            "OP-10",
            "MAT-OIL",
            "L",
            7m,
            DateTimeOffset.Parse("2026-06-15T07:45:00Z"));

        var domainEvent = Assert.Single(request.GetDomainEvents().OfType<MaterialIssueRequestCreatedDomainEvent>());
        var integrationEvent = new MaterialIssueRequestCreatedIntegrationEventConverter().Convert(domainEvent);

        Assert.Equal(MesIntegrationEventTypes.MaterialIssueRequested, integrationEvent.EventType);
        Assert.Equal(MesIntegrationEventVersions.V1, integrationEvent.EventVersion);
        Assert.Equal(MesIntegrationEventSources.BusinessMes, integrationEvent.SourceService);
        Assert.Equal("MIR-001", integrationEvent.Payload.RequestNo);
        Assert.Equal("WO-001", integrationEvent.Payload.WorkOrderId);
        Assert.Equal("OP-10", integrationEvent.Payload.OperationTaskId);
        Assert.Equal("MAT-OIL", integrationEvent.Payload.MaterialId);
        Assert.Equal("L", integrationEvent.Payload.UomCode);
        Assert.Equal(7m, integrationEvent.Payload.RequestedQuantity);
        Assert.Equal("mes:material-issue-requested:org-001:env-dev:MIR-001", integrationEvent.IdempotencyKey);
    }

    [Fact]
    public void Material_issue_created_idempotency_key_is_stable_across_replays_of_the_same_request()
    {
        static MaterialIssueRequestCreatedDomainEvent NewCreation() =>
            MaterialIssueRequest.Create(
                    "org-001",
                    "env-dev",
                    "MIR-001",
                    "WO-001",
                    null,
                    "MAT-OIL",
                    "L",
                    7m,
                    DateTimeOffset.Parse("2026-06-15T07:45:00Z"))
                .GetDomainEvents()
                .OfType<MaterialIssueRequestCreatedDomainEvent>()
                .Single();

        var converter = new MaterialIssueRequestCreatedIntegrationEventConverter();

        Assert.Equal(
            converter.Convert(NewCreation()).IdempotencyKey,
            converter.Convert(NewCreation()).IdempotencyKey);
    }

    [Fact]
    public void Linking_the_warehouse_outbound_is_idempotent_for_the_same_acknowledgement()
    {
        var request = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-001",
            "WO-001",
            null,
            "MAT-OIL",
            "L",
            7m,
            DateTimeOffset.Parse("2026-06-15T07:45:00Z"));
        var preparedAtUtc = DateTimeOffset.Parse("2026-06-15T07:50:00Z");

        Assert.True(request.LinkWarehouseOutbound("MI-MIR-001", "MI-MIR-001-P1", preparedAtUtc));
        Assert.False(request.LinkWarehouseOutbound("MI-MIR-001", "MI-MIR-001-P1", preparedAtUtc.AddMinutes(5)));
        Assert.Equal("MI-MIR-001", request.WmsRequestId);
        Assert.Equal("MI-MIR-001-P1", request.WmsPickingTaskNo);
        Assert.Equal(preparedAtUtc, request.WmsPreparedAtUtc);
    }

    private sealed class StubMesIntegrationEventContextAccessor(MesIntegrationEventContext context)
        : IMesIntegrationEventContextAccessor
    {
        public MesIntegrationEventContext GetContext() => context;
    }
}
