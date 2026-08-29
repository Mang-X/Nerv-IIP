using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.MachineOverheadReconciliationAggregate;

public partial record WorkCenterMachineOverheadReconciliationId : IGuidStronglyTypedId;

public enum AbnormalDowntimeDisposition
{
    None,
    Pending,
    PeriodExpense,
}

public sealed class WorkCenterMachineOverheadReconciliation
    : Entity<WorkCenterMachineOverheadReconciliationId>, IAggregateRoot
{
    private WorkCenterMachineOverheadReconciliation() { }

    private WorkCenterMachineOverheadReconciliation(
        string organizationId,
        string environmentId,
        string workCenterId,
        string accountingPeriodCode,
        WorkCenterMachineOverheadRateId workCenterMachineOverheadRateId,
        int rateRevision,
        string currencyCode,
        decimal actualFixedOverheadAmount,
        decimal actualVariableOverheadAmount,
        long appliedMachineTicks,
        decimal appliedFixedAmount,
        decimal appliedVariableAmount,
        decimal appliedTotalAmount,
        long abnormalDowntimeTicks,
        AbnormalDowntimeDisposition abnormalDowntimeDisposition,
        int revision,
        string recordedBy,
        string sourceReference,
        string reason,
        DateTimeOffset recordedAtUtc)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        WorkCenterId = ErpText.Required(workCenterId, nameof(workCenterId));
        AccountingPeriodCode = ErpText.Required(accountingPeriodCode, nameof(accountingPeriodCode));
        WorkCenterMachineOverheadRateId = workCenterMachineOverheadRateId
            ?? throw new ArgumentNullException(nameof(workCenterMachineOverheadRateId));
        if (rateRevision <= 0) throw new ArgumentOutOfRangeException(nameof(rateRevision));
        RateRevision = rateRevision;
        CurrencyCode = NormalizeCurrencyCode(currencyCode);
        if (actualFixedOverheadAmount < 0m) throw new ArgumentOutOfRangeException(nameof(actualFixedOverheadAmount));
        if (actualVariableOverheadAmount < 0m) throw new ArgumentOutOfRangeException(nameof(actualVariableOverheadAmount));
        if (appliedMachineTicks < 0) throw new ArgumentOutOfRangeException(nameof(appliedMachineTicks));
        if (appliedFixedAmount < 0m) throw new ArgumentOutOfRangeException(nameof(appliedFixedAmount));
        if (appliedVariableAmount < 0m) throw new ArgumentOutOfRangeException(nameof(appliedVariableAmount));
        if (appliedTotalAmount < 0m) throw new ArgumentOutOfRangeException(nameof(appliedTotalAmount));
        if (abnormalDowntimeTicks < 0) throw new ArgumentOutOfRangeException(nameof(abnormalDowntimeTicks));
        ValidateAbnormalDowntime(abnormalDowntimeTicks, abnormalDowntimeDisposition);
        if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision));

        ActualFixedOverheadAmount = actualFixedOverheadAmount;
        ActualVariableOverheadAmount = actualVariableOverheadAmount;
        ActualTotalOverheadAmount = actualFixedOverheadAmount + actualVariableOverheadAmount;
        AppliedMachineTicks = appliedMachineTicks;
        AppliedMachineHours = AppliedMachineTicks / (decimal)TimeSpan.TicksPerHour;
        AppliedFixedAmount = appliedFixedAmount;
        AppliedVariableAmount = appliedVariableAmount;
        AppliedTotalAmount = appliedTotalAmount;
        AppliedRoundingDifferenceAmount = appliedTotalAmount - appliedFixedAmount - appliedVariableAmount;
        UnderOverAppliedFixedAmount = actualFixedOverheadAmount - appliedFixedAmount;
        UnderOverAppliedVariableAmount = actualVariableOverheadAmount - appliedVariableAmount;
        UnderOverAppliedTotalAmount = ActualTotalOverheadAmount - appliedTotalAmount;
        UnallocatedFixedOverheadAmount = Math.Max(UnderOverAppliedFixedAmount, 0m);
        OverAppliedFixedOverheadAmount = Math.Max(-UnderOverAppliedFixedAmount, 0m);
        AbnormalDowntimeTicks = abnormalDowntimeTicks;
        AbnormalDowntimeHours = AbnormalDowntimeTicks / (decimal)TimeSpan.TicksPerHour;
        AbnormalDowntimeDisposition = abnormalDowntimeDisposition;
        Revision = revision;
        RecordedBy = RequireCanonicalActor(recordedBy);
        SourceReference = ErpText.Required(sourceReference, nameof(sourceReference));
        Reason = ErpText.Required(reason, nameof(reason));
        RecordedAtUtc = RequireUtc(recordedAtUtc, nameof(recordedAtUtc));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkCenterId { get; private set; } = string.Empty;
    public string AccountingPeriodCode { get; private set; } = string.Empty;
    public WorkCenterMachineOverheadRateId WorkCenterMachineOverheadRateId { get; private set; } = null!;
    public int RateRevision { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal ActualFixedOverheadAmount { get; private set; }
    public decimal ActualVariableOverheadAmount { get; private set; }
    public decimal ActualTotalOverheadAmount { get; private set; }
    public long AppliedMachineTicks { get; private set; }
    public decimal AppliedMachineHours { get; private set; }
    public decimal AppliedFixedAmount { get; private set; }
    public decimal AppliedVariableAmount { get; private set; }
    public decimal AppliedTotalAmount { get; private set; }
    public decimal AppliedRoundingDifferenceAmount { get; private set; }
    public decimal UnderOverAppliedFixedAmount { get; private set; }
    public decimal UnderOverAppliedVariableAmount { get; private set; }
    public decimal UnderOverAppliedTotalAmount { get; private set; }
    public decimal UnallocatedFixedOverheadAmount { get; private set; }
    public decimal OverAppliedFixedOverheadAmount { get; private set; }
    public long AbnormalDowntimeTicks { get; private set; }
    public decimal AbnormalDowntimeHours { get; private set; }
    public AbnormalDowntimeDisposition AbnormalDowntimeDisposition { get; private set; }
    public int Revision { get; private set; }
    public string RecordedBy { get; private set; } = string.Empty;
    public string SourceReference { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; private set; }
    public bool IsReadyForClose => AbnormalDowntimeDisposition != AbnormalDowntimeDisposition.Pending;

    public static WorkCenterMachineOverheadReconciliation Record(
        string organizationId,
        string environmentId,
        string workCenterId,
        string accountingPeriodCode,
        WorkCenterMachineOverheadRateId workCenterMachineOverheadRateId,
        int rateRevision,
        string currencyCode,
        decimal actualFixedOverheadAmount,
        decimal actualVariableOverheadAmount,
        long appliedMachineTicks,
        decimal appliedFixedAmount,
        decimal appliedVariableAmount,
        decimal appliedTotalAmount,
        long abnormalDowntimeTicks,
        AbnormalDowntimeDisposition abnormalDowntimeDisposition,
        int revision,
        string recordedBy,
        string sourceReference,
        string reason,
        DateTimeOffset recordedAtUtc)
        => new(
            organizationId, environmentId, workCenterId, accountingPeriodCode,
            workCenterMachineOverheadRateId, rateRevision, currencyCode,
            actualFixedOverheadAmount, actualVariableOverheadAmount,
            appliedMachineTicks, appliedFixedAmount, appliedVariableAmount, appliedTotalAmount,
            abnormalDowntimeTicks, abnormalDowntimeDisposition, revision,
            recordedBy, sourceReference, reason, recordedAtUtc);

    private static void ValidateAbnormalDowntime(long ticks, AbnormalDowntimeDisposition disposition)
    {
        if (!Enum.IsDefined(disposition)) throw new ArgumentOutOfRangeException(nameof(disposition));
        if (ticks == 0 && disposition != AbnormalDowntimeDisposition.None)
            throw new ArgumentException("Zero abnormal downtime must use the None disposition.", nameof(disposition));
        if (ticks > 0 && disposition == AbnormalDowntimeDisposition.None)
            throw new ArgumentException("Positive abnormal downtime requires an explicit disposition.", nameof(disposition));
    }

    private static string NormalizeCurrencyCode(string value)
    {
        var normalized = ErpText.Required(value, nameof(value)).ToUpperInvariant();
        return normalized.Length == 3 && normalized.All(character => character is >= 'A' and <= 'Z')
            ? normalized
            : throw new ArgumentException("Currency code must contain exactly three ASCII letters.", nameof(value));
    }

    private static string RequireCanonicalActor(string value)
    {
        var actor = ErpText.Required(value, nameof(value));
        if (!string.Equals(actor, value, StringComparison.Ordinal) || actor.Any(char.IsWhiteSpace))
            throw new ArgumentException("Actor must be canonical.", nameof(value));
        var separator = actor.IndexOf(':', StringComparison.Ordinal);
        return separator > 0 && separator < actor.Length - 1
            ? actor
            : throw new ArgumentException("Actor must be canonical.", nameof(value));
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
        => value != default && value.Offset == TimeSpan.Zero
            ? value
            : throw new ArgumentException("Timestamp must be a nondefault UTC instant.", parameterName);
}
