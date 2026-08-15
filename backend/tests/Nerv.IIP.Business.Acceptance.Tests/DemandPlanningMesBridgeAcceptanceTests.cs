using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;
using DemandPlanningDbContext = Nerv.IIP.Business.DemandPlanning.Infrastructure.ApplicationDbContext;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

public sealed class DemandPlanningMesBridgeAcceptanceTests
{
    [Fact]
    public async Task DemandPlanning_accept_creates_mes_work_order_and_persists_real_downstream_id()
    {
        await using var demandProvider = CreateDemandPlanningProvider();
        await using var mesProvider = CreateMesProvider();
        using var demandScope = demandProvider.CreateScope();
        using var mesScope = mesProvider.CreateScope();
        var demandDb = demandScope.ServiceProvider.GetRequiredService<DemandPlanningDbContext>();
        var mesDb = mesScope.ServiceProvider.GetRequiredService<MesDbContext>();
        var suggestion = PlanningSuggestion.Create(
            "org-001",
            "env-dev",
            new(Guid.CreateVersion7()),
            "planned-work-order",
            "SKU-FG-1000",
            "PCS",
            "SITE-A",
            12m,
            new DateOnly(2026, 6, 30),
            new DateOnly(2026, 6, 24),
            "MRP");
        suggestion.AddPeggingLink("demand", "DEMAND-001", "SKU-FG-1000", null, 12m, "PV-FG-1000", "MBOM-001", "ROUTING-001");
        demandDb.PlanningSuggestions.Add(suggestion);
        await demandDb.SaveChangesAsync(CancellationToken.None);

        var mesBridge = new MesCommandPlanningSuggestionDownstreamBridge(
            new ConvertPlanToWorkOrderCommandHandler(
                mesDb,
                new RuleScheduler(),
                null,
                null,
                new PostgreSqlMesSkuAvailabilityScopeCoordinator(mesDb),
                AcceptanceRoutingSnapshotProvider.Instance));
        await new AcceptPlanningSuggestionCommandHandler(demandDb, mesBridge).Handle(
            new AcceptPlanningSuggestionCommand(suggestion.Id, "BusinessMes", "WorkOrder", null),
            CancellationToken.None);
        await demandDb.SaveChangesAsync(CancellationToken.None);
        await mesDb.SaveChangesAsync(CancellationToken.None);

        var accepted = await demandDb.PlanningSuggestions.SingleAsync(CancellationToken.None);
        var workOrder = Assert.Single(await mesDb.WorkOrders.ToListAsync(CancellationToken.None));
        Assert.Equal("BusinessMes", accepted.AcceptedDownstreamService);
        Assert.Equal("WorkOrder", accepted.AcceptedDownstreamDocumentType);
        Assert.Equal(workOrder.WorkOrderId, accepted.AcceptedDownstreamDocumentId);
        Assert.StartsWith("WO-", accepted.AcceptedDownstreamDocumentId, StringComparison.Ordinal);
        Assert.Equal("SKU-FG-1000", workOrder.SkuId);
        Assert.Equal("PV-FG-1000", workOrder.ProductionVersionId);
        Assert.Equal(12m, workOrder.Quantity);
        Assert.Equal(suggestion.Id.ToString(), workOrder.SourcePlanReference?.SourceDocumentId);
        Assert.Equal("DEMAND-001", workOrder.SourcePlanReference?.SourceDemandReference);

        var productionPlans = await new ListProductionPlansQueryHandler(mesDb).Handle(
            new ListProductionPlansQuery("org-001", "env-dev", null, Keyword: workOrder.SourcePlanReference?.SourceDocumentId, Take: 10),
            CancellationToken.None);
        Assert.Equal(workOrder.SourcePlanReference?.SourceDocumentId, Assert.Single(productionPlans.Items).ProductionPlanId);
    }

