namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;

public partial record OperationLaborSettlementId : IGuidStronglyTypedId;
public partial record OperationLaborSettlementVoidId : IGuidStronglyTypedId;
public partial record OperationLaborSettlementStateId : IGuidStronglyTypedId;
public partial record OperationLaborCoveredReportId : IGuidStronglyTypedId;

public sealed class OperationLaborSettlement : Entity<OperationLaborSettlementId>, IAggregateRoot
{
    private OperationLaborSettlement() { }

    private OperationLaborSettlement(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        string workCenterId,
        long settlementRevision,
        DateTimeOffset completedAtUtc,
        long actualLaborTicks,
        WorkCenterCostRateId workCenterCostRateId,
        int rateRevision,
        string currencyCode,
        decimal hourlyRate,
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
        if (actualLaborTicks < 0) throw new ArgumentOutOfRangeException(nameof(actualLaborTicks));
        ActualLaborTicks = actualLaborTicks;
        ActualLaborHours = actualLaborTicks / (decimal)TimeSpan.TicksPerHour;
        WorkCenterCostRateId = workCenterCostRateId ?? throw new ArgumentNullException(nameof(workCenterCostRateId));
        if (rateRevision <= 0) throw new ArgumentOutOfRangeException(nameof(rateRevision));
        RateRevision = rateRevision;
        CurrencyCode = NormalizeCurrencyCode(currencyCode);
        HourlyRate = ErpText.Positive(hourlyRate, nameof(hourlyRate));
        RateBasisAtUtc = CompletedAtUtc;
        Amount = decimal.Round(ActualLaborHours * HourlyRate, 6, MidpointRounding.AwayFromZero);
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
    public long ActualLaborTicks { get; private set; }
    public decimal ActualLaborHours { get; private set; }
    public WorkCenterCostRateId WorkCenterCostRateId { get; private set; } = null!;
    public int RateRevision { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal HourlyRate { get; private set; }
    public DateTimeOffset RateBasisAtUtc { get; private set; }
    public string RateBasis { get; private set; } = "standard";
    public decimal Amount { get; private set; }
    public string SourceEventId { get; private set; } = string.Empty;
    public string PayloadHash { get; private set; } = string.Empty;

    public static OperationLaborSettlement Create(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        string workCenterId,
        long settlementRevision,
        DateTimeOffset completedAtUtc,
        long actualLaborTicks,
        WorkCenterCostRateId workCenterCostRateId,
        int rateRevision,
        string currencyCode,
        decimal hourlyRate,
        string sourceEventId,
        string payloadHash)
        => new(
            organizationId,
            environmentId,
            workOrderId,
            operationTaskId,
            workCenterId,
            settlementRevision,
            completedAtUtc,
            actualLaborTicks,
            workCenterCostRateId,
            rateRevision,
            currencyCode,
            hourlyRate,
            sourceEventId,
            payloadHash);

    private static string NormalizeCurrencyCode(string value)
    {
        var normalized = ErpText.Required(value, nameof(value)).ToUpperInvariant();
        return normalized.Length == 3 && normalized.All(character => character is >= 'A' and <= 'Z')
            ? normalized
            : throw new ArgumentException("Currency code must contain exactly three ASCII letters.", nameof(value));
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
        => value.Offset == TimeSpan.Zero
            ? value
            : throw new ArgumentException("Timestamp must use UTC offset zero.", parameterName);
}

public sealed class OperationLaborSettlementVoid : Entity<OperationLaborSettlementVoidId>, IAggregateRoot
{
    private OperationLaborSettlementVoid() { }

    private OperationLaborSettlementVoid(
        OperationLaborSettlement settlement,
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
        VoidedAtUtc = voidedAtUtc.Offset == TimeSpan.Zero
            ? voidedAtUtc
            : throw new ArgumentException("Timestamp must use UTC offset zero.", nameof(voidedAtUtc));
        ActualLaborTicks = settlement.ActualLaborTicks;
        ActualLaborHours = settlement.ActualLaborHours;
        WorkCenterCostRateId = settlement.WorkCenterCostRateId;
        RateRevision = settlement.RateRevision;
        CurrencyCode = settlement.CurrencyCode;
        HourlyRate = settlement.HourlyRate;
        RateBasisAtUtc = settlement.RateBasisAtUtc;
        RateBasis = settlement.RateBasis;
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
    public long ActualLaborTicks { get; private set; }
    public decimal ActualLaborHours { get; private set; }
    public WorkCenterCostRateId WorkCenterCostRateId { get; private set; } = null!;
    public int RateRevision { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal HourlyRate { get; private set; }
    public DateTimeOffset RateBasisAtUtc { get; private set; }
    public string RateBasis { get; private set; } = "standard";
    public decimal Amount { get; private set; }
    public string SourceEventId { get; private set; } = string.Empty;
    public string PayloadHash { get; private set; } = string.Empty;

    public static OperationLaborSettlementVoid Create(
        OperationLaborSettlement settlement,
        DateTimeOffset voidedAtUtc,
        string sourceEventId,
        string payloadHash)
        => new(settlement, voidedAtUtc, sourceEventId, payloadHash);
}

public enum OperationLaborSettlementTransition
{
    Activated,
    Voided,
    IgnoredDuplicate,
    IgnoredVoided,
    IgnoredOldRevision,
}

public sealed record OperationLaborSettlementTransitionResult(
    OperationLaborSettlementTransition Transition,
    long? PreviousActiveRevision);

public sealed class OperationLaborSettlementState : Entity<OperationLaborSettlementStateId>, IAggregateRoot
{
    private OperationLaborSettlementState() { }

    private OperationLaborSettlementState(string organizationId, string environmentId, string operationTaskId)
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

    public static OperationLaborSettlementState Open(
        string organizationId,
        string environmentId,
        string operationTaskId)
        => new(organizationId, environmentId, operationTaskId);

    public OperationLaborSettlementTransitionResult ApplySettlement(long revision)
    {
        if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (revision < HighestRevision)
            return new(OperationLaborSettlementTransition.IgnoredOldRevision, ActiveRevision);
        if (revision == HighestRevision)
            return ActiveRevision == revision
                ? new(OperationLaborSettlementTransition.IgnoredDuplicate, ActiveRevision)
                : new(OperationLaborSettlementTransition.IgnoredVoided, ActiveRevision);

        var previousActiveRevision = ActiveRevision;
        HighestRevision = revision;
        ActiveRevision = revision;
        return new(OperationLaborSettlementTransition.Activated, previousActiveRevision);
    }

    public OperationLaborSettlementTransitionResult ApplyVoid(long revision)
    {
        if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (revision < HighestRevision)
            return new(OperationLaborSettlementTransition.IgnoredOldRevision, ActiveRevision);
        if (revision == HighestRevision && ActiveRevision is null)
            return new(OperationLaborSettlementTransition.IgnoredDuplicate, null);

        var previousActiveRevision = ActiveRevision;
        HighestRevision = revision;
        ActiveRevision = null;
        return new(OperationLaborSettlementTransition.Voided, previousActiveRevision);
    }
}

public sealed class OperationLaborCoveredReport : Entity<OperationLaborCoveredReportId>, IAggregateRoot
{
    private OperationLaborCoveredReport() { }

    private OperationLaborCoveredReport(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        long settlementRevision,
        string reportNo)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        WorkOrderId = ErpText.Required(workOrderId, nameof(workOrderId));
        OperationTaskId = ErpText.Required(operationTaskId, nameof(operationTaskId));
        if (settlementRevision <= 0) throw new ArgumentOutOfRangeException(nameof(settlementRevision));
        SettlementRevision = settlementRevision;
        ReportNo = ErpText.Required(reportNo, nameof(reportNo));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string OperationTaskId { get; private set; } = string.Empty;
    public long SettlementRevision { get; private set; }
    public string ReportNo { get; private set; } = string.Empty;

    public static OperationLaborCoveredReport Create(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        long settlementRevision,
        string reportNo)
        => new(organizationId, environmentId, workOrderId, operationTaskId, settlementRevision, reportNo);
}
