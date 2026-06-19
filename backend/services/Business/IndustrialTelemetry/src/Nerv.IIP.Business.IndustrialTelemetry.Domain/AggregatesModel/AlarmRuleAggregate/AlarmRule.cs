using Nerv.IIP.Business.IndustrialTelemetry.Domain.DomainEvents;

namespace Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmRuleAggregate;

public partial record AlarmRuleId : IGuidStronglyTypedId;

public sealed class AlarmRule : Entity<AlarmRuleId>, IAggregateRoot
{
    private static readonly string[] SupportedOperators = [">", ">=", "<", "<=", "==", "!="];

    private AlarmRule()
    {
    }

    private AlarmRule(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string ruleCode,
        string alarmCode,
        string severity,
        string tagKey,
        string comparisonOperator,
        decimal thresholdValue,
        string unitCode,
        bool isEnabled)
    {
        Id = new AlarmRuleId(Guid.CreateVersion7());
        OrganizationId = IndustrialTelemetryText.Required(organizationId, nameof(organizationId));
        EnvironmentId = IndustrialTelemetryText.Required(environmentId, nameof(environmentId));
        DeviceAssetId = IndustrialTelemetryText.Required(deviceAssetId, nameof(deviceAssetId));
        RuleCode = IndustrialTelemetryText.Required(ruleCode, nameof(ruleCode));
        UpdateDefinition(alarmCode, severity, tagKey, comparisonOperator, thresholdValue, unitCode, isEnabled);
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new AlarmRuleConfiguredDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DeviceAssetId { get; private set; } = string.Empty;
    public string RuleCode { get; private set; } = string.Empty;
    public string AlarmCode { get; private set; } = string.Empty;
    public string Severity { get; private set; } = string.Empty;
    public string TagKey { get; private set; } = string.Empty;
    public string ComparisonOperator { get; private set; } = string.Empty;
    public decimal ThresholdValue { get; private set; }
    public string UnitCode { get; private set; } = string.Empty;
    public bool IsEnabled { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static bool IsSupportedComparisonOperator(string comparisonOperator)
    {
        return SupportedOperators.Contains(comparisonOperator, StringComparer.Ordinal);
    }

    public static AlarmRule Configure(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string ruleCode,
        string alarmCode,
        string severity,
        string tagKey,
        string comparisonOperator,
        decimal thresholdValue,
        string unitCode,
        bool isEnabled)
    {
        return new AlarmRule(organizationId, environmentId, deviceAssetId, ruleCode, alarmCode, severity, tagKey, comparisonOperator, thresholdValue, unitCode, isEnabled);
    }

    public void UpdateDefinition(
        string alarmCode,
        string severity,
        string tagKey,
        string comparisonOperator,
        decimal thresholdValue,
        string unitCode,
        bool isEnabled)
    {
        var normalizedOperator = IndustrialTelemetryText.Required(comparisonOperator, nameof(comparisonOperator));
        if (!IsSupportedComparisonOperator(normalizedOperator))
        {
            throw new ArgumentOutOfRangeException(nameof(comparisonOperator), "Unsupported alarm rule comparison operator.");
        }

        AlarmCode = IndustrialTelemetryText.Required(alarmCode, nameof(alarmCode));
        Severity = IndustrialTelemetryText.RequiredLower(severity, nameof(severity));
        TagKey = IndustrialTelemetryText.RequiredLower(tagKey, nameof(tagKey));
        ComparisonOperator = normalizedOperator;
        ThresholdValue = thresholdValue;
        UnitCode = IndustrialTelemetryText.Required(unitCode, nameof(unitCode));
        IsEnabled = isEnabled;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public bool Evaluate(decimal averageValue, decimal maxValue)
    {
        return IsEnabled
            && (Compare(averageValue, ThresholdValue, ComparisonOperator)
                || Compare(maxValue, ThresholdValue, ComparisonOperator));
    }

    private static bool Compare(decimal observedValue, decimal thresholdValue, string comparisonOperator)
    {
        return comparisonOperator switch
        {
            ">" => observedValue > thresholdValue,
            ">=" => observedValue >= thresholdValue,
            "<" => observedValue < thresholdValue,
            "<=" => observedValue <= thresholdValue,
            "==" => observedValue == thresholdValue,
            "!=" => observedValue != thresholdValue,
            _ => throw new ArgumentOutOfRangeException(nameof(comparisonOperator), "Unsupported alarm rule comparison operator."),
        };
    }
}
