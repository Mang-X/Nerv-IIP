using Nerv.IIP.Business.Mes.Domain.AggregatesModel.AndonCallAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.Repositories;

public interface IAndonCallRepository : IRepository<AndonCall, AndonCallId>;

public sealed class AndonCallRepository(ApplicationDbContext context)
    : RepositoryBase<AndonCall, AndonCallId, ApplicationDbContext>(context), IAndonCallRepository;
