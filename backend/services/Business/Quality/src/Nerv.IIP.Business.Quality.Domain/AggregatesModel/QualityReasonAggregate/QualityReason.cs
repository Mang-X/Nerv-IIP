namespace Nerv.IIP.Business.Quality.Domain.AggregatesModel.QualityReasonAggregate;

public partial record QualityReasonId : IGuidStronglyTypedId;

public sealed class QualityReason : Entity<QualityReasonId>, IAggregateRoot
{
    private static readonly HashSet<string> Severities =
    [
        "minor",
        "major",
        "critical",
    ];

    private static readonly HashSet<string> DefaultDispositions =
    [
        "rework",
        "scrap",
        "return-to-supplier",
        "conditional-release",
    ];

    private QualityReason()
    {
    }

    private QualityReason(
        string organizationId,
        string environmentId,
        string reasonCode,
        string reasonName,
        string groupName,
        string severity,
        string? defaultDisposition,
        bool enabled)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        ReasonCode = Required(reasonCode);
        ReasonName = Required(reasonName);
        GroupName = Required(groupName);
        Severity = Supported(severity, Severities, nameof(severity));
        DefaultDisposition = OptionalSupported(defaultDisposition, DefaultDispositions, nameof(defaultDisposition));
        Enabled = enabled;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string ReasonCode { get; private set; } = string.Empty;
    public string ReasonName { get; private set; } = string.Empty;
    public string GroupName { get; private set; } = string.Empty;
    public string Severity { get; private set; } = string.Empty;
    public string? DefaultDisposition { get; private set; }
    public bool Enabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static bool IsSupportedSeverity(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && Severities.Contains(value.Trim().ToLowerInvariant());
    }

    public static bool IsSupportedDefaultDisposition(string? value)
    {
        var normalized = Optional(value)?.ToLowerInvariant();
        return normalized is null || DefaultDispositions.Contains(normalized);
    }

    public static QualityReason Create(
        string organizationId,
        string environmentId,
        string reasonCode,
        string reasonName,
        string groupName,
        string severity,
        string? defaultDisposition,
        bool enabled)
    {
        return new QualityReason(organizationId, environmentId, reasonCode, reasonName, groupName, severity, defaultDisposition, enabled);
    }

    public void Update(string reasonName, string groupName, string severity, string? defaultDisposition)
    {
        ReasonName = Required(reasonName);
        GroupName = Required(groupName);
        Severity = Supported(severity, Severities, nameof(severity));
        DefaultDisposition = OptionalSupported(defaultDisposition, DefaultDispositions, nameof(defaultDisposition));
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
        {
            return;
        }

        Enabled = enabled;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }

    private static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? OptionalSupported(string? value, IReadOnlySet<string> supported, string parameterName)
    {
        var normalized = Optional(value)?.ToLowerInvariant();
        if (normalized is null)
        {
            return null;
        }

        return supported.Contains(normalized)
            ? normalized
            : throw new ArgumentException($"Unsupported value '{value}'.", parameterName);
    }

    private static string Supported(string value, IReadOnlySet<string> supported, string parameterName)
    {
        var normalized = Required(value).ToLowerInvariant();
        return supported.Contains(normalized)
            ? normalized
            : throw new ArgumentException($"Unsupported value '{value}'.", parameterName);
    }
}
