using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;

namespace Nerv.IIP.Business.Mes.Domain.Tests;

/// <summary>
/// 完工入库 Inventory 过账幂等键的两段式回落（#3332）。
/// </summary>
/// <remarks>
/// <para>本类按五条互不重叠的方向布防，别把它们读成同一条：</para>
/// <list type="number">
/// <item><b>上界</b>：任何输入下派生键都装得进下游承载列。去掉回落分支即红。</item>
/// <item><b>存量逐字保持</b>：今天能走通的键（整键 ≤ 列宽）必须一个字节都不变。
/// 把「装得下就原样」改成无条件回落即红。这条钉的是 Inventory 按**精确键**查幂等行——
/// 键变形就查不到已落库的行，同一笔重投会重复落库。</item>
/// <item><b>跨分支不别名</b>：逐字出口与回落出口**永不产出同一个键**。
/// 判别位是「前缀之后是否出现 <c>:</c>」，它由作用域段强制、调用方无法伪造。
/// 把回落分支改回「只摘尾段、作用域段可读」即红（PR #3340 审核抓出的阻断就是那个形状）。</item>
/// <item><b>字面前缀</b>：每一种形态都以 <see cref="FinishedGoodsReceiptInventoryPostingKey.Prefix"/> 开头，
/// 三个服务的 <c>StartsWith</c> 路由在新形态下仍成立。</item>
/// <item><b>作用域守卫不降级</b>：<see cref="FinishedGoodsReceiptInventoryPostingKey.BelongsToScope"/>
/// 对四种形态**全部由三元组重算**后比对，不按形状放行。</item>
/// </list>
/// <para><b>本类不证明什么</b>：它只看键这一层。「下游三处 <c>StartsWith</c> 解析点在新形态下仍成立」
/// 由方向 4 结构性给出（三处都只对前缀做 <c>StartsWith</c>）；而「这四处就是全部解析点」的扫描面
/// 是字面量文本，若有人把前缀抽成别处常量再引用就扫不到，**不是穷举**（见被测类型的注释）。
/// 守卫的值域边界（作用域三段不含 <c>:</c> 这个前提）同样见那里。</para>
/// </remarks>
public sealed class FinishedGoodsReceiptInventoryPostingKeyTests
{
    private const string LegacyBaseKey = "mes:finished-goods-receipt:org-001:env-dev:FGR-001";

    [Fact]
    public void Fallback_form_fits_the_downstream_column_by_construction()
    {
        Assert.True(
            FinishedGoodsReceiptInventoryPostingKey.FallbackMaxLength
                <= FinishedGoodsReceiptInventoryPostingKey.ColumnMaxLength,
            $"回落形态最长 {FinishedGoodsReceiptInventoryPostingKey.FallbackMaxLength}，"
            + $"承载列只放得下 {FinishedGoodsReceiptInventoryPostingKey.ColumnMaxLength}。");
    }

    /// <summary>
    /// 上界：作用域三段与调用方原始键都取到荒谬长度，派生键仍装得进列。
    /// </summary>
    /// <remarks>
    /// 这里的 10000 不是「上界」，是「远超任何列宽的任意长输入」——本方向要证的正是
    /// 上界与输入长度**无关**。与列宽绑定的那条断言在 Acceptance 侧从两边 EF 模型派生。
    /// </remarks>
    [Theory]
    [InlineData(1, 1, 1, 1)]
    [InlineData(100, 100, 100, 0)]
    [InlineData(100, 100, 100, 1)]
    [InlineData(100, 100, 100, 200)]
    [InlineData(100, 100, 100, 10000)]
    [InlineData(1, 1, 1, 10000)]
    [InlineData(40, 1, 1, 60)]
    public void Derived_keys_always_fit_the_downstream_column(int org, int env, int requestNo, int callerKey)
    {
        var organizationId = Repeat('o', org);
        var environmentId = Repeat('e', env);
        var receiptNo = Repeat('r', requestNo);

        var baseKey = FinishedGoodsReceiptInventoryPostingKey.Build(organizationId, environmentId, receiptNo);
        Assert.True(
            baseKey.Length <= FinishedGoodsReceiptInventoryPostingKey.ColumnMaxLength,
            $"基础键长度 {baseKey.Length} 超过列宽 {FinishedGoodsReceiptInventoryPostingKey.ColumnMaxLength}：{baseKey}");

        if (callerKey == 0)
        {
            return;
        }

        var retryKey = FinishedGoodsReceiptInventoryPostingKey.BuildRetry(
            organizationId,
            environmentId,
            receiptNo,
            Repeat('k', callerKey));
        Assert.True(
            retryKey.Length <= FinishedGoodsReceiptInventoryPostingKey.ColumnMaxLength,
            $"重投键长度 {retryKey.Length} 超过列宽 {FinishedGoodsReceiptInventoryPostingKey.ColumnMaxLength}：{retryKey}");
    }

