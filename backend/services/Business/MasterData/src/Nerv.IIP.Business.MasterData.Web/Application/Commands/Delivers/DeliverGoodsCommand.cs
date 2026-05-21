using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeliverAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.OrderAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure.Repositories;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.MasterData.Web.Application.Commands.Delivers;

public record DeliverGoodsCommand(OrderId OrderId) : ICommand<DeliverRecordId>;

public class DeliverGoodsCommandHandler(IDeliverRecordRepository deliverRecordRepository)
    : ICommandHandler<DeliverGoodsCommand, DeliverRecordId>
{
    public Task<DeliverRecordId> Handle(DeliverGoodsCommand request, CancellationToken cancellationToken)
    {
        var record = new DeliverRecord(request.OrderId);
        deliverRecordRepository.Add(record);
        return Task.FromResult(record.Id);
    }
}