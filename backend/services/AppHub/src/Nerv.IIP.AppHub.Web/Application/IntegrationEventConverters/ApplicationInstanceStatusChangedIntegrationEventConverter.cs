using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using Nerv.IIP.AppHub.Web.Application.IntegrationEvents;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.AppHub.Web.Application.IntegrationEventConverters;

public sealed class ApplicationInstanceStatusChangedIntegrationEventConverter
    : IIntegrationEventConverter<ApplicationInstanceStatusChangedDomainEvent, ApplicationInstanceStatusChangedIntegrationEvent>
{
    public ApplicationInstanceStatusChangedIntegrationEvent Convert(ApplicationInstanceStatusChangedDomainEvent domainEvent)
    {
        return new ApplicationInstanceStatusChangedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            "apphub.InstanceStatusChanged",
            1,
            domainEvent.ChangedAtUtc,
            "apphub",
            string.Empty,
            domainEvent.InstanceKey,
            string.Empty,
            string.Empty,
            "apphub",
            $"apphub:instance-status-changed:{domainEvent.InstanceKey}:{domainEvent.ChangedAtUtc:O}",
            new ApplicationInstanceStatusChangedPayload(
                domainEvent.InstanceKey,
                domainEvent.PreviousStatus,
                domainEvent.CurrentStatus,
                domainEvent.ChangedAtUtc));
    }
}
