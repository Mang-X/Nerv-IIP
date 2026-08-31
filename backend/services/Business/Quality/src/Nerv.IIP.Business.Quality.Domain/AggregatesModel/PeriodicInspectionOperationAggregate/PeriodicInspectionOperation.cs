using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;

namespace Nerv.IIP.Business.Quality.Domain.AggregatesModel.PeriodicInspectionOperationAggregate;

public partial record PeriodicInspectionOperationId : IGuidStronglyTypedId;

public partial record PeriodicInspectionProductionReportId : IGuidStronglyTypedId;

public partial record PeriodicInspectionRuntimeContextId : IGuidStronglyTypedId, IComparable<PeriodicInspectionRuntimeContextId>
{
    public int CompareTo(PeriodicInspectionRuntimeContextId? other)
        => Id.CompareTo(other?.Id ?? Guid.Empty);
}

public sealed record PeriodicInspectionTimeWindow(long Sequence, DateTime DueAtUtc);

public sealed record PeriodicInspectionQuantityWindow(
    long Sequence,
    decimal ThresholdQuantity,
    DateTime GeneratedAtUtc);

public sealed class PeriodicInspectionOperation : Entity<PeriodicInspectionOperationId>, IAggregateRoot
{
    private PeriodicInspectionOperation()
    {
    }

