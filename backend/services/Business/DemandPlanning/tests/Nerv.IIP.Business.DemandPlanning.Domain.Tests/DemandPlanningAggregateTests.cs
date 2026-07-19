using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.DemandSourceAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MasterProductionScheduleAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.DomainEvents;

namespace Nerv.IIP.Business.DemandPlanning.Domain.Tests;

public sealed class DemandPlanningAggregateTests
{
    [Fact]
    public void Sales_order_demand_tracks_line_version_customer_and_explainable_cancellation()
    {
        var demand = DemandSource.CreateSalesOrderDemand(
            "org-001",
            "env-dev",
            "sales-order-id-001",
            "SO-DEMO-001",
            "10",
            "CUST-001",
            "SKU-FG-1000",
            "EA",
            "SITE-001",
            2m,
            new DateOnly(2026, 8, 15),
            1);

        Assert.Equal("sales-order", demand.DemandType);
        Assert.Equal("sales-order-id-001", demand.SourceDocumentId);
        Assert.Equal("SO-DEMO-001", demand.SourceReference);
        Assert.Equal("10", demand.SourceLineReference);
        Assert.Equal("CUST-001", demand.CustomerCode);
        Assert.Equal(1, demand.SourceVersion);
        Assert.Equal("active", demand.SourceStatus);

        demand.ApplySalesOrderSnapshot(3m, new DateOnly(2026, 8, 16), 2);
        Assert.Equal(3m, demand.Quantity);
        Assert.Equal(2, demand.SourceVersion);

        demand.CancelFromSalesOrder(3);
        Assert.Equal(0m, demand.Quantity);
        Assert.Equal(3, demand.SourceVersion);
        Assert.Equal("cancelled", demand.SourceStatus);
    }

    [Fact]
    public void Demand_source_creation_requires_planning_dimensions()
    {
        Assert.Throws<ArgumentException>(() => DemandSource.Create(
            string.Empty,
            "env-dev",
            "manual",
            "DEMAND-001",
            "SKU-FG-1000",
            "pcs",
            "SITE-01",
            10m,
            new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void Demand_source_creation_raises_created_event()
    {
        var demand = NewDemand();

        Assert.Equal("SKU-FG-1000", demand.SkuCode);
        Assert.Equal(10m, demand.Quantity);
        Assert.Equal(new DateOnly(2026, 6, 1), demand.DueDate);
        Assert.IsType<DemandSourceCreatedDomainEvent>(demand.GetDomainEvents().Single());
    }

    [Fact]
    public void Mrp_run_moves_from_created_to_running_to_completed_with_snapshot_metadata()
    {
        var run = MrpRun.Create("org-001", "env-dev", new DateOnly(2026, 5, 25), new DateOnly(2026, 6, 30));

        run.Start(new PlanningInputSnapshot(
            "production-version-api",
            "inventory-availability-api",
            1,
            2,
            ["mps", "sales-order"],
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30)));
        run.Complete(suggestionCount: 2);

        Assert.Equal(MrpRunStatus.Completed, run.Status);
        Assert.Equal("production-version-api", run.ProductionEngineeringSnapshotSource);
        Assert.Equal("inventory-availability-api", run.InventorySnapshotSource);
        Assert.Equal(["mps", "sales-order"], run.InputSources);
        Assert.Equal(new DateOnly(2026, 6, 1), run.InputCoverageStart);
        Assert.Equal(new DateOnly(2026, 6, 30), run.InputCoverageEnd);
        Assert.Equal(2, run.SuggestionCount);
        Assert.IsType<MrpRunCompletedDomainEvent>(run.GetDomainEvents().Last());
    }

    [Fact]
    public void Mrp_run_exposes_input_degradation_sources_from_snapshot_metadata()
    {
        var run = MrpRun.Create("org-001", "env-dev", new DateOnly(2026, 5, 25), new DateOnly(2026, 6, 30));

        run.Start(new PlanningInputSnapshot(
            "production-version-api",
            "inventory-http:2;scheduled-receipts:error;master-data-planning-parameters:error",
            1,
            2,
            ["sales-order"],
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 1)));

        Assert.Equal(["scheduled-receipts", "master-data-planning-parameters"], run.InputDegradationSources);
    }

    [Fact]
    public void Master_production_schedule_moves_from_draft_to_reviewed_to_released()
    {
        var bucket = MasterProductionSchedule.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "pcs",
            "SITE-01",
            new DateOnly(2026, 6, 15),
            120m);

        bucket.Update("SKU-FG-1000", "pcs", "SITE-01", new DateOnly(2026, 6, 15), 132m);
        bucket.MarkReviewed("planner.li");
        bucket.Release("planning.manager");

        Assert.Equal(MasterProductionScheduleStatus.Released, bucket.Status);
        Assert.Equal(132m, bucket.Quantity);
        Assert.Equal("planner.li", bucket.ReviewedBy);
        Assert.Equal("planning.manager", bucket.ReleasedBy);
        Assert.NotNull(bucket.ReviewedAtUtc);
        Assert.NotNull(bucket.ReleasedAtUtc);
    }

