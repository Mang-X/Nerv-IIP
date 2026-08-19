using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate;
using Nerv.IIP.AppHub.Web.Application.IntegrationEvents;
using Nerv.IIP.Contracts.AppHubQueries;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.AppHub.Web.Application.IntegrationEventConverters;

public sealed class ApplicationRegisteredIntegrationEventConverter
    : IIntegrationEventConverter<ApplicationRegisteredDomainEvent, ApplicationRegisteredIntegrationEvent>
{
    /// <summary>
    /// 信封的 <c>SourceService</c> 与 <c>Actor</c> 都是 AppHub 服务自身标识，统一引用
    /// <see cref="AppHubIntegrationEventSources.AppHub"/>（#1370 ③：消除裸字面量，取值不变）。
    /// </summary>
    public ApplicationRegisteredIntegrationEvent Convert(ApplicationRegisteredDomainEvent domainEvent)
    {
        return new ApplicationRegisteredIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            "apphub.ApplicationRegistered",
            1,
            DateTimeOffset.UtcNow,
            AppHubIntegrationEventSources.AppHub,
            string.Empty,
            domainEvent.ApplicationKey,
            domainEvent.OrganizationId,
            domainEvent.EnvironmentId,
            AppHubIntegrationEventSources.AppHub,
            $"apphub:application-registered:{domainEvent.OrganizationId}:{domainEvent.EnvironmentId}:{domainEvent.ApplicationKey}:{domainEvent.Version}",
            new ApplicationRegisteredPayload(domainEvent.ApplicationKey, domainEvent.Version));
    }
}
