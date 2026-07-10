using DotNetCore.CAP;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockCounts;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Approval.ApprovalCompletedIntegrationEvent", ConsumerName)]
public sealed class ApprovalCompletedIntegrationEventHandlerForStockCountAdjustment(
    ISender sender,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<ApprovalCompletedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-inventory.stock-count-approval-completed";

    private readonly IntegrationEventConsumerGuard<ApprovalCompletedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            [
                ApprovalIntegrationEventTypes.ApprovalApproved,
                ApprovalIntegrationEventTypes.ApprovalRejected,
                ApprovalIntegrationEventTypes.ApprovalReturned,
            ],
            ApprovalIntegrationEventVersions.V1));

    public Task HandleAsync(ApprovalCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe("Nerv.IIP.Contracts.Approval.ApprovalCompletedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(ApprovalCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(ApprovalCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.SourceService, ApprovalIntegrationEventSources.BusinessApproval, StringComparison.OrdinalIgnoreCase))
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    ConsumerName,
                    integrationEvent,
                    "unexpected-source-service",
                    $"Integration event source service '{integrationEvent.SourceService}' is not supported by consumer '{ConsumerName}'."),
                cancellationToken);
            return;
        }

        var document = integrationEvent.Payload.DocumentReference;
        if (!string.Equals(document.SourceService, "inventory", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(document.DocumentType, "inventory-count-variance", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await sender.Send(
            new CompleteStockCountAdjustmentApprovalCommand(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                document.DocumentId,
                integrationEvent.Payload.ChainId,
                integrationEvent.Payload.Result),
            cancellationToken);
    }
}