    [Fact]
    public void Planning_suggestion_can_be_accepted_once_by_same_downstream_reference()
    {
        var suggestion = NewSuggestion();

        suggestion.Accept("erp", "purchase-request", "PR-001");
        suggestion.Accept("erp", "purchase-request", "PR-001");

        Assert.Equal(PlanningSuggestionStatus.Accepted, suggestion.Status);
        Assert.Equal("PR-001", suggestion.AcceptedDownstreamDocumentId);
        Assert.Single(suggestion.GetDomainEvents().OfType<PlanningSuggestionAcceptedDomainEvent>());
    }

    [Fact]
    public void Planning_suggestion_rejects_conflicting_acceptance_or_terminal_state_changes()
    {
        var accepted = NewSuggestion();
        accepted.Accept("erp", "purchase-request", "PR-001");

        Assert.Throws<InvalidOperationException>(() => accepted.Accept("erp", "purchase-request", "PR-002"));

        var rejected = NewSuggestion();
        rejected.Reject("planner", "obsolete");

        Assert.Throws<InvalidOperationException>(() => rejected.Accept("erp", "purchase-request", "PR-003"));
    }

    [Theory]
    [InlineData("reschedule-in")]
    [InlineData("reschedule-out")]
    [InlineData("cancel")]
    public void Planning_exception_suggestion_does_not_publish_downstream_creation_event(string suggestionType)
    {
        var suggestion = PlanningSuggestion.Create(
            "org-001",
            "env-dev",
            new MrpRunId(Guid.CreateVersion7()),
            suggestionType,
            "SKU-FG-1000",
            "pcs",
            "SITE-01",
            10m,
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 5),
            "scheduled-receipt-exception");

        Assert.DoesNotContain(suggestion.GetDomainEvents(), x => x is PlannedPurchaseSuggestedDomainEvent or PlannedWorkOrderSuggestedDomainEvent);
    }

    [Fact]
    public void Pegging_links_preserve_demand_source_and_version_references()
    {
        var suggestion = NewSuggestion();

        suggestion.AddPeggingLink(
            "demand",
            "DEMAND-001",
            "SKU-FG-1000",
            "SKU-RM-1000",
            19m,
            "PV-001",
            "MBOM-001",
            "ROUTING-001");

        var link = suggestion.PeggingLinks.Single();
        Assert.Equal("DEMAND-001", link.DemandSourceReference);
        Assert.Equal("PV-001", link.ProductionVersionReference);
        Assert.Equal("MBOM-001", link.ManufacturingBomReference);
    }

    private static DemandSource NewDemand()
    {
        return DemandSource.Create(
            "org-001",
            "env-dev",
            "manual",
            "DEMAND-001",
            "SKU-FG-1000",
            "pcs",
            "SITE-01",
            10m,
            new DateOnly(2026, 6, 1));
    }

    private static PlanningSuggestion NewSuggestion()
    {
        return PlanningSuggestion.Create(
            "org-001",
            "env-dev",
            new MrpRunId(Guid.CreateVersion7()),
            "planned-purchase",
            "SKU-RM-1000",
            "pcs",
            "SITE-01",
            19m,
            new DateOnly(2026, 5, 30),
            new DateOnly(2026, 5, 25),
            "MRP-001");
    }
}
