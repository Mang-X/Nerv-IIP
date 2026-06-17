using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.DomainEvents;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;

public sealed class InspectionPassedIntegrationEventConverter(IQualityIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<InspectionPassedDomainEvent, InspectionResultIntegrationEvent>
{
    public InspectionResultIntegrationEvent Convert(InspectionPassedDomainEvent domainEvent)
    {
        return InspectionResultIntegrationEvents.Create(
            domainEvent.InspectionRecord,
            QualityIntegrationEventTypes.InspectionPassed,
            "inspection-passed",
            contextAccessor.GetContext());
    }
}

public sealed class InspectionRejectedIntegrationEventConverter(IQualityIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<InspectionRejectedDomainEvent, InspectionResultIntegrationEvent>
{
    public InspectionResultIntegrationEvent Convert(InspectionRejectedDomainEvent domainEvent)
    {
        return InspectionResultIntegrationEvents.Create(
            domainEvent.InspectionRecord,
            QualityIntegrationEventTypes.InspectionRejected,
            "inspection-rejected",
            contextAccessor.GetContext());
    }
}

internal static class InspectionResultIntegrationEvents
{
    public static InspectionResultIntegrationEvent Create(
        InspectionRecord record,
        string eventType,
        string idempotencyPrefix,
        QualityIntegrationEventContext context)
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        return new InspectionResultIntegrationEvent(
            EventIds.New(),
            eventType,
            QualityIntegrationEventVersions.V1,
            occurredAtUtc,
            QualityIntegrationEventSources.BusinessQuality,
            context.CorrelationId,
            context.CausationId,
            record.OrganizationId,
            record.EnvironmentId,
            context.Actor,
            EventIds.Idempotency(idempotencyPrefix, record.OrganizationId, record.EnvironmentId, record.SourceService, record.SourceDocumentId),
            InspectionIntegrationEventPayloads.ToPayload(record, occurredAtUtc));
    }
}

internal static class InspectionIntegrationEventPayloads
{
    public static InspectionResultPayload ToPayload(InspectionRecord record, DateTimeOffset occurredAtUtc)
    {
        return new InspectionResultPayload(
            record.Id.ToString(),
            record.InspectionPlanId?.ToString(),
            record.SourceType,
            record.SourceService,
            record.SourceDocumentId,
            record.SkuCode,
            record.InspectedQuantity,
            record.Result,
            record.DispositionReason,
            record.DispositionAttachmentFileIds,
            occurredAtUtc,
            ToStockRelease(record),
            record.ResultLines.Select(ToResultLinePayload).ToArray());
    }

    private static StockReleaseDimensionPayload? ToStockRelease(InspectionRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.UomCode)
            || string.IsNullOrWhiteSpace(record.SiteCode)
            || string.IsNullOrWhiteSpace(record.LocationCode)
            || string.IsNullOrWhiteSpace(record.SourceQualityStatus)
            || string.IsNullOrWhiteSpace(record.OwnerType))
        {
            return null;
        }

        return new StockReleaseDimensionPayload(
            record.UomCode,
            record.SiteCode,
            record.LocationCode,
            record.BatchNo,
            record.SerialNo,
            record.SourceQualityStatus,
            record.OwnerType,
            record.OwnerId);
    }

    private static InspectionResultLinePayload ToResultLinePayload(InspectionResultLine line)
    {
        return new InspectionResultLinePayload(
            line.CharacteristicCode,
            line.MeasuredValue,
            line.ObservedValue,
            line.UnitCode,
            line.Result,
            line.DefectReason,
            line.DefectQuantity);
    }
}
