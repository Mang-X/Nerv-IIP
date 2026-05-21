using Nerv.IIP.Business.MasterData.Web.Application.Commands.Delivers;
using Nerv.IIP.Business.MasterData.Web.Application.IntegrationEvents;

namespace Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventHandlers;

public class OrderPaidIntegrationEventHandlerForDeliverGoods(IMediator mediator) : IIntegrationEventHandler<OrderPaidIntegrationEvent>
{
    public async Task HandleAsync(OrderPaidIntegrationEvent eventData, CancellationToken cancellationToken = default)
    {
        var cmd = new DeliverGoodsCommand(eventData.OrderId);
        _ = await mediator.Send(cmd, cancellationToken);
    }
}