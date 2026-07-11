using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Infrastructure.IntegrationEvents;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Approval.ApprovalCompletedIntegrationEvent", ConsumerName)]
public sealed class ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<ApprovalCompletedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-erp.purchase-order-approval-completed";

    private static readonly IntegrationEventConsumerOptions ConsumerOptions = new(
        ConsumerName,
        [
            ApprovalIntegrationEventTypes.ApprovalApproved,
            ApprovalIntegrationEventTypes.ApprovalRejected,
            ApprovalIntegrationEventTypes.ApprovalReturned,
        ],
        ApprovalIntegrationEventVersions.V1);

    private readonly IntegrationEventConsumerGuard<ApprovalCompletedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        ConsumerOptions);

    public Task HandleAsync(ApprovalCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Approval.ApprovalCompletedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(ApprovalCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

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

        if (string.Equals(integrationEvent.Payload.DocumentReference.SourceService, "business-erp", StringComparison.OrdinalIgnoreCase)
            && string.Equals(integrationEvent.Payload.DocumentReference.DocumentType, "sales-order-credit-release", StringComparison.OrdinalIgnoreCase))
        {
            var salesOrder = await dbContext.SalesOrders.SingleOrDefaultAsync(x =>
                x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.SalesOrderNo == integrationEvent.Payload.DocumentReference.DocumentId,
                cancellationToken);
            if (salesOrder is null || !await ErpProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
            {
                return;
            }

            if (string.Equals(integrationEvent.Payload.Result, ApprovalResults.Approved, StringComparison.OrdinalIgnoreCase))
            {
                salesOrder.ReleaseCreditHold();
            }

            return;
        }

        if (!string.Equals(integrationEvent.Payload.DocumentReference.SourceService, "business-erp", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(integrationEvent.Payload.DocumentReference.DocumentType, "purchase-order", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var order = await dbContext.PurchaseOrders
            .Include(x => x.Lines)
            .Include(x => x.ChangeHistory)
            .SingleOrDefaultAsync(x =>
            x.OrganizationId == integrationEvent.OrganizationId
            && x.EnvironmentId == integrationEvent.EnvironmentId
            && x.PurchaseOrderNo == integrationEvent.Payload.DocumentReference.DocumentId,
            cancellationToken);
        if (order is null)
        {
            return;
        }

        if (!await ErpProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        try
        {
            if (string.Equals(integrationEvent.Payload.Result, ApprovalResults.Approved, StringComparison.OrdinalIgnoreCase))
            {
                if (order.Status == Domain.AggregatesModel.PurchaseOrderAggregate.PurchaseOrderStatus.PendingApproval)
                {
                    order.ReleaseAfterApproval(integrationEvent.Payload.ChainId);
                }
                else
                {
                    order.ApplyApprovedChange(integrationEvent.Payload.ChainId);
                }
            }
            else if (string.Equals(integrationEvent.Payload.Result, ApprovalResults.Rejected, StringComparison.OrdinalIgnoreCase)
                || string.Equals(integrationEvent.Payload.Result, ApprovalResults.Returned, StringComparison.OrdinalIgnoreCase))
            {
                if (order.Status == Domain.AggregatesModel.PurchaseOrderAggregate.PurchaseOrderStatus.PendingApproval)
                {
                    order.ReturnToEditableAfterApprovalRejected(integrationEvent.Payload.ChainId);
                }
                else
                {
                    order.RejectChange(integrationEvent.Payload.ChainId);
                }
            }
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message, exception);
        }
    }
}

internal static class ErpProcessedIntegrationEventInbox
{
    public static Task<bool> TryRecordAsync(
        ApplicationDbContext dbContext,
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken)
    {
        return ProcessedIntegrationEventInbox.TryRecordAsync(
            dbContext,
            dbContext.ProcessedIntegrationEvents,
            consumerName,
            integrationEvent,
            record => new ProcessedIntegrationEvent(
                record.ConsumerName,
                record.EventId,
                record.EventType,
                record.EventVersion,
                record.SourceService,
                record.IdempotencyKey,
                record.ProcessedAtUtc),
            cancellationToken);
    }
}
