using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;

namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;

public partial record WorkOrderId : IGuidStronglyTypedId;

public sealed record RoutingStepSnapshot(
    string OperationTaskId,
    int OperationSequence,
    string WorkCenterId,
    IReadOnlyCollection<string> AlternativeWorkCenterIds,
    TimeSpan Duration,
    bool RequiresQualityInspection = false,
    string? OperationCode = null);

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
    public const string StartedStatus = "started";
    public const string HoldStatus = "hold";
    public const string CompletedStatus = "completed";
    public const string ClosedStatus = "closed";
    public const string CancelledStatus = "cancelled";
    public const string ScrappedStatus = "scrapped";

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
        SourcePlanReference? sourcePlanReference,
        decimal overReceiptTolerancePercent)
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
        OverReceiptTolerancePercent = DomainGuard.NonNegative(overReceiptTolerancePercent, nameof(overReceiptTolerancePercent));
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
    public decimal CompletedQuantity { get; private set; }
    public decimal ScrapQuantity { get; private set; }
    public decimal OverReceiptTolerancePercent { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public string? HoldReason { get; private set; }
    public string? CancelReason { get; private set; }

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
        SourcePlanReference? sourcePlanReference = null,
        decimal overReceiptTolerancePercent = 0m)
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
            sourcePlanReference,
            overReceiptTolerancePercent);
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
                step.Duration,
                SkuId,
                UomCode,
                Quantity,
                step.RequiresQualityInspection,
                step.OperationCode))
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

    public void BindProductionVersion(string productionVersionId)
    {
        var normalizedProductionVersionId = DomainGuard.Required(productionVersionId, nameof(productionVersionId));
        if (Status != CreatedStatus)
        {
            throw new InvalidOperationException("Only created work orders can be rebound to a production version.");
        }

        if (!string.IsNullOrWhiteSpace(ProductionVersionId) &&
            !string.Equals(ProductionVersionId, normalizedProductionVersionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Work order is already bound to a different production version.");
        }

        ProductionVersionId = normalizedProductionVersionId;
    }

    public void RebindProductionVersionForEngineeringChange(string productionVersionId)
    {
        var normalizedProductionVersionId = DomainGuard.Required(productionVersionId, nameof(productionVersionId));
        if (Status is not CreatedStatus and not ReleasedStatus)
        {
            throw new InvalidOperationException("Only not-started work orders can be rebound after an engineering change.");
        }

        ProductionVersionId = normalizedProductionVersionId;
    }

    private void ThrowIfCannotRelease()
    {
        if (Status == ReleasedStatus)
        {
            throw new InvalidOperationException("Work order has already been released.");
        }

        if (Status is CompletedStatus or ClosedStatus or CancelledStatus or ScrappedStatus)
        {
            throw new InvalidOperationException("Work order is already in a closed state.");
        }
    }

    public void Start(DateTimeOffset startedAtUtc)
    {
        _ = startedAtUtc;
        if (Status is not ReleasedStatus and not HoldStatus)
        {
            throw new InvalidOperationException("Only released or held work orders can be started.");
        }

        Status = StartedStatus;
        HoldReason = null;
    }

    public void Hold(string reason)
    {
        if (Status is CompletedStatus or ClosedStatus or CancelledStatus or ScrappedStatus)
        {
            throw new InvalidOperationException("Closed work orders cannot be held.");
        }

        HoldReason = DomainGuard.Required(reason, nameof(reason));
        Status = HoldStatus;
    }

    public void ResolveEngineeringChangeHold(string statusBeforeHold)
    {
        var normalizedStatus = DomainGuard.Required(statusBeforeHold, nameof(statusBeforeHold));
        if (Status != HoldStatus)
        {
            return;
        }

        if (normalizedStatus is not CreatedStatus and not ReleasedStatus and not StartedStatus)
        {
            throw new InvalidOperationException($"Cannot restore work order from engineering change hold to status '{normalizedStatus}'.");
        }

        Status = normalizedStatus;
        HoldReason = null;
    }

    public bool Cancel(string reason, DateTimeOffset cancelledAtUtc, IReadOnlyCollection<string>? materialIssueRequestNos = null)
    {
        if (Status is CompletedStatus or ClosedStatus)
        {
            throw new InvalidOperationException("Completed work orders must be closed, not cancelled.");
        }

        if (Status == ScrappedStatus)
        {
            throw new InvalidOperationException("Scrapped work orders cannot be cancelled.");
        }

        if (Status == CancelledStatus)
        {
            return false;
        }

        CancelReason = DomainGuard.Required(reason, nameof(reason));
        Status = CancelledStatus;
        AddDomainEvent(new WorkOrderCancelledDomainEvent(
            this,
            cancelledAtUtc,
            CancelReason,
            (materialIssueRequestNos ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
        return true;
    }

    public void RecordProductionProgress(decimal goodQuantity, decimal scrapQuantity, DateTimeOffset reportedAtUtc)
    {
        _ = reportedAtUtc;
        DomainGuard.NonNegative(goodQuantity, nameof(goodQuantity));
        DomainGuard.NonNegative(scrapQuantity, nameof(scrapQuantity));
        if (goodQuantity + scrapQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(goodQuantity), "At least one progress quantity must be positive.");
        }

        if (Status is CancelledStatus or ClosedStatus or ScrappedStatus)
        {
            throw new InvalidOperationException("Work order is not executable.");
        }

        var maxQuantity = Quantity * (1m + OverReceiptTolerancePercent / 100m);
        if (CompletedQuantity + ScrapQuantity + goodQuantity + scrapQuantity > maxQuantity)
        {
            throw new InvalidOperationException("Reported quantity exceeds work order tolerance.");
        }

        CompletedQuantity += goodQuantity;
        ScrapQuantity += scrapQuantity;
        var wasCompleted = Status == CompletedStatus;
        Status = CompletedQuantity + ScrapQuantity >= Quantity ? CompletedStatus : StartedStatus;
        if (!wasCompleted && Status == CompletedStatus)
        {
            AddDomainEvent(new WorkOrderCompletedDomainEvent(this, reportedAtUtc));
        }
    }

    public void ReverseProductionProgress(decimal goodQuantity, decimal scrapQuantity, DateTimeOffset reversedAtUtc)
    {
        _ = reversedAtUtc;
        DomainGuard.NonNegative(goodQuantity, nameof(goodQuantity));
        DomainGuard.NonNegative(scrapQuantity, nameof(scrapQuantity));
        if (goodQuantity + scrapQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(goodQuantity), "At least one progress quantity must be positive.");
        }

        if (Status == ClosedStatus)
        {
            throw new InvalidOperationException("已关闭工单不允许冲销报工。");
        }

        if (Status is CancelledStatus or ScrappedStatus)
        {
            throw new InvalidOperationException("Work order is not executable.");
        }

        if (CompletedQuantity < goodQuantity || ScrapQuantity < scrapQuantity)
        {
            throw new InvalidOperationException("Production report reversal would make work order progress negative.");
        }

        CompletedQuantity -= goodQuantity;
        ScrapQuantity -= scrapQuantity;
        if (Status == CompletedStatus && CompletedQuantity + ScrapQuantity < Quantity)
        {
            Status = StartedStatus;
        }
    }

    public void Close(DateTimeOffset closedAtUtc)
    {
        if (Status != CompletedStatus)
        {
            throw new InvalidOperationException("Only completed work orders can be closed.");
        }

        Status = ClosedStatus;
        ClosedAtUtc = closedAtUtc;
        AddDomainEvent(new WorkOrderClosedDomainEvent(this, closedAtUtc));
    }
}
