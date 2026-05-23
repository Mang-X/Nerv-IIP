using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.Repositories;

public interface IOperationTaskRepository : IRepository<OperationTask, OperationTaskId>
{
    Task<IReadOnlyCollection<OperationTask>> GetByScopeWorkOrdersAsync(
        string organizationId,
        string environmentId,
        IReadOnlyCollection<string> workOrderIds,
        CancellationToken cancellationToken = default);
}

public sealed class OperationTaskRepository(ApplicationDbContext context)
    : RepositoryBase<OperationTask, OperationTaskId, ApplicationDbContext>(context), IOperationTaskRepository
{
    public async Task<IReadOnlyCollection<OperationTask>> GetByScopeWorkOrdersAsync(
        string organizationId,
        string environmentId,
        IReadOnlyCollection<string> workOrderIds,
        CancellationToken cancellationToken = default)
    {
        return await DbContext.OperationTasks
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                workOrderIds.Contains(x.WorkOrderId))
            .ToListAsync(cancellationToken);
    }
}
