using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;

namespace Nerv.IIP.Business.Erp.Web.Tests;

internal sealed class TestWorkOrderCostMutationLock : IWorkOrderCostMutationLock
{
    public static TestWorkOrderCostMutationLock Instance { get; } = new();

    public Task AcquireReportAsync(
        string organizationId,
        string environmentId,
        string reportNo,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task AcquireAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        CancellationToken cancellationToken)
        => Task.CompletedTask;
}
