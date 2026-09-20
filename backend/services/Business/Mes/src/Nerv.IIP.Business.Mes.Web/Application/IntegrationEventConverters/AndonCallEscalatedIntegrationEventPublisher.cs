using MediatR;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;

internal sealed class AndonCallEscalatedIntegrationEventPublisher(
    IMesIntegrationEventOutboxPublisher publisher,
    IHostEnvironment environment) : INotificationHandler<AndonCallEscalatedDomainEvent>
{
    public Task Handle(AndonCallEscalatedDomainEvent notification, CancellationToken cancellationToken)
    {
        var call = notification.Call;
        var key = $"andon-call-escalated:{call.Id}";
        var message = new AndonCallEscalatedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}", AndonCallEscalatedIntegrationEvent.Type, AndonCallEscalatedIntegrationEvent.Version,
            call.EscalatedAtUtc!.Value, MesIntegrationEventSources.BusinessMes, key, call.RaiseIntentKey,
            call.OrganizationId, call.EnvironmentId, "system:business-mes", key,
            new(call.Id.ToString(), call.Category.ToString(), call.WorkOrderId, call.OperationTaskIdValue,
                call.WorkCenterId, call.CallerId, call.RaisedAtUtc, call.EscalationRecipientId!, call.EscalationTimeoutSeconds!.Value));
        return publisher.PublishAsync(AndonCallEscalatedIntegrationEvent.Topic(environment.EnvironmentName), message);
    }
}
