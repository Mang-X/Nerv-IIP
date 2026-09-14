using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nerv.IIP.Coding;

/// <summary>
/// <see cref="CodeCounter"/> 与 <see cref="CodeIdempotencyKey"/> 这两个**共享实体**的 EF 配置的唯一出处（#3307）。
/// </summary>
/// <remarks>
/// <para><b>改前的形状</b>：这两个类型声明在本包里，配置却在 7 个服务的 Infrastructure 里
/// 各有一份逐字节副本（<c>Coding/CodeEntityTypeConfigurations.cs</c>，归一化 namespace 行后 diff 为空，
/// 7/7 实读），靠各自的 <c>ApplyConfigurationsFromAssembly</c> 装配。
/// 7 份列宽可以单边漂移，且没有任何机制会因为它们不一致而红。
/// 本文件把那 7 份删成 1 份——治的是**重复本身**，不是「校验重复是否一致」
/// （后者抓不到「7 份被同样改坏」，见本仓判例）。</para>
///
/// <para><b>为什么走显式扩展方法而不是让 7 个服务继续扫描程序集</b>：
/// <c>ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly)</c> 按定义只看服务自己的程序集，
/// 配置一旦搬进本包就不在它的扫描面上。本仓已有同形先例
/// （<c>IntegrationEventDeadLetterModelBuilderExtensions.ConfigureIntegrationEventDeadLetters</c>），沿用之。</para>
///
/// <para><b>失效方向（写清楚，别读成完备）</b>：本收拢是**显式 hook，不是编译期强制**。
/// 新服务声明了 <c>DbSet&lt;CodeIdempotencyKey&gt;</c> 却忘了调
/// <see cref="CodingModelBuilderExtensions.ConfigureCodingEntities"/>，或者再写一份服务本地配置盖掉它，
/// 编译期都不报错。这不是假想——Maintenance 就没调 <c>ConfigureIntegrationEventDeadLetters()</c>，
/// 而是留了一份服务本地的死信箱配置。
/// 这条失效方向由 <c>CodeIdempotencyKeyCrossServiceWidthContractTests</c> 看守：
/// 它在同一个进程里读 7 个服务**各自真实的 EF 模型**（migration 就是从这个模型生成的），
/// 逐服务做闭集枚举 + 计数封闭，忘调 hook 会让该服务的值域塌成空集而报红。</para>
///
/// <para><b>不在本文件射程内</b>：全仓其它 <c>idempotency_key</c> 列（BarcodeLabel / Inventory / Wms 的 128、
/// Mes / Quality 消费者收件箱的 512、死信箱的 500 等）与本共享实体不同族、不同所有者，
/// 本文件既不管辖也不声称覆盖。</para>
/// </remarks>
public static class CodingModelBuilderExtensions
{
    /// <summary>
    /// 把共享编码实体的表、列、列宽与唯一索引装配到调用方的模型上。
    /// 服务自己的 <c>DbSet</c> 声明不构成配置——必须显式调用本方法。
    ///
    /// <para><b>调用位置有语义，别随手挪</b>：7 个服务都把本调用放在
    /// <c>ApplyConfigurationsFromAssembly(...)</c> **之后**。EF 对同一实体的多次配置是**后写覆盖**，
    /// 这个位置让共享配置对服务本地的 <c>IEntityTypeConfiguration&lt;CodeIdempotencyKey&gt;</c> 具有权威性：
    /// 某个服务再偷偷加一份本地配置把列宽改成别的值，也会被本方法盖回唯一出处。
    /// **这是实测结论不是推断**——#3307 的变异矩阵里，给 Mes 加一份 <c>HasMaxLength(120)</c> 的本地配置
    /// 而保持本调用在后，模型读数仍是 150（契约用例绿）；把本调用挪到
    /// <c>ApplyConfigurationsFromAssembly</c> **之前**、其余不变，同一份本地配置就赢了，
    /// 模型读数变成 120 且契约用例转红。
    /// 换句话说：**挪动这一行会把一条现在成立的保护静默拆掉**，而编译期不会有任何提示。</para>
    /// </summary>
    public static void ConfigureCodingEntities(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<CodeCounter>(ConfigureCodeCounter);
        modelBuilder.Entity<CodeIdempotencyKey>(ConfigureCodeIdempotencyKey);
    }

    private static void ConfigureCodeCounter(EntityTypeBuilder<CodeCounter> builder)
    {
        builder.ToTable("code_counters", table => table.HasComment("Service-local code counters scoped by organization, environment, rule key, optional site and reset bucket."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasComment("Code counter surrogate identifier.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization scope for the code counter.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment scope for the code counter.");
        builder.Property(x => x.RuleKey).HasColumnName("rule_key").IsRequired().HasMaxLength(100).HasComment("Code rule key governed by this counter.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").IsRequired().HasMaxLength(100).HasComment("Optional site or plant scope; empty string means global within organization and environment.");
        builder.Property(x => x.ResetKey).HasColumnName("reset_key").IsRequired().HasMaxLength(16).HasComment("Sequence reset bucket derived from the active code rule.");
        builder.Property(x => x.CurrentValue).HasColumnName("current_value").HasComment("Last allocated sequence value within the counter scope.");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().HasComment("Optimistic concurrency token incremented whenever the counter advances.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.RuleKey, x.SiteCode, x.ResetKey }).IsUnique().HasDatabaseName("ux_code_counters_scope");
    }

    private static void ConfigureCodeIdempotencyKey(EntityTypeBuilder<CodeIdempotencyKey> builder)
    {
        builder.ToTable("code_idempotency_keys", table => table.HasComment("Service-local idempotency records that bind create request keys to allocated codes."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasComment("Code idempotency record surrogate identifier.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization scope for the idempotency key.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment scope for the idempotency key.");
        builder.Property(x => x.RuleKey).HasColumnName("rule_key").IsRequired().HasMaxLength(100).HasComment("Code rule key governed by the idempotency key.");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(CodeIdempotencyKey.IdempotencyKeyMaxLength).HasComment("Client supplied stable idempotency key for ordinary create requests.");
        builder.Property(x => x.Code).HasColumnName("code").IsRequired().HasMaxLength(128).HasComment("Allocated business code returned for this idempotency key.");
        // 已知边界，未立案（#3307 实读登记）：payload_fingerprint 与 idempotency_key 一样是**原样落库**——
        // CodeAllocator.Fingerprint 是 string.Join('|', ...)，**不是摘要**。因此 1000 是对**原始拼接结果**
        // 的真上界（不像存定长摘要的列那样对原始输入零约束），而该结果的长度由**请求字段总长**决定：
        // 字段够宽的命令走进 CodeAllocator 会在 SaveChangesAsync 撞 PostgreSQL 22001，而**不是**在入口被拒——
        // 与 #3288 同形。
        // ⚠️ 这句话**不是**「这里是安全的」，也不是「1000 够用」——它只说明上界在哪、由谁决定、越界时在哪里炸。
        // 当前**无实测溢出**（本仓 #3318 / #3273 / #3275 / #3322 那一族每张都由真实溢出触发，这条不是），
        // 故按「不过度防御、不为边界问题大量开票」未单独立案。
        // 重启条件是**真的撞到 22001**，不是「理论上可能」。
        builder.Property(x => x.PayloadFingerprint).HasColumnName("payload_fingerprint").IsRequired().HasMaxLength(1000).HasComment("Canonical request payload fingerprint used to reject key reuse with different create data.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasComment("UTC timestamp when the idempotency key was first recorded.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.RuleKey, x.IdempotencyKey }).IsUnique().HasDatabaseName("ux_code_idempotency_keys_scope");
    }
}
