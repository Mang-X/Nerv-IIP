using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.DemandSourceAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Queries;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

// DomainInvariant: #4095 已确认的独立净需求、累计供应与严格三色规则。
public sealed class MaterialDeliveryTests
{
    private static readonly DateTimeOffset Latest = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-1, -1, -1, true, "Green")]
    [InlineData(0, -1, -1, true, "Yellow")]
    [InlineData(-1, 0, -1, true, "Yellow")]
    [InlineData(-1, -1, 0, true, "Yellow")]
    [InlineData(1, -1, -1, false, "Red")]
    [InlineData(-1, 1, -1, false, "Red")]
    [InlineData(-1, -1, 1, false, "Red")]
    [InlineData(-1, -1, -1, false, "Yellow")]
    public void Strict_status_preserves_late_evidence_even_with_missing_sources(int now, int arrival, int start, bool complete, string expected)
    {
        Assert.Equal(expected, MaterialDeliveryProjection.EvaluateStatus(Latest.AddMinutes(now), Latest,
            Latest.AddMinutes(arrival), Latest.AddMinutes(start), complete).ToString());
        Assert.Equal("Yellow", MaterialDeliveryProjection.EvaluateStatus(Latest, null, null, null, false).ToString());
    }

    [Fact]
    public async Task Batched_requirement_keeps_one_gap_and_all_line_sources_and_uses_cumulative_PO_date()
    {
        var services = new ServiceCollection();
        services.AddMediatR(x => x.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(x => x.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var run = new MrpRunId(Guid.NewGuid());
        var due = new DateOnly(2026, 10, 12);
        var suggestion = PlanningSuggestion.Create("org", "env", run, "planned-work-order", "SKU", "EA", "SITE", 20,
            due, due.AddDays(-3), "net-requirement");
        suggestion.SetNetRequirementExplanation(12, 2, 0, 2, 0, 0, 10, 20, 0, 1, "sales-order", "gross - available", null);
        suggestion.AddPeggingLink("demand", "SO", "SKU", null, 5, null, null, null, "sales-order", 5, "10");
        suggestion.AddPeggingLink("demand", "SO", "SKU", null, 7, null, null, null, "sales-order", 7, "20");
        suggestion.Accept("BusinessMes", "WorkOrder", "WO");
        db.PlanningSuggestions.Add(suggestion);
        db.DemandSources.AddRange(
            DemandSource.CreateSalesOrderDemand("org", "env", "SO-ID", "SO", "10", "C", "SKU", "EA", "SITE", 5, due, 1),
            DemandSource.CreateSalesOrderDemand("org", "env", "SO-ID", "SO", "20", "C", "SKU", "EA", "SITE", 7, due.AddDays(-1), 1),
            DemandSource.CreateSalesOrderDemand("other", "env", "SO-ID", "SO", "20", "C", "SKU", "EA", "SITE", 7, due.AddDays(-5), 1));
        await db.SaveChangesAsync();
        var upstream = new Sources(suggestion.Id.ToString());
        var handler = new GetMaterialDeliveriesQueryHandler(db, upstream, new FixedTime(Latest.AddDays(-2)));
        var result = await handler.Handle(new("org", "env", run, "PLAN"), default);
        var row = Assert.Single(result.Items);
        Assert.Equal(10, row.NetRequirementQuantity);
        Assert.Equal(20, row.NetRequirementSource.PlannedQuantity);
        Assert.Equal(2, row.DemandSources.Count);
        Assert.Equal(new[] { "10", "20" }, row.DemandSources.Select(x => x.SourceLineReference));
        Assert.Equal(suggestion.ReleaseDate, row.LatestProcurementDate);
        Assert.Equal(new DateOnly(2026, 10, 9), row.ExpectedArrivalDate);
        Assert.Equal(10, row.CoveredQuantity);
        Assert.Equal(2, row.SupplySources.Count);
        Assert.Equal("Green", row.Status.ToString());
        Assert.Equal("PLAN", result.PlanId);
        Assert.Equal("WO", Assert.Single(row.SchedulingSources).WorkOrderId);
        Assert.Equal(2, Assert.Single(upstream.Selections).DueSources.Count);
        Assert.Equal(new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero), upstream.Selections.Single().DueSources.Min(x => x.DueUtc));

        upstream.Supply = upstream.Supply.Take(1).ToArray();
        var shortage = Assert.Single((await handler.Handle(new("org", "env", run, "PLAN"), default)).Items);
        Assert.Null(shortage.ExpectedArrivalDate);
        Assert.Equal(6, shortage.UncoveredQuantity);
        Assert.Equal("Yellow", shortage.Status.ToString());
        Assert.Contains("supply-insufficient", shortage.Reasons);

        var noPlan = Assert.Single((await handler.Handle(new("org", "env", run, null), default)).Items);
        Assert.Null(noPlan.ExpectedStartUtc);
        Assert.Equal("Yellow", noPlan.Status.ToString());
        Assert.Contains("plan-not-selected", noPlan.Reasons);
        suggestion.AddPeggingLink("demand", "SO", "SKU", null, 1, null, null, null, "sales", 1);
        await db.SaveChangesAsync();
        var unknown = Assert.Single((await handler.Handle(new("org", "env", run, "PLAN"), default)).Items);
        var legacy = Assert.Single(unknown.DemandSources, x => x.SourceLineReference is null);
        Assert.Null(legacy.DueDate);
        Assert.Null(legacy.DemandSourceId);
        Assert.Equal("Yellow", unknown.Status.ToString());
        Assert.Contains("demand-due-source-missing", unknown.Reasons);
    }

    [Fact]
    public void Procurement_rows_use_exact_parent_path_and_keep_unknown_sales_line_identity()
    {
        var run = new MrpRunId(Guid.NewGuid());
        var date = new DateOnly(2026, 10, 12);
        var parent = PlanningSuggestion.Create("org", "env", run, "planned-work-order", "FG", "EA", "SITE", 10,
            date, date.AddDays(-1), "net-requirement", new PlanningSuggestionId(Guid.NewGuid()));
        parent.AddPeggingLink("demand", "SO", "FG", null, 10, null, null, null, "sales", 10, "10");
        var component = PlanningSuggestion.Create("org", "env", run, "planned-purchase", "RM", "EA", "SITE", 20,
            parent.ReleaseDate, date.AddDays(-3), "component-net-requirement", new PlanningSuggestionId(Guid.NewGuid()));
        component.AddPeggingLink("demand", "SO", "FG", "RM", 20, null, null, null, "sales", 20, "10");
        Assert.True(parent.IsAssemblyParentOf(component));
        var wrongDate = PlanningSuggestion.Create("org", "env", run, "planned-purchase", "RM", "EA", "SITE", 20,
            date, date.AddDays(-3), "component-net-requirement", new PlanningSuggestionId(Guid.NewGuid()));
        wrongDate.AddPeggingLink("demand", "SO", "FG", "RM", 20, null, null, null, "sales", 20, "10");
        Assert.False(parent.IsAssemblyParentOf(wrongDate));
        var legacy = PlanningSuggestion.Create("org", "env", run, "planned-purchase", "RM", "EA", "SITE", 20,
            parent.ReleaseDate, date.AddDays(-3), "component-net-requirement", new PlanningSuggestionId(Guid.NewGuid()));
        legacy.AddPeggingLink("demand", "SO", "FG", "RM", 20, null, null, null, "sales", 20);
        Assert.False(parent.IsAssemblyParentOf(legacy));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Sources(string suggestionId) : IMaterialDeliverySourcesClient
    {
        public IReadOnlyCollection<MaterialDeliverySupplySource> Supply { get; set; } =
        [new("PO-1", "10", "SITE", "SKU", "EA", new(2026, 10, 8), 4, []),
         new("PO-2", "20", "SITE", "SKU", "EA", new(2026, 10, 9), 6, []),
         new("WRONG-UOM", "10", "SITE", "SKU", "BOX", new(2026, 10, 8), 100, []),
         new("WRONG-SITE", "10", "OTHER", "SKU", "EA", new(2026, 10, 8), 100, [])];
        public IReadOnlyCollection<MaterialDeliverySourceSelection> Selections { get; private set; } = [];
        public Task<IReadOnlyCollection<MaterialDeliverySupplySource>> GetSupplyAsync(string org, string env, CancellationToken ct) => Task.FromResult(Supply);
        public Task<MaterialDeliverySourcesResponse> GetSchedulingAsync(string org, string env, string plan,
            IReadOnlyCollection<MaterialDeliverySourceSelection> sources, CancellationToken ct)
        {
            Selections = sources;
            var bounds = sources.Single().DueSources.Select(x => new MaterialDeliveryBoundContract(x.SourceReference, x.DueUtc, 1440,
                x.DueUtc.AddDays(-1), ["OP"])).ToArray();
            return Task.FromResult(new MaterialDeliverySourcesResponse(plan,
                [new(suggestionId, "WO", "scheduled", Latest.AddDays(-1), bounds.Min(x => x.LatestStartUtc),
                    bounds.MinBy(x => x.LatestStartUtc)!.SourceReference,
                    [new("OP", 10, "created", 0, 20, 1440, Latest.AddDays(-2), Latest.AddDays(-1), "scheduled", [], "ROUTE")], bounds)]));
        }
    }
}
