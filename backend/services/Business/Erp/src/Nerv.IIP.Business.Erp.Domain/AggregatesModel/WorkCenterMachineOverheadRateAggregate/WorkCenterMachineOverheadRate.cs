using Nerv.IIP.Business.Erp.Domain.AggregatesModel;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;

public partial record WorkCenterMachineOverheadRateId : IGuidStronglyTypedId;

public enum MachineOverheadApplicability
{
    Applicable,
    NotApplicable,
}

public class WorkCenterMachineOverheadRate
    : Entity<WorkCenterMachineOverheadRateId>, IAggregateRoot
{
    protected WorkCenterMachineOverheadRate()
    {
    }

    private WorkCenterMachineOverheadRate(
        string organizationId,
        string environmentId,
        string workCenterId,
        string accountingPeriodCode,
        MachineOverheadApplicability applicability,
        decimal fixedOverheadBudget,
        decimal variableOverheadBudget,
        decimal normalCapacityMachineHours,
        string currencyCode,
        int revision,
        string changedBy,
        string reason,
        DateTimeOffset changedAtUtc)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        WorkCenterId = ErpText.Required(workCenterId, nameof(workCenterId));
        AccountingPeriodCode = ErpText.Required(accountingPeriodCode, nameof(accountingPeriodCode));
        Applicability = applicability;
        ValidateCostBasis(applicability, fixedOverheadBudget, variableOverheadBudget, normalCapacityMachineHours);
        FixedOverheadBudget = fixedOverheadBudget;
        VariableOverheadBudget = variableOverheadBudget;
        NormalCapacityMachineHours = normalCapacityMachineHours;
        FixedHourlyRate = applicability == MachineOverheadApplicability.Applicable
            ? DivideRate(fixedOverheadBudget, normalCapacityMachineHours)
            : 0m;
        VariableHourlyRate = applicability == MachineOverheadApplicability.Applicable
            ? DivideRate(variableOverheadBudget, normalCapacityMachineHours)
            : 0m;
        TotalHourlyRate = FixedHourlyRate + VariableHourlyRate;
        CurrencyCode = NormalizeCurrencyCode(currencyCode);
        if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision), revision, "Revision must be positive.");
        Revision = revision;
        ChangedBy = RequireCanonicalActor(changedBy);
        Reason = ErpText.Required(reason, nameof(reason));
        ChangedAtUtc = RequireUtc(changedAtUtc, nameof(changedAtUtc));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkCenterId { get; private set; } = string.Empty;
    public string AccountingPeriodCode { get; private set; } = string.Empty;
    public MachineOverheadApplicability Applicability { get; private set; }
    public decimal FixedOverheadBudget { get; private set; }
    public decimal VariableOverheadBudget { get; private set; }
    public decimal NormalCapacityMachineHours { get; private set; }
    public decimal FixedHourlyRate { get; private set; }
    public decimal VariableHourlyRate { get; private set; }
    public decimal TotalHourlyRate { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public int Revision { get; private set; }
    public string ChangedBy { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset ChangedAtUtc { get; private set; }

    public static WorkCenterMachineOverheadRate DefineApplicable(
        string organizationId,
        string environmentId,
        string workCenterId,
        string accountingPeriodCode,
        decimal fixedOverheadBudget,
        decimal variableOverheadBudget,
        decimal normalCapacityMachineHours,
        string currencyCode,
        int revision,
        string changedBy,
        string reason,
        DateTimeOffset changedAtUtc)
        => new(
            organizationId,
            environmentId,
            workCenterId,
            accountingPeriodCode,
            MachineOverheadApplicability.Applicable,
            fixedOverheadBudget,
            variableOverheadBudget,
            normalCapacityMachineHours,
            currencyCode,
            revision,
            changedBy,
            reason,
            changedAtUtc);

    public static WorkCenterMachineOverheadRate DefineNotApplicable(
        string organizationId,
        string environmentId,
        string workCenterId,
        string accountingPeriodCode,
        string currencyCode,
        int revision,
        string changedBy,
        string reason,
        DateTimeOffset changedAtUtc)
        => new(
            organizationId,
            environmentId,
            workCenterId,
            accountingPeriodCode,
            MachineOverheadApplicability.NotApplicable,
            0m,
            0m,
            0m,
            currencyCode,
            revision,
            changedBy,
            reason,
            changedAtUtc);

    private static void ValidateCostBasis(
        MachineOverheadApplicability applicability,
        decimal fixedOverheadBudget,
        decimal variableOverheadBudget,
        decimal normalCapacityMachineHours)
    {
        if (applicability == MachineOverheadApplicability.NotApplicable)
        {
            if (fixedOverheadBudget != 0m || variableOverheadBudget != 0m || normalCapacityMachineHours != 0m)
                throw new ArgumentOutOfRangeException(nameof(applicability), "Not-applicable revisions must have zero cost values.");
            return;
        }

        if (fixedOverheadBudget < 0m)
            throw new ArgumentOutOfRangeException(nameof(fixedOverheadBudget), fixedOverheadBudget, "Fixed overhead budget cannot be negative.");
        if (variableOverheadBudget < 0m)
            throw new ArgumentOutOfRangeException(nameof(variableOverheadBudget), variableOverheadBudget, "Variable overhead budget cannot be negative.");
        if (fixedOverheadBudget == 0m && variableOverheadBudget == 0m)
            throw new ArgumentOutOfRangeException(nameof(fixedOverheadBudget), "An applicable rate requires a positive overhead budget.");
        if (normalCapacityMachineHours <= 0m)
            throw new ArgumentOutOfRangeException(nameof(normalCapacityMachineHours), normalCapacityMachineHours, "Normal capacity machine hours must be positive.");
    }

    private static decimal DivideRate(decimal budget, decimal normalCapacityMachineHours)
        => decimal.Round(budget / normalCapacityMachineHours, 6, MidpointRounding.ToEven);

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
            throw new ArgumentException("Actor must be a canonical authenticated actor.", nameof(value));
        var separator = actor.IndexOf(':', StringComparison.Ordinal);
        return separator > 0 && separator < actor.Length - 1
            ? actor
            : throw new ArgumentException("Actor must be a canonical authenticated actor.", nameof(value));
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
        => value != default && value.Offset == TimeSpan.Zero
            ? value
            : throw new ArgumentException("Timestamp must be a nondefault UTC instant.", parameterName);
}