    private PeriodicInspectionOperation(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationId)
    {
        Id = new PeriodicInspectionOperationId(Guid.CreateVersion7());
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        WorkOrderId = Required(workOrderId);
        OperationId = Required(operationId);
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string OperationId { get; private set; } = string.Empty;
    public string? SkuCode { get; private set; }
    public int? OperationSequence { get; private set; }
    public string? WorkCenterId { get; private set; }
    public DateTime? ReleasedAtUtc { get; private set; }
    public string? CompletionSkuCode { get; private set; }
    public int? CompletionOperationSequence { get; private set; }
    public string? CompletionWorkCenterId { get; private set; }
    public string? CompletionUomCode { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public List<PeriodicInspectionProductionReport> ProductionReports { get; private set; } = [];
    public List<PeriodicInspectionRuntimeContext> RuntimeContexts { get; private set; } = [];

    public static PeriodicInspectionOperation CreatePending(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationId)
        => new(organizationId, environmentId, workOrderId, operationId);

    public void ApplyRelease(
        string skuCode,
        int operationSequence,
        string workCenterId,
        DateTime releasedAtUtc,
        IReadOnlyCollection<PeriodicInspectionPlanSnapshot> plans)
    {
        var normalizedSkuCode = Required(skuCode);
        var normalizedWorkCenterId = Required(workCenterId);
        if (operationSequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(operationSequence), "Operation sequence must be positive.");
        }

        if (releasedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Release time must be UTC.", nameof(releasedAtUtc));
        }

        if (ReleasedAtUtc.HasValue)
        {
            if (SkuCode != normalizedSkuCode
                || OperationSequence != operationSequence
                || WorkCenterId != normalizedWorkCenterId
                || ReleasedAtUtc != releasedAtUtc)
            {
                throw new InvalidOperationException("Conflicting work-order release facts were received for the same operation.");
            }

            return;
        }

        if (ProductionReports.Any(report => report.WorkCenterId != normalizedWorkCenterId))
        {
            throw new InvalidOperationException("Production report work center conflicts with work-order release facts.");
        }

        if (ProductionReports.Any(report => report.ReportedAtUtc < releasedAtUtc))
        {
            throw new InvalidOperationException("Production report time precedes the work-order release time.");
        }

        if (CompletedAtUtc.HasValue
            && (CompletionSkuCode != normalizedSkuCode
                || CompletionOperationSequence != operationSequence
                || CompletionWorkCenterId != normalizedWorkCenterId
                || CompletedAtUtc < releasedAtUtc))
        {
            throw new InvalidOperationException("Operation completion facts conflict with work-order release facts.");
        }

        if (CompletionUomCode is not null
            && ProductionReports.Any(report => report.UomCode != CompletionUomCode))
        {
            throw new InvalidOperationException("Operation completion UOM conflicts with production report facts.");
        }

        SkuCode = normalizedSkuCode;
        OperationSequence = operationSequence;
        WorkCenterId = normalizedWorkCenterId;
        ReleasedAtUtc = releasedAtUtc;

        foreach (var plan in plans.OrderBy(x => x.InspectionPlanId.ToString(), StringComparer.Ordinal))
        {
            if (RuntimeContexts.Any(x => x.InspectionPlanId == plan.InspectionPlanId))
            {
                continue;
            }

            var context = PeriodicInspectionRuntimeContext.Create(
                Id,
                OrganizationId,
                EnvironmentId,
                WorkOrderId,
                OperationId,
                normalizedSkuCode,
                operationSequence,
                normalizedWorkCenterId,
                releasedAtUtc,
                plan);
            context.Reconcile(ProductionReports, CompletedAtUtc);
            RuntimeContexts.Add(context);
        }
    }

    public bool RecordProductionReport(
        string reportNo,
        string workCenterId,
        decimal goodQuantity,
        string uomCode,
        DateTime reportedAtUtc,
        bool isReversal,
        string? reversedReportNo)
    {
        var candidate = PeriodicInspectionProductionReport.Create(
            Id,
            reportNo,
            workCenterId,
            goodQuantity,
            uomCode,
            reportedAtUtc,
            isReversal,
            reversedReportNo);
        var existing = ProductionReports.SingleOrDefault(x => x.ReportNo == candidate.ReportNo);
        if (existing is not null)
        {
            if (!existing.HasSameFacts(candidate))
            {
                throw new InvalidOperationException($"Conflicting production report '{candidate.ReportNo}' was received.");
            }

            return false;
        }

        if (ProductionReports.Count > 0
            && ProductionReports.Any(x => !string.Equals(x.UomCode, candidate.UomCode, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Production reports for the same operation must use one UOM.");
        }

        if (ReleasedAtUtc.HasValue
            && (candidate.WorkCenterId != WorkCenterId || candidate.ReportedAtUtc < ReleasedAtUtc))
        {
            throw new InvalidOperationException("Production report facts conflict with the work-order release.");
        }

        if (CompletionUomCode is not null && candidate.UomCode != CompletionUomCode)
        {
            throw new InvalidOperationException("Production report UOM conflicts with the operation completion UOM.");
        }

        ProductionReports.Add(candidate);
        foreach (var context in RuntimeContexts)
        {
            context.Reconcile(ProductionReports, CompletedAtUtc);
        }

        return true;
    }

    public bool Complete(
        string skuCode,
        int operationSequence,
        string workCenterId,
        string uomCode,
        DateTime completedAtUtc)
    {
        var normalizedSkuCode = Required(skuCode);
        var normalizedWorkCenterId = Required(workCenterId);
        var normalizedUomCode = Required(uomCode).ToUpperInvariant();
        if (operationSequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(operationSequence), "Operation sequence must be positive.");
        }

        if (completedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Completion time must be UTC.", nameof(completedAtUtc));
        }

        if (CompletedAtUtc.HasValue)
        {
            if (CompletionSkuCode != normalizedSkuCode
                || CompletionOperationSequence != operationSequence
                || CompletionWorkCenterId != normalizedWorkCenterId
                || CompletionUomCode != normalizedUomCode
                || CompletedAtUtc != completedAtUtc)
            {
                throw new InvalidOperationException("Conflicting completion facts were received for the same operation.");
            }

            return false;
        }

        if (ReleasedAtUtc.HasValue
            && (SkuCode != normalizedSkuCode
                || OperationSequence != operationSequence
                || WorkCenterId != normalizedWorkCenterId
                || completedAtUtc < ReleasedAtUtc))
        {
            throw new InvalidOperationException("Operation completion facts conflict with the work-order release.");
        }

        if (ProductionReports.Any(report => report.UomCode != normalizedUomCode))
        {
            throw new InvalidOperationException("Operation completion UOM conflicts with production report facts.");
        }

        CompletionSkuCode = normalizedSkuCode;
        CompletionOperationSequence = operationSequence;
        CompletionWorkCenterId = normalizedWorkCenterId;
        CompletionUomCode = normalizedUomCode;
        CompletedAtUtc = completedAtUtc;
        foreach (var context in RuntimeContexts)
        {
            context.Reconcile(ProductionReports, CompletedAtUtc);
        }

        return true;
    }

    private static string Required(string value)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", nameof(value))
            : value.Trim();
}

public sealed record PeriodicInspectionPlanSnapshot(
    InspectionPlanId InspectionPlanId,
    int InspectionPlanVersion,
    decimal? TimeIntervalHours,
    decimal? QuantityInterval,
    string? AssignedInspectorUserId,
    string? AssignedTeamId)
{
    public static PeriodicInspectionPlanSnapshot From(InspectionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Status != "active" || plan.Category != "operation")
        {
            throw new InvalidOperationException("Only active operation inspection plans can be snapshotted.");
        }

        if (!plan.TimeIntervalHours.HasValue && !plan.QuantityInterval.HasValue)
        {
            throw new InvalidOperationException("Periodic inspection plan snapshot requires an interval.");
        }

        return new(
            plan.Id,
            plan.Version,
            plan.TimeIntervalHours,
            plan.QuantityInterval,
            plan.AssignedInspectorUserId,
            plan.AssignedTeamId);
    }
}

public sealed class PeriodicInspectionProductionReport : Entity<PeriodicInspectionProductionReportId>
{
    private PeriodicInspectionProductionReport()
    {
    }

