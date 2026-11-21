namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;

public interface IWorkOrderCostMutationLock
{
    Task AcquireReportAsync(
        string organizationId,
        string environmentId,
        string reportNo,
        CancellationToken cancellationToken);

    Task AcquireAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        CancellationToken cancellationToken);
}
