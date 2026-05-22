using Nerv.IIP.Business.Quality.Domain.DomainEvents;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;

public sealed class NcrOpenedIntegrationEventConverter(IQualityIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<NonconformanceReportOpenedDomainEvent, NcrOpenedIntegrationEvent>
{
    public NcrOpenedIntegrationEvent Convert(NonconformanceReportOpenedDomainEvent domainEvent)
    {
        var ncr = domainEvent.NonconformanceReport;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new NcrOpenedIntegrationEvent(
            EventIds.New(),
            QualityIntegrationEventTypes.NcrOpened,
            QualityIntegrationEventVersions.V1,
            occurredAtUtc,
            QualityIntegrationEventSources.BusinessQuality,
            context.CorrelationId,
            context.CausationId,
            ncr.OrganizationId,
            ncr.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("ncr-opened", ncr.OrganizationId, ncr.EnvironmentId, ncr.NcrCode),
            new NcrOpenedPayload(
                ncr.Id.ToString(),
                ncr.NcrCode,
                ncr.SourceType,
                ncr.SourceDocumentId,
                ncr.SkuCode,
                ncr.DefectQuantity,
                ncr.DefectReason,
                ncr.BatchNo,
                ncr.SerialNo,
                ncr.Status,
                occurredAtUtc));
    }
}

public sealed class NcrDispositionDecidedIntegrationEventConverter(IQualityIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<NonconformanceReportDispositionDecidedDomainEvent, NcrDispositionDecidedIntegrationEvent>
{
    public NcrDispositionDecidedIntegrationEvent Convert(NonconformanceReportDispositionDecidedDomainEvent domainEvent)
    {
        var ncr = domainEvent.NonconformanceReport;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new NcrDispositionDecidedIntegrationEvent(
            EventIds.New(),
            QualityIntegrationEventTypes.DispositionDecided,
            QualityIntegrationEventVersions.V1,
            occurredAtUtc,
            QualityIntegrationEventSources.BusinessQuality,
            context.CorrelationId,
            context.CausationId,
            ncr.OrganizationId,
            ncr.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("ncr-disposition-decided", ncr.OrganizationId, ncr.EnvironmentId, ncr.NcrCode),
            new NcrDispositionDecidedPayload(
                ncr.Id.ToString(),
                ncr.NcrCode,
                ncr.SkuCode,
                ncr.DefectQuantity,
                ncr.DispositionType ?? string.Empty,
                ncr.DispositionApprovalChainId,
                ncr.ReworkWorkOrderId,
                ncr.ScrapMovementId,
                ncr.ReturnDocumentId,
                occurredAtUtc));
    }
}

public sealed class NcrClosedIntegrationEventConverter(IQualityIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<NonconformanceReportClosedDomainEvent, NcrClosedIntegrationEvent>
{
    public NcrClosedIntegrationEvent Convert(NonconformanceReportClosedDomainEvent domainEvent)
    {
        var ncr = domainEvent.NonconformanceReport;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new NcrClosedIntegrationEvent(
            EventIds.New(),
            QualityIntegrationEventTypes.NcrClosed,
            QualityIntegrationEventVersions.V1,
            occurredAtUtc,
            QualityIntegrationEventSources.BusinessQuality,
            context.CorrelationId,
            context.CausationId,
            ncr.OrganizationId,
            ncr.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("ncr-closed", ncr.OrganizationId, ncr.EnvironmentId, ncr.NcrCode),
            new NcrClosedPayload(
                ncr.Id.ToString(),
                ncr.NcrCode,
                ncr.SkuCode,
                ncr.DefectQuantity,
                ncr.DispositionType ?? string.Empty,
                ncr.ReworkWorkOrderId,
                ncr.ScrapMovementId,
                ncr.ReturnDocumentId,
                occurredAtUtc));
    }
}

internal static class EventIds
{
    public static string New() => $"evt-{Guid.CreateVersion7():N}";

    public static string Idempotency(params string[] parts) => $"quality:{string.Join(':', parts)}";
}
