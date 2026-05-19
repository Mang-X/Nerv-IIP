using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate;
using Nerv.IIP.AppHub.Web.Application.IntegrationEvents;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.AppHub.Web.Application.IntegrationEventConverters;

public sealed class ApplicationRegisteredIntegrationEventConverter
    : IIntegrationEventConverter<ApplicationRegisteredDomainEvent, ApplicationRegisteredIntegrationEvent>
{
    public ApplicationRegisteredIntegrationEvent Convert(ApplicationRegisteredDomainEvent domainEvent)
    {
        return new ApplicationRegisteredIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            "apphub.ApplicationRegistered",
            1,
            DateTimeOffset.UtcNow,
            "apphub",
            string.Empty,
            domainEvent.ApplicationKey,
            domainEvent.OrganizationId,
            domainEvent.EnvironmentId,
            "apphub",
            $"apphub:application-registered:{domainEvent.OrganizationId}:{domainEvent.EnvironmentId}:{domainEvent.ApplicationKey}:{domainEvent.Version}",
            new ApplicationRegisteredPayload(domainEvent.ApplicationKey, domainEvent.Version));
    }
}
