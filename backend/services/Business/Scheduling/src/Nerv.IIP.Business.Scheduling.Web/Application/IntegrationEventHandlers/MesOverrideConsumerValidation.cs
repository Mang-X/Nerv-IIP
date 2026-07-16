using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;

internal static class MesOverrideConsumerValidation
{
    private const int ProjectionIdentityMaxLength = 128;
    private const int ScopeIdentityMaxLength = 64;
    private static readonly IntegrationEventEnvelopeValidator EnvelopeValidator = new();

    public static IntegrationEventEnvelopeValidationResult ValidateEnvelope<TIntegrationEvent>(
        TIntegrationEvent integrationEvent,
        IntegrationEventConsumerOptions consumerOptions)
        where TIntegrationEvent : IIntegrationEventEnvelope =>
        EnvelopeValidator.Validate(integrationEvent, consumerOptions);

    public static bool IsValidDispatch(
        MesOperationTaskManuallyDispatchedIntegrationEvent integrationEvent,
        MesOverrideInboxIdentity inboxIdentity)
    {
        var payload = integrationEvent.Payload;
        return inboxIdentity.IsValid &&
            IsValidProjectionEnvelopeIdentity(integrationEvent) &&
            IsCanonicalIdentity(payload.WorkOrderId) &&
            IsCanonicalIdentity(payload.OperationTaskId) &&
            IsCanonicalIdentity(payload.ResourceId) &&
            IsCanonicalIdentity(payload.WorkCenterId) &&
            payload.OperationSequence > 0 &&
            payload.DispatchRevision >= 0 &&
            payload.EndUtc > payload.StartUtc;
    }

    public static bool IsValidClear(
        MesOperationTaskManualDispatchClearedIntegrationEvent integrationEvent,
        MesOverrideInboxIdentity inboxIdentity)
    {
        var payload = integrationEvent.Payload;
        return inboxIdentity.IsValid &&
            IsValidProjectionEnvelopeIdentity(integrationEvent) &&
            IsCanonicalIdentity(payload.WorkOrderId) &&
            IsCanonicalIdentity(payload.OperationTaskId) &&
            IsCanonicalIdentity(payload.ResourceId) &&
            IsCanonicalIdentity(payload.WorkCenterId) &&
            payload.OperationSequence > 0 &&
            payload.DispatchRevision > 0 &&
            payload.EndUtc > payload.StartUtc &&
            IsRecognizedClearReason(payload.ReasonCode);
    }

    private static bool IsValidProjectionEnvelopeIdentity(IIntegrationEventEnvelope integrationEvent) =>
        IsCanonicalIdentity(integrationEvent.OrganizationId, ScopeIdentityMaxLength) &&
        IsCanonicalIdentity(integrationEvent.EnvironmentId, ScopeIdentityMaxLength) &&
        IsCanonicalIdentity(integrationEvent.EventId) &&
        IsCanonicalIdentity(integrationEvent.Actor);

    private static bool IsRecognizedClearReason(string reasonCode) =>
        reasonCode is MesManualDispatchClearReasonCodes.DeviceCleared or
            MesManualDispatchClearReasonCodes.OperationCancelled;

    private static bool IsCanonicalIdentity(
        string value,
        int maxLength = ProjectionIdentityMaxLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value == value.Trim() &&
        value.Length <= maxLength;
}
