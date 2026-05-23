using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.DomainEvents;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;

public sealed class InspectionPassedIntegrationEventConverter(IQualityIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<InspectionPassedDomainEvent, InspectionPassedIntegrationEvent>
{
    public InspectionPassedIntegrationEvent Convert(InspectionPassedDomainEvent domainEvent)
    {
        var record = domainEvent.InspectionRecord;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new InspectionPassedIntegrationEvent(
            EventIds.New(),
            QualityIntegrationEventTypes.InspectionPassed,
            QualityIntegrationEventVersions.V1,
            occurredAtUtc,
            QualityIntegrationEventSources.BusinessQuality,
            context.CorrelationId,
            context.CausationId,
            record.OrganizationId,
            record.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("inspection-passed", record.OrganizationId, record.EnvironmentId, record.SourceService, record.SourceDocumentId),
            ToPayload(record, occurredAtUtc));
    }

    private static InspectionResultPayload ToPayload(InspectionRecord record, DateTimeOffset occurredAtUtc)
    {
        return InspectionIntegrationEventPayloads.ToPayload(record, occurredAtUtc);
    }
}

public sealed class InspectionRejectedIntegrationEventConverter(IQualityIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<InspectionRejectedDomainEvent, InspectionRejectedIntegrationEvent>
{
    public InspectionRejectedIntegrationEvent Convert(InspectionRejectedDomainEvent domainEvent)
    {
        var record = domainEvent.InspectionRecord;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new InspectionRejectedIntegrationEvent(
            EventIds.New(),
            QualityIntegrationEventTypes.InspectionRejected,
            QualityIntegrationEventVersions.V1,
            occurredAtUtc,
            QualityIntegrationEventSources.BusinessQuality,
            context.CorrelationId,
            context.CausationId,
            record.OrganizationId,
            record.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("inspection-rejected", record.OrganizationId, record.EnvironmentId, record.SourceService, record.SourceDocumentId),
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
            occurredAtUtc);
    }
}
