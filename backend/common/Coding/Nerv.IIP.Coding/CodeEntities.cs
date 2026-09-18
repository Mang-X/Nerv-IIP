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

    /// <summary>
    /// <c>code_idempotency_keys.code</c> 的列宽，也是 <see cref="CodeAllocator"/> **自己装得下的码**的物理上界。
    ///
    /// <para><b>它是分配器自己的上界，不是业务码的上界。</b>调用方给的 <c>RequestedCode</c> 被
    /// <see cref="CodeAllocator.AllocateAsync"/> 原样采用（唯一加工是 <c>Normalize</c> 的 <c>Trim()</c>），
    /// 再写进这一列；<c>CodeAllocator</c> 到落库之间不做摘要也不截断，所以超出本值的码
    /// 过去会在 <c>SaveChangesAsync</c> 换来 PostgreSQL <c>22001</c>——用户看到 500 而不是校验提示（GitHub #3454）。
    /// 现在 <see cref="CodeAllocator.AllocateAsync"/> 在触库之前就用本常量把它拒掉。</para>
    ///
    /// <para><b>覆盖边界（别读成「码一定存得下」）</b>：本常量只描述**共享编码表这一列**。
    /// 分配出来的码同时还会被调用方写进它自己聚合的码列，那些列由各服务拥有、宽度各不相同
    /// （实读：MasterData <c>units_of_measure.code</c> 50、<c>tooling_assets.code</c> 64、多数业务码列 100、
    /// Quality <c>measuring_devices.device_code</c> 128）。
    /// 逐个调用点的**有效上界是这一列与该业务列的最小值**，而 <c>CodeAllocator</c> 在结构上看不见后者。
    /// ⇒ 本守卫是**必要条件不是充分条件**：它关掉的是「分配器自己这一列溢出」，
    /// 比 128 更窄的业务列仍由各自服务的校验层负责，⛔ 不要把一个不属于本链路的窄上界配进本常量。</para>
    ///
    /// <para><b>失效方向</b>：① 改本常量会同时移动 EF 配置与分配器守卫，
    /// 两侧同步移动的断言恒绿——钉住「128 这个数字本身」的是
    /// <c>CodeAllocatorRequestedCodeWidthContractTests.Introducing_the_guard_did_not_change_the_column_width</c>
    /// 以及各服务 <c>ApplicationDbContextModelSnapshot</c>（改常量会让 7 份 snapshot 与模型不符，
    /// 由 <c>PostgreSQL Provider Tests</c> 的 <c>MigrateAsync</c> 抛 <c>PendingModelChangesWarning</c> 显形；
    /// 本仓没有 pending-model-changes 门禁，见 #3347）。
    /// ② 守卫只管**长度**，⛔ 不管形状：合法长度但不符规则形状的码（例如 SKU 码写成 <c>!!!</c>）照样放行，
    /// 这是 #3454 有意留下的边界——「调用方可以指定码」是业务能力。
    /// ③ 守卫只管 <c>RequestedCode</c>，⛔ 不管 <c>NextCodeAsync</c> 按规则**生成**的码；
    /// 规则段配得足够宽时生成结果同样可能越过本值，那条路径不在 #3454 射程内。</para>
    /// </summary>
    public const int CodeMaxLength = 128;

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
