using Nerv.IIP.Business.Wms.Domain.DomainEvents;
using Nerv.IIP.Contracts.Wms;

namespace Nerv.IIP.Business.Wms.Web.Application.IntegrationEventConverters;

public sealed class InboundOrderCompletedIntegrationEventConverter
    : IIntegrationEventConverter<InboundOrderCompletedDomainEvent, WmsIntegrationEvent>
{
    public WmsIntegrationEvent Convert(InboundOrderCompletedDomainEvent domainEvent)
    {
        var order = domainEvent.InboundOrder;
        var line = order.Lines.First();
        return WmsIntegrationEventFactory.NewEvent(
            WmsIntegrationEventTypes.InboundOrderCompleted,
            order.OrganizationId,
            order.EnvironmentId,
            $"wms:inbound-completed:{order.OrganizationId}:{order.EnvironmentId}:{order.InboundOrderNo}",
            new WmsIntegrationPayload(order.InboundOrderNo, line.LineNo, line.SkuCode, line.UomCode, order.SiteCode, line.StagingLocationCode, line.ReceivedQuantity, order.Status.ToString(), null, null));
    }
}

public sealed class OutboundOrderCompletedIntegrationEventConverter
    : IIntegrationEventConverter<OutboundOrderCompletedDomainEvent, WmsIntegrationEvent>
{
    public WmsIntegrationEvent Convert(OutboundOrderCompletedDomainEvent domainEvent)
    {
        var order = domainEvent.OutboundOrder;
        var line = order.Lines.First();
        return WmsIntegrationEventFactory.NewEvent(
            WmsIntegrationEventTypes.OutboundOrderCompleted,
            order.OrganizationId,
            order.EnvironmentId,
            $"wms:outbound-completed:{order.OrganizationId}:{order.EnvironmentId}:{order.OutboundOrderNo}",
            new WmsIntegrationPayload(order.OutboundOrderNo, line.LineNo, line.SkuCode, line.UomCode, order.SiteCode, line.PickLocationCode, line.RequestedQuantity, order.Status.ToString(), null, null));
    }
}

public sealed class CountExecutionCompletedIntegrationEventConverter
    : IIntegrationEventConverter<CountExecutionCompletedDomainEvent, WmsIntegrationEvent>
{
    public WmsIntegrationEvent Convert(CountExecutionCompletedDomainEvent domainEvent)
    {
        var count = domainEvent.CountExecution;
        return WmsIntegrationEventFactory.NewEvent(
            WmsIntegrationEventTypes.CountExecutionCompleted,
            count.OrganizationId,
            count.EnvironmentId,
            $"wms:count-completed:{count.OrganizationId}:{count.EnvironmentId}:{count.CountNo}",
            new WmsIntegrationPayload(count.CountNo, null, count.SkuCode, count.UomCode, count.SiteCode, count.LocationCode, count.VarianceQuantity, count.Status.ToString(), null, null));
    }
}

public sealed class WcsTaskDispatchedIntegrationEventConverter
    : IIntegrationEventConverter<WcsTaskDispatchedDomainEvent, WmsIntegrationEvent>
{
    public WmsIntegrationEvent Convert(WcsTaskDispatchedDomainEvent domainEvent)
    {
        var task = domainEvent.WcsTask;
        return WmsIntegrationEventFactory.NewEvent(
            WmsIntegrationEventTypes.WcsTaskDispatched,
            task.OrganizationId,
            task.EnvironmentId,
            $"wms:wcs-dispatched:{task.OrganizationId}:{task.EnvironmentId}:{task.AdapterType}:{task.ExternalTaskId}:{task.AttemptCount}",
            new WmsIntegrationPayload(task.ExternalTaskId, null, null, null, null, null, null, task.Status.ToString(), null, null));
    }
}

public sealed class WcsTaskFailedIntegrationEventConverter
    : IIntegrationEventConverter<WcsTaskFailedDomainEvent, WmsIntegrationEvent>
{
    public WmsIntegrationEvent Convert(WcsTaskFailedDomainEvent domainEvent)
    {
        var task = domainEvent.WcsTask;
        return WmsIntegrationEventFactory.NewEvent(
            WmsIntegrationEventTypes.WcsTaskFailed,
            task.OrganizationId,
            task.EnvironmentId,
            $"wms:wcs-failed:{task.OrganizationId}:{task.EnvironmentId}:{task.AdapterType}:{task.ExternalTaskId}:{task.FailureCode}",
            new WmsIntegrationPayload(task.ExternalTaskId, null, null, null, null, null, null, task.Status.ToString(), task.FailureCode, task.FailureMessage));
    }
}

public sealed class WcsTaskCompletedIntegrationEventConverter
    : IIntegrationEventConverter<WcsTaskCompletedDomainEvent, WmsIntegrationEvent>
{
    public WmsIntegrationEvent Convert(WcsTaskCompletedDomainEvent domainEvent)
    {
        var task = domainEvent.WcsTask;
        return WmsIntegrationEventFactory.NewEvent(
            WmsIntegrationEventTypes.WcsTaskCompleted,
            task.OrganizationId,
            task.EnvironmentId,
            $"wms:wcs-completed:{task.OrganizationId}:{task.EnvironmentId}:{task.AdapterType}:{task.ExternalTaskId}:{task.AttemptCount}",
            new WmsIntegrationPayload(task.ExternalTaskId, null, null, null, null, null, null, task.Status.ToString(), null, null));
    }
}

internal static class WmsIntegrationEventFactory
{
    public static WmsIntegrationEvent NewEvent(
        string eventType,
        string organizationId,
        string environmentId,
        string idempotencyKey,
        WmsIntegrationPayload payload)
    {
        return new WmsIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            eventType,
            WmsIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            WmsIntegrationEventSources.BusinessWms,
            organizationId,
            environmentId,
            idempotencyKey,
            payload);
    }
}
