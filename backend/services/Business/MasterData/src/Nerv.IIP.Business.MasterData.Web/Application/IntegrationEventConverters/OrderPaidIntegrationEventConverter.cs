using Nerv.IIP.Business.MasterData.Domain.DomainEvents;
using Nerv.IIP.Business.MasterData.Web.Application.IntegrationEvents;

namespace Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;

public class OrderPaidIntegrationEventConverter
    : IIntegrationEventConverter<OrderPaidDomainEvent, OrderPaidIntegrationEvent>
{
    public OrderPaidIntegrationEvent Convert(OrderPaidDomainEvent domainEvent)
    {
        return new OrderPaidIntegrationEvent(domainEvent.Order.Id);
    }
}