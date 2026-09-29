using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;
using Nerv.IIP.Contracts.DemandPlanning;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesDemandPlanningBridgeTests
{
    [Fact]
    public async Task Assembly_parent_relation_resolves_after_parent_work_order_is_created()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var handler = new PlanningSuggestionAcceptedIntegrationEventHandlerForCreateMesWorkOrder(
            dbContext, new InMemoryIntegrationEventDeadLetterStore(),
            routingSnapshotProvider: SingleOperationRoutingSnapshotProvider.Instance);
        var acceptedAtUtc = DateTimeOffset.Parse("2026-09-29T08:00:00Z");
        var parentEvent = NewAcceptedSuggestionEvent(acceptedAtUtc, "SUG-PARENT") with
        {
            Payload = NewAcceptedSuggestionEvent(acceptedAtUtc, "SUG-PARENT").Payload with
            {
                SkuCode = "SKU-ASSEMBLY",
            },
        };
        var childEvent = NewAcceptedSuggestionEvent(acceptedAtUtc, "SUG-CHILD") with
        {
            Payload = NewAcceptedSuggestionEvent(acceptedAtUtc, "SUG-CHILD").Payload with
            {
                SkuCode = "SKU-COMPONENT",
                AssemblyParentSuggestionIds = ["SUG-PARENT"],
            },
        };

        await handler.HandleAsync(childEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var child = Assert.Single(await dbContext.WorkOrders.ToListAsync(CancellationToken.None));
        Assert.Equal(["SUG-PARENT"], child.SourcePlanReference?.AssemblyParentSuggestionIds);

        await handler.HandleAsync(parentEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var parent = Assert.Single(await dbContext.WorkOrders
            .Where(x => x.SkuId == "SKU-ASSEMBLY")
            .ToListAsync(CancellationToken.None));
        var result = await new ListMesWorkOrdersQueryHandler(dbContext).Handle(
            new ListMesWorkOrdersQuery("org-001", "env-dev", null), CancellationToken.None);

        Assert.Equal([parent.WorkOrderId], Assert.Single(result.Items, x => x.WorkOrderId == child.WorkOrderId)
            .AssemblyParentWorkOrderIds);
        Assert.Empty(Assert.Single(result.Items, x => x.WorkOrderId == parent.WorkOrderId)
            .AssemblyParentWorkOrderIds!);
        Assert.Null(child.SourceWorkOrderId);
    }

    [Fact]
    public async Task Accepted_suggestion_event_adds_pegged_parent_to_existing_work_orders_from_same_suggestion()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-CHILD", "SKU-COMPONENT", "PV-001",
            12m, 100, DateTimeOffset.Parse("2026-10-10T00:00:00Z"), "PCS",
            new SourcePlanReference("DemandPlanning", "PlanningSuggestion", "SUG-CHILD", "SO-1"));
        dbContext.WorkOrders.AddRange(workOrder, WorkOrder.Create(
            "org-001", "env-dev", "WO-CHILD-SPLIT", "SKU-COMPONENT", "PV-001",
            6m, 100, DateTimeOffset.Parse("2026-10-10T00:00:00Z"), "PCS",
            new SourcePlanReference("DemandPlanning", "PlanningSuggestion", "SUG-CHILD", "SO-1")));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var acceptedEvent = NewAcceptedSuggestionEvent(DateTimeOffset.Parse("2026-09-29T08:00:00Z"), "SUG-CHILD") with
        {
            Payload = NewAcceptedSuggestionEvent(DateTimeOffset.Parse("2026-09-29T08:00:00Z"), "SUG-CHILD").Payload with
            {
                AssemblyParentSuggestionIds = ["SUG-PARENT"],
            },
        };
        var handler = new PlanningSuggestionAcceptedIntegrationEventHandlerForCreateMesWorkOrder(
            dbContext, new InMemoryIntegrationEventDeadLetterStore());
        await handler.HandleAsync(acceptedEvent, CancellationToken.None);
        await handler.HandleAsync(acceptedEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        var persisted = await dbContext.WorkOrders.ToListAsync(CancellationToken.None);
        Assert.Equal(2, persisted.Count);
        Assert.All(persisted, order =>
        {
            Assert.Equal(["SUG-PARENT"], order.SourcePlanReference?.AssemblyParentSuggestionIds);
            Assert.Equal(2, order.Version);
        });
    }

    [Fact]
    public async Task Missing_routing_snapshot_is_dead_lettered_as_terminal_without_retry_poisoning()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new PlanningSuggestionAcceptedIntegrationEventHandlerForCreateMesWorkOrder(
            dbContext,
            deadLetters,
            routingSnapshotProvider: MissingRoutingSnapshotProvider.Instance);
        var acceptedAtUtc = DateTimeOffset.Parse("2026-07-21T08:00:00Z");
        var integrationEvent = NewAcceptedSuggestionEvent(acceptedAtUtc, "SUG-MISSING-ROUTING");

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(await dbContext.WorkOrders.ToListAsync(CancellationToken.None));
        Assert.Contains(
            await deadLetters.ListAsync(
                PlanningSuggestionAcceptedIntegrationEventHandlerForCreateMesWorkOrder.ConsumerName,
                IntegrationEventDeadLetterStatus.Pending,
                CancellationToken.None),
            x => x.FailureCode == "mes.planningSuggestionAccepted.routingSnapshotMissing");
    }

    [Fact]
    public async Task Accepted_suggestion_for_consumed_disabled_sku_is_terminally_rejected_without_retry_poisoning()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var changedAtUtc = DateTimeOffset.Parse("2026-07-18T08:00:00Z");
        var skuDisabledHandler = new SkuDisabledIntegrationEventHandlerForProjectMesSkuAvailability(dbContext, deadLetters);
        await skuDisabledHandler.HandleAsync(
            new SkuDisabledIntegrationEvent(
                "evt-sku-disabled-demand",
                MasterDataIntegrationEventTypes.SkuDisabled,
                MasterDataIntegrationEventVersions.V1,
                changedAtUtc,
                MasterDataIntegrationEventSources.BusinessMasterData,
                "corr-sku-disabled-demand",
                "cause-sku-disabled-demand",
                "org-001",
                "env-dev",
                "user:masterdata-admin",
                "sku-disabled-demand",
                new MasterDataDisabledPayload("sku", "SKU-DISABLED", "disabled", "retired", changedAtUtc)),
            CancellationToken.None);

        var suggestionHandler = new PlanningSuggestionAcceptedIntegrationEventHandlerForCreateMesWorkOrder(dbContext, deadLetters);
        await suggestionHandler.HandleAsync(
            new PlanningSuggestionAcceptedIntegrationEvent(
                "evt-demand-disabled-sku",
                DemandPlanningIntegrationEventTypes.PlanningSuggestionAccepted,
                DemandPlanningIntegrationEventVersions.V1,
                changedAtUtc.AddMinutes(1),
                DemandPlanningIntegrationEventSources.BusinessDemandPlanning,
                "corr-demand-disabled-sku",
                "cause-demand-disabled-sku",
                "org-001",
                "env-dev",
                "user:planner",
                "demand-disabled-sku",
                new PlanningSuggestionAcceptedPayload(
                    "SUG-DISABLED-SKU",
                    "MRP-001",
                    "planned-work-order",
                    "SKU-DISABLED",
                    "PCS",
                    "SITE-A",
                    12m,
                    new DateOnly(2026, 7, 31),
                    new DateOnly(2026, 7, 18),
                    "DEMAND-001",
                    "PV-001",
                    "BusinessMes",
                    "WorkOrder",
                    null)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(await dbContext.WorkOrders.ToListAsync(CancellationToken.None));
        Assert.Equal(2, await dbContext.ProcessedIntegrationEvents.CountAsync(CancellationToken.None));
        Assert.Contains(
            await deadLetters.ListAsync(
                PlanningSuggestionAcceptedIntegrationEventHandlerForCreateMesWorkOrder.ConsumerName,
                IntegrationEventDeadLetterStatus.Pending,
                CancellationToken.None),
            x => x.FailureCode == "mes.planningSuggestionAccepted.skuDisabled");
    }

    [Fact]
    public async Task Accepted_planned_work_order_suggestion_creates_queryable_mes_work_order()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new PlanningSuggestionAcceptedIntegrationEventHandlerForCreateMesWorkOrder(
            dbContext,
            deadLetters,
            routingSnapshotProvider: SingleOperationRoutingSnapshotProvider.Instance);
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

    [Fact]
    public async Task Accepted_batched_suggestion_persists_every_demand_reference_and_lights_traceability_for_each_order()
    {
        // #1286：CAP 消费路径同样必须持久化合批建议的全部需求源引用，追溯读面为每张订单点亮 pegged-to-plan 边。
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new PlanningSuggestionAcceptedIntegrationEventHandlerForCreateMesWorkOrder(
            dbContext,
            deadLetters,
            routingSnapshotProvider: SingleOperationRoutingSnapshotProvider.Instance);
        var acceptedAtUtc = DateTimeOffset.Parse("2026-07-30T08:00:00Z");
        var integrationEvent = new PlanningSuggestionAcceptedIntegrationEvent(
            EventId: "evt-demand-mes-batched-001",
            EventType: DemandPlanningIntegrationEventTypes.PlanningSuggestionAccepted,
            EventVersion: DemandPlanningIntegrationEventVersions.V1,
            OccurredAtUtc: acceptedAtUtc,
            SourceService: DemandPlanningIntegrationEventSources.BusinessDemandPlanning,
            CorrelationId: "corr-demand-mes-batched-001",
            CausationId: "cmd-accept-suggestion-batched-001",
            OrganizationId: "org-001",
            EnvironmentId: "env-dev",
            Actor: "user:planner",
            IdempotencyKey: "demand-planning:planning-suggestion-accepted:org-001:env-dev:SUG-WO-BATCH-001",
            Payload: new PlanningSuggestionAcceptedPayload(
                SuggestionId: "SUG-WO-BATCH-001",
                MrpRunId: "MRP-001",
                SuggestionType: "planned-work-order",
                SkuCode: "SKU-FG-1000",
                UomCode: "PCS",
                SiteCode: "SITE-A",
                Quantity: 220m,
                RequiredDate: new DateOnly(2026, 8, 20),
                ReleaseDate: new DateOnly(2026, 8, 14),
                DemandSourceReference: "SO-2026-00001",
                ProductionVersionReference: "PV-FG-1000",
                DownstreamService: "BusinessMes",
                DownstreamDocumentType: "WorkOrder",
                DownstreamDocumentId: null,
                DemandSourceReferences: ["SO-2026-00001", "SO-20260730-000005"]));

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var workOrder = Assert.Single(await dbContext.WorkOrders.ToListAsync(CancellationToken.None));
        Assert.Equal("SUG-WO-BATCH-001", workOrder.SourcePlanReference?.SourceDocumentId);
        Assert.Equal("SO-2026-00001", workOrder.SourcePlanReference?.SourceDemandReference);
        Assert.Equal(
            new[] { "SO-2026-00001", "SO-20260730-000005" },
            workOrder.SourcePlanReference?.SourceDemandReferences);

        var traceability = await new GetWorkOrderTraceabilityQueryHandler(dbContext).Handle(
            new GetWorkOrderTraceabilityQuery("org-001", "env-dev", workOrder.WorkOrderId),
            CancellationToken.None);
        foreach (var salesOrderNo in new[] { "SO-2026-00001", "SO-20260730-000005" })
        {
            Assert.Contains(traceability.Nodes, node =>
                node.NodeId == salesOrderNo && node.NodeType == "DemandSource");
            Assert.Contains(traceability.Edges, edge =>
                edge.FromNodeId == salesOrderNo &&
                edge.ToNodeId == "SUG-WO-BATCH-001" &&
                edge.RelationType == "pegged-to-plan");
        }

        Assert.Contains(traceability.Edges, edge =>
            edge.FromNodeId == "SUG-WO-BATCH-001" &&
            edge.ToNodeId == workOrder.WorkOrderId &&
            edge.RelationType == "converted-to-work-order");
    }

    private static PlanningSuggestionAcceptedIntegrationEvent NewAcceptedSuggestionEvent(
        DateTimeOffset acceptedAtUtc,
        string suggestionId)
    {
        return new PlanningSuggestionAcceptedIntegrationEvent(
            EventId: $"evt-{suggestionId}",
            EventType: DemandPlanningIntegrationEventTypes.PlanningSuggestionAccepted,
            EventVersion: DemandPlanningIntegrationEventVersions.V1,
            OccurredAtUtc: acceptedAtUtc,
            SourceService: DemandPlanningIntegrationEventSources.BusinessDemandPlanning,
            CorrelationId: $"corr-{suggestionId}",
            CausationId: $"cause-{suggestionId}",
            OrganizationId: "org-001",
            EnvironmentId: "env-dev",
            Actor: "user:planner",
            IdempotencyKey: $"demand-planning:planning-suggestion-accepted:org-001:env-dev:{suggestionId}",
            Payload: new PlanningSuggestionAcceptedPayload(
                suggestionId,
                "MRP-001",
                "planned-work-order",
                "SKU-FG-1000",
                "PCS",
                "SITE-A",
                12m,
                new DateOnly(2026, 7, 31),
                new DateOnly(2026, 7, 21),
                "DEMAND-001",
                "PV-001",
                "BusinessMes",
                "WorkOrder",
                null));
    }

    private sealed class MissingRoutingSnapshotProvider : IMesRoutingSnapshotProvider
    {
        public static readonly MissingRoutingSnapshotProvider Instance = new();

        public Task<MesRoutingSnapshotResult> GetSnapshotAsync(
            MesRoutingSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(MesRoutingSnapshotResult.Missing("product-engineering:routing:missing"));
        }
    }
}
