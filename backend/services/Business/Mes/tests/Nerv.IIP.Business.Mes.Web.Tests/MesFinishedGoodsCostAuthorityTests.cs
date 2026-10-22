using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.IntegrationEvents;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Production;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesFinishedGoodsCostAuthorityTests
{
    [Fact]
    public async Task Authority_read_returns_erp_capitalized_cost_only_for_exact_receipt_scope()
    {
        var options = CreateOptions();
        var completedAtUtc = DateTimeOffset.Parse("2026-08-25T08:00:00Z");
        await using (var seed = new ApplicationDbContext(options, new NoopMediator()))
        {
            var workOrder = CreateWorkOrder(completedAtUtc);
            workOrder.ApplyCapitalizedUnitCost(12.34m);
            seed.WorkOrders.Add(workOrder);
            seed.FinishedGoodsReceiptRequests.Add(FinishedGoodsReceiptRequest.Create(
                "org-001", "env-dev", "FGR-001", "WO-001", "FG-001", 5m, "ea", completedAtUtc));
            seed.ProcessedIntegrationEvents.Add(new ProcessedIntegrationEvent(
                WorkOrderCostCapitalizedIntegrationEventHandler.ConsumerName,
                "erp-cost-event-001",
                ErpIntegrationEventTypes.WorkOrderCostCapitalized,
                ErpIntegrationEventVersions.V1,
                ErpIntegrationEventSources.BusinessErp,
                "work-order-cost-capitalized:org-001:env-dev:WO-001",
                completedAtUtc));
            await seed.SaveChangesAsync();
        }

        await using var verification = new ApplicationDbContext(options, new NoopMediator());
        var response = await new GetFinishedGoodsReceiptCostAuthorityQueryHandler(verification).Handle(
            new GetFinishedGoodsReceiptCostAuthorityQuery(
                new MesFinishedGoodsReceiptCostAuthorityRequest(
                    "org-001",
                    "env-dev",
                    "FGR-001",
                    "WO-001",
                    "mes:finished-goods-receipt:org-001:env-dev:FGR-001")),
            CancellationToken.None);

        Assert.Equal(MesFinishedGoodsCostAuthorityStatuses.Available, response.Status);
        Assert.Equal(12.34m, response.CapitalizedUnitCost);
        Assert.Equal("erp-cost-event-001", response.ProvenanceEventId);
    }

    [Fact]
    public async Task Authority_read_stays_pending_when_erp_provenance_is_missing()
    {
        var options = CreateOptions();
        var completedAtUtc = DateTimeOffset.Parse("2026-08-25T08:00:00Z");
        await using (var seed = new ApplicationDbContext(options, new NoopMediator()))
        {
            var workOrder = CreateWorkOrder(completedAtUtc);
            workOrder.ApplyCapitalizedUnitCost(12.34m);
            seed.WorkOrders.Add(workOrder);
            seed.FinishedGoodsReceiptRequests.Add(FinishedGoodsReceiptRequest.Create(
                "org-001", "env-dev", "FGR-001", "WO-001", "FG-001", 5m, "ea", completedAtUtc));
            await seed.SaveChangesAsync();
        }

        await using var verification = new ApplicationDbContext(options, new NoopMediator());
        var response = await new GetFinishedGoodsReceiptCostAuthorityQueryHandler(verification).Handle(
            new GetFinishedGoodsReceiptCostAuthorityQuery(
                new MesFinishedGoodsReceiptCostAuthorityRequest(
                    "org-001",
                    "env-dev",
                    "FGR-001",
                    "WO-001",
                    "mes:finished-goods-receipt:org-001:env-dev:FGR-001")),
            CancellationToken.None);

        Assert.Equal(MesFinishedGoodsCostAuthorityStatuses.Pending, response.Status);
        Assert.Equal("erp-capitalization-provenance-not-observed", response.ReasonCode);
        Assert.Null(response.CapitalizedUnitCost);
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-cost-authority-{Guid.CreateVersion7():N}")
            .Options;

    private static WorkOrder CreateWorkOrder(DateTimeOffset completedAtUtc)
    {
        var workOrder = WorkOrder.Create(
            "org-001", "env-dev", "WO-001", "FG-001", "PV-001", 5m, 10,
            completedAtUtc.AddHours(1), "ea");
        workOrder.MarkReleased();
        workOrder.Start(completedAtUtc.AddMinutes(-10));
        workOrder.RecordProductionProgress(5m, 0m, completedAtUtc);
        return workOrder;
    }
}