    [Fact]
    public async Task Real_chain_accept_of_batched_suggestion_lights_traceability_for_every_pegged_sales_order()
    {
        // #1286：真实链路（接受建议→桥接→MES 建单）。MRP 合批建议 peg 到多张销售订单，
        // 追溯读面必须为每张订单输出 pegged-to-plan → converted-to-work-order 因果链（与 #1276 种子黄金测试同形）。
        await using var demandProvider = CreateDemandPlanningProvider();
        await using var mesProvider = CreateMesProvider();
        using var demandScope = demandProvider.CreateScope();
        using var mesScope = mesProvider.CreateScope();
        var demandDb = demandScope.ServiceProvider.GetRequiredService<DemandPlanningDbContext>();
        var mesDb = mesScope.ServiceProvider.GetRequiredService<MesDbContext>();
        var suggestion = PlanningSuggestion.Create(
            "org-001",
            "env-dev",
            new(Guid.CreateVersion7()),
            "planned-work-order",
            "SKU-FG-1000",
            "PCS",
            "SITE-A",
            220m,
            new DateOnly(2026, 8, 20),
            new DateOnly(2026, 8, 14),
            "MRP");
        // 合批：同 SKU 同交期的两张销售订单 peg 到同一条建议；scheduled-receipt 引用不是需求源。
        suggestion.AddPeggingLink("demand", "SO-2026-00001", "SKU-FG-1000", null, 100m, "PV-FG-1000", "MBOM-001", "ROUTING-001");
        suggestion.AddPeggingLink("demand", "SO-20260730-000005", "SKU-FG-1000", null, 120m, "PV-FG-1000", "MBOM-001", "ROUTING-001");
        suggestion.AddPeggingLink("scheduled-receipt", "erp:purchase-order:PO-0001", "SKU-FG-1000", null, 10m, null, null, null);
        demandDb.PlanningSuggestions.Add(suggestion);
        await demandDb.SaveChangesAsync(CancellationToken.None);

        var mesBridge = new MesCommandPlanningSuggestionDownstreamBridge(
            new ConvertPlanToWorkOrderCommandHandler(
                mesDb,
                new RuleScheduler(),
                null,
                null,
                new PostgreSqlMesSkuAvailabilityScopeCoordinator(mesDb),
                AcceptanceRoutingSnapshotProvider.Instance));
        await new AcceptPlanningSuggestionCommandHandler(demandDb, mesBridge).Handle(
            new AcceptPlanningSuggestionCommand(suggestion.Id, "BusinessMes", "WorkOrder", null),
            CancellationToken.None);
        await demandDb.SaveChangesAsync(CancellationToken.None);
        await mesDb.SaveChangesAsync(CancellationToken.None);

        var suggestionId = suggestion.Id.ToString();
        var workOrder = Assert.Single(await mesDb.WorkOrders.ToListAsync(CancellationToken.None));
        Assert.NotNull(workOrder.SourcePlanReference);
        Assert.Equal("DemandPlanning", workOrder.SourcePlanReference!.SourceSystem);
        Assert.Equal("PlanningSuggestion", workOrder.SourcePlanReference.SourceDocumentType);
        Assert.Equal(suggestionId, workOrder.SourcePlanReference.SourceDocumentId);
        Assert.Equal(
            new[] { "SO-2026-00001", "SO-20260730-000005" },
            workOrder.SourcePlanReference.SourceDemandReferences);

        var traceability = await new GetWorkOrderTraceabilityQueryHandler(mesDb).Handle(
            new GetWorkOrderTraceabilityQuery("org-001", "env-dev", workOrder.WorkOrderId),
            CancellationToken.None);

        Assert.Contains(traceability.Nodes, node =>
            node.NodeId == suggestionId && node.NodeType == "PlanningSuggestion");
        foreach (var salesOrderNo in new[] { "SO-2026-00001", "SO-20260730-000005" })
        {
            Assert.Contains(traceability.Nodes, node =>
                node.NodeId == salesOrderNo && node.NodeType == "DemandSource");
            Assert.Contains(traceability.Edges, edge =>
                edge.FromNodeId == salesOrderNo &&
                edge.ToNodeId == suggestionId &&
                edge.RelationType == "pegged-to-plan");
        }

        Assert.Contains(traceability.Edges, edge =>
            edge.FromNodeId == suggestionId &&
            edge.ToNodeId == workOrder.WorkOrderId &&
            edge.RelationType == "converted-to-work-order");
        // scheduled-receipt 引用不是需求源，不得伪造 pegged-to-plan 边。
        Assert.DoesNotContain(traceability.Edges, edge =>
            edge.FromNodeId == "erp:purchase-order:PO-0001" && edge.RelationType == "pegged-to-plan");
    }

    private static ServiceProvider CreateDemandPlanningProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMediator, NoopMediator>();
        services.AddDbContext<DemandPlanningDbContext>(options =>
            options.UseInMemoryDatabase($"acceptance-demand-planning-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }

    private static ServiceProvider CreateMesProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMediator, NoopMediator>();
        services.AddDbContext<MesDbContext>(options =>
            options.UseInMemoryDatabase($"acceptance-mes-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }

    private sealed class MesCommandPlanningSuggestionDownstreamBridge(ConvertPlanToWorkOrderCommandHandler handler)
        : IPlanningSuggestionDownstreamBridge
    {
        public async Task<PlanningSuggestionDownstreamReference> CreateDownstreamAsync(
            PlanningSuggestion suggestion,
            PlanningSuggestionDownstreamRequest request,
            CancellationToken cancellationToken)
        {
            var productionVersion = suggestion.PeggingLinks
                .Select(x => x.ProductionVersionReference)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            // 与 HttpMesPlanningSuggestionDownstreamBridge 同形：完整携带 demand 类型需求源引用。
            // 主引用一律走聚合的 GetPrimaryDemandSourceReference()，不在测试里重抄一份回退规则
            // ——否则规则改了测试仍按旧口径断言，越绿越错。
            var demandReferences = suggestion.GetDemandSourceReferences();
            var demandReference = suggestion.GetPrimaryDemandSourceReference();
            var dueUtc = new DateTimeOffset(suggestion.RequiredDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var result = await handler.Handle(
                new ConvertPlanToWorkOrderCommand(
                    suggestion.OrganizationId,
                    suggestion.EnvironmentId,
                    suggestion.Id.ToString(),
                    null,
                    DateTimeOffset.Parse("2026-06-24T08:00:00Z"),
                    suggestion.SkuCode,
                    productionVersion,
                    suggestion.Quantity,
                    suggestion.UomCode,
                    dueUtc,
                    null,
                    "DemandPlanning",
                    "PlanningSuggestion",
                    suggestion.Id.ToString(),
                    demandReference,
                    request.IdempotencyKey,
                    demandReferences),
                cancellationToken);
            return new PlanningSuggestionDownstreamReference("BusinessMes", "WorkOrder", result.ReferenceId);
        }
    }

    private sealed class AcceptanceRoutingSnapshotProvider : IMesRoutingSnapshotProvider
    {
        public static readonly AcceptanceRoutingSnapshotProvider Instance = new();

        public Task<MesRoutingSnapshotResult> GetSnapshotAsync(
            MesRoutingSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(MesRoutingSnapshotResult.Captured(
                "acceptance:PV-FG-1000:ROUTING-001:A",
                [new MesRoutingOperationSnapshot(10, "MIX", "WC-MIX", [], 30, false)]));
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
