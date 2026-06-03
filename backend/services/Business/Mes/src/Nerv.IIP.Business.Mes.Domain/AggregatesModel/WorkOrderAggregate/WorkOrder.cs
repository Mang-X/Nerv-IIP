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

public sealed class SourcePlanReference
{
    private SourcePlanReference()
    {
    }

    public SourcePlanReference(
        string sourceSystem,
        string sourceDocumentType,
        string sourceDocumentId,
        string? sourceDemandReference)
    {
        SourceSystem = DomainGuard.Required(sourceSystem, nameof(sourceSystem));
        SourceDocumentType = DomainGuard.Required(sourceDocumentType, nameof(sourceDocumentType));
        SourceDocumentId = DomainGuard.Required(sourceDocumentId, nameof(sourceDocumentId));
        SourceDemandReference = string.IsNullOrWhiteSpace(sourceDemandReference) ? null : sourceDemandReference.Trim();
    }

    public string SourceSystem { get; private set; } = string.Empty;
    public string SourceDocumentType { get; private set; } = string.Empty;
    public string SourceDocumentId { get; private set; } = string.Empty;
    public string? SourceDemandReference { get; private set; }
}

public sealed class WorkOrder : Entity<WorkOrderId>, IAggregateRoot
{
    public const string CreatedStatus = "created";
    public const string ReleasedStatus = "released";
    public const string CompletedStatus = "completed";
    public const string CancelledStatus = "cancelled";

    private WorkOrder()
    {
    }

    private WorkOrder(
        string organizationId,
        string environmentId,
        string workOrderId,
        string skuId,
        string? productionVersionId,
        string? uomCode,
        decimal quantity,
        int priority,
        DateTimeOffset dueUtc,
        SourcePlanReference? sourcePlanReference)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        WorkOrderIdValue = DomainGuard.Required(workOrderId, nameof(workOrderId));
        SkuId = DomainGuard.Required(skuId, nameof(skuId));
        ProductionVersionId = string.IsNullOrWhiteSpace(productionVersionId) ? null : productionVersionId.Trim();
        UomCode = string.IsNullOrWhiteSpace(uomCode) ? null : uomCode.Trim();
        Quantity = DomainGuard.Positive(quantity, nameof(quantity));
        Priority = priority;
        DueUtc = dueUtc;
        SourcePlanReference = sourcePlanReference;
        Status = CreatedStatus;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkOrderIdValue { get; private set; } = string.Empty;
    public string SkuId { get; private set; } = string.Empty;
    public string? ProductionVersionId { get; private set; }
    public string? UomCode { get; private set; }
    public decimal Quantity { get; private set; }
    public int Priority { get; private set; }
    public DateTimeOffset DueUtc { get; private set; }
    public SourcePlanReference? SourcePlanReference { get; private set; }
    public string Status { get; private set; } = string.Empty;
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
        DateTimeOffset dueUtc,
        string? uomCode = null,
        SourcePlanReference? sourcePlanReference = null)
    {
        var workOrder = new WorkOrder(
            organizationId,
            environmentId,
            workOrderId,
            skuId,
            productionVersionId,
            uomCode,
            quantity,
            priority,
            dueUtc,
            sourcePlanReference);
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

        ThrowIfCannotRelease();

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
        Status = ReleasedStatus;
        AddDomainEvent(new WorkOrderReleasedDomainEvent(this, tasks));
        return tasks;
    }

    public void MarkReleased()
    {
        ThrowIfCannotRelease();

        Status = ReleasedStatus;
        AddDomainEvent(new WorkOrderReleasedDomainEvent(this, []));
    }

    private void ThrowIfCannotRelease()
    {
        if (Status == ReleasedStatus)
        {
            throw new InvalidOperationException("Work order has already been released.");
        }

        if (Status is CompletedStatus or CancelledStatus)
        {
            throw new InvalidOperationException("Work order is already in a closed state.");
        }
    }
}
