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
        bool isEnabled,
        decimal deadbandValue,
        int onDelaySeconds,
        int offDelaySeconds,
        int minDurationSeconds,
        string? priority)
    {
        Id = new AlarmRuleId(Guid.CreateVersion7());
        OrganizationId = IndustrialTelemetryText.Required(organizationId, nameof(organizationId));
        EnvironmentId = IndustrialTelemetryText.Required(environmentId, nameof(environmentId));
        DeviceAssetId = IndustrialTelemetryText.Required(deviceAssetId, nameof(deviceAssetId));
        RuleCode = IndustrialTelemetryText.Required(ruleCode, nameof(ruleCode));
        UpdateDefinition(alarmCode, severity, tagKey, comparisonOperator, thresholdValue, unitCode, isEnabled, deadbandValue, onDelaySeconds, offDelaySeconds, minDurationSeconds, priority);
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
    public decimal DeadbandValue { get; private set; }
    public int OnDelaySeconds { get; private set; }
    public int OffDelaySeconds { get; private set; }
    public int MinDurationSeconds { get; private set; }
    public string Priority { get; private set; } = string.Empty;
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
        bool isEnabled,
        decimal deadbandValue = 0m,
        int onDelaySeconds = 0,
        int offDelaySeconds = 0,
        int minDurationSeconds = 0,
        string? priority = null)
    {
        return new AlarmRule(organizationId, environmentId, deviceAssetId, ruleCode, alarmCode, severity, tagKey, comparisonOperator, thresholdValue, unitCode, isEnabled, deadbandValue, onDelaySeconds, offDelaySeconds, minDurationSeconds, priority);
    }

    public void UpdateDefinition(
        string alarmCode,
        string severity,
        string tagKey,
        string comparisonOperator,
        decimal thresholdValue,
        string unitCode,
        bool isEnabled,
        decimal deadbandValue = 0m,
        int onDelaySeconds = 0,
        int offDelaySeconds = 0,
        int minDurationSeconds = 0,
        string? priority = null)
    {
        var normalizedOperator = IndustrialTelemetryText.Required(comparisonOperator, nameof(comparisonOperator));
        if (!IsSupportedComparisonOperator(normalizedOperator))
        {
            throw new ArgumentOutOfRangeException(nameof(comparisonOperator), "Unsupported alarm rule comparison operator.");
        }

        if (deadbandValue < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deadbandValue), "Alarm rule deadband must be non-negative.");
        }

        if (onDelaySeconds < 0 || offDelaySeconds < 0 || minDurationSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(onDelaySeconds), "Alarm rule delay values must be non-negative.");
        }

        AlarmCode = IndustrialTelemetryText.Required(alarmCode, nameof(alarmCode));
        Severity = IndustrialTelemetryText.RequiredLower(severity, nameof(severity));
        TagKey = IndustrialTelemetryText.RequiredLower(tagKey, nameof(tagKey));
        ComparisonOperator = normalizedOperator;
        ThresholdValue = thresholdValue;
        UnitCode = IndustrialTelemetryText.Required(unitCode, nameof(unitCode));
        IsEnabled = isEnabled;
        DeadbandValue = deadbandValue;
        OnDelaySeconds = onDelaySeconds;
        OffDelaySeconds = offDelaySeconds;
        MinDurationSeconds = minDurationSeconds;
        Priority = IndustrialTelemetryText.Optional(priority) ?? Severity;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public bool Evaluate(decimal averageValue, decimal maxValue)
    {
        if (!IsEnabled)
        {
            return false;
        }

        return ComparisonOperator is "==" or "!="
            ? Compare(averageValue, ThresholdValue, ComparisonOperator)
            : Compare(averageValue, ThresholdValue, ComparisonOperator)
                || Compare(maxValue, ThresholdValue, ComparisonOperator);
    }

    public int RequiredTriggerSeconds => Math.Max(OnDelaySeconds, MinDurationSeconds);

    public bool IsReturnToNormal(decimal averageValue, decimal maxValue)
    {
        if (!IsEnabled)
        {
            return false;
        }

        return ComparisonOperator switch
        {
            ">" => maxValue <= ThresholdValue - DeadbandValue && averageValue <= ThresholdValue - DeadbandValue,
            ">=" => maxValue < ThresholdValue - DeadbandValue && averageValue < ThresholdValue - DeadbandValue,
            "<" => averageValue >= ThresholdValue + DeadbandValue && maxValue >= ThresholdValue + DeadbandValue,
            "<=" => averageValue > ThresholdValue + DeadbandValue && maxValue > ThresholdValue + DeadbandValue,
            "==" => averageValue != ThresholdValue,
            "!=" => averageValue == ThresholdValue,
            _ => throw new ArgumentOutOfRangeException(nameof(ComparisonOperator), "Unsupported alarm rule comparison operator."),
        };
    }

    public decimal SelectObservedValue(decimal averageValue, decimal maxValue)
    {
        return ComparisonOperator is ">" or ">="
            ? maxValue
            : averageValue;
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
