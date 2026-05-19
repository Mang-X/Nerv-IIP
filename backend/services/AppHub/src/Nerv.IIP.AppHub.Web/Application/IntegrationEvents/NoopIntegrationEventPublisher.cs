using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.AppHub.Web.Application.IntegrationEvents;

public sealed class NoopIntegrationEventPublisher : IIntegrationEventPublisher
{
    public Task PublishAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        where TIntegrationEvent : notnull
    {
        return Task.CompletedTask;
    }
}
