using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;

namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderTransformationAggregate;

public partial record WorkOrderTransformationId : IGuidStronglyTypedId;

public enum WorkOrderTransformationType
{
    Split = 0,
    Merge = 1,
}

public enum WorkOrderTransformationStatus
{
    Applied = 0,
}

public enum WorkOrderLineageType
{
    Split = 0,
    Merge = 1,
}

/// <summary>
/// Immutable work-order facts captured at the transformation boundary.
/// The application layer maps persisted work orders into this value before changing any aggregate.
/// </summary>
public sealed record WorkOrderTransformationWorkOrderSnapshot(
    string WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    string? UomCode,
    decimal Quantity,
    string Status,
    long Version);

public partial record WorkOrderTransformationLineId : IGuidStronglyTypedId;

public sealed class WorkOrderTransformationLine : Entity<WorkOrderTransformationLineId>
{
    private WorkOrderTransformationLine()
    {
    }

    private WorkOrderTransformationLine(
        WorkOrderLineageType lineageType,
        string organizationId,
        string environmentId,
        string sourceWorkOrderId,
        string targetWorkOrderId,
        decimal quantity,
        decimal sourceQuantity,
        decimal targetQuantity,
        string uomCode,
        string sourceStatus,
        string targetStatus,
        long sourceVersion,
        long targetVersion)
    {
        Id = new WorkOrderTransformationLineId(Guid.CreateVersion7());
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        LineageType = lineageType;
        SourceWorkOrderId = sourceWorkOrderId;
        TargetWorkOrderId = targetWorkOrderId;
        Quantity = quantity;
        SourceQuantity = sourceQuantity;
        TargetQuantity = targetQuantity;
        UomCode = uomCode;
        SourceStatus = sourceStatus;
        TargetStatus = targetStatus;
        SourceVersion = sourceVersion;
        TargetVersion = targetVersion;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public WorkOrderLineageType LineageType { get; private set; }
    public string SourceWorkOrderId { get; private set; } = string.Empty;
    public string TargetWorkOrderId { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal SourceQuantity { get; private set; }
    public decimal TargetQuantity { get; private set; }
    public string UomCode { get; private set; } = string.Empty;
    public string SourceStatus { get; private set; } = string.Empty;
    public string TargetStatus { get; private set; } = string.Empty;
    public long SourceVersion { get; private set; }
    public long TargetVersion { get; private set; }

    internal static WorkOrderTransformationLine Create(
        WorkOrderLineageType lineageType,
        string organizationId,
        string environmentId,
        WorkOrderTransformationWorkOrderSnapshot source,
        WorkOrderTransformationWorkOrderSnapshot target,
        decimal quantity) =>
        new(
            lineageType,
            organizationId,
            environmentId,
            source.WorkOrderId,
            target.WorkOrderId,
            quantity,
            source.Quantity,
            target.Quantity,
            source.UomCode!.Trim(),
            source.Status,
            target.Status,
            source.Version,
            target.Version);
}

public sealed class WorkOrderTransformation : Entity<WorkOrderTransformationId>, IAggregateRoot
{
    private readonly List<WorkOrderTransformationLine> lines = [];

    private WorkOrderTransformation()
    {
    }

    private WorkOrderTransformation(
        string organizationId,
        string environmentId,
        WorkOrderTransformationType type,
        string idempotencyKey,
        string requestFingerprint,
        string actorId,
        string reason,
        DateTimeOffset occurredAtUtc)
    {
        Id = new WorkOrderTransformationId(Guid.CreateVersion7());
        OrganizationId = RequiredBounded(organizationId, nameof(organizationId), 100);
        EnvironmentId = RequiredBounded(environmentId, nameof(environmentId), 100);
        Type = type;
        Status = WorkOrderTransformationStatus.Applied;
        IdempotencyKey = RequiredBounded(idempotencyKey, nameof(idempotencyKey), 150);
        RequestFingerprint = RequiredBounded(requestFingerprint, nameof(requestFingerprint), 128);
        ActorId = RequiredBounded(actorId, nameof(actorId), 200);
        Reason = RequiredBounded(reason, nameof(reason), 500);
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public WorkOrderTransformationType Type { get; private set; }
    public WorkOrderTransformationStatus Status { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public string ActorId { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public IReadOnlyCollection<WorkOrderTransformationLine> Lines => lines;

    public static WorkOrderTransformation CreateSplit(
        string organizationId,
        string environmentId,
        WorkOrderTransformationWorkOrderSnapshot source,
        IReadOnlyCollection<WorkOrderTransformationWorkOrderSnapshot> targets,
        string idempotencyKey,
        string requestFingerprint,
        string actorId,
        string reason,
        DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count < 2)
        {
            throw new InvalidOperationException("工单拆分至少需要两个子工单。");
        }

        EnsureTransformable(source, "拆分");
        EnsureSnapshot(source, "source");
        EnsureUniqueWorkOrderIds(targets, "拆分目标工单");
        foreach (var target in targets)
        {
            EnsureSnapshot(target, "target");
            EnsureCreatedTarget(target);
            EnsureSameProductionContext(source, target, "拆分");
            if (string.Equals(source.WorkOrderId, target.WorkOrderId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("拆分源工单不能同时作为子工单。");
            }
        }

        var targetQuantity = targets.Sum(x => x.Quantity);
        EnsureQuantityConserved(source.Quantity, targetQuantity, "拆分");

        var transformation = new WorkOrderTransformation(
            organizationId,
            environmentId,
            WorkOrderTransformationType.Split,
            idempotencyKey,
            requestFingerprint,
            actorId,
            reason,
            occurredAtUtc);
        foreach (var target in targets)
        {
            transformation.lines.Add(WorkOrderTransformationLine.Create(
                WorkOrderLineageType.Split,
                transformation.OrganizationId,
                transformation.EnvironmentId,
                source,
                target,
                target.Quantity));
        }

        return transformation;
    }

    public static WorkOrderTransformation CreateMerge(
        string organizationId,
        string environmentId,
        IReadOnlyCollection<WorkOrderTransformationWorkOrderSnapshot> sources,
        WorkOrderTransformationWorkOrderSnapshot target,
        string idempotencyKey,
        string requestFingerprint,
        string actorId,
        string reason,
        DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(target);
        if (sources.Count < 2)
        {
            throw new InvalidOperationException("工单合并至少需要两个源工单。");
        }

        EnsureUniqueWorkOrderIds(sources, "合并源工单");
        EnsureSnapshot(target, "target");
        EnsureCreatedTarget(target);
        foreach (var source in sources)
        {
            EnsureTransformable(source, "合并");
            EnsureSnapshot(source, "source");
            EnsureSameProductionContext(source, target, "合并");
            if (string.Equals(source.WorkOrderId, target.WorkOrderId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("合并目标工单不能同时作为源工单。");
            }
        }

        var sourceQuantity = sources.Sum(x => x.Quantity);
        EnsureQuantityConserved(sourceQuantity, target.Quantity, "合并");

        var transformation = new WorkOrderTransformation(
            organizationId,
            environmentId,
            WorkOrderTransformationType.Merge,
            idempotencyKey,
            requestFingerprint,
            actorId,
            reason,
            occurredAtUtc);
        foreach (var source in sources)
        {
            transformation.lines.Add(WorkOrderTransformationLine.Create(
                WorkOrderLineageType.Merge,
                transformation.OrganizationId,
                transformation.EnvironmentId,
                source,
                target,
                source.Quantity));
        }

        return transformation;
    }

    public void EnsureReplayMatches(string requestFingerprint)
    {
        var normalizedFingerprint = RequiredBounded(requestFingerprint, nameof(requestFingerprint), 128);
        if (!string.Equals(RequestFingerprint, normalizedFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("相同幂等键不能复用不同的拆分或合并请求载荷。");
        }
    }

    private static void EnsureTransformable(
        WorkOrderTransformationWorkOrderSnapshot snapshot,
        string operation)
    {
        if (snapshot.Status is not WorkOrder.CreatedStatus and not WorkOrder.ReleasedStatus)
        {
            throw new InvalidOperationException(
                $"状态为 {snapshot.Status} 的工单不允许{operation}，仅 created/released 可执行。");
        }
    }

    private static void EnsureSnapshot(
        WorkOrderTransformationWorkOrderSnapshot snapshot,
        string role)
    {
        RequiredBounded(snapshot.WorkOrderId, $"{role}.workOrderId", 100);
        RequiredBounded(snapshot.SkuId, $"{role}.skuId", 100);
        RequiredBounded(snapshot.Status, $"{role}.status", 30);
        RequiredBounded(snapshot.UomCode ?? string.Empty, $"{role}.uomCode", 50);
        DomainGuard.Positive(snapshot.Quantity, $"{role}.quantity");
        if (snapshot.Version <= 0)
        {
            throw new ArgumentOutOfRangeException($"{role}.version", "工单版本必须为正数。");
        }

        if (snapshot.ProductionVersionId is not null)
        {
            RequiredBounded(snapshot.ProductionVersionId, $"{role}.productionVersionId", 100);
        }
    }

    private static void EnsureCreatedTarget(WorkOrderTransformationWorkOrderSnapshot target)
    {
        if (!string.Equals(target.Status, WorkOrder.CreatedStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("拆分或合并产生的目标工单必须从 created 状态开始。");
        }
    }

    private static void EnsureUniqueWorkOrderIds(
        IEnumerable<WorkOrderTransformationWorkOrderSnapshot> snapshots,
        string role)
    {
        var ids = snapshots.Select(x => x.WorkOrderId).ToArray();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
        {
            throw new InvalidOperationException($"{role}不能包含重复的工单身份。");
        }
    }

    private static void EnsureSameProductionContext(
        WorkOrderTransformationWorkOrderSnapshot source,
        WorkOrderTransformationWorkOrderSnapshot target,
        string operation)
    {
        if (!string.Equals(source.SkuId, target.SkuId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{operation}只能处理同 SKU 工单。");
        }

        var sourceUom = RequiredBounded(source.UomCode ?? string.Empty, "source.uomCode", 50);
        var targetUom = RequiredBounded(target.UomCode ?? string.Empty, "target.uomCode", 50);
        if (!string.Equals(sourceUom, targetUom, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{operation}要求所有工单使用相同 UOM。");
        }

        if (!string.Equals(source.ProductionVersionId, target.ProductionVersionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{operation}要求所有工单使用相同生产版本。");
        }
    }

    private static void EnsureQuantityConserved(decimal sourceQuantity, decimal targetQuantity, string operation)
    {
        if (sourceQuantity != targetQuantity)
        {
            throw new InvalidOperationException(
                $"{operation}数量不守恒：源数量 {sourceQuantity:0.######}，目标数量 {targetQuantity:0.######}。");
        }
    }

    private static string RequiredBounded(string value, string parameterName, int maxLength)
    {
        var normalized = DomainGuard.Required(value, parameterName);
        return normalized.Length <= maxLength
            ? normalized
            : throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maxLength} characters.");
    }
}