    /// <summary>
    /// 存量逐字保持（硬约束）：改动前后同一组入参产出的键必须逐字节相同。
    /// 期望值是**改动前实现的字面产物**，不是用新实现算出来的，故这条不会随实现一起漂移。
    /// </summary>
    [Fact]
    public void Keys_that_already_fit_are_preserved_byte_for_byte()
    {
        Assert.Equal(
            LegacyBaseKey,
            FinishedGoodsReceiptInventoryPostingKey.Build("org-001", "env-dev", "FGR-001"));
        Assert.Equal(
            LegacyBaseKey + ":retry-0f6b2c1e",
            FinishedGoodsReceiptInventoryPostingKey.BuildRetry("org-001", "env-dev", "FGR-001", "retry-0f6b2c1e"));
    }

    /// <summary>
    /// 逐字保持的边界恰在列宽上：整键 = 列宽仍原样，+1 才回落。
    /// </summary>
    [Fact]
    public void Verbatim_branch_ends_exactly_at_the_column_width()
    {
        const string organizationId = "o";
        const string environmentId = "e";
        const string requestNo = "r";
        var readableLength = FinishedGoodsReceiptInventoryPostingKey.Prefix.Length
            + organizationId.Length + 1 + environmentId.Length + 1 + requestNo.Length;

        var exact = Repeat('k', FinishedGoodsReceiptInventoryPostingKey.ColumnMaxLength - readableLength - 1);
        var atLimit = FinishedGoodsReceiptInventoryPostingKey.BuildRetry(organizationId, environmentId, requestNo, exact);
        Assert.Equal(FinishedGoodsReceiptInventoryPostingKey.ColumnMaxLength, atLimit.Length);
        Assert.EndsWith(":" + exact, atLimit, StringComparison.Ordinal);

        var overflowing = FinishedGoodsReceiptInventoryPostingKey.BuildRetry(
            organizationId,
            environmentId,
            requestNo,
            exact + "k");
        Assert.DoesNotContain(exact + "k", overflowing, StringComparison.Ordinal);
        Assert.True(overflowing.Length <= FinishedGoodsReceiptInventoryPostingKey.ColumnMaxLength);
    }

