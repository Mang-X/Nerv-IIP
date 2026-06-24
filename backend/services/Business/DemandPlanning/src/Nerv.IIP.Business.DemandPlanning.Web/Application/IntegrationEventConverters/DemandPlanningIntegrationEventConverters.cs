using Nerv.IIP.Business.DemandPlanning.Domain.DomainEvents;
using Nerv.IIP.Contracts.DemandPlanning;
using static Nerv.IIP.Business.DemandPlanning.Web.Application.IntegrationEventConverters.DemandPlanningIntegrationEventConverterHelpers;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.IntegrationEventConverters;

public sealed class MrpRunCompletedIntegrationEventConverter
    : IIntegrationEventConverter<MrpRunCompletedDomainEvent, DemandPlanningIntegrationEvent<MrpRunCompletedPayload>>
{
    public DemandPlanningIntegrationEvent<MrpRunCompletedPayload> Convert(MrpRunCompletedDomainEvent domainEvent)
    {
        var run = domainEvent.MrpRun;
        return Envelope(
            DemandPlanningIntegrationEventTypes.MrpRunCompleted,
            run.OrganizationId,
            run.EnvironmentId,
            EventIds.Idempotency("mrp-run-completed", run.OrganizationId, run.EnvironmentId, PublicId(run.Id)),
            new MrpRunCompletedPayload(
                PublicId(run.Id),
                run.HorizonStart,
                run.HorizonEnd,
                run.DemandCount,
                run.AvailabilityCount,
                run.SuggestionCount,
                run.ProductionEngineeringSnapshotSource,
                run.InventorySnapshotSource));
    }
}

public sealed class PlannedPurchaseSuggestedIntegrationEventConverter
    : IIntegrationEventConverter<PlannedPurchaseSuggestedDomainEvent, DemandPlanningIntegrationEvent<PlanningSuggestionPayload>>
{
    public DemandPlanningIntegrationEvent<PlanningSuggestionPayload> Convert(PlannedPurchaseSuggestedDomainEvent domainEvent)
    {
        return SuggestionEnvelope(DemandPlanningIntegrationEventTypes.PlannedPurchaseSuggested, domainEvent.PlanningSuggestion);
    }
}

public sealed class PlannedWorkOrderSuggestedIntegrationEventConverter
    : IIntegrationEventConverter<PlannedWorkOrderSuggestedDomainEvent, DemandPlanningIntegrationEvent<PlanningSuggestionPayload>>
{
    public DemandPlanningIntegrationEvent<PlanningSuggestionPayload> Convert(PlannedWorkOrderSuggestedDomainEvent domainEvent)
    {
        return SuggestionEnvelope(DemandPlanningIntegrationEventTypes.PlannedWorkOrderSuggested, domainEvent.PlanningSuggestion);
    }
}

public sealed class PlanningSuggestionAcceptedIntegrationEventConverter
    : IIntegrationEventConverter<PlanningSuggestionAcceptedDomainEvent, PlanningSuggestionAcceptedIntegrationEvent>
{
    public PlanningSuggestionAcceptedIntegrationEvent Convert(PlanningSuggestionAcceptedDomainEvent domainEvent)
    {
        var suggestion = domainEvent.PlanningSuggestion;
        var payload = new PlanningSuggestionAcceptedPayload(
            PublicId(suggestion.Id),
            PublicId(suggestion.MrpRunId),
            suggestion.SuggestionType,
            suggestion.SkuCode,
            suggestion.UomCode,
            suggestion.SiteCode,
            suggestion.Quantity,
            suggestion.RequiredDate,
            suggestion.ReleaseDate,
            suggestion.PeggingLinks.Select(x => x.DemandSourceReference).FirstOrDefault(),
            suggestion.PeggingLinks.Select(x => x.ProductionVersionReference).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
            suggestion.AcceptedDownstreamService ?? string.Empty,
            suggestion.AcceptedDownstreamDocumentType ?? string.Empty,
            suggestion.AcceptedDownstreamDocumentId);
        return new PlanningSuggestionAcceptedIntegrationEvent(
            EventIds.New(),
            DemandPlanningIntegrationEventTypes.PlanningSuggestionAccepted,
            DemandPlanningIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            DemandPlanningIntegrationEventSources.BusinessDemandPlanning,
            "system:demand-planning",
            "system:demand-planning",
            suggestion.OrganizationId,
            suggestion.EnvironmentId,
            "system:demand-planning",
            EventIds.Idempotency("planning-suggestion-accepted", suggestion.OrganizationId, suggestion.EnvironmentId, PublicId(suggestion.Id)),
            payload);
    }
}

internal static class DemandPlanningIntegrationEventConverterHelpers
{
    public static DemandPlanningIntegrationEvent<TPayload> Envelope<TPayload>(
        string eventType,
        string organizationId,
        string environmentId,
        string idempotencyKey,
        TPayload payload)
    {
        return new DemandPlanningIntegrationEvent<TPayload>(
            EventIds.New(),
            eventType,
            DemandPlanningIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            DemandPlanningIntegrationEventSources.BusinessDemandPlanning,
            "system:demand-planning",
            "system:demand-planning",
            organizationId,
            environmentId,
            "system:demand-planning",
            idempotencyKey,
            payload);
    }

    public static DemandPlanningIntegrationEvent<PlanningSuggestionPayload> SuggestionEnvelope(
        string eventType,
        Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate.PlanningSuggestion suggestion)
    {
        return Envelope(
            eventType,
            suggestion.OrganizationId,
            suggestion.EnvironmentId,
            EventIds.Idempotency("planning-suggestion", suggestion.OrganizationId, suggestion.EnvironmentId, PublicId(suggestion.Id), eventType),
            new PlanningSuggestionPayload(
                PublicId(suggestion.Id),
                PublicId(suggestion.MrpRunId),
                suggestion.SuggestionType,
                suggestion.SkuCode,
                suggestion.UomCode,
                suggestion.SiteCode,
                suggestion.Quantity,
                suggestion.RequiredDate,
                suggestion.ReleaseDate,
                suggestion.PeggingLinks.Select(x => new PlanningSuggestionPeggingPayload(
                    x.DemandSourceReference,
                    x.ParentSkuCode,
                    x.ComponentSkuCode,
                    x.Quantity,
                    x.ProductionVersionReference,
                    x.ManufacturingBomReference,
                    x.RoutingReference)).ToArray()));
    }

    public static string PublicId(object? stronglyTypedId)
    {
        return stronglyTypedId?.ToString() ?? "unassigned";
    }
}

internal static class EventIds
{
    public static string New() => $"evt-{Guid.CreateVersion7():N}";

    public static string Idempotency(params string[] parts) => $"demand-planning:{string.Join(':', parts)}";
}