    private PeriodicInspectionProductionReport(
        PeriodicInspectionOperationId operationContextId,
        string reportNo,
        string workCenterId,
        decimal goodQuantity,
        string uomCode,
        DateTime reportedAtUtc,
        bool isReversal,
        string? reversedReportNo)
    {
        Id = new PeriodicInspectionProductionReportId(Guid.CreateVersion7());
        OperationContextId = operationContextId;
        ReportNo = Required(reportNo);
        WorkCenterId = Required(workCenterId);
        UomCode = Required(uomCode).ToUpperInvariant();
        ReportedAtUtc = reportedAtUtc.Kind == DateTimeKind.Utc
            ? reportedAtUtc
            : throw new ArgumentException("Report time must be UTC.", nameof(reportedAtUtc));
        IsReversal = isReversal;
        ReversedReportNo = Optional(reversedReportNo);

        if ((!isReversal && goodQuantity < 0m) || (isReversal && goodQuantity > 0m))
        {
            throw new ArgumentOutOfRangeException(nameof(goodQuantity), "Good quantity sign must match reversal semantics.");
        }

        if (isReversal != (ReversedReportNo is not null))
        {
            throw new ArgumentException("Reversal lineage must be present only for reversal reports.", nameof(reversedReportNo));
        }

        GoodQuantity = goodQuantity;
    }

    public PeriodicInspectionOperationId OperationContextId { get; private set; } = default!;
    public string ReportNo { get; private set; } = string.Empty;
    public string WorkCenterId { get; private set; } = string.Empty;
    public decimal GoodQuantity { get; private set; }
    public string UomCode { get; private set; } = string.Empty;
    public DateTime ReportedAtUtc { get; private set; }
    public bool IsReversal { get; private set; }
    public string? ReversedReportNo { get; private set; }

    internal static PeriodicInspectionProductionReport Create(
        PeriodicInspectionOperationId operationContextId,
        string reportNo,
        string workCenterId,
        decimal goodQuantity,
        string uomCode,
        DateTime reportedAtUtc,
        bool isReversal,
        string? reversedReportNo)
        => new(operationContextId, reportNo, workCenterId, goodQuantity, uomCode, reportedAtUtc, isReversal, reversedReportNo);

    internal bool HasSameFacts(PeriodicInspectionProductionReport other)
        => ReportNo == other.ReportNo
           && WorkCenterId == other.WorkCenterId
           && GoodQuantity == other.GoodQuantity
           && UomCode == other.UomCode
           && ReportedAtUtc == other.ReportedAtUtc
           && IsReversal == other.IsReversal
           && ReversedReportNo == other.ReversedReportNo;

    private static string Required(string value)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.") : value.Trim();

