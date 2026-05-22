using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;

namespace Nerv.IIP.Business.Quality.Domain.DomainEvents;

public record NonconformanceReportOpenedDomainEvent(NonconformanceReport NonconformanceReport) : IDomainEvent;

public record NonconformanceReportDispositionDecidedDomainEvent(NonconformanceReport NonconformanceReport) : IDomainEvent;

public record NonconformanceReportClosedDomainEvent(NonconformanceReport NonconformanceReport) : IDomainEvent;
