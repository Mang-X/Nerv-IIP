using System.Reflection;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Nerv.IIP.Business.Erp.Domain;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Erp.Web.Tests;

/// <summary>
/// GitHub #3278 / S2：<c>journal_vouchers</c> 的来源单据两列（<c>source_type</c> + <c>source_no</c>）。
///
/// 本票的承重风险**不是**「列建得对不对」，而是「17 个建凭证位点漏填一处，
/// 该行来源列恒空而所有门禁照绿」（S2 不建唯一索引也不建非空约束，没有任何约束会因此变红）。
/// 因此防漏是**结构性**的，不是断言性的：<see cref="JournalVoucher.Post"/> 是唯一构造入口
/// （构造函数私有），而它的来源两参数**没有默认值** ⇒ 漏填在编译期就是 CS7036。
///
/// 这个类只负责看住那个结构不被悄悄拆掉，以及列的形状：
/// <list type="number">
/// <item><see cref="Every_public_factory_of_a_journal_voucher_demands_the_source_document"/>：
///   加一个带默认值的 <c>Post</c> 重载就会让编译期保证失效——这条反射断言看住这个方向；</item>
/// <item><see cref="Source_columns_are_nullable_and_wide_enough_for_every_production_source_id"/>：
///   两列必须**可空**（非空会让迁移自身在存量库上失败），且宽到装得下生产侧最宽的来源标识；</item>
/// <item><see cref="Post_rejects_a_blank_source_no"/>：占位空串也不算填了；</item>
/// <item><see cref="All_enumerates_every_declared_source_type_and_codes_stay_distinct"/>：
///   码表是**手工登记表**，类型里声明却不登记编译期不拦，靠这条反射对撞看住。</item>
/// </list>
///
/// <b>值域边界（声明放弃了什么）</b>：
/// 1. 本类**不**证明「所有落库的凭证都经过 <see cref="JournalVoucher.Post"/>」。
///    绕开 EF 的原生 SQL 写入（<c>ExecuteSql</c> / <c>FromSql</c>）不在扫描面内；
///    S2 落地时对 <c>backend/services/Business/Erp/src</c> 实测**代码命中 0**（同名字样另有 1 处，
///    是 <c>JournalVoucher</c> 里自述该次扫描的注释——护栏自指，别当旁路读）。
///    日后新增生产侧原生 SQL 写入会让这个保证**静默**失效。
/// 2. 列宽读的是 **EF 模型**而不是迁移脚本；模型/迁移漂移由「空迁移探针」负责，不由本类负责。
/// 3. 「真表上这两列真的可空、真的读得回来」由 <c>ErpCostAccountingPostgresAcceptanceTests</c>
///    的真 Postgres 用例负责——EF InMemory 既看不见列宽也看不见可空性。
/// 4. ⭐ **本类完全不管「填对」**。编译期闭合只保证每个位点**填了**；把所有位点的 <c>sourceNo</c>
///    一律填成同一个常量，本类全绿（复审实测）。逐族钉住实际取值的是
///    <c>JournalVoucherSourceValueTests</c>。
/// 5. <see cref="JournalVoucher.Post"/> 是唯一**新建**入口，不是唯一写入口：EF 物化走私有无参
///    构造函数，绕开这两个参数——本 PR 那条真库用例里读回来源列为 <see langword="null"/> 的存量行
///    就是证据。
/// </summary>
public sealed class JournalVoucherSourceContractTests
{
    /// <summary>
    /// 结构性防漏的**看门断言**：任何能造出 <see cref="JournalVoucher"/> 的公开入口都必须
    /// 要求来源两参数，且**不得带默认值**。带了默认值，「漏填」就从编译错误退化成静默空值。
    /// </summary>
    [Fact]
    public void Every_public_factory_of_a_journal_voucher_demands_the_source_document()
    {
        Assert.Empty(typeof(JournalVoucher).GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        var factories = typeof(JournalVoucher)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(x => x.ReturnType == typeof(JournalVoucher))
            .ToList();

        Assert.NotEmpty(factories);
        foreach (var factory in factories)
        {
            var parameters = factory.GetParameters();
            var sourceType = Assert.Single(parameters, x => x.ParameterType == typeof(JournalVoucherSourceType));
            var sourceNo = Assert.Single(parameters, x => x.Name == "sourceNo");

            Assert.False(sourceType.IsOptional, $"{factory.Name} 的 {sourceType.Name} 带了默认值，漏填不再是编译错误。");
            Assert.False(sourceNo.IsOptional, $"{factory.Name} 的 {sourceNo.Name} 带了默认值，漏填不再是编译错误。");
            Assert.Equal(typeof(string), sourceNo.ParameterType);
        }
    }

    /// <summary>
    /// 两列**必须可空**：owner 2026-09-14 裁定不回填存量行，非空约束会让本迁移自身在存量库上失败。
    /// 列宽必须 ≥ 生产侧最宽的来源标识——最宽的一支是工单成本迟到调整的 <c>sourceId</c>
    /// （<c>machine-{OperationTaskId}-r{SettlementRevision}-void</c>，见
    /// <c>ErpVoucherNoLengthContractTests</c> 里那条复审更正过的读数），不是上游单号列宽 100。
    /// </summary>
    [Fact]
    public void Source_columns_are_nullable_and_wide_enough_for_every_production_source_id()
    {
        using var dbContext = CreateModelOnlyDbContext();
        var entity = dbContext.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(JournalVoucher))!;
        var sourceType = entity.FindProperty(nameof(JournalVoucher.SourceType))!;
        var sourceNo = entity.FindProperty(nameof(JournalVoucher.SourceNo))!;

        Assert.Equal("source_type", sourceType.GetColumnName());
        Assert.Equal("source_no", sourceNo.GetColumnName());
        Assert.True(sourceType.IsNullable, "source_type 必须可空，否则迁移在存量库上直接失败。");
        Assert.True(sourceNo.IsNullable, "source_no 必须可空，否则迁移在存量库上直接失败。");

        var longestCode = JournalVoucherSourceType.All.Max(x => x.Code.Length);
        Assert.True(
            sourceType.GetMaxLength() >= longestCode,
            $"source_type 列宽 {sourceType.GetMaxLength()} 装不下最长码值 {longestCode}。");
        Assert.True(
            sourceNo.GetMaxLength() >= ErpVoucherNoLengthContractTests.WidestAdjustmentSourceIdWidth,
            $"source_no 列宽 {sourceNo.GetMaxLength()} 装不下最宽来源标识 {ErpVoucherNoLengthContractTests.WidestAdjustmentSourceIdWidth}。");
        Assert.True(
            sourceNo.GetMaxLength() >= entity.FindProperty(nameof(JournalVoucher.VoucherNo))!.GetMaxLength(),
            "手工凭证把凭证号写进 source_no，故 source_no 不得窄于 voucher_no。");
    }

