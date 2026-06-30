using Nerv.IIP.Business.Quality.Domain.AggregatesModel.CorrectiveActionAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Domain.DomainEvents;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Web.Application.DomainEventHandlers;

public sealed class CorrectiveActionLifecycleDomainEventHandlerForCloseRecordedScrapNcr(
    INonconformanceReportRepository nonconformanceReportRepository)
    : IDomainEventHandler<CorrectiveActionEffectivenessVerifiedDomainEvent>,
        IDomainEventHandler<CorrectiveActionClosedDomainEvent>
{
    public Task Handle(CorrectiveActionEffectivenessVerifiedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        return RedriveRecordedScrapNcrClosureAsync(domainEvent.CorrectiveAction, cancellationToken);
    }

    public Task Handle(CorrectiveActionClosedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        return RedriveRecordedScrapNcrClosureAsync(domainEvent.CorrectiveAction, cancellationToken);
    }

    private async Task RedriveRecordedScrapNcrClosureAsync(
        CorrectiveAction correctiveAction,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(correctiveAction.SourceNcrId)
            || !Guid.TryParse(correctiveAction.SourceNcrId, out var sourceNcrGuid))
        {
            return;
        }

        var ncr = await nonconformanceReportRepository.GetAsync(
            new NonconformanceReportId(sourceNcrGuid),
            cancellationToken);
        if (ncr is null
            || string.Equals(ncr.Status, "closed", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(ncr.DispositionType, QualityNcrDispositionTypes.Scrap, StringComparison.OrdinalIgnoreCase)
            || !NonconformanceReport.RequiresEffectiveCapa(ncr.SourceType, ncr.DispositionType)
            || string.IsNullOrWhiteSpace(ncr.ScrapMovementId))
        {
            return;
        }

        ncr.Close(null, null, null);
    }
}
