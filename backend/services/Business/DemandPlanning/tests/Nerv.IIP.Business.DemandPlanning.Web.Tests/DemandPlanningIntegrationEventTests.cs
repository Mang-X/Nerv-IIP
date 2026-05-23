using System.Text.Json;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.DomainEvents;
using Nerv.IIP.Business.DemandPlanning.Web.Application.IntegrationEventConverters;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

public sealed class DemandPlanningIntegrationEventTests
{
    [Fact]
    public void Mrp_run_completed_event_uses_required_event_name()
    {
        var run = MrpRun.Create("org-001", "env-dev", new DateOnly(2026, 5, 25), new DateOnly(2026, 6, 30));
        run.Start(new PlanningInputSnapshot("pe-snapshot", "inventory-snapshot", 1, 2));
        run.Complete(2);

        var integrationEvent = new MrpRunCompletedIntegrationEventConverter().Convert(new MrpRunCompletedDomainEvent(run));
        var json = JsonSerializer.Serialize(integrationEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("demandPlanning.MrpRunCompleted", integrationEvent.EventType);
        Assert.Equal(1, integrationEvent.EventVersion);
        Assert.Equal("business-demand-planning", integrationEvent.SourceService);
        Assert.Contains("\"eventType\":\"demandPlanning.MrpRunCompleted\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("planned-purchase", "demandPlanning.PlannedPurchaseSuggested")]
    [InlineData("planned-work-order", "demandPlanning.PlannedWorkOrderSuggested")]
    public void Planning_suggestion_events_use_required_event_names(string suggestionType, string expectedEventType)
    {
        var suggestion = NewSuggestion(suggestionType);
        suggestion.AddPeggingLink("demand", "DEMAND-001", "SKU-FG-1000", "SKU-RM-1000", 19m, "PV-001", "MBOM-001", "ROUTING-001");

        var integrationEvent = suggestionType == "planned-purchase"
            ? new PlannedPurchaseSuggestedIntegrationEventConverter().Convert(new PlannedPurchaseSuggestedDomainEvent(suggestion))
            : new PlannedWorkOrderSuggestedIntegrationEventConverter().Convert(new PlannedWorkOrderSuggestedDomainEvent(suggestion));

        Assert.Equal(expectedEventType, integrationEvent.EventType);
        Assert.Equal("DEMAND-001", integrationEvent.Payload.Pegging.Single().DemandSourceReference);
    }

    [Fact]
    public void Planning_suggestion_accepted_event_uses_required_event_name()
    {
        var suggestion = NewSuggestion("planned-purchase");
        suggestion.Accept("erp", "purchase-request", "PR-001");

        var integrationEvent = new PlanningSuggestionAcceptedIntegrationEventConverter().Convert(new PlanningSuggestionAcceptedDomainEvent(suggestion));

        Assert.Equal("demandPlanning.PlanningSuggestionAccepted", integrationEvent.EventType);
        Assert.Equal("PR-001", integrationEvent.Payload.DownstreamDocumentId);
    }

    private static PlanningSuggestion NewSuggestion(string suggestionType)
    {
        return PlanningSuggestion.Create("org-001", "env-dev", new(Guid.CreateVersion7()), suggestionType, "SKU-RM-1000", "pcs", "SITE-01", 19m, new DateOnly(2026, 6, 1), "MRP-001");
    }
}
