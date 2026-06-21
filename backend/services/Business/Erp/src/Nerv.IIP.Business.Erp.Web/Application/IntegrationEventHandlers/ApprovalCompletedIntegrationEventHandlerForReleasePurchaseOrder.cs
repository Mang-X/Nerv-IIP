using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Approval.ApprovalCompletedIntegrationEvent", ConsumerName)]
public sealed class ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder(ApplicationDbContext dbContext)
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

    private static readonly IntegrationEventEnvelopeValidator Validator = new();

    public async Task HandleAsync(ApprovalCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var validation = Validator.Validate(integrationEvent, ConsumerOptions);
        if (!validation.IsValid)
        {
            throw new KnownException(validation.Message);
        }

        if (!string.Equals(integrationEvent.SourceService, ApprovalIntegrationEventSources.BusinessApproval, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(integrationEvent.Payload.DocumentReference.SourceService, "business-erp", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(integrationEvent.Payload.DocumentReference.DocumentType, "purchase-order", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var order = await dbContext.PurchaseOrders.SingleOrDefaultAsync(x =>
            x.OrganizationId == integrationEvent.OrganizationId
            && x.EnvironmentId == integrationEvent.EnvironmentId
            && x.PurchaseOrderNo == integrationEvent.Payload.DocumentReference.DocumentId,
            cancellationToken);
        if (order is null)
        {
            return;
        }

        try
        {
            if (string.Equals(integrationEvent.Payload.Result, ApprovalResults.Approved, StringComparison.OrdinalIgnoreCase))
            {
                order.ReleaseAfterApproval(integrationEvent.Payload.ChainId);
            }
            else if (string.Equals(integrationEvent.Payload.Result, ApprovalResults.Rejected, StringComparison.OrdinalIgnoreCase)
                || string.Equals(integrationEvent.Payload.Result, ApprovalResults.Returned, StringComparison.OrdinalIgnoreCase))
            {
                order.CancelAfterApprovalRejected(integrationEvent.Payload.ChainId);
            }
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message, exception);
        }
    }

    [CapSubscribe("Nerv.IIP.Contracts.Approval.ApprovalCompletedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(ApprovalCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }
}