    private static string? Optional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class PeriodicInspectionRuntimeContext : Entity<PeriodicInspectionRuntimeContextId>
{
    public const long MaximumSupportedPendingQuantityWindows = 10_000;

    private PeriodicInspectionRuntimeContext()
    {
    }

    private PeriodicInspectionRuntimeContext(
        PeriodicInspectionOperationId operationContextId,
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationId,
        string skuCode,
        int operationSequence,
        string workCenterId,
        DateTime releasedAtUtc,
        PeriodicInspectionPlanSnapshot plan)
    {
        Id = new PeriodicInspectionRuntimeContextId(Guid.CreateVersion7());
        OperationContextId = operationContextId;
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        WorkOrderId = workOrderId;
        OperationId = operationId;
        SkuCode = skuCode;
        OperationSequence = operationSequence;
        WorkCenterId = workCenterId;
        ReleasedAtUtc = releasedAtUtc;
        InspectionPlanId = plan.InspectionPlanId;
        InspectionPlanVersion = plan.InspectionPlanVersion;
        TimeIntervalHours = plan.TimeIntervalHours;
        QuantityInterval = plan.QuantityInterval;
        AssignedInspectorUserId = plan.AssignedInspectorUserId;
        AssignedTeamId = plan.AssignedTeamId;
        Status = "active";
    }

    public PeriodicInspectionOperationId OperationContextId { get; private set; } = default!;
    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string OperationId { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public int OperationSequence { get; private set; }
    public string WorkCenterId { get; private set; } = string.Empty;
    public DateTime ReleasedAtUtc { get; private set; }
    public InspectionPlanId InspectionPlanId { get; private set; } = default!;
    public int InspectionPlanVersion { get; private set; }
    public decimal? TimeIntervalHours { get; private set; }
    public decimal? QuantityInterval { get; private set; }
    public string? AssignedInspectorUserId { get; private set; }
    public string? AssignedTeamId { get; private set; }
    public DateTime? FirstActivityAtUtc { get; private set; }
    public string? UomCode { get; private set; }
    public decimal CumulativeGoodQuantity { get; private set; }
    public decimal QuantityHighWater { get; private set; }
    public long LastGeneratedQuantityWindowSequence { get; private set; }
    public DateTime? QuantityGenerationAnchorAtUtc { get; private set; }
    public DateTime? QuantityContinuationNextAttemptAtUtc { get; private set; }
    public DateTime? TimeScheduleAnchorAtUtc { get; private set; }
    public long LastGeneratedTimeWindowSequence { get; private set; }
    public DateTime? NextTimeWindowAtUtc { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTime? CompletedAtUtc { get; private set; }

    internal static PeriodicInspectionRuntimeContext Create(
        PeriodicInspectionOperationId operationContextId,
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationId,
        string skuCode,
        int operationSequence,
        string workCenterId,
        DateTime releasedAtUtc,
        PeriodicInspectionPlanSnapshot plan)
        => new(
            operationContextId,
            organizationId,
            environmentId,
            workOrderId,
            operationId,
            skuCode,
            operationSequence,
            workCenterId,
            releasedAtUtc,
            plan);

    internal void Reconcile(
        IReadOnlyCollection<PeriodicInspectionProductionReport> reports,
        DateTime? completedAtUtc)
    {
        FirstActivityAtUtc = reports.Count == 0 ? null : reports.Min(x => x.ReportedAtUtc);
        UomCode = reports.Count == 0 ? null : reports.First().UomCode;
        CumulativeGoodQuantity = reports.Sum(x => x.GoodQuantity);
        QuantityHighWater = reports.Where(x => !x.IsReversal).Sum(x => x.GoodQuantity);
        CompletedAtUtc = completedAtUtc;
        Status = completedAtUtc.HasValue ? "closed" : "active";
        if (completedAtUtc.HasValue)
        {
            NextTimeWindowAtUtc = null;
        }
        else if (LastGeneratedTimeWindowSequence == 0 && TimeIntervalHours.HasValue && FirstActivityAtUtc.HasValue)
        {
            NextTimeWindowAtUtc = TryAddTicks(FirstActivityAtUtc.Value, GetIntervalTicks());
        }
    }

    public IReadOnlyList<PeriodicInspectionTimeWindow> TakeDueTimeWindows(DateTime nowUtc, int maxWindows)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Current time must be UTC.", nameof(nowUtc));
        }

        if (maxWindows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWindows), "Maximum windows must be positive.");
        }

        // Reconcile closes a context by clearing the watermark. Keep the status check as a
        // fail-closed depth defense for malformed or legacy persisted rows that violate that invariant.
        if (Status != "active"
            || !TimeIntervalHours.HasValue
            || !FirstActivityAtUtc.HasValue
            || !NextTimeWindowAtUtc.HasValue)
        {
            return [];
        }

