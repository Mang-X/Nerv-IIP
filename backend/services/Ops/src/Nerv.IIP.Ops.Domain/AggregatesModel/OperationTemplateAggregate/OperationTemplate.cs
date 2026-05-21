using NetCorePal.Extensions.Domain;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Ops.Domain.AggregatesModel.OperationTemplateAggregate;

public partial record OperationTemplateId : IStringStronglyTypedId;

public sealed record OperationTemplateSnapshot(
    string OperationCode,
    bool Enabled,
    int DefaultMaxAttempts,
    int DefaultLeaseDurationSeconds,
    bool RequiresApproval);

public sealed class OperationTemplate : Entity<OperationTemplateId>, IAggregateRoot
{
    private static readonly HashSet<string> AllowedRiskLevels = new(StringComparer.Ordinal)
    {
        "low",
        "medium",
        "high",
        "critical"
    };

    private OperationTemplate()
    {
        Id = new OperationTemplateId(string.Empty);
    }

    private OperationTemplate(
        OperationTemplateId id,
        string operationCode,
        string displayName,
        string parameterSchemaJson,
        string riskLevel,
        int defaultMaxAttempts,
        int defaultLeaseDurationSeconds,
        bool requiresApproval,
        DateTimeOffset now)
    {
        Id = id;
        OperationCode = NormalizeRequired(operationCode, nameof(operationCode));
        DisplayName = NormalizeRequired(displayName, nameof(displayName));
        ParameterSchemaJson = NormalizeSchema(parameterSchemaJson);
        RiskLevel = NormalizeRiskLevel(riskLevel);
        DefaultMaxAttempts = ClampDefaultMaxAttempts(defaultMaxAttempts);
        DefaultLeaseDurationSeconds = ClampDefaultLeaseDurationSeconds(defaultLeaseDurationSeconds);
        RequiresApproval = requiresApproval;
        Enabled = true;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public string OperationCode { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string ParameterSchemaJson { get; private set; } = "{}";
    public string RiskLevel { get; private set; } = string.Empty;
    public int DefaultMaxAttempts { get; private set; }
    public int DefaultLeaseDurationSeconds { get; private set; }
    public bool RequiresApproval { get; private set; }
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public Deleted Deleted { get; private set; } = new(false);
    public RowVersion RowVersion { get; private set; } = new(0);

    public static OperationTemplate Create(
        OperationTemplateId id,
        string operationCode,
        string displayName,
        string parameterSchemaJson,
        string riskLevel,
        int defaultMaxAttempts,
        int defaultLeaseDurationSeconds,
        bool requiresApproval,
        DateTimeOffset now)
    {
        return new OperationTemplate(
            id,
            operationCode,
            displayName,
            parameterSchemaJson,
            riskLevel,
            defaultMaxAttempts,
            defaultLeaseDurationSeconds,
            requiresApproval,
            now);
    }

    public static OperationTemplateSnapshot CreateSnapshot(
        string operationCode,
        bool enabled,
        int defaultMaxAttempts,
        int defaultLeaseDurationSeconds,
        bool requiresApproval)
    {
        return new OperationTemplateSnapshot(
            NormalizeRequired(operationCode, nameof(operationCode)),
            enabled,
            ClampDefaultMaxAttempts(defaultMaxAttempts),
            ClampDefaultLeaseDurationSeconds(defaultLeaseDurationSeconds),
            requiresApproval);
    }

    public OperationTemplateSnapshot ToSnapshot()
    {
        return new OperationTemplateSnapshot(
            OperationCode,
            Enabled,
            DefaultMaxAttempts,
            DefaultLeaseDurationSeconds,
            RequiresApproval);
    }

    public void Disable(DateTimeOffset now)
    {
        Enabled = false;
        UpdatedAtUtc = now;
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationTaskRequestException($"{parameterName} is required.");
        }

        return value.Trim();
    }

    private static string NormalizeSchema(string parameterSchemaJson)
    {
        return string.IsNullOrWhiteSpace(parameterSchemaJson) ? "{}" : parameterSchemaJson.Trim();
    }

    private static string NormalizeRiskLevel(string riskLevel)
    {
        var normalized = NormalizeRequired(riskLevel, nameof(riskLevel)).ToLowerInvariant();
        if (!AllowedRiskLevels.Contains(normalized))
        {
            throw new InvalidOperationTaskRequestException($"Unsupported operation template risk level: {riskLevel}");
        }

        return normalized;
    }

    private static int ClampDefaultMaxAttempts(int value)
    {
        return Math.Clamp(value, 1, 10);
    }

    private static int ClampDefaultLeaseDurationSeconds(int value)
    {
        return Math.Clamp(value, 30, 3600);
    }
}
