using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Nerv.IIP.Business.Erp.Domain;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Coding;

namespace Nerv.IIP.Business.Erp.Web.Tests;

/// <summary>
/// GitHub #3278 / S7：消费侧凭证号分配器**幂等键**的物理上界与唯一性。
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>这不是形式主义，是被算出来的</b>：<c>code_idempotency_keys.idempotency_key</c> 列宽 150，
/// 而 <c>journal_vouchers.source_no</c> 列宽 150、<c>source_type</c> 列宽 32。
/// 「<c>{类型}:{单号}</c>」这种可读拼法的上界是 183 &gt; 150，顶格来源单号落库就是 PostgreSQL <c>22001</c>——
/// 与 #3229 同形。本类把上界从**两侧 EF 模型**读出来对撞，任一侧单边加宽/收窄即红。
/// </para>
/// <para>
/// <b>本类不证明什么</b>：⛔ 不证明「所有凭证号都走分配器」（那需要源码扫描，owner 已在 #3231 裁定不再建）；
/// ⛔ 不证明分配器产出的号在库里唯一（那是 <c>(org, env, voucher_no)</c> 唯一索引的事，真库 lane 才看得见）。
/// </para>
/// </remarks>
public sealed class ConsumerJournalVoucherNumberKeyContractTests
{
    /// <summary>
    /// 逐族（<see cref="JournalVoucherSourceType.All"/> 闭集）跑**顶格**来源单号，
    /// 断言幂等键仍塞得进 <c>idempotency_key</c> 列。两个列宽都从 EF 模型读，⛔ 不手抄。
    /// </summary>
    [Fact]
    public void Idempotency_key_fits_the_column_for_every_registered_source_type_at_a_saturated_source_no()
    {
        var sourceNoWidth = MaxLengthOf<JournalVoucher>(nameof(JournalVoucher.SourceNo));
        var sourceTypeWidth = MaxLengthOf<JournalVoucher>(nameof(JournalVoucher.SourceType));
        var keyWidth = MaxLengthOf<CodeIdempotencyKey>(nameof(CodeIdempotencyKey.IdempotencyKey));
        var saturatedSourceNo = new string('S', sourceNoWidth);

        // 前提读数：可读拼法确实越界——否则下面那条「摘要式塞得下」就没有承重对象。
        Assert.True(sourceTypeWidth + 1 + sourceNoWidth > keyWidth);
        Assert.Equal(12, JournalVoucherSourceType.All.Count);

        foreach (var sourceType in JournalVoucherSourceType.All)
        {
            var key = ConsumerJournalVoucherNumber.IdempotencyKeyOf(sourceType, saturatedSourceNo);
            Assert.True(
                key.Length <= keyWidth,
                $"Source type '{sourceType.Code}' produced a {key.Length}-char idempotency key; column holds {keyWidth}.");
            Assert.StartsWith(ConsumerJournalVoucherNumber.KeyPrefix + sourceType.Code + ":", key, StringComparison.Ordinal);
        }

        // 类型上界：族码顶格（列宽 32）时也塞得下。这一格与上面的逐族枚举不同轴——
        // 逐族跑的是**今天登记的**码值，这一格跑的是**列允许的**最宽码值。
        Assert.True(
            ConsumerJournalVoucherNumber.KeyPrefix.Length + sourceTypeWidth + 1 + ConsumerJournalVoucherNumber.DigestLength <= keyWidth);
    }

    /// <summary>
    /// 键长与来源单号长度**无关**——这正是摘要式取代可读拼法的理由。
    /// ⛔ 这条不能换成「键长 &lt;= 150」：那条在单号只有一位时也成立，鉴别不了截断式与摘要式。
    /// </summary>
    [Fact]
    public void Idempotency_key_length_does_not_grow_with_the_source_no()
    {
        var sourceType = JournalVoucherSourceType.WorkOrderCostAdjustment;
        var shortKey = ConsumerJournalVoucherNumber.IdempotencyKeyOf(sourceType, "X");
        var longKey = ConsumerJournalVoucherNumber.IdempotencyKeyOf(sourceType, new string('X', 150));

        Assert.Equal(shortKey.Length, longKey.Length);
        Assert.NotEqual(shortKey, longKey);
        Assert.Equal(
            ConsumerJournalVoucherNumber.KeyPrefix.Length + sourceType.Code.Length + 1 + ConsumerJournalVoucherNumber.DigestLength,
            shortKey.Length);
    }

