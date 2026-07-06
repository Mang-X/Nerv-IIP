using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using Nerv.IIP.Contracts.AppHubQueries;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.AppHub.Web.Application.IntegrationEventConverters;

public sealed class ConnectorHostUnreachableIntegrationEventConverter
    : IIntegrationEventConverter<ConnectorHostUnreachableDomainEvent, ConnectorHostUnreachableIntegrationEvent>
{
    public ConnectorHostUnreachableIntegrationEvent Convert(ConnectorHostUnreachableDomainEvent domainEvent)
    {
        var timeoutSeconds = Math.Max(0, (int)domainEvent.HeartbeatTimeout.TotalSeconds);
        var eventId = $"evt-{Guid.CreateVersion7():N}";
        var idempotencyKey = $"apphub:connector-host-unreachable:{domainEvent.OrganizationId}:{domainEvent.EnvironmentId}:{domainEvent.ConnectorHostId}:{domainEvent.InstanceKey}:{domainEvent.DetectedAtUtc:O}";
        return new ConnectorHostUnreachableIntegrationEvent(
            eventId,
            AppHubIntegrationEventTypes.ConnectorHostUnreachable,
            AppHubIntegrationEventVersions.V1,
            domainEvent.DetectedAtUtc,
            AppHubIntegrationEventSources.AppHub,
            $"corr-{eventId}",
            domainEvent.InstanceKey,
            domainEvent.OrganizationId,
            domainEvent.EnvironmentId,
            AppHubIntegrationEventSources.AppHub,
            idempotencyKey,
            new ConnectorHostUnreachablePayload(
                domainEvent.ConnectorHostId,
                domainEvent.InstanceKey,
                domainEvent.LastHeartbeatAtUtc,
                domainEvent.DetectedAtUtc,
                timeoutSeconds));
    }
}

public sealed class ConnectorHostRestoredIntegrationEventConverter
    : IIntegrationEventConverter<ConnectorHostRestoredDomainEvent, ConnectorHostRestoredIntegrationEvent>
{
    public ConnectorHostRestoredIntegrationEvent Convert(ConnectorHostRestoredDomainEvent domainEvent)
    {
        var eventId = $"evt-{Guid.CreateVersion7():N}";
        var idempotencyKey = $"apphub:connector-host-restored:{domainEvent.OrganizationId}:{domainEvent.EnvironmentId}:{domainEvent.ConnectorHostId}:{domainEvent.InstanceKey}:{domainEvent.RestoredAtUtc:O}";
        return new ConnectorHostRestoredIntegrationEvent(
            eventId,
            AppHubIntegrationEventTypes.ConnectorHostRestored,
            AppHubIntegrationEventVersions.V1,
            domainEvent.RestoredAtUtc,
            AppHubIntegrationEventSources.AppHub,
            $"corr-{eventId}",
            domainEvent.InstanceKey,
            domainEvent.OrganizationId,
            domainEvent.EnvironmentId,
            AppHubIntegrationEventSources.AppHub,
            idempotencyKey,
            new ConnectorHostRestoredPayload(
                domainEvent.ConnectorHostId,
                domainEvent.InstanceKey,
                domainEvent.RestoredAtUtc));
    }
}
