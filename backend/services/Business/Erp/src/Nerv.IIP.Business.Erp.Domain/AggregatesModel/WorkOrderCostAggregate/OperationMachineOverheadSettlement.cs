using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;

public partial record OperationMachineOverheadSettlementId : IGuidStronglyTypedId;
public partial record OperationMachineOverheadSettlementVoidId : IGuidStronglyTypedId;
public partial record OperationMachineOverheadSettlementStateId : IGuidStronglyTypedId;

public sealed class OperationMachineOverheadSettlement
    : Entity<OperationMachineOverheadSettlementId>, IAggregateRoot
{
    private OperationMachineOverheadSettlement() { }

    private OperationMachineOverheadSettlement(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        string workCenterId,
        long settlementRevision,
        DateTimeOffset completedAtUtc,
        MachineOverheadApplicability applicability,
        string? deviceAssetId,
        long? actualMachineTicks,
        string? machineTimeBasisCode,
        WorkCenterMachineOverheadRateId workCenterMachineOverheadRateId,
        string accountingPeriodCode,
        int rateRevision,
        string currencyCode,
        decimal fixedHourlyRate,
        decimal variableHourlyRate,
        string sourceEventId,
        string payloadHash)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        WorkOrderId = ErpText.Required(workOrderId, nameof(workOrderId));
        OperationTaskId = ErpText.Required(operationTaskId, nameof(operationTaskId));
        WorkCenterId = ErpText.Required(workCenterId, nameof(workCenterId));
        if (settlementRevision <= 0) throw new ArgumentOutOfRangeException(nameof(settlementRevision));
        SettlementRevision = settlementRevision;
        CompletedAtUtc = RequireUtc(completedAtUtc, nameof(completedAtUtc));
        Applicability = applicability;
        WorkCenterMachineOverheadRateId = workCenterMachineOverheadRateId
            ?? throw new ArgumentNullException(nameof(workCenterMachineOverheadRateId));
        AccountingPeriodCode = ErpText.Required(accountingPeriodCode, nameof(accountingPeriodCode));
        if (rateRevision <= 0) throw new ArgumentOutOfRangeException(nameof(rateRevision));
        RateRevision = rateRevision;
        CurrencyCode = NormalizeCurrencyCode(currencyCode);

        if (applicability == MachineOverheadApplicability.Applicable)
        {
            DeviceAssetId = ErpText.Required(deviceAssetId!, nameof(deviceAssetId));
            if (actualMachineTicks is null or < 0) throw new ArgumentOutOfRangeException(nameof(actualMachineTicks));
            ActualMachineTicks = actualMachineTicks;
            ActualMachineHours = actualMachineTicks.Value / (decimal)TimeSpan.TicksPerHour;
            MachineTimeBasisCode = ErpText.Required(machineTimeBasisCode!, nameof(machineTimeBasisCode));
            if (fixedHourlyRate < 0m) throw new ArgumentOutOfRangeException(nameof(fixedHourlyRate));
            if (variableHourlyRate < 0m) throw new ArgumentOutOfRangeException(nameof(variableHourlyRate));
            FixedHourlyRate = fixedHourlyRate;
            VariableHourlyRate = variableHourlyRate;
            FixedAmount = RoundAmount(ActualMachineTicks.Value, fixedHourlyRate);
            VariableAmount = RoundAmount(ActualMachineTicks.Value, variableHourlyRate);
            Amount = RoundAmount(ActualMachineTicks.Value, fixedHourlyRate + variableHourlyRate);
        }
        else if (applicability == MachineOverheadApplicability.NotApplicable)
        {
            if (deviceAssetId is not null || actualMachineTicks is not null || machineTimeBasisCode is not null)
                throw new ArgumentException("Not-applicable machine overhead must not contain machine evidence.");
            if (fixedHourlyRate != 0m || variableHourlyRate != 0m)
                throw new ArgumentOutOfRangeException(nameof(fixedHourlyRate), "Not-applicable machine overhead must have zero rates.");
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(applicability));
        }

        SourceEventId = ErpText.Required(sourceEventId, nameof(sourceEventId));
        PayloadHash = ErpText.Required(payloadHash, nameof(payloadHash));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string OperationTaskId { get; private set; } = string.Empty;
    public string WorkCenterId { get; private set; } = string.Empty;
    public long SettlementRevision { get; private set; }
    public DateTimeOffset CompletedAtUtc { get; private set; }
    public MachineOverheadApplicability Applicability { get; private set; }
    public string? DeviceAssetId { get; private set; }
    public long? ActualMachineTicks { get; private set; }
    public decimal? ActualMachineHours { get; private set; }
    public string? MachineTimeBasisCode { get; private set; }
    public WorkCenterMachineOverheadRateId WorkCenterMachineOverheadRateId { get; private set; } = null!;
    public string AccountingPeriodCode { get; private set; } = string.Empty;
    public int RateRevision { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal FixedHourlyRate { get; private set; }
    public decimal VariableHourlyRate { get; private set; }
    public decimal FixedAmount { get; private set; }
    public decimal VariableAmount { get; private set; }
    public decimal Amount { get; private set; }
    public string SourceEventId { get; private set; } = string.Empty;
    public string PayloadHash { get; private set; } = string.Empty;

    public static OperationMachineOverheadSettlement CreateApplied(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        string workCenterId,
        long settlementRevision,
        DateTimeOffset completedAtUtc,
        string deviceAssetId,
        long actualMachineTicks,
        string machineTimeBasisCode,
        WorkCenterMachineOverheadRateId workCenterMachineOverheadRateId,
        string accountingPeriodCode,
        int rateRevision,
        string currencyCode,
        decimal fixedHourlyRate,
        decimal variableHourlyRate,
        string sourceEventId,
        string payloadHash)
        => new(
            organizationId, environmentId, workOrderId, operationTaskId, workCenterId,
            settlementRevision, completedAtUtc, MachineOverheadApplicability.Applicable,
            deviceAssetId, actualMachineTicks, machineTimeBasisCode,
            workCenterMachineOverheadRateId, accountingPeriodCode, rateRevision, currencyCode,
            fixedHourlyRate, variableHourlyRate, sourceEventId, payloadHash);

    public static OperationMachineOverheadSettlement CreateNotApplicable(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        string workCenterId,
        long settlementRevision,
        DateTimeOffset completedAtUtc,
        WorkCenterMachineOverheadRateId workCenterMachineOverheadRateId,
        string accountingPeriodCode,
        int rateRevision,
        string currencyCode,
        string sourceEventId,
        string payloadHash)
        => new(
            organizationId, environmentId, workOrderId, operationTaskId, workCenterId,
            settlementRevision, completedAtUtc, MachineOverheadApplicability.NotApplicable,
            null, null, null, workCenterMachineOverheadRateId, accountingPeriodCode, rateRevision,
            currencyCode, 0m, 0m, sourceEventId, payloadHash);

    private static decimal RoundAmount(long ticks, decimal hourlyRate)
        => decimal.Round(ticks * hourlyRate / TimeSpan.TicksPerHour, 6, MidpointRounding.ToEven);

    private static string NormalizeCurrencyCode(string value)
    {
        var normalized = ErpText.Required(value, nameof(value)).ToUpperInvariant();
        return normalized.Length == 3 && normalized.All(character => character is >= 'A' and <= 'Z')
            ? normalized
            : throw new ArgumentException("Currency code must contain exactly three ASCII letters.", nameof(value));
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
        => value != default && value.Offset == TimeSpan.Zero
            ? value
            : throw new ArgumentException("Timestamp must be a nondefault UTC instant.", parameterName);
}

public sealed class OperationMachineOverheadSettlementVoid
    : Entity<OperationMachineOverheadSettlementVoidId>, IAggregateRoot
{
    private OperationMachineOverheadSettlementVoid() { }

    private OperationMachineOverheadSettlementVoid(
        OperationMachineOverheadSettlement settlement,
        DateTimeOffset voidedAtUtc,
        string sourceEventId,
        string payloadHash)
    {
        OrganizationId = settlement.OrganizationId;
        EnvironmentId = settlement.EnvironmentId;
        WorkOrderId = settlement.WorkOrderId;
        OperationTaskId = settlement.OperationTaskId;
        WorkCenterId = settlement.WorkCenterId;
        SettlementRevision = settlement.SettlementRevision;
        CompletedAtUtc = settlement.CompletedAtUtc;
        VoidedAtUtc = RequireUtc(voidedAtUtc, nameof(voidedAtUtc));
        Applicability = settlement.Applicability;
        DeviceAssetId = settlement.DeviceAssetId;
        ActualMachineTicks = settlement.ActualMachineTicks;
        ActualMachineHours = settlement.ActualMachineHours;
        MachineTimeBasisCode = settlement.MachineTimeBasisCode;
        WorkCenterMachineOverheadRateId = settlement.WorkCenterMachineOverheadRateId;
        AccountingPeriodCode = settlement.AccountingPeriodCode;
        RateRevision = settlement.RateRevision;
        CurrencyCode = settlement.CurrencyCode;
        FixedHourlyRate = settlement.FixedHourlyRate;
        VariableHourlyRate = settlement.VariableHourlyRate;
        FixedAmount = -settlement.FixedAmount;
        VariableAmount = -settlement.VariableAmount;
        Amount = -settlement.Amount;
        SourceEventId = ErpText.Required(sourceEventId, nameof(sourceEventId));
        PayloadHash = ErpText.Required(payloadHash, nameof(payloadHash));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string OperationTaskId { get; private set; } = string.Empty;
    public string WorkCenterId { get; private set; } = string.Empty;
    public long SettlementRevision { get; private set; }
    public DateTimeOffset CompletedAtUtc { get; private set; }
    public DateTimeOffset VoidedAtUtc { get; private set; }
    public MachineOverheadApplicability Applicability { get; private set; }
    public string? DeviceAssetId { get; private set; }
    public long? ActualMachineTicks { get; private set; }
    public decimal? ActualMachineHours { get; private set; }
    public string? MachineTimeBasisCode { get; private set; }
    public WorkCenterMachineOverheadRateId WorkCenterMachineOverheadRateId { get; private set; } = null!;
    public string AccountingPeriodCode { get; private set; } = string.Empty;
    public int RateRevision { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal FixedHourlyRate { get; private set; }
    public decimal VariableHourlyRate { get; private set; }
    public decimal FixedAmount { get; private set; }
    public decimal VariableAmount { get; private set; }
    public decimal Amount { get; private set; }
    public string SourceEventId { get; private set; } = string.Empty;
    public string PayloadHash { get; private set; } = string.Empty;

    public static OperationMachineOverheadSettlementVoid Create(
        OperationMachineOverheadSettlement settlement,
        DateTimeOffset voidedAtUtc,
        string sourceEventId,
        string payloadHash)
        => new(settlement, voidedAtUtc, sourceEventId, payloadHash);

    private static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
        => value != default && value.Offset == TimeSpan.Zero
            ? value
            : throw new ArgumentException("Timestamp must be a nondefault UTC instant.", parameterName);
}

public enum OperationMachineOverheadSettlementTransition
{
    Activated,
    Voided,
    IgnoredDuplicate,
    IgnoredVoided,
    IgnoredOldRevision,
}

public sealed record OperationMachineOverheadSettlementTransitionResult(
    OperationMachineOverheadSettlementTransition Transition,
    long? PreviousActiveRevision);

public sealed class OperationMachineOverheadSettlementState
    : Entity<OperationMachineOverheadSettlementStateId>, IAggregateRoot
{
    private OperationMachineOverheadSettlementState() { }

    private OperationMachineOverheadSettlementState(
        string organizationId,
        string environmentId,
        string operationTaskId)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        OperationTaskId = ErpText.Required(operationTaskId, nameof(operationTaskId));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string OperationTaskId { get; private set; } = string.Empty;
    public long HighestRevision { get; private set; }
    public long? ActiveRevision { get; private set; }

    public static OperationMachineOverheadSettlementState Open(
        string organizationId,
        string environmentId,
        string operationTaskId)
        => new(organizationId, environmentId, operationTaskId);

    public OperationMachineOverheadSettlementTransitionResult ApplySettlement(long revision)
    {
        if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (revision < HighestRevision)
            return new(OperationMachineOverheadSettlementTransition.IgnoredOldRevision, ActiveRevision);
        if (revision == HighestRevision)
            return ActiveRevision == revision
                ? new(OperationMachineOverheadSettlementTransition.IgnoredDuplicate, ActiveRevision)
                : new(OperationMachineOverheadSettlementTransition.IgnoredVoided, ActiveRevision);

        var previous = ActiveRevision;
        HighestRevision = revision;
        ActiveRevision = revision;
        return new(OperationMachineOverheadSettlementTransition.Activated, previous);
    }

    public OperationMachineOverheadSettlementTransitionResult ApplyVoid(long revision)
    {
        if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (revision < HighestRevision)
            return new(OperationMachineOverheadSettlementTransition.IgnoredOldRevision, ActiveRevision);
        if (revision == HighestRevision && ActiveRevision is null)
            return new(OperationMachineOverheadSettlementTransition.IgnoredDuplicate, null);

        var previous = ActiveRevision;
        HighestRevision = revision;
        ActiveRevision = null;
        return new(OperationMachineOverheadSettlementTransition.Voided, previous);
    }
}
