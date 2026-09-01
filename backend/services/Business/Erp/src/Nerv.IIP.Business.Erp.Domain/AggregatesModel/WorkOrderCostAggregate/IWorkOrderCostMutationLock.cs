namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;

public interface IWorkOrderCostMutationLock
{
    Task AcquireAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        CancellationToken cancellationToken);
}
