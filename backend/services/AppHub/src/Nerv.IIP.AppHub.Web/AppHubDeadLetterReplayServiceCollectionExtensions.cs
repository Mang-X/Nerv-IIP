using Microsoft.EntityFrameworkCore;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.AppHub.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.AppHub.Web;

public static class AppHubDeadLetterReplayServiceCollectionExtensions
{
    public static IServiceCollection AddAppHubDeadLetterReplay(this IServiceCollection services)
    {
        services.AddObservedIntegrationEventDeadLetterStore();
        services.AddIntegrationEventDeadLetterReplay<OperationTaskCompletedIntegrationEvent,
            OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState>(
            OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState.ConsumerName,
            (consumer, integrationEvent, token) => consumer.HandleAsync(integrationEvent, token),
            (provider, integrationEvent, token) => HasInboxAsync(provider,
                OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState.ConsumerName, integrationEvent, token));
        services.AddIntegrationEventDeadLetterReplay<OperationTaskFailedIntegrationEvent,
            OperationTaskFailedIntegrationEventHandlerForRefreshInstanceState>(
            OperationTaskFailedIntegrationEventHandlerForRefreshInstanceState.ConsumerName,
            (consumer, integrationEvent, token) => consumer.HandleAsync(integrationEvent, token),
            (provider, integrationEvent, token) => HasInboxAsync(provider,
                OperationTaskFailedIntegrationEventHandlerForRefreshInstanceState.ConsumerName, integrationEvent, token));
        return services;
    }

    private static Task<bool> HasInboxAsync(IServiceProvider provider, string consumerName,
        IIntegrationEventEnvelope integrationEvent, CancellationToken token) =>
        provider.GetRequiredService<ApplicationDbContext>().ProcessedIntegrationEvents.AsNoTracking()
            .AnyAsync(x => x.ConsumerName == consumerName && x.IdempotencyKey == integrationEvent.IdempotencyKey, token);
}
