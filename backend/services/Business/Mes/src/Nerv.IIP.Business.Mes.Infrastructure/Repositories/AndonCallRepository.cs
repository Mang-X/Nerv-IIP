using Nerv.IIP.Business.Mes.Domain.AggregatesModel.AndonCallAggregate;
using Npgsql;

namespace Nerv.IIP.Business.Mes.Infrastructure.Repositories;

public interface IAndonCallRepository : IRepository<AndonCall, AndonCallId>;

public sealed class AndonCallRepository(ApplicationDbContext context)
    : RepositoryBase<AndonCall, AndonCallId, ApplicationDbContext>(context), IAndonCallRepository
{
    public static bool IsWriteConflict(DbUpdateException exception) => exception switch
    {
        DbUpdateConcurrencyException => exception.Entries.Count > 0 && exception.Entries.All(x => x.Entity is AndonCall),
        { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_andon_calls_organization_id_environment_id_raise_intent_key" } } => true,
        _ => false
    };
}
