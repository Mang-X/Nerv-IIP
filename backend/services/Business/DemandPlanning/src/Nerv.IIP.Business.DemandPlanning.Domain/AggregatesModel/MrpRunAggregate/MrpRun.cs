using Nerv.IIP.Business.DemandPlanning.Domain.DomainEvents;

namespace Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;

public partial record MrpRunId : IGuidStronglyTypedId;

public enum MrpRunStatus
{
    /// <summary>已受理排队，等待后台执行（对外语义 queued）。</summary>
    Created = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
}

public sealed record PlanningInputSnapshot(
    string ProductionEngineeringSnapshotSource,
    string InventorySnapshotSource,
    int DemandCount,
    int AvailabilityCount,
    IReadOnlyCollection<string>? InputSources = null,
    DateOnly? InputCoverageStart = null,
    DateOnly? InputCoverageEnd = null);

public static class PlanningInputDegradation
{
    public static IReadOnlyCollection<string> FromSnapshotSources(params string[] snapshotSources)
    {
        // Snapshot sources are semicolon-separated adapter segments. Optional adapters
        // must emit "<source>:error" for degraded inputs; see PlanningInputAdapters.
        return snapshotSources
            .SelectMany(source => source.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(ParseDegradedSource)
            .Where(source => source is not null)
            .Select(source => source!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ParseDegradedSource(string segment)
    {
        var separatorIndex = segment.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex == segment.Length - 1)
        {
            return null;
        }

        var status = segment[(separatorIndex + 1)..];
        return string.Equals(status, "error", StringComparison.OrdinalIgnoreCase)
            ? segment[..separatorIndex]
            : null;
    }
}

public sealed class MrpRun : Entity<MrpRunId>, IAggregateRoot
{
    private IReadOnlyCollection<string>? inputDegradationSources;
    private IReadOnlyCollection<string>? inputSources;

    private MrpRun()
    {
    }

    private MrpRun(string organizationId, string environmentId, DateOnly horizonStart, DateOnly horizonEnd)
    {
        if (horizonEnd < horizonStart)
        {
            throw new ArgumentException("MRP horizon end must be on or after horizon start.", nameof(horizonEnd));
        }

        OrganizationId = DemandPlanningText.Required(organizationId, nameof(organizationId));
        EnvironmentId = DemandPlanningText.Required(environmentId, nameof(environmentId));
        HorizonStart = horizonStart;
        HorizonEnd = horizonEnd;
        Status = MrpRunStatus.Created;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public DateOnly HorizonStart { get; private set; }
    public DateOnly HorizonEnd { get; private set; }
    public MrpRunStatus Status { get; private set; }
    public string ProductionEngineeringSnapshotSource { get; private set; } = string.Empty;
    public string InventorySnapshotSource { get; private set; } = string.Empty;
    public string InputSourceSummary { get; private set; } = string.Empty;
    public IReadOnlyCollection<string> InputSources =>
        inputSources ??= InputSourceSummary
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    public DateOnly? InputCoverageStart { get; private set; }
    public DateOnly? InputCoverageEnd { get; private set; }
    public bool HasInputDegradation => InputDegradationSources.Count > 0;
    public IReadOnlyCollection<string> InputDegradationSources =>
        inputDegradationSources ??= PlanningInputDegradation.FromSnapshotSources(
            ProductionEngineeringSnapshotSource,
            InventorySnapshotSource);
    public string? FailureReason { get; private set; }
    public int DemandCount { get; private set; }
    public int AvailabilityCount { get; private set; }
    public int SuggestionCount { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public static MrpRun Create(string organizationId, string environmentId, DateOnly horizonStart, DateOnly horizonEnd)
    {
        return new MrpRun(organizationId, environmentId, horizonStart, horizonEnd);
    }

    /// <summary>
    /// 排队 → 运行中（异步任务模式 #1306）：worker 在独立事务里先提交此状态，
    /// 让前端轮询能看到「排队中 → 计算中 → 终态」的真实进程；
    /// 若之后进程崩溃，DB 里遗留的 Running 记录由启动恢复扫描置为失败。
    /// </summary>
    public void MarkRunning()
    {
        if (Status != MrpRunStatus.Created)
        {
            throw new InvalidOperationException("Only created MRP runs can be marked as running.");
        }

        StartedAtUtc = DateTimeOffset.UtcNow;
        Status = MrpRunStatus.Running;
    }

    /// <summary>
    /// 记录本次计算读取的输入快照元数据；只允许在运行中状态写入（计算事务内）。
    /// </summary>
    public void RecordInputSnapshot(PlanningInputSnapshot snapshot)
    {
        if (Status != MrpRunStatus.Running)
        {
            throw new InvalidOperationException("Input snapshot metadata can only be recorded on running MRP runs.");
        }

        ProductionEngineeringSnapshotSource = DemandPlanningText.Required(snapshot.ProductionEngineeringSnapshotSource);
        InventorySnapshotSource = DemandPlanningText.Required(snapshot.InventorySnapshotSource);
        inputSources = (snapshot.InputSources ?? [])
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Select(source => source.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        InputSourceSummary = string.Join(';', inputSources);
        InputCoverageStart = snapshot.InputCoverageStart;
        InputCoverageEnd = snapshot.InputCoverageEnd;
        inputDegradationSources = PlanningInputDegradation.FromSnapshotSources(
            ProductionEngineeringSnapshotSource,
            InventorySnapshotSource);
        DemandCount = snapshot.DemandCount;
        AvailabilityCount = snapshot.AvailabilityCount;
    }

    /// <summary>排队 → 运行中并记录快照元数据（同事务便捷路径，供种子与单测使用）。</summary>
    public void Start(PlanningInputSnapshot snapshot)
    {
        MarkRunning();
        RecordInputSnapshot(snapshot);
    }

    public void Complete(int suggestionCount)
    {
        if (Status != MrpRunStatus.Running)
        {
            throw new InvalidOperationException("Only running MRP runs can be completed.");
        }

        SuggestionCount = suggestionCount;
        CompletedAtUtc = DateTimeOffset.UtcNow;
        Status = MrpRunStatus.Completed;
        this.AddDomainEvent(new MrpRunCompletedDomainEvent(this));
    }

    public const int FailureReasonMaxLength = 512;

    /// <summary>
    /// 异步执行模式下，后台计算失败（或服务重启中断）时把 run 置为失败并记录可读原因。
    /// 允许从排队（Created）或运行中（Running）进入失败态；终态不可再迁移。
    /// 有意不发领域事件（与 <see cref="Complete"/> 不对称）：MrpRunCompleted 集成事件的
    /// 消费者只关心成功产出的建议，失败态目前没有任何下游消费者，读面轮询已足够；
    /// 将来若有消费者依赖失败通知，再补 MrpRunFailedDomainEvent。
    /// </summary>
    public void Fail(string reason)
    {
        if (Status is not (MrpRunStatus.Created or MrpRunStatus.Running))
        {
            throw new InvalidOperationException("Only queued or running MRP runs can be marked as failed.");
        }

        var normalized = DemandPlanningText.Required(reason, nameof(reason));
        FailureReason = normalized.Length <= FailureReasonMaxLength
            ? normalized
            : normalized[..FailureReasonMaxLength];
        CompletedAtUtc = DateTimeOffset.UtcNow;
        Status = MrpRunStatus.Failed;
    }
}
