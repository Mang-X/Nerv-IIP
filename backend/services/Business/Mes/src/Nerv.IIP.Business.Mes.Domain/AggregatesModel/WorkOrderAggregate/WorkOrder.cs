using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;

namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;

public partial record WorkOrderId : IGuidStronglyTypedId;

public sealed record RoutingStepSnapshot(
    string OperationTaskId,
    int OperationSequence,
    string WorkCenterId,
    IReadOnlyCollection<string> AlternativeWorkCenterIds,
    TimeSpan Duration);

public sealed class WorkOrder : Entity<WorkOrderId>, IAggregateRoot
{
    private WorkOrder()
    {
    }

    private WorkOrder(
        string organizationId,
        string environmentId,
        string workOrderId,
        string skuId,
        string? productionVersionId,
        decimal quantity,
        int priority,
        DateTimeOffset dueUtc)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        WorkOrderIdValue = Required(workOrderId);
        SkuId = Required(skuId);
        ProductionVersionId = string.IsNullOrWhiteSpace(productionVersionId) ? null : productionVersionId.Trim();
        Quantity = quantity > 0 ? quantity : throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        Priority = priority;
        DueUtc = dueUtc;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkOrderIdValue { get; private set; } = string.Empty;
    public string SkuId { get; private set; } = string.Empty;
    public string? ProductionVersionId { get; private set; }
    public decimal Quantity { get; private set; }
    public int Priority { get; private set; }
    public DateTimeOffset DueUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public string WorkOrderId => WorkOrderIdValue;

    public static WorkOrder Create(
        string organizationId,
        string environmentId,
        string workOrderId,
        string skuId,
        string? productionVersionId,
        decimal quantity,
        int priority,
        DateTimeOffset dueUtc)
    {
        var workOrder = new WorkOrder(
            organizationId,
            environmentId,
            workOrderId,
            skuId,
            productionVersionId,
            quantity,
            priority,
            dueUtc);
        workOrder.AddDomainEvent(new WorkOrderCreatedDomainEvent(workOrder));
        return workOrder;
    }

    public IReadOnlyCollection<OperationTask> Release(
        DateTimeOffset earliestStartUtc,
        IReadOnlyCollection<RoutingStepSnapshot> routingSteps)
    {
        ArgumentNullException.ThrowIfNull(routingSteps);
        if (routingSteps.Count == 0)
        {
            throw new ArgumentException("At least one routing step is required.", nameof(routingSteps));
        }

        var tasks = routingSteps
            .OrderBy(x => x.OperationSequence)
            .Select(step => OperationTask.Queue(
                OrganizationId,
                EnvironmentId,
                WorkOrderId,
                step.OperationTaskId,
                step.OperationSequence,
                step.WorkCenterId,
                step.AlternativeWorkCenterIds,
                earliestStartUtc,
                step.Duration))
            .ToList();
        AddDomainEvent(new WorkOrderReleasedDomainEvent(this, tasks));
        return tasks;
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }
}
