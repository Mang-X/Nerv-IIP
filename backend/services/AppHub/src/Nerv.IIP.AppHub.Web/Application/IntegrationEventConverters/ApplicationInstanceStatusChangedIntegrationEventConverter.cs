using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using Nerv.IIP.AppHub.Web.Application.IntegrationEvents;
using Nerv.IIP.Contracts.AppHubQueries;
using NetCorePal.Extensions.DistributedTransactions;
using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.AppHub.Web.Application.IntegrationEventConverters;

public sealed class ApplicationInstanceStatusChangedIntegrationEventConverter
    : IIntegrationEventConverter<ApplicationInstanceStatusChangedDomainEvent, ApplicationInstanceStatusChangedIntegrationEvent>
{
    /// <summary>
    /// 信封的 <c>SourceService</c> 与 <c>Actor</c> 都是 AppHub 服务自身标识，统一引用
    /// <see cref="AppHubIntegrationEventSources.AppHub"/>（#1370 ③：消除裸字面量，取值不变）。
    /// </summary>
    public ApplicationInstanceStatusChangedIntegrationEvent Convert(ApplicationInstanceStatusChangedDomainEvent domainEvent)
    {
        return new ApplicationInstanceStatusChangedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            "apphub.InstanceStatusChanged",
            1,
            domainEvent.ChangedAtUtc,
            AppHubIntegrationEventSources.AppHub,
            string.Empty,
            domainEvent.InstanceKey,
            string.Empty,
            string.Empty,
            AppHubIntegrationEventSources.AppHub,
            IntegrationEventIdempotencyKey.Compose(
                "apphub:instance-status-changed:",
                domainEvent.InstanceKey,
                domainEvent.ChangedAtUtc.ToString("O")),
            new ApplicationInstanceStatusChangedPayload(
                domainEvent.InstanceKey,
                domainEvent.PreviousStatus,
                domainEvent.CurrentStatus,
                domainEvent.ChangedAtUtc));
    }
}
