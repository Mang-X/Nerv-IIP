using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountAdjustmentAggregate;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Approval.ApprovalCompletedIntegrationEvent", ConsumerName)]
public sealed class ApprovalCompletedIntegrationEventHandlerForStockCountAdjustment(
    ApplicationDbContext dbContext,
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

        var adjustment = await dbContext.StockCountAdjustments.SingleOrDefaultAsync(x =>
            x.OrganizationId == integrationEvent.OrganizationId
            && x.EnvironmentId == integrationEvent.EnvironmentId
            && x.CountTaskCode == document.DocumentId
            && x.ApprovalChainId == integrationEvent.Payload.ChainId,
            cancellationToken);
        if (adjustment is null || adjustment.Status != StockCountAdjustmentStatuses.PendingApproval)
        {
            return;
        }

        var task = await dbContext.StockCountTasks.SingleAsync(x =>
            x.OrganizationId == adjustment.OrganizationId
            && x.EnvironmentId == adjustment.EnvironmentId
            && x.CountTaskCode == adjustment.CountTaskCode,
            cancellationToken);
        var ledger = await dbContext.StockLedgers.SingleAsync(x =>
            x.OrganizationId == task.LedgerOrganizationId
            && x.EnvironmentId == task.LedgerEnvironmentId
            && x.SkuCode == task.SkuCode
            && x.UomCode == task.UomCode
            && x.SiteCode == task.SiteCode
            && x.LocationCode == task.LocationCode
            && x.LotNo == task.LotNo
            && x.SerialNo == task.SerialNo
            && x.QualityStatus == task.QualityStatus
            && x.OwnerType == task.OwnerType
            && x.OwnerId == task.OwnerId,
            cancellationToken);

        try
        {
            if (string.Equals(integrationEvent.Payload.Result, ApprovalResults.Approved, StringComparison.OrdinalIgnoreCase))
            {
                var movement = task.ConfirmApprovedAdjustment(ledger, adjustment.IdempotencyKey);
                dbContext.StockMovements.Add(movement);
                adjustment.MarkPosted(movement);
                return;
            }

            if (string.Equals(integrationEvent.Payload.Result, ApprovalResults.Rejected, StringComparison.OrdinalIgnoreCase)
                || string.Equals(integrationEvent.Payload.Result, ApprovalResults.Returned, StringComparison.OrdinalIgnoreCase))
            {
                task.RequireRecountAfterApprovalRejection(ledger);
                adjustment.VoidAfterApprovalRejection();
            }
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message, exception);
        }
    }
}