    /// <summary>
    /// #3278 / S5 **改写**了这条断言的方向。
    ///
    /// S2 版本断言的是「现在**还不是**唯一索引」，用意是把 S5 未落地这件事写死，防止有人提前建。
    /// S5 落地后那个前提不存在了——它要证的事已经变了，不是「它碍事」。
    /// 换成断言唯一 + partial filter。
    ///
    /// ⚠️ <b>filter 今天的行为效果是零</b>：存量 NULL 行本来就由 PostgreSQL 默认的
    /// <c>NULLS DISTINCT</c> 放行，去掉 filter 它们照样落得进去（已实测）。写它、并把它当断言钉住，
    /// 是因为那条放行来自 <b>provider 默认值</b>而不是本仓的声明——一条
    /// <c>NULLS NOT DISTINCT</c> 或把列改成 <c>NOT NULL</c> 都会静默改掉它。
    ///
    /// ⭐ <b>钉的是谓词全形，不是子串</b>（#3278 / S5 复审返修）。此前写成
    /// <c>Assert.Contains("source_type IS NOT NULL")</c> 时，把 filter 改成
    /// <c>"… AND source_type &lt;&gt; 'APPAY'"</c>（让整个付款执行族退出幂等约束）
    /// 能穿过全部 30 格断言——子串判据只看「提到了这两列」，看不见后面追加的豁免。
    ///
    /// <b>值域边界</b>：本格读的是 EF 模型，不是数据库。索引在真库上到底建成什么样、
    /// <c>Down()</c> 回不回得干净，由 <c>ErpCostAccountingPostgresAcceptanceTests</c> 里那两格读 <c>pg_index</c> 证——
    /// 那两格钉的是 PostgreSQL 归一化后的 <c>pg_get_expr</c> 全形，与本格钉的原串是两种表示，各自独立。
    /// </summary>
    [Fact]
    public void The_source_document_index_is_unique_and_excludes_legacy_null_rows()
    {
        using var dbContext = CreateModelOnlyDbContext();
        var entity = dbContext.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(JournalVoucher))!;

