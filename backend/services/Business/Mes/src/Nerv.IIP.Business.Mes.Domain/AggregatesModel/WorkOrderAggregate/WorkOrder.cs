using System.Collections.Immutable;
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
        string? sourceDemandReference,
        IReadOnlyCollection<string>? sourceDemandReferences = null)
    {
        SourceSystem = DomainGuard.Required(sourceSystem, nameof(sourceSystem));
        SourceDocumentType = DomainGuard.Required(sourceDocumentType, nameof(sourceDocumentType));
        SourceDocumentId = DomainGuard.Required(sourceDocumentId, nameof(sourceDocumentId));
        SourceDemandReference = string.IsNullOrWhiteSpace(sourceDemandReference) ? null : sourceDemandReference.Trim();
        var references = new List<string>();
        if (SourceDemandReference is not null)
        {
            references.Add(SourceDemandReference);
        }

        foreach (var candidate in sourceDemandReferences ?? [])
        {
            var reference = candidate?.Trim();
            if (string.IsNullOrEmpty(reference) || references.Contains(reference, StringComparer.Ordinal))
            {
                continue;
            }

            references.Add(reference);
        }

        // AsReadOnly：`IReadOnlyList<string>` 只是静态类型上的只读，直接交出 List 实例
        // 调用方一个向下转型就能绕过聚合、在 EF 变更跟踪背后改掉这条追溯链。
        SourceDemandReferences = references.AsReadOnly();
    }

    public string SourceSystem { get; private set; } = string.Empty;
    public string SourceDocumentType { get; private set; } = string.Empty;
    public string SourceDocumentId { get; private set; } = string.Empty;
    public string? SourceDemandReference { get; private set; }

    /// <summary>
    /// 来源建议 peg 到的全部需求源引用（含主引用，按传入顺序去重）。
    /// 合批建议一张工单对应多张需求源单据；单值 <see cref="SourceDemandReference"/> 只能点亮其中一张，
    /// 追溯读面按本集合为每个需求源生成 pegged-to-plan 边。
    /// 升级前的历史行本列为 null，读面回退单值引用；新建工单恒为非空集合。
    /// </summary>
    public IReadOnlyList<string>? SourceDemandReferences { get; private set; }
}

public sealed class WorkOrder : Entity<WorkOrderId>, IAggregateRoot
{
    public const string StandardType = "standard";
    public const string ReworkType = "rework";
    public const string CreatedStatus = "created";
    public const string ReleasedStatus = "released";
    public const string StartedStatus = "started";
    public const string HoldStatus = "hold";
    public const string CompletedStatus = "completed";
    public const string ClosedStatus = "closed";
    public const string CancelledStatus = "cancelled";
    public const string ScrappedStatus = "scrapped";
    public const string SplitStatus = "split";
    public const string MergedStatus = "merged";
    public const string MaterialRequirementSnapshotCapturedStatus = "captured";
    public const string MaterialRequirementSnapshotNoRequirementsStatus = "no-requirements";

