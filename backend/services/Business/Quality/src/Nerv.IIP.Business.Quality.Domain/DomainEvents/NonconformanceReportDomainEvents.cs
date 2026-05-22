using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;

namespace Nerv.IIP.Business.Quality.Domain.DomainEvents;

public sealed record NonconformanceReportOpenedDomainEvent(NonconformanceReport NonconformanceReport) : IDomainEvent;

public sealed record NonconformanceReportDispositionDecidedDomainEvent(NonconformanceReport NonconformanceReport) : IDomainEvent;

public sealed record NonconformanceReportClosedDomainEvent(NonconformanceReport NonconformanceReport) : IDomainEvent;
