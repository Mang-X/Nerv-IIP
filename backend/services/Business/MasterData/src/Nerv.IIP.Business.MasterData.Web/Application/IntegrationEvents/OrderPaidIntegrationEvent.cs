using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.OrderAggregate;

namespace Nerv.IIP.Business.MasterData.Web.Application.IntegrationEvents;

public record OrderPaidIntegrationEvent(OrderId OrderId);