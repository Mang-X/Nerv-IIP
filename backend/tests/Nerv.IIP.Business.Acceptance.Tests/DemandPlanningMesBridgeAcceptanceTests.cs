using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
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
            new ConvertPlanToWorkOrderCommandHandler(mesDb));
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
            var demandReference = suggestion.PeggingLinks
                .Select(x => x.DemandSourceReference)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
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
                    request.IdempotencyKey),
                cancellationToken);
            return new PlanningSuggestionDownstreamReference("BusinessMes", "WorkOrder", result.ReferenceId);
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
