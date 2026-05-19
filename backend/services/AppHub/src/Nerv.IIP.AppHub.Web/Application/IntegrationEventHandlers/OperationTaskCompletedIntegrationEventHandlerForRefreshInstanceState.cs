using MediatR;
using Nerv.IIP.AppHub.Web.Application.Commands;
using Nerv.IIP.Contracts.Ops;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.AppHub.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Ops.OperationTaskCompletedIntegrationEvent", "apphub.refresh-instance-state")]
public sealed class OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState(ISender sender)
    : IIntegrationEventHandler<OperationTaskCompletedIntegrationEvent>
{
    public async Task HandleAsync(OperationTaskCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await sender.Send(new RefreshInstanceStateAfterOperationCommand(integrationEvent), cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Ops.OperationTaskFailedIntegrationEvent", "apphub.refresh-instance-state")]
public sealed class OperationTaskFailedIntegrationEventHandlerForRefreshInstanceState(ISender sender)
    : IIntegrationEventHandler<OperationTaskFailedIntegrationEvent>
{
    public async Task HandleAsync(OperationTaskFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await sender.Send(new RefreshInstanceStateAfterFailedOperationCommand(integrationEvent), cancellationToken);
    }
}