        var index = Assert.Single(
            entity.GetIndexes(),
            x => x.Properties.Select(p => p.Name).SequenceEqual(
                [nameof(JournalVoucher.OrganizationId), nameof(JournalVoucher.EnvironmentId), nameof(JournalVoucher.SourceType), nameof(JournalVoucher.SourceNo)]));

        Assert.True(index.IsUnique, "来源两列的索引必须唯一，它承接的是原先由 voucher_no 承担的幂等语义。");
        // ⭐ 全等，不是 Contains：子串判据放行「在后面追加一条豁免 conjunct」这类变异。
        Assert.Equal("source_type IS NOT NULL AND source_no IS NOT NULL", index.GetFilter(), StringComparer.Ordinal);

        // voucher_no 那条唯一索引本票不动：凭证号改短号是 S6/S7，它还在承重。
        var voucherNoIndex = Assert.Single(
            entity.GetIndexes(),
            x => x.Properties.Select(p => p.Name).SequenceEqual(
                [nameof(JournalVoucher.OrganizationId), nameof(JournalVoucher.EnvironmentId), nameof(JournalVoucher.VoucherNo)]));
        Assert.True(voucherNoIndex.IsUnique);
    }

    /// <summary>占位空串不算填了：空白由构造期拒绝，而不是落成一行空值。</summary>
    [Fact]
    public void Post_rejects_a_blank_source_no()
    {
        foreach (var blank in new[] { string.Empty, "   " })
        {
            Assert.Throws<ArgumentException>(() => JournalVoucher.Post(
                "org-001",
                "env-dev",
                "JV-BLANK-001",
                new DateOnly(2026, 9, 14),
                [
                    new JournalVoucherLineDraft("1401", 10m, 0m, "debit"),
                    new JournalVoucherLineDraft("2202", 0m, 10m, "credit"),
                ],
                JournalVoucherSourceType.Manual,
                blank));
        }

        Assert.Throws<ArgumentNullException>(() => JournalVoucher.Post(
            "org-001",
            "env-dev",
            "JV-BLANK-002",
            new DateOnly(2026, 9, 14),
            [
                new JournalVoucherLineDraft("1401", 10m, 0m, "debit"),
                new JournalVoucherLineDraft("2202", 0m, 10m, "credit"),
            ],
            null!,
            "JV-BLANK-002"));
    }

    /// <summary>
    /// 码表是手工登记表：在类型里声明一个静态实例却不追加到 <see cref="JournalVoucherSourceType.All"/>，
    /// 编译期不会拦。这条反射对撞把那个方向补住，并顺带钉住码值互异（码值会落库）。
    /// </summary>
    [Fact]
    public void All_enumerates_every_declared_source_type_and_codes_stay_distinct()
    {
        var declared = typeof(JournalVoucherSourceType)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(x => x.PropertyType == typeof(JournalVoucherSourceType))
            .Select(x => (JournalVoucherSourceType)x.GetValue(null)!)
            .ToList();

        Assert.Equal(declared.Count, JournalVoucherSourceType.All.Count);
        Assert.Equal(
            declared.Select(x => x.Code).Order(StringComparer.Ordinal),
            JournalVoucherSourceType.All.Select(x => x.Code).Order(StringComparer.Ordinal));
        Assert.Equal(
            JournalVoucherSourceType.All.Count,
            JournalVoucherSourceType.All.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count());
    }

    private static ApplicationDbContext CreateModelOnlyDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=nerv_iip_journal_voucher_source_contract;Username=nerv;Password=nerv",
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ErpFacts.Schema))
            .Options;
        return new ApplicationDbContext(options, new SourceContractNoopMediator());
    }

    private sealed class SourceContractNoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException("This test mediator only supports publish.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");
    }
}
