using NetCorePal.Extensions.Repository.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.OrderAggregate;
using NetCorePal.Extensions.Repository;

namespace Nerv.IIP.Business.MasterData.Infrastructure.Repositories;

public interface IOrderRepository : IRepository<Order, OrderId>
{
}

public class OrderRepository(ApplicationDbContext context) : RepositoryBase<Order, OrderId, ApplicationDbContext>(context), IOrderRepository
{
}