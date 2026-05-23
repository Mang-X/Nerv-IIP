using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.Repositories;

public interface IWorkOrderRepository : IRepository<WorkOrder, WorkOrderId>
{
    Task<bool> ExistsAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        CancellationToken cancellationToken = default);
}

public sealed class WorkOrderRepository(ApplicationDbContext context)
    : RepositoryBase<WorkOrder, WorkOrderId, ApplicationDbContext>(context), IWorkOrderRepository
{
    public async Task<bool> ExistsAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        CancellationToken cancellationToken = default)
    {
        return await DbContext.WorkOrders.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.WorkOrderIdValue == workOrderId,
            cancellationToken);
    }
}
