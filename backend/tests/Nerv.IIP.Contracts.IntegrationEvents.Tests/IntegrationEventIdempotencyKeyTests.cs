using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.IntegrationEvents.Tests;

/// <summary>
/// <see cref="IntegrationEventIdempotencyKey"/> 的行为契约（#3339）。
/// </summary>
/// <remarks>
/// <para>四条被证的性质，逐条对应票面验收：</para>
/// <list type="number">
/// <item><b>预算内逐字保持</b>：产出与改动前的纯拼接**逐字相同**（存量键查得到）。</item>
/// <item><b>超预算回落有界</b>：产出长度恒为 <c>前缀 + 摘要</c>，与输入多长无关。</item>
/// <item><b>两形态互斥且不可伪造</b>：逐字形态在前缀后恒含 <c>:</c>、回落形态恒不含。
/// 这里**复现了 PR #3340 抓出的那条构造**并证明它在本处走不通。</item>
/// <item><b>判别式的前提被守住</b>：尾段少于 2 段一律抛，不静默降级成「按长度互斥」。</item>
/// </list>
/// <para><b>本类不证明什么</b>：不证明不同前缀之间不撞、不证明逐字形态内部分段单射——
/// 那两条是纯拼接键的**既有**性质，本票未引入也未消除（见被测类型的值域边界注释）。</para>
/// </remarks>
public sealed class IntegrationEventIdempotencyKeyTests
{
    private const string Prefix = "wms:wcs-retry-exhausted:";

    /// <summary>
    /// 存量键逐字保持：预算内的产出必须与**改动前的表达式**一字不差。
    /// 期望值在本类里**独立拼一遍**（不调用被测类型），否则会退化成同义反复。
    /// </summary>
    [Theory]
    [InlineData("org-001", "env-dev", "x")]
    [InlineData("", "", "")]
    [InlineData("org:with:colons", "env", "tail:with:colons")]
    public void Within_budget_keys_are_preserved_byte_for_byte(string a, string b, string c)
    {
        var expected = $"{Prefix}{a}:{b}:{c}";

        Assert.Equal(expected, IntegrationEventIdempotencyKey.Compose(Prefix, a, b, c));
    }

    /// <summary>
    /// <c>ComposeServiceScoped</c> 与改动前各服务那份
    /// <c>$"{service}:{string.Join(':', parts)}"</c> 逐字相同。
    /// </summary>
    [Fact]
    public void Service_scoped_compose_matches_the_pre_change_join_expression()
    {
        string[] parts = ["stock-movement-posted", "org-001", "env-dev", "wms", "DOC-001", "idem-in-001"];

        Assert.Equal(
            $"inventory:{string.Join(':', parts)}",
            IntegrationEventIdempotencyKey.ComposeServiceScoped("inventory:", parts));
    }

    /// <summary>边界：恰好等于预算的键仍然逐字保持（回落条件是**严格超过**）。</summary>
    [Fact]
    public void A_key_exactly_at_the_budget_is_still_preserved_verbatim()
    {
        var tail = new string('t', IntegrationEventIdempotencyKey.Budget - Prefix.Length - 1);
        var key = IntegrationEventIdempotencyKey.Compose(Prefix, tail, string.Empty);

        Assert.Equal(IntegrationEventIdempotencyKey.Budget, key.Length);
        Assert.Equal($"{Prefix}{tail}:", key);
    }

    [Fact]
    public void Over_budget_keys_fall_back_to_a_bounded_digest_of_the_whole_readable_key()
    {
        var tail = new string('t', IntegrationEventIdempotencyKey.Budget * 3);
        var readable = $"{Prefix}{tail}:x";

        var key = IntegrationEventIdempotencyKey.Compose(Prefix, tail, "x");

        Assert.Equal(Prefix + Base64UrlDigest(readable), key);
        Assert.Equal(Prefix.Length + IntegrationEventIdempotencyKey.DigestLength, key.Length);
        Assert.True(key.Length <= IntegrationEventIdempotencyKey.Budget);
    }

    /// <summary>
    /// 回落形态的长度**与输入长度无关**——这条挡住「回落里还留了一段原文」这类改法。
    /// </summary>
    [Fact]
    public void Fallback_length_is_independent_of_how_long_the_input_was()
    {
        var lengths = new[] { 1, 5, 50, 500 }
            .Select(multiplier => IntegrationEventIdempotencyKey
                .Compose(Prefix, new string('t', IntegrationEventIdempotencyKey.Budget * multiplier), "x")
                .Length)
            .Distinct()
            .ToArray();

        Assert.Single(lengths);
    }

    /// <summary>
    /// ⭐ PR #3340 抓出的那条别名构造，在本处走不通。
    /// <para>构造：取任意超界键 <c>K2</c>，算出它的回落产出 <c>out2</c>（谁都能算），
    /// 再试图让另一把键**走逐字出口**产出同一个 <c>out2</c>。
    /// 只要尾段 &gt;= 2，<c>string.Join</c> 一定在前缀后写下一个 <c>:</c>，
    /// 而 <c>out2</c> 在前缀后是 base64url（不含 <c>:</c>）⇒ 两者恒不相等。</para>
    /// </summary>
    [Fact]
    public void A_digest_shaped_value_cannot_be_forged_through_the_verbatim_branch()
    {
        var over = new string('z', IntegrationEventIdempotencyKey.Budget + 1);
        var out2 = IntegrationEventIdempotencyKey.Compose(Prefix, over, "x");
        var digestSegment = out2[Prefix.Length..];

        Assert.True(IntegrationEventIdempotencyKey.IsDigested(Prefix, out2));

        // 攻击者把摘要段整段塞进尾段，仍然拿不到同一把键：join 的那个 ':' 拿不掉。
        foreach (var forged in new[]
        {
            IntegrationEventIdempotencyKey.Compose(Prefix, digestSegment, string.Empty),
            IntegrationEventIdempotencyKey.Compose(Prefix, string.Empty, digestSegment),
            IntegrationEventIdempotencyKey.Compose(Prefix, digestSegment, digestSegment),
        })
        {
            Assert.NotEqual(out2, forged);
            Assert.False(IntegrationEventIdempotencyKey.IsDigested(Prefix, forged));
        }
    }

