namespace Nerv.IIP.Coding;

public sealed record CodeCounterScope(
    string OrganizationId,
    string EnvironmentId,
    string RuleKey,
    string SiteCode,
    string ResetKey,
    long Start);

public interface ICodeStore
{
    Task<CodeIdempotencyKey?> FindIdempotencyRecordAsync(
        string organizationId,
        string environmentId,
        string ruleKey,
        string idempotencyKey,
        CancellationToken cancellationToken);

    void AddIdempotencyRecord(CodeIdempotencyKey idempotencyKey);

    Task<long> ReserveNextCounterValueAsync(CodeCounterScope scope, CancellationToken cancellationToken);
}
