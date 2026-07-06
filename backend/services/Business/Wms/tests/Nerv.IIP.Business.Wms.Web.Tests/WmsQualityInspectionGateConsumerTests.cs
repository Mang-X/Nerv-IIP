using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.SupplierReturnAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsQualityInspectionGateConsumerTests
{
    [Fact]
    public async Task Quality_passed_event_releases_wms_putaway_gate_for_received_stock()
    {
        await using var dbContext = CreateContext(nameof(Quality_passed_event_releases_wms_putaway_gate_for_received_stock));
        var inbound = QualityRequiredInboundOrder("IN-QA-PASS-001");
        dbContext.InboundOrders.Add(inbound);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new CompleteInboundOrderCommandHandler(dbContext).Handle(
            new CompleteInboundOrderCommand(inbound.Id, "idem-in-pass-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());
        await handler.HandleAsync(CreateInspectionEvent(QualityIntegrationEventTypes.InspectionPassed, "IN-QA-PASS-001"), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(InboundOrderStatus.Completed, inbound.Status);
        var task = await new CreatePutawayTaskCommandHandler(dbContext).Handle(
            new CreatePutawayTaskCommand(inbound.Id, "PUT-QA-PASS-001", "LINE-001", "LOC-STAGE", "LOC-A-01", 5m),
            CancellationToken.None);
        Assert.Contains(dbContext.WarehouseTasks.Local, x => x.Id == task);
    }

    [Fact]
    public async Task Quality_rejected_event_keeps_putaway_blocked_and_records_supplier_return_fact()
    {
        await using var dbContext = CreateContext(nameof(Quality_rejected_event_keeps_putaway_blocked_and_records_supplier_return_fact));
        var inbound = QualityRequiredInboundOrder("IN-QA-REJ-001");
        dbContext.InboundOrders.Add(inbound);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new CompleteInboundOrderCommandHandler(dbContext).Handle(
            new CompleteInboundOrderCommand(inbound.Id, "idem-in-rej-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());
        await handler.HandleAsync(CreateInspectionEvent(QualityIntegrationEventTypes.InspectionRejected, "IN-QA-REJ-001"), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var supplierReturn = Assert.Single(dbContext.Set<SupplierReturnRequest>().Local);
        Assert.Equal("IN-QA-REJ-001", supplierReturn.InboundOrderNo);
        Assert.Equal("QI-001", supplierReturn.InspectionRecordId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CreatePutawayTaskCommandHandler(dbContext).Handle(
            new CreatePutawayTaskCommand(inbound.Id, "PUT-QA-REJ-001", "LINE-001", "LOC-STAGE", "LOC-A-01", 5m),
            CancellationToken.None));
    }

    [Fact]
    public async Task Quality_conditional_release_event_allows_restricted_putaway_gate()
    {
        await using var dbContext = CreateContext(nameof(Quality_conditional_release_event_allows_restricted_putaway_gate));
        var inbound = QualityRequiredInboundOrder("IN-QA-COND-001");
        dbContext.InboundOrders.Add(inbound);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new CompleteInboundOrderCommandHandler(dbContext).Handle(
            new CompleteInboundOrderCommand(inbound.Id, "idem-in-cond-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());
        await handler.HandleAsync(CreateInspectionEvent(QualityIntegrationEventTypes.InspectionConditionalReleased, "IN-QA-COND-001"), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var task = await new CreatePutawayTaskCommandHandler(dbContext).Handle(
            new CreatePutawayTaskCommand(inbound.Id, "PUT-QA-COND-001", "LINE-001", "LOC-STAGE", "LOC-RESTRICTED-01", 5m),
            CancellationToken.None);
        Assert.Contains(dbContext.WarehouseTasks.Local, x => x.Id == task);
    }

    private static InboundOrder QualityRequiredInboundOrder(string inboundOrderNo)
    {
        return InboundOrder.Create(
            "org-001",
            "env-dev",
            inboundOrderNo,
            "purchase-receipt",
            "PO-001",
            "SITE-01",
            [new InboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 5m, "LOC-STAGE", "LOT-001", null, "quality", "company", "owner-001")]);
    }

    private static InspectionResultIntegrationEvent CreateInspectionEvent(string eventType, string inboundOrderNo)
    {
        var result = eventType == QualityIntegrationEventTypes.InspectionPassed
            ? "passed"
            : eventType == QualityIntegrationEventTypes.InspectionConditionalReleased
                ? "conditional-release"
                : "rejected";
        return new InspectionResultIntegrationEvent(
            "quality-event-001",
            eventType,
            QualityIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            QualityIntegrationEventSources.BusinessQuality,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "system:quality",
            $"quality:inspection-result:org-001:env-dev:QI-001:{eventType}",
            new InspectionResultPayload(
                "QI-001",
                "PLAN-001",
                "receiving",
                "wms",
                inboundOrderNo,
                "SKU-FG-1000",
                5m,
                result,
                eventType == QualityIntegrationEventTypes.InspectionRejected ? "critical-defect" : null,
                [],
                DateTimeOffset.UtcNow,
                new StockReleaseDimensionPayload("kg", "SITE-01", "LOC-STAGE", "LOT-001", null, "quality", "company", "owner-001")));
    }

    private static ApplicationDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }
}
