using Nerv.IIP.Business.Quality.Domain.DomainEvents;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;

public sealed class CapaOpenedIntegrationEventConverter(IQualityIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<CorrectiveActionOpenedDomainEvent, CapaOpenedIntegrationEvent>
{
    public CapaOpenedIntegrationEvent Convert(CorrectiveActionOpenedDomainEvent domainEvent)
    {
        var capa = domainEvent.CorrectiveAction;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new CapaOpenedIntegrationEvent(
            EventIds.New(),
            QualityIntegrationEventTypes.CapaOpened,
            QualityIntegrationEventVersions.V1,
            occurredAtUtc,
            QualityIntegrationEventSources.BusinessQuality,
            context.CorrelationId,
            context.CausationId,
            capa.OrganizationId,
            capa.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("capa-opened", capa.OrganizationId, capa.EnvironmentId, capa.CapaCode),
            new CapaOpenedPayload(
                capa.Id.ToString(),
                capa.CapaCode,
                capa.SourceNcrId,
                capa.OwnerUserId,
                capa.Status,
                capa.DueAtUtc,
                capa.CreatedAtUtc));
    }
}

public sealed class CapaEffectivenessVerifiedIntegrationEventConverter(IQualityIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<CorrectiveActionEffectivenessVerifiedDomainEvent, CapaEffectivenessVerifiedIntegrationEvent>
{
    public CapaEffectivenessVerifiedIntegrationEvent Convert(CorrectiveActionEffectivenessVerifiedDomainEvent domainEvent)
    {
        var capa = domainEvent.CorrectiveAction;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new CapaEffectivenessVerifiedIntegrationEvent(
            EventIds.New(),
            QualityIntegrationEventTypes.CapaEffectivenessVerified,
            QualityIntegrationEventVersions.V1,
            occurredAtUtc,
            QualityIntegrationEventSources.BusinessQuality,
            context.CorrelationId,
            context.CausationId,
            capa.OrganizationId,
            capa.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("capa-effectiveness-verified", capa.OrganizationId, capa.EnvironmentId, capa.CapaCode),
            new CapaEffectivenessVerifiedPayload(
                capa.Id.ToString(),
                capa.CapaCode,
                capa.SourceNcrId,
                capa.EffectivenessInspectionRecordId?.ToString()
                    ?? throw new InvalidOperationException("CAPA effectiveness inspection id is required for event conversion."),
                capa.EffectivenessVerifiedByUserId
                    ?? throw new InvalidOperationException("CAPA verified-by user id is required for event conversion."),
                capa.EffectivenessResult
                    ?? throw new InvalidOperationException("CAPA effectiveness result is required for event conversion."),
                capa.EffectivenessVerifiedAtUtc
                    ?? throw new InvalidOperationException("CAPA verified time is required for event conversion.")));
    }
}

public sealed class CapaClosedIntegrationEventConverter(IQualityIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<CorrectiveActionClosedDomainEvent, CapaClosedIntegrationEvent>
{
    public CapaClosedIntegrationEvent Convert(CorrectiveActionClosedDomainEvent domainEvent)
    {
        var capa = domainEvent.CorrectiveAction;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new CapaClosedIntegrationEvent(
            EventIds.New(),
            QualityIntegrationEventTypes.CapaClosed,
            QualityIntegrationEventVersions.V1,
            occurredAtUtc,
            QualityIntegrationEventSources.BusinessQuality,
            context.CorrelationId,
            context.CausationId,
            capa.OrganizationId,
            capa.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("capa-closed", capa.OrganizationId, capa.EnvironmentId, capa.CapaCode),
            new CapaClosedPayload(
                capa.Id.ToString(),
                capa.CapaCode,
                capa.SourceNcrId,
                capa.CloseApprovalChainId,
                capa.ClosedByUserId
                    ?? throw new InvalidOperationException("CAPA closed-by user id is required for event conversion."),
                capa.ClosedAtUtc
                    ?? throw new InvalidOperationException("CAPA closed time is required for event conversion.")));
    }
}