        var intervalTicks = GetIntervalTicks();
        var anchor = TimeScheduleAnchorAtUtc ?? FirstActivityAtUtc.Value;
        var windows = new List<PeriodicInspectionTimeWindow>(maxWindows);
        var sequence = checked(LastGeneratedTimeWindowSequence + 1);
        var dueAtUtc = NextTimeWindowAtUtc.Value;
        while (windows.Count < maxWindows && dueAtUtc <= nowUtc)
        {
            windows.Add(new PeriodicInspectionTimeWindow(sequence, dueAtUtc));
            sequence = checked(sequence + 1);
            var nextDueAtUtc = TryAddTicks(dueAtUtc, intervalTicks);
            if (!nextDueAtUtc.HasValue)
            {
                NextTimeWindowAtUtc = null;
                break;
            }

            dueAtUtc = nextDueAtUtc.Value;
            NextTimeWindowAtUtc = dueAtUtc;
        }

        if (windows.Count > 0)
        {
            TimeScheduleAnchorAtUtc ??= anchor;
            LastGeneratedTimeWindowSequence = windows[^1].Sequence;
        }

        return windows;
    }

    public IReadOnlyList<PeriodicInspectionQuantityWindow> TakeDueQuantityWindows(
        DateTime occurredAtUtc,
        int maxWindows,
        DateTime? continuationNextAttemptAtUtc = null)
    {
        if (occurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Quantity generation trigger time must be UTC.", nameof(occurredAtUtc));
        }

        if (maxWindows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWindows), "Maximum windows must be positive.");
        }

        if (continuationNextAttemptAtUtc.HasValue
            && continuationNextAttemptAtUtc.Value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Quantity continuation next-attempt time must be UTC.",
                nameof(continuationNextAttemptAtUtc));
        }

        if ((Status != "active" && !(Status == "closed" && QuantityGenerationAnchorAtUtc.HasValue))
            || !QuantityInterval.HasValue
            || UomCode is null
            || QuantityHighWater <= 0m)
        {
            return [];
        }

        var targetSequenceValue = decimal.Floor(QuantityHighWater / QuantityInterval.Value);
        var pendingSequenceValue = targetSequenceValue - LastGeneratedQuantityWindowSequence;
        if (pendingSequenceValue > MaximumSupportedPendingQuantityWindows)
        {
            throw new InvalidOperationException(
                $"Quantity backlog {pendingSequenceValue} exceeds the supported pending-window limit "
                + $"{MaximumSupportedPendingQuantityWindows}; the source event must fail closed before partial generation.");
        }

        if (targetSequenceValue > long.MaxValue)
        {
            throw new InvalidOperationException(
                $"Quantity window target {targetSequenceValue} exceeds the supported sequence limit {long.MaxValue}.");
        }

        var targetSequence = decimal.ToInt64(targetSequenceValue);
        if (targetSequence <= LastGeneratedQuantityWindowSequence)
        {
            QuantityGenerationAnchorAtUtc = null;
            QuantityContinuationNextAttemptAtUtc = null;
            return [];
        }

        QuantityGenerationAnchorAtUtc ??= occurredAtUtc;
        QuantityContinuationNextAttemptAtUtc = continuationNextAttemptAtUtc ?? occurredAtUtc;
        var windows = new List<PeriodicInspectionQuantityWindow>(
            (int)Math.Min(maxWindows, targetSequence - LastGeneratedQuantityWindowSequence));
        for (var sequence = checked(LastGeneratedQuantityWindowSequence + 1);
             sequence <= targetSequence && windows.Count < maxWindows;
             sequence = checked(sequence + 1))
        {
            windows.Add(new PeriodicInspectionQuantityWindow(
                sequence,
                checked(sequence * QuantityInterval.Value),
                QuantityGenerationAnchorAtUtc.Value));
        }

        LastGeneratedQuantityWindowSequence = windows[^1].Sequence;
        if (LastGeneratedQuantityWindowSequence == targetSequence)
        {
            QuantityGenerationAnchorAtUtc = null;
            QuantityContinuationNextAttemptAtUtc = null;
        }

        return windows;
    }

    public void DeferQuantityContinuation(DateTime nextAttemptAtUtc)
    {
        if (nextAttemptAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Quantity continuation next-attempt time must be UTC.", nameof(nextAttemptAtUtc));
        }

        if (QuantityGenerationAnchorAtUtc.HasValue)
        {
            QuantityContinuationNextAttemptAtUtc = nextAttemptAtUtc;
        }
    }

    private long GetIntervalTicks()
        => checked((long)decimal.Round(
            TimeIntervalHours!.Value * TimeSpan.TicksPerHour,
            decimals: 0,
            MidpointRounding.AwayFromZero));

    private static DateTime? TryAddTicks(DateTime value, long ticks)
    {
        try
        {
            return value.AddTicks(ticks);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
