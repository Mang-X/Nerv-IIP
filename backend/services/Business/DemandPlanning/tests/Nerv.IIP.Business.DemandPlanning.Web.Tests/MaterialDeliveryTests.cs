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
            due, due.AddDays(-3), "net-requirement", netRequirementReference: Guid.NewGuid());
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
        var upstream = new Sources();
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

    [Fact]
    public async Task Lot_max_twelve_split_keeps_one_thirty_unit_requirement_and_all_three_suggestions()
    {
        var services = new ServiceCollection();
        services.AddMediatR(x => x.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(x => x.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var run = new MrpRunId(Guid.NewGuid());
        var due = new DateOnly(2026, 10, 12);
        var input = new MrpCalculationInput("org", "env", due.AddDays(-5), due.AddDays(5),
            [new("SO", "SKU", "EA", "SITE", 30, due, "sales-order", "10")], [],
            [new("SKU", "PV", "MBOM", "ROUTE", null, 12, null)], [], [], [], []);
        var calculated = MrpCalculator.Calculate(input).Where(x => x.SuggestionType == "planned-work-order").ToArray();
        Assert.Equal(new decimal[] { 12, 12, 6 }, calculated.Select(x => x.Quantity));
        AddCalculatedSuggestions(db, run, calculated);
        db.DemandSources.Add(DemandSource.CreateSalesOrderDemand("org", "env", "SO-ID", "SO", "10", "C", "SKU", "EA", "SITE", 30, due, 1));
        await db.SaveChangesAsync();
        var upstream = new Sources();
        var handler = new GetMaterialDeliveriesQueryHandler(db, upstream, new FixedTime(Latest.AddDays(-2)));
        var result = await handler.Handle(new("org", "env", run, null), default);
        var row = Assert.Single(result.Items);
        Assert.Equal(30, row.NetRequirementQuantity);
        Assert.Equal(30, row.NetRequirementSource.PlannedQuantity);
        Assert.Single(row.DemandSources);
        Assert.Equal(3, row.SuggestionSources.Count);
        Assert.Equal(new decimal[] { 6, 12, 12 }, row.SuggestionSources.Select(x => x.PlannedQuantity).Order());
        Assert.Equal(db.PlanningSuggestions.Select(x => x.Id.ToString()).Order(), row.SuggestionSources.Select(x => x.SuggestionId).Order());
        var index = 0;
        foreach (var batch in db.PlanningSuggestions)
            batch.Accept("BusinessMes", "WorkOrder", $"WO-{++index}");
        await db.SaveChangesAsync();
        upstream.Supply = [new("PO", "10", "SITE", "SKU", "EA", new(2026, 10, 9), 30, [])];
        var scheduled = Assert.Single((await handler.Handle(new("org", "env", run, "PLAN"), default)).Items);
        Assert.Equal(3, scheduled.SchedulingSources.Count);
        Assert.Equal(3, scheduled.SchedulingSources.Select(x => x.WorkOrderId).Distinct().Count());
        Assert.Equal(scheduled.SuggestionSources.Select(x => x.SuggestionId).Order(), scheduled.SchedulingSources.Select(x => x.SuggestionId).Order());
        Assert.Equal(30, scheduled.CoveredQuantity);
        Assert.Equal("Green", scheduled.Status.ToString());
    }

    [Fact]
    public async Task Normal_and_reserve_components_on_same_day_remain_distinct_net_requirements()
    {
        var services = new ServiceCollection();
        services.AddMediatR(x => x.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(x => x.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var run = new MrpRunId(Guid.NewGuid());
        var due = new DateOnly(2026, 10, 12);
        var input = new MrpCalculationInput("org", "env", due.AddDays(-5), due,
            [new("SO", "FG", "EA", "SITE", 5, due, "sales-order", "10")], [],
            [new("FG", "PV", "MBOM", "ROUTE")], [new("FG", "RM", "EA", 1)], [],
            [new("FG", "EA", "SITE", 1, 3, null, null, null, "make"),
             new("RM", "EA", "SITE", 0, 0, null, null, null, "buy")], []);
        var calculated = MrpCalculator.Calculate(input);
        var components = calculated.Where(x => x.SkuCode == "RM" && x.SuggestionType == "planned-purchase").ToArray();
        Assert.Equal(new decimal[] { 3, 5 }, components.Select(x => x.NetRequirementExplanation.NetRequirementQuantity).Order());
        Assert.All(components, x => Assert.Equal("component", x.NetRequirementExplanation.PrimarySourceType));
        Assert.All(components, x => Assert.Equal(due.AddDays(-1), x.RequiredDate));
        AddCalculatedSuggestions(db, run, calculated.Where(x => x.SuggestionType is "planned-purchase" or "planned-work-order"));
        await db.SaveChangesAsync();
        var handler = new GetMaterialDeliveriesQueryHandler(db, new Sources(), new FixedTime(Latest.AddDays(-2)));
        var result = await handler.Handle(new("org", "env", run, null), default);
        var rows = result.Items.Where(x => x.SkuCode == "RM").ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal(new decimal[] { 3, 5 }, rows.Select(x => x.NetRequirementQuantity).Order());
        Assert.Equal(8, rows.Sum(x => x.NetRequirementQuantity));
        Assert.All(rows, x => Assert.Single(x.SuggestionSources));
        Assert.All(rows, x => Assert.Equal(x.NetRequirementQuantity, x.NetRequirementSource.PlannedQuantity));
    }

    [Fact]
    public async Task Identical_sources_and_explanations_keep_two_persisted_requirements_after_rejection()
    {
        var services = new ServiceCollection();
        services.AddMediatR(x => x.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(x => x.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var run = new MrpRunId(Guid.NewGuid());
        var input = NetRequirementIdentityTests.CollisionInput() with
        {
            Demands = [new("SKU", "SKU", "EA", "SITE", 3, NetRequirementIdentityTests.Date, "safety-stock")],
            PlanningParameters = [new("SKU", "EA", "SITE", 0, 3, null, null, null, "buy")]
        };
        var calculated = MrpCalculator.Calculate(input).ToArray();
        Assert.Equal(2, calculated.Length);
        Assert.Equal(calculated[0].PeggingLinks.ToArray(), calculated[1].PeggingLinks.ToArray());
        Assert.Equal(calculated[0].NetRequirementExplanation, calculated[1].NetRequirementExplanation);
        AddCalculatedSuggestions(db, run, calculated);
        await db.SaveChangesAsync();
        var handler = new GetMaterialDeliveriesQueryHandler(db, new Sources(), new FixedTime(Latest));
        var result = await handler.Handle(new("org", "env", run, null), default);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(calculated.Select(x => x.NetRequirementReference!.Value.ToString()).Order(),
            result.Items.Select(x => x.NetRequirementReference).Order());
        Assert.All(result.Items, x => { Assert.Equal(3, x.NetRequirementQuantity); Assert.Single(x.SuggestionSources); });
        foreach (var suggestion in db.PlanningSuggestions) suggestion.Reject("planner", "same reason");
        await db.SaveChangesAsync();
        var rejected = await handler.Handle(new("org", "env", run, null), default);
        Assert.Equal(result.Items.Select(x => x.NetRequirementReference).Order(), rejected.Items.Select(x => x.NetRequirementReference).Order());
        Assert.All(rejected.Items, x => Assert.Equal("Rejected", Assert.Single(x.SuggestionSources).Status));
    }

    [Fact]
    public async Task Historical_split_without_identity_preserves_raw_suggestions_without_precise_requirement_totals()
    {
        var services = new ServiceCollection();
        services.AddMediatR(x => x.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(x => x.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var run = new MrpRunId(Guid.NewGuid());
        var historical = MrpCalculator.Calculate(NetRequirementIdentityTests.SplitInput())
            .Select(x => x with { NetRequirementReference = null }).ToArray();
        AddCalculatedSuggestions(db, run, historical);
        await db.SaveChangesAsync();
        db.PlanningSuggestions.First().Reject("planner", "same reason");
        await db.SaveChangesAsync();
        var upstream = new Sources();
        var handler = new GetMaterialDeliveriesQueryHandler(db, upstream, new FixedTime(Latest));
        var result = await handler.Handle(new("org", "env", run, "PLAN"), default);
        Assert.Empty(result.Items);
        Assert.Empty(upstream.Selections);
        Assert.Equal(3, result.UnknownRequirementSuggestions.Count);
        Assert.Equal(db.PlanningSuggestions.Select(x => x.Id.ToString()).Order(),
            result.UnknownRequirementSuggestions.Select(x => x.SuggestionSource.SuggestionId).Order());
        Assert.All(result.UnknownRequirementSuggestions, x =>
        {
            Assert.Equal("net-requirement-identity-unknown", x.Reason);
            Assert.Equal(30, x.RawNetRequirementSource.NetRequirementQuantity);
            Assert.Single(x.DemandSources);
        });
        Assert.Equal(new decimal[] { 6, 12, 12 }, result.UnknownRequirementSuggestions.Select(x => x.RawNetRequirementSource.PlannedQuantity).Order());
        Assert.Single(result.UnknownRequirementSuggestions, x => x.SuggestionSource.Status == "Rejected");
    }

    private static void AddCalculatedSuggestions(ApplicationDbContext db, MrpRunId run,
        IEnumerable<CalculatedPlanningSuggestion> calculated)
    {
        foreach (var batch in calculated)
        {
            var suggestion = PlanningSuggestion.Create("org", "env", run, batch.SuggestionType, batch.SkuCode, batch.UomCode,
                batch.SiteCode, batch.Quantity, batch.RequiredDate, batch.ReleaseDate, batch.ReasonCode,
                netRequirementReference: batch.NetRequirementReference);
            var net = batch.NetRequirementExplanation;
            suggestion.SetNetRequirementExplanation(net.GrossDemandQuantity, net.OnHandQuantity, net.ReservedQuantity,
                net.AvailableToNetQuantity, net.ScheduledReceiptQuantity, net.SafetyStockQuantity, net.NetRequirementQuantity,
                net.PlannedQuantity, net.ScrapRate, net.YieldRate, net.PrimarySourceType, net.Formula, null);
            foreach (var link in batch.PeggingLinks)
                suggestion.AddPeggingLink(link.PeggingType, link.DemandSourceReference, link.ParentSkuCode,
                    link.ComponentSkuCode, link.Quantity, link.ProductionVersionReference, link.ManufacturingBomReference,
                    link.RoutingReference, link.SourceType, link.GrossDemandQuantity, link.SourceLineReference);
            db.PlanningSuggestions.Add(suggestion);
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Sources : IMaterialDeliverySourcesClient
    {
        public IReadOnlyCollection<MaterialDeliverySupplySource> Supply { get; set; } =
        [new("PO-1", "10", "SITE", "SKU", "EA", new(2026, 10, 8), 4, []),
         new("PO-2", "20", "SITE", "SKU", "EA", new(2026, 10, 9), 6, []),
         new("WRONG-UOM", "10", "SITE", "SKU", "BOX", new(2026, 10, 8), 100, []),
         new("WRONG-SITE", "10", "OTHER", "SKU", "EA", new(2026, 10, 8), 100, []),
         new("WRONG-SKU", "10", "SITE", "OTHER-SKU", "EA", new(2026, 10, 8), 100, [])];
        public IReadOnlyCollection<MaterialDeliverySourceSelection> Selections { get; private set; } = [];
        public Task<IReadOnlyCollection<MaterialDeliverySupplySource>> GetSupplyAsync(string org, string env, CancellationToken ct) => Task.FromResult(Supply);
        public Task<MaterialDeliverySourcesResponse> GetSchedulingAsync(string org, string env, string plan,
            IReadOnlyCollection<MaterialDeliverySourceSelection> sources, CancellationToken ct)
        {
            Selections = sources;
            var items = sources.Select(source =>
            {
                var bounds = source.DueSources.Select(x => new MaterialDeliveryBoundContract(x.SourceReference, x.DueUtc, 1440,
                    x.DueUtc.AddDays(-1), ["OP"])).ToArray();
                return new MaterialDeliveryOrderSourceContract(source.SuggestionId, source.WorkOrderId, "scheduled", Latest.AddDays(-1),
                    bounds.Min(x => x.LatestStartUtc), bounds.MinBy(x => x.LatestStartUtc)!.SourceReference,
                    [new("OP", 10, "created", 0, 20, 1440, Latest.AddDays(-2), Latest.AddDays(-1), "scheduled", [], "ROUTE")], bounds);
            }).ToArray();
            return Task.FromResult(new MaterialDeliverySourcesResponse(plan, items));
        }
    }
}
