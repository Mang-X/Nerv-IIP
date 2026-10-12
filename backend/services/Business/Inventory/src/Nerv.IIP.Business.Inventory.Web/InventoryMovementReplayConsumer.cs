using NetCorePal.Extensions.DistributedTransactions;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Inventory;

namespace Nerv.IIP.Business.Inventory.Web;

// The original consumer reports business rejection through a failure event, not an exception.
// Observe that result only within replay; keep its production command, validation and publisher.
internal sealed class InventoryMovementReplayConsumer(IServiceProvider provider, IIntegrationEventPublisher publisher)
{
    public async Task HandleAsync(InventoryMovementRequestedIntegrationEvent integrationEvent, CancellationToken token)
    {
        var observed = new PostingOutcomePublisher(publisher);
        var consumer = ActivatorUtilities.CreateInstance<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>(provider, observed);
        await consumer.HandleAsync(integrationEvent, token);
        if (observed.Failure is { } failure)
            throw new InvalidOperationException($"{failure.FailureCode}: {failure.FailureMessage}");
    }

    private sealed class PostingOutcomePublisher(IIntegrationEventPublisher inner) : IIntegrationEventPublisher
    {
        internal StockMovementPostingFailedPayload? Failure { get; private set; }

        public async Task PublishAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken cancellationToken) where TIntegrationEvent : notnull
        {
            await inner.PublishAsync(integrationEvent, cancellationToken);
            if (integrationEvent is StockMovementPostingFailedIntegrationEvent failed) Failure = failed.Payload;
        }
    }
}
