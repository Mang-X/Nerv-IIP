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
            QualityStockReleaseTargetStatuses.Unrestricted,
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
            QualityStockReleaseTargetStatuses.Blocked,
            contextAccessor.GetContext());
    }
}

public sealed class InspectionConditionalReleasedIntegrationEventConverter(IQualityIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<InspectionConditionalReleasedDomainEvent, InspectionResultIntegrationEvent>
{
    public InspectionResultIntegrationEvent Convert(InspectionConditionalReleasedDomainEvent domainEvent)
    {
        return InspectionResultIntegrationEvents.Create(
            domainEvent.InspectionRecord,
            QualityIntegrationEventTypes.InspectionConditionalReleased,
            "inspection-conditional-release",
            QualityStockReleaseTargetStatuses.Restricted,
            contextAccessor.GetContext());
    }
}

internal static class InspectionResultIntegrationEvents
{
    public static InspectionResultIntegrationEvent Create(
        InspectionRecord record,
        string eventType,
        string idempotencyPrefix,
        string? targetQualityStatus,
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
            InspectionIntegrationEventPayloads.ToPayload(record, occurredAtUtc, targetQualityStatus));
    }
}

internal static class InspectionIntegrationEventPayloads
{
    public static InspectionResultPayload ToPayload(InspectionRecord record, DateTimeOffset occurredAtUtc, string? targetQualityStatus)
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
            ToStockRelease(record, targetQualityStatus),
            record.ResultLines.Select(ToResultLinePayload).ToArray(),
            record.BatchNo,
            record.SerialNo,
            record.SiteCode,
            record.LocationCode,
            record.OwnerType,
            record.OwnerId,
            record.UomCode);
    }

    private static StockReleaseDimensionPayload? ToStockRelease(InspectionRecord record, string? targetQualityStatus)
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
            record.OwnerId,
            targetQualityStatus);
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