    /// <summary>
    /// 判别式的结构性读数：**任何**预算内输入（含全空段、含冒号段）在前缀之后都至少有一个
    /// <see cref="IntegrationEventIdempotencyKey.ReadableSeparator"/>；回落形态一个都没有。
    /// </summary>
    [Theory]
    [InlineData("", "")]
    [InlineData("a", "b")]
    [InlineData("::", "::")]
    [InlineData("-_", "AZaz09")]
    public void The_two_shapes_are_disjoint_by_character_class(string a, string b)
    {
        var verbatim = IntegrationEventIdempotencyKey.Compose(Prefix, a, b);
        var fallback = IntegrationEventIdempotencyKey.Compose(
            Prefix,
            new string('z', IntegrationEventIdempotencyKey.Budget + 1),
            b);

        Assert.Contains(IntegrationEventIdempotencyKey.ReadableSeparator, verbatim[Prefix.Length..]);
        Assert.DoesNotContain(IntegrationEventIdempotencyKey.ReadableSeparator, fallback[Prefix.Length..]);
    }

    /// <summary>
    /// 两把不同的超界键不会折叠成同一把（否则就是 <c>InventoryIdempotencyKeyPolicy.Compose</c>
    /// 注释里写死禁止的那件事）。
    /// </summary>
    [Fact]
    public void Two_distinct_over_budget_inputs_do_not_collapse_into_one_key()
    {
        var baseTail = new string('z', IntegrationEventIdempotencyKey.Budget + 1);

        var first = IntegrationEventIdempotencyKey.Compose(Prefix, baseTail, "a");
        var second = IntegrationEventIdempotencyKey.Compose(Prefix, baseTail, "b");
        var thirdDiffersOnlyInTheLastByte = IntegrationEventIdempotencyKey.Compose(Prefix, baseTail + "0", "a");

        Assert.NotEqual(first, second);
        Assert.NotEqual(first, thirdDiffersOnlyInTheLastByte);
        Assert.NotEqual(second, thirdDiffersOnlyInTheLastByte);
    }

    /// <summary>null 段按空串处理，与改动前 <c>string.Join</c> 的行为一致。</summary>
    [Fact]
    public void Null_tail_parts_behave_like_empty_strings()
    {
        Assert.Equal(
            IntegrationEventIdempotencyKey.Compose(Prefix, string.Empty, "x"),
            IntegrationEventIdempotencyKey.Compose(Prefix, null, "x"));
    }

    /// <summary>
    /// 判别式的前提被守住：少于 <see cref="IntegrationEventIdempotencyKey.MinimumTailParts"/> 段一律抛。
    /// **不静默降级**——静默降级会让那一条出口退回「按长度互斥」，也就是 #3340 修掉的缺陷。
    /// </summary>
    [Fact]
    public void Fewer_tail_parts_than_the_discriminator_needs_throws()
    {
        Assert.Throws<ArgumentException>(() => IntegrationEventIdempotencyKey.Compose(Prefix, "only-one"));
        Assert.Throws<ArgumentException>(() => IntegrationEventIdempotencyKey.Compose(Prefix));
        Assert.Throws<ArgumentException>(
            () => IntegrationEventIdempotencyKey.ComposeServiceScoped("wms:", "kind", "only-one-tail"));
    }

    [Fact]
    public void A_prefix_that_does_not_end_with_the_readable_separator_throws()
    {
        Assert.Throws<ArgumentException>(() => IntegrationEventIdempotencyKey.Compose("wms:wcs-failed", "a", "b"));
    }

    /// <summary>
    /// 前缀长到放不下摘要时就地抛：否则回落形态自己就超预算，
    /// 那是「护栏产出越界值」而不是「护栏挡住越界值」。
    /// </summary>
    [Fact]
    public void A_prefix_too_long_to_leave_room_for_the_digest_throws()
    {
        var tooLong = new string('p', IntegrationEventIdempotencyKey.MaxPrefixLength) + ':';

        Assert.Throws<ArgumentException>(() => IntegrationEventIdempotencyKey.Compose(tooLong, "a", "b"));
    }

    /// <summary>
    /// <see cref="IntegrationEventIdempotencyKey.MaxPrefixLength"/> 与
    /// <see cref="IntegrationEventIdempotencyKey.DigestLength"/> 都是**派生量**，
    /// 这条把派生关系本身钉住（不手抄 43 / 469）。
    /// </summary>
    [Fact]
    public void Derived_lengths_are_derived_not_hand_copied()
    {
        Assert.Equal(
            Base64Url.GetEncodedLength(SHA256.HashSizeInBytes),
            IntegrationEventIdempotencyKey.DigestLength);
        Assert.Equal(
            IntegrationEventIdempotencyKey.Budget - IntegrationEventIdempotencyKey.DigestLength,
            IntegrationEventIdempotencyKey.MaxPrefixLength);
    }

    private static string Base64UrlDigest(string value) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