    /// <summary>
    /// 报工不再受理的工单状态。<see cref="RecordProductionProgress"/> 用它判「工单是否还可执行」，
    /// #3000 的发布投影回填用**同一份**集合挑「哪些工单的工序还会再撞首件门禁」。
    ///
    /// 两侧必须同源：报工命令的准入路径只看工序 <c>InProgress</c>，工单状态在准入判断里一次都不出现
    /// （`MesProductionCommands` 的首件门禁调用点就紧跟在那句工序状态检查之后），
    /// 真正筛掉工单的就是本集合。回填若另起一套工单状态白名单，白名单一旦比本集合窄，
    /// 落在差集里的工序读首件确认就永远是 not-synchronized，被门禁永久拒且无自愈路径
    /// —— <c>completed</c> 正是这样一个差集：它不在本集合里（超收容差显式为「已达量后继续报工」留了空间），
    /// 却曾被回填的白名单排除。
    /// </summary>
    public static readonly ImmutableArray<string> NonExecutableStatuses =
    [
        CancelledStatus,
        ClosedStatus,
        ScrappedStatus,
        SplitStatus,
        MergedStatus,
    ];

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
        decimal overReceiptTolerancePercent,
        string workOrderType,
        string? sourceWorkOrderId = null,
        string? sourceOperationTaskId = null,
        string? sourceDefectNo = null,
        string? sourceNcrId = null,
        string? sourceNcrCode = null,
        string? sourceLotNo = null,
        string? sourceSerialNo = null,
        DateTimeOffset? sourceReworkRequestedAtUtc = null)
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
        WorkOrderType = DomainGuard.Required(workOrderType, nameof(workOrderType));
        SourceWorkOrderId = string.IsNullOrWhiteSpace(sourceWorkOrderId) ? null : sourceWorkOrderId.Trim();
        SourceOperationTaskId = string.IsNullOrWhiteSpace(sourceOperationTaskId) ? null : sourceOperationTaskId.Trim();
        SourceDefectNo = string.IsNullOrWhiteSpace(sourceDefectNo) ? null : sourceDefectNo.Trim();
        SourceNcrId = string.IsNullOrWhiteSpace(sourceNcrId) ? null : sourceNcrId.Trim();
        SourceNcrCode = string.IsNullOrWhiteSpace(sourceNcrCode) ? null : sourceNcrCode.Trim();
        SourceLotNo = string.IsNullOrWhiteSpace(sourceLotNo) ? null : sourceLotNo.Trim();
        SourceSerialNo = string.IsNullOrWhiteSpace(sourceSerialNo) ? null : sourceSerialNo.Trim();
        SourceReworkRequestedAtUtc = sourceReworkRequestedAtUtc;
        Status = CreatedStatus;
        Version = 1;
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
    public long Version { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public decimal CompletedQuantity { get; private set; }
    public decimal ScrapQuantity { get; private set; }
    public int CostReportCount { get; private set; }
    public int MaterialMovementCount { get; private set; }
    public decimal? CapitalizedUnitCost { get; private set; }
    public decimal OverReceiptTolerancePercent { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public string? HoldReason { get; private set; }
    public string? CancelReason { get; private set; }
    public string? MaterialRequirementSnapshotStatus { get; private set; }
    public DateTimeOffset? MaterialRequirementSnapshotEvaluatedAtUtc { get; private set; }
    public string? MaterialRequirementSnapshotProductionVersionId { get; private set; }
    public string WorkOrderType { get; private set; } = StandardType;
    public string? SourceWorkOrderId { get; private set; }
    public string? SourceOperationTaskId { get; private set; }
    public string? SourceDefectNo { get; private set; }
    public string? SourceNcrId { get; private set; }
    public string? SourceNcrCode { get; private set; }
    public string? SourceLotNo { get; private set; }
    public string? SourceSerialNo { get; private set; }
    public DateTimeOffset? SourceReworkRequestedAtUtc { get; private set; }

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
            overReceiptTolerancePercent,
            StandardType);
        workOrder.AddDomainEvent(new WorkOrderCreatedDomainEvent(workOrder));
        return workOrder;
    }

    public static WorkOrder CreateRework(
        string organizationId,
        string environmentId,
        string workOrderId,
        string skuId,
        string? productionVersionId,
        string? uomCode,
        decimal quantity,
        int priority,
        DateTimeOffset dueUtc,
        string sourceWorkOrderId,
        string? sourceOperationTaskId,
        string sourceDefectNo,
        string sourceNcrId,
        string sourceNcrCode,
        string? sourceLotNo,
        string? sourceSerialNo,
        DateTimeOffset requestedAtUtc,
        string correlationId,
        string causationId)
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
            null,
            0m,
            ReworkType,
            DomainGuard.Required(sourceWorkOrderId, nameof(sourceWorkOrderId)),
            sourceOperationTaskId,
            DomainGuard.Required(sourceDefectNo, nameof(sourceDefectNo)),
            DomainGuard.Required(sourceNcrId, nameof(sourceNcrId)),
            DomainGuard.Required(sourceNcrCode, nameof(sourceNcrCode)),
            sourceLotNo,
            sourceSerialNo,
            requestedAtUtc);
        workOrder.AddDomainEvent(new ReworkWorkOrderCreatedDomainEvent(
            workOrder,
            requestedAtUtc,
            DomainGuard.Required(correlationId, nameof(correlationId)),
            DomainGuard.Required(causationId, nameof(causationId))));
        return workOrder;
    }

    /// <summary>
    /// 发布工单并当场按工艺路线建出工序任务。
    ///
    /// <para><paramref name="earliestStartUtc"/> 与 <paramref name="releasedAt"/> 是**两件事**，不可一值两用：
    /// 前者是本次发布给工序定的**最早可开工时刻**，语义上允许落在未来（下达后下一班才开工是正常排产）；
    /// 后者是**发布这件事发生的时刻**，会作为发布事实发给 Quality，落在未来会让该工序此后的每一条报工
    /// 都被 <c>PeriodicInspectionOperation</c> 判为「报工早于发布」进死信。</para>
    ///
    /// <para>工序在这一刻才建出，因此不可能已有报工，
    /// <paramref name="releasedAt"/> 的报工下界项由调用方传 <c>null</c> 即可。</para>
    /// </summary>
    public IReadOnlyCollection<OperationTask> Release(
        DateTimeOffset earliestStartUtc,
        WorkOrderReleaseFactTime releasedAt,
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
        AdvanceVersion();
        AddDomainEvent(new WorkOrderReleasedDomainEvent(this, tasks, releasedAt));
        return tasks;
    }

    /// <summary>
    /// 只翻状态、不携带工序的发布。
    ///
    /// <para><b>时刻取值。</b>本重载拿不到任何工序，也就拿不到报工集合，
    /// 聚合自己知道的唯一下界是 <see cref="CreatedAtUtc"/>——工单不可能早于自己被创建就被发布。
    /// 这个下界是**读取当刻**的 <see cref="CreatedAtUtc"/>：调用方若要把创建时刻回拨成历史时刻，
    /// 必须在调用本方法**之前**回拨（唯一生产调用方 <c>WorldHistorySeedService</c> 已按此顺序，
    /// 并在调用点写明了该顺序要求）。</para>
    ///
    /// <para><b>为什么这条不携带工序的发布事实不会伤到 Quality。</b>真实理由是
    /// <c>WorldHistorySeedService.SaveHistoryFactsAsync</c> 在写盘前按 <c>ChangeTracker</c>
    /// 统一 <c>ClearDomainEvents()</c>，**本事件从不出域**。
    /// 不要拿「Quality 对空 operations 一律拒收」当安全性依据——
    /// <c>PeriodicInspectionReleaseProjection.ValidateReleasedOperations</c> 对空 operations 是
    /// <c>throw</c>，即**整封进死信**，不是无害跳过；那是「出事了」，不是「没事」。</para>
    /// </summary>
    public void MarkReleased()
    {
        ThrowIfCannotRelease();

        Status = ReleasedStatus;
        AdvanceVersion();
        AddDomainEvent(new WorkOrderReleasedDomainEvent(
            this,
            [],
            WorkOrderReleaseFactTime.AtAggregateCreation(CreatedAtUtc)));
    }

    /// <summary>
    /// 对已经有工序快照的工单补记发布（计划转工单后再下达这条主流程）。
    /// 这些工序可能早已开工报工，因此发布事实的时刻必须按「不晚于该工单任何一条既有报工」取下界；
    /// 该不变量由 <see cref="WorkOrderReleaseFactTime"/> 的构造口径承担，本方法不再收裸时刻。
    /// </summary>
    public void MarkReleased(IReadOnlyCollection<OperationTask> operationTasks, WorkOrderReleaseFactTime releasedAt)
    {
        ArgumentNullException.ThrowIfNull(operationTasks);
        if (operationTasks.Count == 0)
        {
            throw new ArgumentException("At least one operation task is required.", nameof(operationTasks));
        }

        ThrowIfCannotRelease();

        Status = ReleasedStatus;
        AdvanceVersion();
        AddDomainEvent(new WorkOrderReleasedDomainEvent(this, operationTasks, releasedAt));
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

        if (ProductionVersionId is null)
        {
            ProductionVersionId = normalizedProductionVersionId;
            AdvanceVersion();
        }
    }

    public void RecordMaterialRequirementSnapshot(string status, DateTimeOffset evaluatedAtUtc)
    {
        var normalizedStatus = DomainGuard.Required(status, nameof(status)).ToLowerInvariant();
        if (normalizedStatus is not MaterialRequirementSnapshotCapturedStatus
            and not MaterialRequirementSnapshotNoRequirementsStatus)
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Unsupported material requirement snapshot status.");
        }

        if (string.IsNullOrWhiteSpace(ProductionVersionId))
        {
            throw new InvalidOperationException(
                "A production version is required before material readiness can be proven.");
        }

        MaterialRequirementSnapshotStatus = normalizedStatus;
        MaterialRequirementSnapshotEvaluatedAtUtc = evaluatedAtUtc;
        MaterialRequirementSnapshotProductionVersionId = ProductionVersionId;
        AdvanceVersion();
    }

    public void RebindProductionVersionForEngineeringChange(string productionVersionId)
    {
        var normalizedProductionVersionId = DomainGuard.Required(productionVersionId, nameof(productionVersionId));
        if (Status is not CreatedStatus and not ReleasedStatus)
        {
            throw new InvalidOperationException("Only not-started work orders can be rebound after an engineering change.");
        }

        ProductionVersionId = normalizedProductionVersionId;
        if (Status == CreatedStatus)
        {
            MaterialRequirementSnapshotStatus = null;
            MaterialRequirementSnapshotEvaluatedAtUtc = null;
            MaterialRequirementSnapshotProductionVersionId = null;
        }
        AdvanceVersion();
    }

    private void ThrowIfCannotRelease()
    {
        if (Status == ReleasedStatus)
        {
            throw new InvalidOperationException("Work order has already been released.");
        }

        if (Status is CompletedStatus or ClosedStatus or CancelledStatus or ScrappedStatus or SplitStatus or MergedStatus)
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
        AdvanceVersion();
    }

    public void Hold(string reason)
    {
        if (Status is CompletedStatus or ClosedStatus or CancelledStatus or ScrappedStatus or SplitStatus or MergedStatus)
        {
            throw new InvalidOperationException("Closed work orders cannot be held.");
        }

        HoldReason = DomainGuard.Required(reason, nameof(reason));
        Status = HoldStatus;
        AdvanceVersion();
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
        AdvanceVersion();
    }

    public bool Cancel(string reason, DateTimeOffset cancelledAtUtc, IReadOnlyCollection<string>? materialIssueRequestNos = null)
    {
        if (Status is CompletedStatus or ClosedStatus or SplitStatus or MergedStatus)
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
        AdvanceVersion();
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

        if (NonExecutableStatuses.Contains(Status))
        {
            throw new InvalidOperationException("Work order is not executable.");
        }

        var configuredMaximumQuantity = Quantity * (1m + OverReceiptTolerancePercent / 100m);
        var hardMaximumQuantity = Quantity * 1.2m;
        var maxQuantity = Math.Min(configuredMaximumQuantity, hardMaximumQuantity);
        if (CompletedQuantity + ScrapQuantity + goodQuantity + scrapQuantity > maxQuantity)
        {
            if (hardMaximumQuantity < configuredMaximumQuantity)
            {
                throw new InvalidOperationException(
                    $"生产工单 {WorkOrderIdValue} 的累计报工数量超过计划量 {Quantity} 的 120% 硬上限 {hardMaximumQuantity}。请调整报工数量或工单计划量后重试。");
            }

            throw new InvalidOperationException(
                $"生产工单 {WorkOrderIdValue} 的累计报工数量超过允许上限 {maxQuantity}。请调整报工数量或工单超产容差后重试。");
        }

        CompletedQuantity += goodQuantity;
        ScrapQuantity += scrapQuantity;
        var wasCompleted = Status == CompletedStatus;
        Status = CompletedQuantity + ScrapQuantity >= Quantity ? CompletedStatus : StartedStatus;
        AdvanceVersion();
        if (!wasCompleted && Status == CompletedStatus)
        {
            AddDomainEvent(new WorkOrderCompletedDomainEvent(this, reportedAtUtc));
        }
    }

    public void RegisterCostReport(int materialMovementCount)
    {
        if (materialMovementCount < 0) throw new ArgumentOutOfRangeException(nameof(materialMovementCount));
        CostReportCount++;
        MaterialMovementCount += materialMovementCount;
        AdvanceVersion();
    }

    public void ApplyCapitalizedUnitCost(decimal unitCost)
    {
        var normalizedUnitCost = DomainGuard.Positive(unitCost, nameof(unitCost));
        if (CapitalizedUnitCost.HasValue && CapitalizedUnitCost.Value != normalizedUnitCost)
        {
            throw new InvalidOperationException("Work order already has a different capitalized unit cost.");
        }

        CapitalizedUnitCost = normalizedUnitCost;
        AdvanceVersion();
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

        if (Status is CancelledStatus or ScrappedStatus or SplitStatus or MergedStatus)
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

        AdvanceVersion();
    }

    public void Close(DateTimeOffset closedAtUtc)
    {
        if (Status != CompletedStatus)
        {
            throw new InvalidOperationException("Only completed work orders can be closed.");
        }

        Status = ClosedStatus;
        ClosedAtUtc = closedAtUtc;
        AdvanceVersion();
        AddDomainEvent(new WorkOrderClosedDomainEvent(this, closedAtUtc));
    }

    public void MarkSplit()
    {
        EnsureTransformable();
        Status = SplitStatus;
        AdvanceVersion();
    }

    public void MarkMerged()
    {
        EnsureTransformable();
        Status = MergedStatus;
        AdvanceVersion();
    }

    private void EnsureTransformable()
    {
        if (Status is not CreatedStatus and not ReleasedStatus)
        {
            throw new InvalidOperationException(
                $"状态为 {Status} 的工单不允许拆分或合并，仅 created/released 可执行。");
        }
    }

    private void AdvanceVersion() => Version++;
}