    /// <summary>
    /// 摘要输入是带长度前缀的规范串，所以「段划分不同但拼起来一样」的两组输入不会塌成同一个键。
    /// </summary>
    [Fact]
    public void Segment_split_does_not_collapse_two_different_sources_onto_one_key()
    {
        // 裸拼 "{类型码}{单号}" 时这两组完全相同："WOC"+"ADJ-1" 与 "WOCADJ"+"-1"。
        var left = ConsumerJournalVoucherNumber.CanonicalKey(JournalVoucherSourceType.WorkOrderCapitalization, "ADJ-1");
        var right = ConsumerJournalVoucherNumber.CanonicalKey(JournalVoucherSourceType.WorkOrderCostAdjustment, "-1");
        Assert.Equal(
            JournalVoucherSourceType.WorkOrderCapitalization.Code + "ADJ-1",
            JournalVoucherSourceType.WorkOrderCostAdjustment.Code + "-1");
        Assert.NotEqual(left, right);
        Assert.NotEqual(
            ConsumerJournalVoucherNumber.IdempotencyKeyOf(JournalVoucherSourceType.WorkOrderCapitalization, "ADJ-1"),
            ConsumerJournalVoucherNumber.IdempotencyKeyOf(JournalVoucherSourceType.WorkOrderCostAdjustment, "-1"));
    }