    /// <summary>
    /// **跨分支不别名**：逐字出口与回落出口永不产出同一个键。
    /// </summary>
    /// <remarks>
    /// <para>夹具就是 PR #3340 审核构造出来的那个反例，**必须是对抗性的**才有鉴别力：
    /// <c>K1 = 摘要(K2)</c> 且 <c>K1</c> 短到能走逐字出口。本用例先把这个前提断言出来，
    /// 否则它会退化成两把随便的键之间的比较。</para>
    /// <para><b>它必须真的跨分支。</b>上一版这条用例两次调用都用 100 字符的组织 id、
    /// 两次都落在回落分支，于是它证的只是「回落分支内部长度互斥」，跨分支这一面零覆盖，
    /// 名字比它证到的东西大。这里用**短作用域**，让 <c>K1</c> 确实走逐字出口。</para>
    /// </remarks>
    [Fact]
    public void Verbatim_and_fallback_forms_never_alias_across_branches()
    {
        const string organizationId = "org-001";
        const string environmentId = "env-dev";
        const string requestNo = "FGR-1";

        var overlongKey = Repeat('B', 80);
        var digestOfOverlongKey = Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(overlongKey)));

        // 夹具是对抗性的：短键恰好等于长键的摘要。
        Assert.Equal(FinishedGoodsReceiptInventoryPostingKey.DigestLength, digestOfOverlongKey.Length);

        var viaVerbatim = FinishedGoodsReceiptInventoryPostingKey.BuildRetry(
            organizationId, environmentId, requestNo, digestOfOverlongKey);
        var viaFallback = FinishedGoodsReceiptInventoryPostingKey.BuildRetry(
            organizationId, environmentId, requestNo, overlongKey);

        // 左边确实走逐字出口（原始键逐字出现在结果里），右边确实走回落出口（原文一个字节都不在）。
        Assert.Equal(
            $"{FinishedGoodsReceiptInventoryPostingKey.Prefix}{organizationId}:{environmentId}:{requestNo}:{digestOfOverlongKey}",
            viaVerbatim);
        Assert.DoesNotContain(overlongKey, viaFallback, StringComparison.Ordinal);

        // 改动前（只摘尾段、作用域段保持可读）两者会落成同一个字符串，这就是那条阻断。
        Assert.NotEqual(viaVerbatim, viaFallback);
    }

    /// <summary>
    /// 判别位在调用方控制之外：逐字形态在前缀之后**必含** <c>:</c>（<c>Readable</c> 格式串里的字面量，
    /// 无条件存在），回落形态在前缀之后**恒不含**（两段都是 base64url，段间用 <c>.</c>，
    /// 且不含任何调用方原文字节）。
    /// </summary>
    /// <remarks>
    /// <para>夹具刻意让调用方往原始键里塞 <c>:</c>、<c>.</c>，以及塞一个**长得像回落形态**的串——
    /// 都伪造不出「前缀之后无 <c>:</c>」，因为作用域段那两个 <c>:</c> 不归调用方管
    /// （它们来自格式串字面量，而三段取值来自聚合，不是调用方逐请求可控的输入）。</para>
    ///
    /// <para><b>⚠️ 本用例是两个半边的唯一单变量守门人，不得当冗余删掉。</b>
    /// 「回落形态不与逐字形态别名」这条性质由**两个半边**共同保证，而它们对该性质是**冗余的**：
    /// <list type="number">
    /// <item>半 1 —— 回落时作用域段**恒摘要**（⇒ 前缀后冒号数 1 vs ≥3，靠**计数**互斥）；</item>
    /// <item>半 2 —— 回落段间分隔符取 <see cref="FinishedGoodsReceiptInventoryPostingKey.FallbackSeparator"/>
    /// 而非 <see cref="FinishedGoodsReceiptInventoryPostingKey.ReadableSeparator"/>
    /// （⇒ 前缀后冒号数 0 vs ≥2，靠**字符**互斥）。</item>
    /// </list>
    /// 任一半单独存在都足以阻止别名 ⇒ **单侧回退不会让
    /// <see cref="Verbatim_and_fallback_forms_never_alias_across_branches"/> 报红**
    /// （实测：只回退半 1、或只回退半 2，那条用例都是绿的；只有两半一起回退才红）。
    /// 那时**唯一会红的就是本用例**——它断言的是判别位的**布尔性**本身
    /// （「携带调用方原文 ⇔ 前缀之后有 <c>:</c>」），半 1 与半 2 各自单独回退都能把它打红。</para>
    ///
    /// <para>⇒ 删掉本用例不会有任何门禁变红，但会让「两半各自还在不在」失去唯一的机器判据。
    /// 本仓判例「相邻同型守卫会兜住变异」的反面用法：**冗余本身不是问题，不知道自己冗余才是。**</para>
    /// </remarks>
    [Theory]
    [InlineData("plain")]
    [InlineData("a:b:c")]
    [InlineData("a.b.c")]
    [InlineData("mes:finished-goods-receipt:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB")]
    public void Caller_cannot_forge_the_form_discriminator(string callerKey)
    {
        const string organizationId = "org-001";
        const string environmentId = "env-dev";
        const string requestNo = "FGR-1";

        var key = FinishedGoodsReceiptInventoryPostingKey.BuildRetry(organizationId, environmentId, requestNo, callerKey);
        var afterPrefix = key[FinishedGoodsReceiptInventoryPostingKey.Prefix.Length..];
        var carriesCallerBytes = key.Contains(callerKey, StringComparison.Ordinal);

        // 走逐字出口 ⇔ 前缀之后有 ':'；走回落出口 ⇔ 前缀之后没有 ':'。两者必居其一且互斥。
        Assert.Equal(
            carriesCallerBytes,
            afterPrefix.Contains(FinishedGoodsReceiptInventoryPostingKey.ReadableSeparator));
        Assert.True(FinishedGoodsReceiptInventoryPostingKey.BelongsToScope(
            organizationId, environmentId, requestNo, key));
    }

    /// <summary>
    /// 字面前缀在每一种形态里都在。三个服务（Inventory 授权解析、Mes 失败回写路由、Erp 成本归集）
    /// 都按它 <c>StartsWith</c>，尤其 Inventory 那处丢了前缀会退化成 <c>NotRequired()</c>。
    /// </summary>
    [Fact]
    public void Every_form_keeps_the_cross_service_literal_prefix()
    {
        foreach (var key in AllFourForms())
        {
            Assert.StartsWith(FinishedGoodsReceiptInventoryPostingKey.Prefix, key, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 四种形态两两不同，且都装得进承载列。四种形态就是本类型的全部值域。
    /// </summary>
    [Fact]
    public void The_four_forms_are_pairwise_distinct_and_bounded()
    {
        var forms = AllFourForms().ToArray();

        Assert.Equal(4, forms.Length);
        Assert.Equal(forms.Length, forms.Distinct(StringComparer.Ordinal).Count());
        Assert.All(forms, key =>
            Assert.True(key.Length <= FinishedGoodsReceiptInventoryPostingKey.ColumnMaxLength, key));
    }

    /// <summary>
    /// 作用域守卫不降级：四种形态全部由 <c>(org, env, requestNo)</c> 重算后被接受。
    /// </summary>
    [Fact]
    public void Scope_guard_recognises_every_form_of_its_own_receipt()
    {
        Assert.True(FinishedGoodsReceiptInventoryPostingKey.BelongsToScope(
            "org-001", "env-dev", "FGR-001",
            FinishedGoodsReceiptInventoryPostingKey.Build("org-001", "env-dev", "FGR-001")));
        Assert.True(FinishedGoodsReceiptInventoryPostingKey.BelongsToScope(
            "org-001", "env-dev", "FGR-001",
            FinishedGoodsReceiptInventoryPostingKey.BuildRetry("org-001", "env-dev", "FGR-001", "retry-1")));

        // 作用域短、只有尾段超界：新形态下作用域段也一起摘要，守卫必须跟着认。
        Assert.True(FinishedGoodsReceiptInventoryPostingKey.BelongsToScope(
            "org-001", "env-dev", "FGR-001",
            FinishedGoodsReceiptInventoryPostingKey.BuildRetry("org-001", "env-dev", "FGR-001", Repeat('k', 300))));

        var longOrg = Repeat('o', 100);
        var longEnv = Repeat('e', 100);
        var longNo = Repeat('r', 100);
        Assert.True(FinishedGoodsReceiptInventoryPostingKey.BelongsToScope(
            longOrg, longEnv, longNo,
            FinishedGoodsReceiptInventoryPostingKey.Build(longOrg, longEnv, longNo)));
        Assert.True(FinishedGoodsReceiptInventoryPostingKey.BelongsToScope(
            longOrg, longEnv, longNo,
            FinishedGoodsReceiptInventoryPostingKey.BuildRetry(longOrg, longEnv, longNo, Repeat('k', 300))));
    }

    /// <summary>
    /// 守卫仍然拒绝别人家的键——包括**摘要形态**的别人家键。
    /// 这一条是「重算」与「按形状放行」的分界：若守卫改成认长度/认字符集，本条即绿而缺陷已发生。
    /// </summary>
    [Fact]
    public void Scope_guard_rejects_another_receipts_key_in_both_forms()
    {
        Assert.False(FinishedGoodsReceiptInventoryPostingKey.BelongsToScope(
            "org-001", "env-dev", "FGR-001",
            FinishedGoodsReceiptInventoryPostingKey.Build("org-001", "env-dev", "FGR-002")));

        var longEnv = Repeat('e', 100);
        var mine = FinishedGoodsReceiptInventoryPostingKey.Build("org-001", longEnv, "FGR-001");
        var theirs = FinishedGoodsReceiptInventoryPostingKey.Build("org-001", longEnv, "FGR-002");
        Assert.NotEqual(mine, theirs);
        Assert.Equal(FinishedGoodsReceiptInventoryPostingKey.DigestedScopeLength, theirs.Length);
        Assert.False(FinishedGoodsReceiptInventoryPostingKey.BelongsToScope("org-001", longEnv, "FGR-001", theirs));

        // 别人家的**回落重投键**同样被拒（作用域摘要不同）。
        Assert.False(FinishedGoodsReceiptInventoryPostingKey.BelongsToScope(
            "org-001", longEnv, "FGR-001",
            FinishedGoodsReceiptInventoryPostingKey.BuildRetry("org-001", longEnv, "FGR-002", Repeat('k', 300))));
    }

    /// <summary>
    /// 回落是确定性且注入的：同输入同输出、异输入异输出。幂等语义不因回落而折叠。
    /// </summary>
    /// <remarks>本条只覆盖**回落分支内部**；跨分支那一面由
    /// <see cref="Verbatim_and_fallback_forms_never_alias_across_branches"/> 承担，两者不合并计数。</remarks>
    [Fact]
    public void Fallback_is_deterministic_and_injective()
    {
        var longOrg = Repeat('o', 100);
        var first = FinishedGoodsReceiptInventoryPostingKey.BuildRetry(longOrg, "env-dev", "FGR-001", Repeat('a', 300));
        var again = FinishedGoodsReceiptInventoryPostingKey.BuildRetry(longOrg, "env-dev", "FGR-001", Repeat('a', 300));
        var other = FinishedGoodsReceiptInventoryPostingKey.BuildRetry(longOrg, "env-dev", "FGR-001", Repeat('a', 299) + "b");

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
    }

    /// <summary>
    /// 聚合的两个发出点（首次过账与重投）都走同一个构造，因此都受上界约束。
    /// 光测构造函数不够：缺陷是在聚合发出的领域事件上打出来的。
    /// </summary>
    [Fact]
    public void Aggregate_emitted_keys_stay_inside_the_downstream_column()
    {
        var organizationId = Repeat('o', 100);
        var environmentId = Repeat('e', 100);
        var requestNo = Repeat('r', 100);

        var receipt = FinishedGoodsReceiptRequest.Create(
            organizationId, environmentId, requestNo, "WO-001", "FG-001", 8m, "ea", DateTimeOffset.UtcNow, unitCost: 12m);
        var created = Assert.IsType<FinishedGoodsReceiptRequestedDomainEvent>(Assert.Single(receipt.GetDomainEvents()));
        Assert.True(
            created.IdempotencyKey.Length <= FinishedGoodsReceiptInventoryPostingKey.ColumnMaxLength,
            $"首次过账键长度 {created.IdempotencyKey.Length}：{created.IdempotencyKey}");

        receipt.MarkInventoryPostingFailed("posting-rejected", "库存过账被拒。", DateTimeOffset.UtcNow);
        receipt.RetryInventoryPosting(Repeat('k', 200));
        var retried = receipt.GetDomainEvents()
            .OfType<FinishedGoodsReceiptRequestedDomainEvent>()
            .Last();
        Assert.True(
            retried.IdempotencyKey.Length <= FinishedGoodsReceiptInventoryPostingKey.ColumnMaxLength,
            $"重投键长度 {retried.IdempotencyKey.Length}：{retried.IdempotencyKey}");
        Assert.True(FinishedGoodsReceiptRequest.IsInventoryPostingIdempotencyKey(
            organizationId, environmentId, requestNo, retried.IdempotencyKey));
    }

    private static IEnumerable<string> AllFourForms()
    {
        var longOrg = Repeat('o', 100);
        yield return FinishedGoodsReceiptInventoryPostingKey.Build("org-001", "env-dev", "FGR-001");
        yield return FinishedGoodsReceiptInventoryPostingKey.BuildRetry("org-001", "env-dev", "FGR-001", "retry-1");
        yield return FinishedGoodsReceiptInventoryPostingKey.Build(longOrg, "env-dev", "FGR-001");
        yield return FinishedGoodsReceiptInventoryPostingKey.BuildRetry(longOrg, "env-dev", "FGR-001", Repeat('k', 300));
    }

    private static string Repeat(char value, int count) => new(value, count);
}
