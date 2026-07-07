using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.DemandSourceAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.ForecastInputAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;

namespace Nerv.IIP.Business.DemandPlanning.Domain.DomainEvents;

public sealed record DemandSourceCreatedDomainEvent(DemandSource DemandSource) : IDomainEvent;

public sealed record ForecastInputCreatedDomainEvent(ForecastInput ForecastInput) : IDomainEvent;

public sealed record MrpRunCompletedDomainEvent(MrpRun MrpRun) : IDomainEvent;

public sealed record PlannedPurchaseSuggestedDomainEvent(PlanningSuggestion PlanningSuggestion) : IDomainEvent;

public sealed record PlannedWorkOrderSuggestedDomainEvent(PlanningSuggestion PlanningSuggestion) : IDomainEvent;

public sealed record PlanningSuggestionAcceptedDomainEvent(PlanningSuggestion PlanningSuggestion) : IDomainEvent;