    /// <summary>
    /// 全部已登记族 × 两个来源单号 ⇒ 键两两互异。
    /// 少了这条，「键里只写摘要、丢掉类型段」这种改法会让不同族的同号来源塌成一个键而全绿。
    /// </summary>
    [Fact]
    public void Keys_are_distinct_across_every_registered_source_type()
    {
        var keys = JournalVoucherSourceType.All
            .SelectMany(sourceType => new[] { "SRC-0001", "SRC-0002" }
                .Select(sourceNo => ConsumerJournalVoucherNumber.IdempotencyKeyOf(sourceType, sourceNo)))
            .ToArray();

        Assert.Equal(JournalVoucherSourceType.All.Count * 2, keys.Length);
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// 摘要的**输入成分**由冻结黄金向量钉死：hex 由外部独立实现（Python <c>hashlib</c>）算出后硬编码，
    /// ⛔ 不是先用 <see cref="ConsumerJournalVoucherNumber.Digest"/> 求值再用同一个 <c>Digest</c> 复算。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>这一条打的轴是「摘要输入里有什么」，与本类其余各条（形状 / 长度 / 互异）都不同轴。</b>
    /// 复审实测：本 PR 首轮那 17 格变异**没有一格**打在这条轴上——往
    /// <see cref="ConsumerJournalVoucherNumber.CanonicalKey"/> 里掺一个
    /// <c>DateTime.UtcNow:yyyyMMdd</c>，<c>Erp.Web.Tests</c> + 真 PostgreSQL **零红**、
    /// <c>FullChain</c> + 真 PostgreSQL **零红**，全格存活。
    /// </para>
    /// <para>
    /// ⚠️ <b>为什么「键必须与时间无关」是承重的</b>：<c>journal-voucher</c> 走
    /// <c>StandardCodeRules.Document</c>，段里含 <c>DateOf("yyyyMMdd")</c> + <c>SequenceOf(6, ResetPeriod.Day)</c>。
    /// CAP 在 23:59 投递、次日 00:01 重投时，键若带任何时间成分就会被当成**新键**重新分号。
    /// 后果分级：位点 ① 只是白烧一个号（它前面还有来源两列查重早退）；
    /// ②③④⑤ 会拿着新号去写第二张凭证 ⇒ 撞 S5 那条来源唯一索引 ⇒
    /// <c>DbUpdateException</c> 从调用方 UoW 抛出、在 <see cref="ConsumerJournalVoucherNumber.TryAllocateAsync"/>
    /// 的 <c>try</c> 之外 ⇒ 逃逸成 poison message（#877）。
    /// </para>
    /// <para>
    /// <b>失效方向</b>：黄金向量只钉住这 5 组输入对应的输出。换掉哈希算法、改规范串分隔符、
    /// 改长度前缀写法都会红；但**新增**一个族而不补向量，本条不会红
    /// （族的枚举完备性由 <see cref="Keys_are_distinct_across_every_registered_source_type"/>
    /// 与 <c>JournalVoucherSourceContractTests</c> 的反射对撞承担，不由本条承担）。
    /// </para>
    /// </remarks>
    [Theory]
    // 生成方式（可复算）：canonical = $"{code.Length}\u001F{code}\u001F{no.Length}\u001F{no}"，
    // 再取 SHA-256 的大写十六进制。以下 hex 由 Python hashlib 独立算出。
    [InlineData("GRIR", "RCV-AXIS-0001", "781974319F5A8BC52F765C30F174B2A630D00291345518DB0875EC13317A207E")]
    [InlineData("PRTN", "PRTN-AXIS-0001", "AF8CDE74F0AA275221EB58AAC78E33DC34356679E268CF63291D0557FF015D2A")]
    [InlineData("CN", "CN-AXIS-0001", "C0580D6CC0EB5129A22B7E89CF3FB4D9912E93BC298E4BF34BA3CF5722662CB0")]
    [InlineData("WOC", "MOVE-AXIS-0001", "91AB3163C4268995966ED1AFAA86A1C49CA2B232B80B34EBCF5860DD968401BF")]
    [InlineData("WOCADJ", "RPT-AXIS-0001", "56A0D3832F7556678501F57953B4CC44817DB0C14B01D8D18F5432921898DC9F")]
    public void Digest_input_is_frozen_by_golden_vectors(string sourceTypeCode, string sourceNo, string expectedDigest)
    {
        var sourceType = Assert.Single(JournalVoucherSourceType.All, x => x.Code == sourceTypeCode);

        Assert.Equal(expectedDigest, ConsumerJournalVoucherNumber.Digest(sourceType, sourceNo));
        Assert.Equal(
            $"{ConsumerJournalVoucherNumber.KeyPrefix}{sourceTypeCode}:{expectedDigest}",
            ConsumerJournalVoucherNumber.IdempotencyKeyOf(sourceType, sourceNo));
    }

    /// <summary>
    /// 同一来源在**两个不同时刻**取到的键逐字节相同——这是上面那条轴的行为侧对照：
    /// 黄金向量钉的是「输入是哪几段」，这一条钉的是「输入里没有随调用时刻变化的东西」。
    /// ⛔ 不用假时钟：本方法根本不接受 <c>TimeProvider</c>，掺进来的任何时间源都是真实时钟，
    /// 用例只要跨一次真实时间推进后重算即可。
    /// </summary>
    [Fact]
    public async Task Idempotency_key_does_not_change_between_two_moments_in_time()
    {
        var first = ConsumerJournalVoucherNumber.IdempotencyKeyOf(
            JournalVoucherSourceType.PurchaseReturn, "PRTN-AXIS-0001");
        await Task.Delay(TimeSpan.FromMilliseconds(30), CancellationToken.None);
        var second = ConsumerJournalVoucherNumber.IdempotencyKeyOf(
            JournalVoucherSourceType.PurchaseReturn, "PRTN-AXIS-0001");

        Assert.Equal(first, second);
    }

    private static int MaxLengthOf<TEntity>(string propertyName)
        where TEntity : class
    {
        using var dbContext = CreateModelOnlyDbContext();
        return dbContext.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(TEntity))!
            .FindProperty(propertyName)!
            .GetMaxLength()!.Value;
    }

    private static ApplicationDbContext CreateModelOnlyDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=nerv_iip_consumer_voucher_key_contract;Username=nerv;Password=nerv",
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ErpFacts.Schema))
            .Options;
        return new ApplicationDbContext(options, new KeyContractNoopMediator());
    }

    private sealed class KeyContractNoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => Task.FromResult<TResponse>(default!);

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => Task.CompletedTask;

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => Task.FromResult<object?>(null);

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
