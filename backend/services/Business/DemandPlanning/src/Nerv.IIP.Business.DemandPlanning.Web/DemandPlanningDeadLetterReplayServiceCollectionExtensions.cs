using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.DemandPlanning.Web;

public static class DemandPlanningDeadLetterReplayServiceCollectionExtensions
{
    public static IServiceCollection AddDemandPlanningDeadLetterReplay(this IServiceCollection services)
    {
        services.AddObservedIntegrationEventDeadLetterStore();
        Add<SalesOrderReleasedIntegrationEvent, SalesOrderReleasedIntegrationEventHandlerForProjectDemandSource>(services);
        Add<SalesOrderChangedIntegrationEvent, SalesOrderChangedIntegrationEventHandlerForProjectDemandSource>(services);
        Add<SalesOrderCancelledIntegrationEvent, SalesOrderCancelledIntegrationEventHandlerForProjectDemandSource>(services);
        Add<SalesOrderDeliveryRegisteredIntegrationEvent, SalesOrderDeliveryRegisteredIntegrationEventHandlerForProjectDemandSource>(services);
        return services;
    }

    private static void Add<TEvent, TConsumer>(IServiceCollection services)
        where TEvent : IIntegrationEventEnvelope
        where TConsumer : class, IIntegrationEventHandler<TEvent>
    {
        const string consumerName = SalesOrderReleasedIntegrationEventHandlerForProjectDemandSource.ConsumerName;
        services.AddIntegrationEventDeadLetterReplay<TEvent, TConsumer>(consumerName,
            (consumer, integrationEvent, token) => consumer.HandleAsync(integrationEvent, token),
            (provider, integrationEvent, token) => provider.GetRequiredService<ApplicationDbContext>()
                .ProcessedIntegrationEvents.AsNoTracking().AnyAsync(x =>
                    x.ConsumerName == consumerName && x.IdempotencyKey == integrationEvent.IdempotencyKey, token));
    }
}
