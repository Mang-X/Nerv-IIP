using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;

namespace Nerv.IIP.Business.Quality.Domain.DomainEvents;

public sealed record InspectionPlanActivatedDomainEvent(InspectionPlan InspectionPlan) : IDomainEvent;

public sealed record InspectionPassedDomainEvent(InspectionRecord InspectionRecord) : IDomainEvent;

public sealed record InspectionConditionalReleasedDomainEvent(InspectionRecord InspectionRecord) : IDomainEvent;

public sealed record InspectionRejectedDomainEvent(InspectionRecord InspectionRecord) : IDomainEvent;

public sealed record NonconformanceReportOpenedDomainEvent(NonconformanceReport NonconformanceReport) : IDomainEvent;

public sealed record NonconformanceReportDispositionDecidedDomainEvent(NonconformanceReport NonconformanceReport) : IDomainEvent;

public sealed record NonconformanceReportClosedDomainEvent(NonconformanceReport NonconformanceReport) : IDomainEvent;
