namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.CodeRuleAggregate;

public partial record CodeRuleId : IGuidStronglyTypedId;

public class CodeRule : Entity<CodeRuleId>, IAggregateRoot
{
    protected CodeRule()
    {
    }

    private CodeRule(
        string organizationId,
        string environmentId,
        string ruleKey,
        string displayName,
        string appliesTo,
        int scope,
        string segmentsJson,
        bool isActive,
        int version)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        RuleKey = Required(ruleKey);
        DisplayName = Required(displayName);
        AppliesTo = Optional(appliesTo) ?? string.Empty;
        Scope = scope;
        SegmentsJson = Required(segmentsJson);
        IsActive = isActive;
        Version = version <= 0 ? throw new ArgumentOutOfRangeException(nameof(version)) : version;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;

    public string EnvironmentId { get; private set; } = string.Empty;

    public string RuleKey { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string AppliesTo { get; private set; } = string.Empty;

    public int Scope { get; private set; }

    public string SegmentsJson { get; private set; } = "[]";

    public bool IsActive { get; private set; }

    public int Version { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static CodeRule Create(
        string organizationId,
        string environmentId,
        string ruleKey,
        string displayName,
        string appliesTo,
        int scope,
        string segmentsJson,
        bool isActive,
        int version)
    {
        return new CodeRule(organizationId, environmentId, ruleKey, displayName, appliesTo, scope, segmentsJson, isActive, version);
    }

    public void ReplaceDefinition(string displayName, string appliesTo, int scope, string segmentsJson, bool isActive, int version)
    {
        DisplayName = Required(displayName);
        AppliesTo = Optional(appliesTo) ?? string.Empty;
        Scope = scope;
        SegmentsJson = Required(segmentsJson);
        IsActive = isActive;
        Version = version <= 0 ? throw new ArgumentOutOfRangeException(nameof(version)) : version;
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
}
