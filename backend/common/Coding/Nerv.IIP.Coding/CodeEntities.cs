#pragma warning disable S1144 // EF Core sets surrogate identifiers through materialization.
namespace Nerv.IIP.Coding;

public sealed class CodeCounter
{
    private CodeCounter() { }

    public CodeCounter(string organizationId, string environmentId, string ruleKey, string siteCode, string resetKey)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        RuleKey = ruleKey;
        SiteCode = siteCode;
        ResetKey = resetKey;
    }

    public long Id { get; private set; }

    public string OrganizationId { get; private set; } = string.Empty;

    public string EnvironmentId { get; private set; } = string.Empty;

    public string RuleKey { get; private set; } = string.Empty;

    public string SiteCode { get; private set; } = string.Empty;

    public string ResetKey { get; private set; } = string.Empty;

    public long CurrentValue { get; private set; }

    public long Version { get; private set; }

    public long AdvanceFrom(long start)
    {
        CurrentValue = CurrentValue < start - 1 ? start : CurrentValue + 1;
        Version++;
        return CurrentValue;
    }
}

public sealed class CodeIdempotencyKey
{
    private CodeIdempotencyKey() { }

    public CodeIdempotencyKey(
        string organizationId,
        string environmentId,
        string ruleKey,
        string idempotencyKey,
        string code,
        string payloadFingerprint,
        DateTimeOffset createdAtUtc)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        RuleKey = ruleKey;
        IdempotencyKey = idempotencyKey;
        Code = code;
        PayloadFingerprint = payloadFingerprint;
        CreatedAtUtc = createdAtUtc;
    }

    public long Id { get; private set; }

    public string OrganizationId { get; private set; } = string.Empty;

    public string EnvironmentId { get; private set; } = string.Empty;

    public string RuleKey { get; private set; } = string.Empty;

    public string IdempotencyKey { get; private set; } = string.Empty;

    public string Code { get; private set; } = string.Empty;

    public string PayloadFingerprint { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
