using DotNetCore.CAP;
using Nerv.IIP.AppHub.Web.Application.IntegrationEvents;

namespace Nerv.IIP.AppHub.Web.Application.IntegrationEventHandlers;

public sealed class AppHubPublishedEventSink : ICapSubscribe
{
    public const string ConsumerName = "apphub.published-event-sink";

    [CapSubscribe(nameof(ApplicationRegisteredIntegrationEvent), Group = ConsumerName)]
    public Task HandleAsync(ApplicationRegisteredIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    [CapSubscribe(nameof(ApplicationInstanceStatusChangedIntegrationEvent), Group = ConsumerName)]
    public Task HandleAsync(ApplicationInstanceStatusChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
