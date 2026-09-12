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
    /// <summary>
    /// <c>code_idempotency_keys.idempotency_key</c> 的列宽，也是**调用方可控的原始幂等键**的物理上界。
    ///
    /// <para><b>它真的是上界</b>：<c>CodeAllocator</c> 到落库之间不对这把键做摘要或截断——
    /// 唯一的加工是 <c>Normalize</c>（<c>CodeAllocator.cs:359-362</c>）的 <c>Trim()</c>，
    /// 该包里唯一的 <c>SHA256</c> 调用是编码规则的校验位段（<c>HashModChecksum</c>），不在本列写入路径上。
    /// 所以超出本值的键会在 <c>SaveChangesAsync</c> 换来 PostgreSQL <c>22001</c>，
    /// 而不是像「存定长摘要」的列那样对原始键零约束（#3286 的判例形状在此**不适用**）。</para>
    ///
    /// <para><b>本常量是这一列宽在全仓的唯一出处（#3307）。</b>改前 7 个服务
    /// （DemandPlanning / Erp / Maintenance / MasterData / Mes / ProductEngineering / Quality）
    /// 各自持有一份逐字节复制的 <c>CodeEntityTypeConfigurations.cs</c>、各写一遍 <c>HasMaxLength(150)</c>，
    /// 可以单边漂移且没有任何机制会红。那 7 份已整体删除，配置收进
    /// <see cref="CodingModelBuilderExtensions.ConfigureCodingEntities"/>。</para>
    ///
    /// <para><b>失效方向（别读成「列宽已被全仓钉死」）</b>：收拢消灭的是「7 份之间互相漂移」，
    /// 不是「谁都改不动这一列」。改本常量一处即同时改动 7 个服务的模型，
    /// 届时 7 份 <c>ApplicationDbContextModelSnapshot</c> 全部与模型不符——
    /// 那一面由各服务的 <c>PostgreSQL Provider Tests</c> 的 <c>MigrateAsync</c>
    /// 抛 <c>PendingModelChangesWarning</c> 承担（本仓没有 pending-model-changes 门禁，见 #3347）。
    /// 另一条失效方向是**服务不调用那个扩展**（或再写一份服务本地配置覆盖它）：
    /// 收拢用的是显式 hook，不是编译期强制；本仓已有先例——Maintenance 就没调
    /// <c>ConfigureIntegrationEventDeadLetters()</c> 而是留了一份服务本地副本。
    /// 这条失效方向由 <c>CodeIdempotencyKeyCrossServiceWidthContractTests</c> 从 7 个服务的
    /// 真实 EF 模型逐个读取来看守。</para>
    /// </summary>
    public const int IdempotencyKeyMaxLength = 150;

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
