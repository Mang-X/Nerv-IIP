using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Contracts.DemandPlanning;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesDemandPlanningBridgeTests
{
    [Fact]
    public async Task Accepted_planned_work_order_suggestion_creates_queryable_mes_work_order()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new PlanningSuggestionAcceptedIntegrationEventHandlerForCreateMesWorkOrder(dbContext, deadLetters);
        var acceptedAtUtc = DateTimeOffset.Parse("2026-06-24T08:00:00Z");
        var integrationEvent = new PlanningSuggestionAcceptedIntegrationEvent(
            EventId: "evt-demand-mes-001",
            EventType: DemandPlanningIntegrationEventTypes.PlanningSuggestionAccepted,
            EventVersion: DemandPlanningIntegrationEventVersions.V1,
            OccurredAtUtc: acceptedAtUtc,
            SourceService: DemandPlanningIntegrationEventSources.BusinessDemandPlanning,
            CorrelationId: "corr-demand-mes-001",
            CausationId: "cmd-accept-suggestion-001",
            OrganizationId: "org-001",
            EnvironmentId: "env-dev",
            Actor: "user:planner",
            IdempotencyKey: "demand-planning:planning-suggestion-accepted:org-001:env-dev:SUG-WO-001",
            Payload: new PlanningSuggestionAcceptedPayload(
                SuggestionId: "SUG-WO-001",
                MrpRunId: "MRP-001",
                SuggestionType: "planned-work-order",
                SkuCode: "SKU-FG-1000",
                UomCode: "PCS",
                SiteCode: "SITE-A",
                Quantity: 12m,
                RequiredDate: new DateOnly(2026, 6, 30),
                ReleaseDate: new DateOnly(2026, 6, 24),
                DemandSourceReference: "DEMAND-001",
                ProductionVersionReference: "PV-FG-1000",
                DownstreamService: "BusinessMes",
                DownstreamDocumentType: "WorkOrder",
                DownstreamDocumentId: null));

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var workOrder = Assert.Single(await dbContext.WorkOrders.ToListAsync(CancellationToken.None));
        Assert.StartsWith("WO-", workOrder.WorkOrderId);
        Assert.Equal("SKU-FG-1000", workOrder.SkuId);
        Assert.Equal("PV-FG-1000", workOrder.ProductionVersionId);
        Assert.Equal(12m, workOrder.Quantity);
        Assert.Equal("PCS", workOrder.UomCode);
        Assert.Equal("DemandPlanning", workOrder.SourcePlanReference?.SourceSystem);
        Assert.Equal("PlanningSuggestion", workOrder.SourcePlanReference?.SourceDocumentType);
        Assert.Equal("SUG-WO-001", workOrder.SourcePlanReference?.SourceDocumentId);
        Assert.Equal("DEMAND-001", workOrder.SourcePlanReference?.SourceDemandReference);
        Assert.Equal(1, await dbContext.ProcessedIntegrationEvents.CountAsync(
            x => x.ConsumerName == PlanningSuggestionAcceptedIntegrationEventHandlerForCreateMesWorkOrder.ConsumerName,
            CancellationToken.None));
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));

        var productionPlans = await new ListProductionPlansQueryHandler(dbContext).Handle(
            new ListProductionPlansQuery("org-001", "env-dev", null, Keyword: "SUG-WO-001", Take: 10),
            CancellationToken.None);
        var productionPlan = Assert.Single(productionPlans.Items);
        Assert.Equal("SUG-WO-001", productionPlan.ProductionPlanId);
        Assert.Equal("SUG-WO-001", productionPlan.SourceDocumentId);
        Assert.Equal("created", productionPlan.Status);
    }
}
