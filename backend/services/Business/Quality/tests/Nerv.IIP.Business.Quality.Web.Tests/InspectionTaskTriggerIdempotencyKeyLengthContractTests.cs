using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Quality.Web.Application.Seed;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// GitHub #2977：<c>inspection_tasks.trigger_idempotency_key</c> 的列宽必须容得下**全部**写面的最坏情况，
/// 而那个最坏情况是**算出来的**，不是挑出来的。
///
/// **受治理写面 vs 只进 fits 的写面**
///
/// 「受治理」的判据是**这一段的取值由调用方控制且没有声明上界**——只有这种写面才需要用列宽兜住。
/// 本类看守 Quality 自己拼的两条受治理写面（首件、周期检）：它们每一段变量都同时写进**同一行**的
/// 另一个有界列，因此上界可以完全在本服务的 EF 模型里闭合。
///
/// 世界观历史 seed 也写这一列，但它的三段输入**全部来自它自己的定长生成器**
/// （<c>WorldHistorySpec.WorkOrderNo(i)</c> 产出 <c>"WO-2026-00001"</c>），实际键长约 40 字符，
/// 没有任何调用方能把它撑宽。所以它**只进 fits、不进 defines**——见
/// <see cref="The_world_history_seed_keys_fit_the_column_without_defining_it"/>，
/// 那条用例用**实跑真实 seed 事实流**测长度，而不是对着本行列宽做假想饱和。
///
/// **本类钉住的五件事**
/// <list type="bullet">
/// <item><see cref="Trigger_idempotency_key_column_width_matches_the_policy_constant"/>：列宽从
/// **EF 模型闭集枚举**（<c>IDesignTimeModel</c>，按列名扫全模型）读回，与
/// <see cref="InspectionTaskTriggerKey.MaxLength"/> 对撞——单边改 <c>HasMaxLength</c>、改常量、
/// 或新增一张带该列的表都会红；</item>
/// <item><see cref="The_column_width_is_not_smaller_than_any_governed_write_face"/>：
/// **不许少留**，用 <c>&gt;=</c>。任何受治理写面**变宽**都会红，逼一次重新推导；</item>
/// <item><see cref="The_column_width_is_derived_from_the_widest_governed_write_face"/>：
/// **不许多留**，用等式并**点名**定界的那条写面。
/// 之所以与上一条拆成两句强度不同的话：某条写面**变窄或消失**时，红的是这一条
/// （可解释、可安全更新），而不是上一条——否则会逼一次列宽缩窄迁移，而该列在增长型事实表上
/// 且带唯一索引，缩窄意味着全表重写 + 校验扫描；</item>
/// <item><see cref="Creating_a_task_rejects_a_trigger_key_wider_than_the_column"/>：真实聚合工厂上
/// 「上界那一位放行、+1 位拒绝」，守卫住在构造函数里，任何写面绕不开；</item>
/// <item><see cref="Widening_the_column_did_not_change_any_key_composition"/>：各形状的**逐字产出**
/// 被钉死，任何人顺手「收敛键构成」都会红。#2977 的验收标准里「既有触发点的去重语义不变」
/// 就是靠这一条 + 各触发点既有用例承担。</item>
/// </list>
///
/// **合同分类（`docs/governance/testing/validity.md` 六类合同来源）**
/// <list type="bullet">
/// <item><c>ProviderBehavior</c>：权威来源是 migration / schema 约束
/// （<c>20260910092001_WidenInspectionTaskTriggerIdempotencyKey.cs</c> 把该列改为
/// <c>character varying(474)</c>）与 PostgreSQL 对超宽赋值抛 <c>22001</c> 的官方行为。
/// **但本类的执行形态只是「EF 模型读取 + 纯函数 + 内存聚合」**，按 validity.md 的证明范围表，
/// 它**不能**证明真库落库行为；那一面由 PR 正文里一次性 <c>postgres:18</c> 的迁移与读写读数承担。</item>
/// <item><c>Regression</c>：权威来源是 **GitHub #2977**（正文 + 编排者第一条评论里补全的写面判定表），
/// 错误行为「上界不闭合、超宽时落库抛 22001」与期望行为「列宽容得下全部写面」均出自该 Issue 的
/// 验收条件；<see cref="The_pre_change_column_width_could_not_hold_the_governed_write_faces"/>
/// 即最小复现。</item>
/// </list>
///
/// **值域边界（声明放弃了什么，别读成完备）**
/// <list type="number">
/// <item>本类**只覆盖 Quality 自己拼的形状**。四条把上游 <c>IdempotencyKey</c> 透传 / 再拼一段的
/// 写面（WMS 收货、ERP 收货、MES 工序完工、MES 成品入库申请）的上界在**别的服务的 EF 模型**里，
/// 由 <c>Nerv.IIP.Business.Acceptance.Tests.InspectionTaskTriggerKeyCrossServiceWidthContractTests</c>
/// 跑真实 converter 实测。那四条任意一条变宽，**本类一格都不会红**。</item>
/// <item>列宽读的是 **EF 模型**而不是迁移脚本。只改迁移不改模型（或反之）本类抓不到。</item>
/// <item>本类**不证明**「没有第八个写面」。写面枚举维度是「<c>InspectionTask.CreatePending</c> 的全部
/// 调用点」（本 PR 实读 4 处生产调用点，展开成 8 个 <c>triggerIdempotencyKey</c> 实参位点），
/// 这是人读的结果；机器侧兜底的是构造函数里的守卫，不是本类。</item>
/// <item>周期检那一行的鉴别力最弱：83 远低于列宽，<c>&gt;=</c> 与「改前列宽装不下」两条对它都不成立
/// （后者已按名字显式排除）。**别把 <see cref="GovernedWriteFaces"/> 的两行读成两份等强的鉴别力**；
/// 周期检的真正防线是 <see cref="Widening_the_column_did_not_change_any_key_composition"/>
/// 里那两条逐字断言。</item>
/// <item><see cref="Widening_the_column_did_not_change_any_key_composition"/> 里的两条 seed 断言是
/// **键构成 golden，不是列宽契约**：世界观种子拆除时直接删掉这两行即可，不牵动 schema、不需要迁移。</item>
/// </list>
/// </summary>
public sealed class InspectionTaskTriggerIdempotencyKeyLengthContractTests
{
    /// <summary>被治理的列名。EF 模型按这个名字**闭集枚举**，不是手写白名单。</summary>
    private const string TriggerKeyColumnName = "trigger_idempotency_key";

    /// <summary>
    /// EF 模型里 <see cref="TriggerKeyColumnName"/> 列的总数。这条计数是**闭集下界**：
    /// 新增一张带该列的表、或改列名让值域塌成空集，都会红——否则 <c>Assert.All</c> 对空集恒真。
    /// </summary>
    private const int TriggerKeyColumnCount = 1;

    /// <summary>
    /// 改前列宽（#2977 的复现基线，**不是现行上界**）。它只出现在
    /// <see cref="The_pre_change_column_width_could_not_hold_the_governed_write_faces"/> 里，
    /// 用途是证明缺陷可复现；现行上界一律从 <see cref="InspectionTaskTriggerKey.MaxLength"/> 与
    /// EF 模型取，任何地方都不再手抄。
    /// </summary>
    private const int PreChangeColumnMaxLength = 300;

    [Fact]
    public void Trigger_idempotency_key_column_width_matches_the_policy_constant()
    {
        using var fixture = CreateFixture();
        var columns = TriggerKeyColumns(fixture.Model);

        var columnCount = columns.Length;
        Assert.Equal(TriggerKeyColumnCount, columnCount);
        Assert.All(
            columns,
            property => Assert.Equal(InspectionTaskTriggerKey.MaxLength, property.GetMaxLength()));
    }

    /// <summary>
    /// **不许少留**：列宽不小于任何受治理写面的最坏情况。
    ///
    /// 这一半用 <c>&gt;=</c> 而不是等式——任何写面**变宽**都会红（这是必须逼一次重新推导的方向），
    /// 而写面**变窄或消失**不红这条。后者由
    /// <see cref="The_column_width_is_derived_from_the_widest_governed_write_face"/> 承担。
    /// </summary>
    [Theory]
    [MemberData(nameof(GovernedWriteFaces))]
    public void The_column_width_is_not_smaller_than_any_governed_write_face(string faceName, int worstCaseLength)
    {
        using var fixture = CreateFixture();
        var columnWidth = ColumnWidth(fixture.Model);

        Assert.NotEmpty(faceName);
        Assert.True(
            worstCaseLength <= columnWidth,
            $"受治理写面 {faceName} 的最坏情况 {worstCaseLength} 超出列宽 {columnWidth}。");
    }

    /// <summary>
    /// **不许多留**：列宽恰等于最宽受治理写面的最坏情况，并**点名**是哪一条。
    ///
    /// 这条是本票「上界从关系派生、不手抄」的真对撞：它不经过
    /// <see cref="InspectionTaskTriggerKey.MaxLength"/>，直接把 EF 模型读到的列宽
    /// 与实跑真实拼接函数算出来的最坏情况比。
    ///
    /// **点名的是首件写面，不是世界观历史 seed。** seed 的输入全部来自它自己的定长生成器，
    /// 让一段计划拆除的演示代码去定界这张增长型事实表上的列，等于围绕种子建立维护契约；
    /// 种子拆除时这条**不会**红，也**不需要**任何缩窄迁移。
    /// </summary>
    [Fact]
    public void The_column_width_is_derived_from_the_widest_governed_write_face()
    {
        using var fixture = CreateFixture();
        var columnWidth = ColumnWidth(fixture.Model);
        var faces = GovernedWriteFaceWorstCases(fixture.Model);

        Assert.NotEmpty(faces);
        Assert.Equal(columnWidth, faces.Values.Max());
        Assert.Equal(
            nameof(FirstArticleInspection),
            faces.MaxBy(face => face.Value).Key);
    }

    /// <summary>
    /// 世界观历史 seed **只进 fits、不进 defines**。
    ///
    /// 长度用**实跑真实事实流**（<c>WorldHistoryQualitySpec.BuildInspectionFacts</c>）量，
    /// 不是对着本行的 <c>source_document_id</c>(250) / <c>source_document_line_id</c>(250) 做假想饱和
    /// ——那样算会得到 621，比真实值宽约 580 字符，并把列宽的定界权交给一段计划拆除的演示代码。
    ///
    /// 第二条断言把「seed **不是**最宽写面」写死：谁再把 seed 抬成定界者都会红。
    ///
    /// **种子拆除时直接删掉本用例即可**，不牵动 schema、不需要缩窄迁移。
    /// </summary>
    [Fact]
    public void The_world_history_seed_keys_fit_the_column_without_defining_it()
    {
        using var fixture = CreateFixture();
        var columnWidth = ColumnWidth(fixture.Model);
        var governedWorstCase = GovernedWriteFaceWorstCases(fixture.Model).Values.Max();

        var seedFacts = WorldHistoryQualitySpec.BuildInspectionFacts(new DateOnly(2026, 7, 26), 1.0d);
        Assert.NotEmpty(seedFacts);
        var widestSeedKey = seedFacts.Max(fact => fact.TriggerIdempotencyKey.Length);

        Assert.True(
            widestSeedKey <= columnWidth,
            $"世界观历史 seed 的最长触发键 {widestSeedKey} 超出列宽 {columnWidth}。");
        Assert.True(
            widestSeedKey < governedWorstCase,
            $"世界观历史 seed 的最长触发键 {widestSeedKey} 不低于最宽受治理写面 {governedWorstCase}，"
            + "说明它反过来定界了列宽——本票明确不接受这个形状。");
    }

    /// <summary>
    /// 守卫住在聚合构造函数里：上界那一位放行、+1 位拒绝，且**绝不截断**
    /// （截断会把仅末几位不同的两个键折叠成同一个，那会让两个不同的上游事实撞成一张任务）。
    /// </summary>
    [Fact]
    public void Creating_a_task_rejects_a_trigger_key_wider_than_the_column()
    {
        using var fixture = CreateFixture();
        var columnWidth = ColumnWidth(fixture.Model);

        var fitted = CreatePendingWithTriggerKey(new string('k', columnWidth));
        Assert.Equal(columnWidth, fitted.TriggerIdempotencyKey.Length);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => CreatePendingWithTriggerKey(new string('k', columnWidth + 1)));
        Assert.Equal("triggerIdempotencyKey", exception.ParamName);

        // 反截断：仅末位不同的两个顶格键必须仍然互异。
        var left = CreatePendingWithTriggerKey(new string('k', columnWidth - 1) + "a");
        var right = CreatePendingWithTriggerKey(new string('k', columnWidth - 1) + "b");
        Assert.NotEqual(left.TriggerIdempotencyKey, right.TriggerIdempotencyKey);
    }

    /// <summary>改前列宽在同样的输入下装不下——缺陷可复现。</summary>
    [Theory]
    [MemberData(nameof(GovernedWriteFaces))]
    public void The_pre_change_column_width_could_not_hold_the_governed_write_faces(
        string faceName,
        int worstCaseLength)
    {
        using var fixture = CreateFixture();
        var columnWidth = ColumnWidth(fixture.Model);

        Assert.True(
            PreChangeColumnMaxLength < columnWidth,
            $"改前列宽 {PreChangeColumnMaxLength} 没有低于现行列宽 {columnWidth}，缺陷描述有误。");
        if (!string.Equals(faceName, nameof(PeriodicInspectionSourceLine), StringComparison.Ordinal))
        {
            Assert.True(
                worstCaseLength > PreChangeColumnMaxLength,
                $"写面 {faceName} 的最坏情况 {worstCaseLength} 没有超出改前列宽 {PreChangeColumnMaxLength}。");
        }
    }

    /// <summary>
    /// **去重语义不变**：三种本地形状的逐字产出被钉死。#2977 的裁定是「放宽列宽、不改键构成」，
    /// 因为该列上有唯一索引 <c>ux_inspection_tasks_scope_trigger_key</c>——改构成会让存量键与新键并存，
    /// 在制工单重复开任务且不可回滚。任何人顺手「收敛键构成」都会红在这里。
    /// </summary>
    [Fact]
    public void Widening_the_column_did_not_change_any_key_composition()
    {
        Assert.Equal(
            "quality:first-article:org-001:env-dev:WO-001:OP-010",
            FirstArticleInspection.TriggerIdempotencyKey("org-001", "env-dev", "WO-001", "OP-010"));
        Assert.Equal(
            "quality:periodic-time:0198f000-0000-7000-8000-000000000001:7",
            PeriodicInspectionSourceLine.TriggerIdempotencyKey(
                PeriodicInspectionSourceLine.TimeKind,
                Guid.Parse("0198f000-0000-7000-8000-000000000001"),
                7));
        Assert.Equal(
            "quality:periodic-quantity:0198f000-0000-7000-8000-000000000001:7",
            PeriodicInspectionSourceLine.TriggerIdempotencyKey(
                PeriodicInspectionSourceLine.QuantityKind,
                Guid.Parse("0198f000-0000-7000-8000-000000000001"),
                7));
        Assert.Equal(
            "seed:world-history:mes:WO-001:70",
            WorldHistoryQualitySpec.TriggerIdempotencyKey("mes", "WO-001", "70"));
        Assert.Equal(
            "seed:world-history:mes:WO-001:-",
            WorldHistoryQualitySpec.TriggerIdempotencyKey("mes", "WO-001", null));
    }

    public static TheoryData<string, int> GovernedWriteFaces()
    {
        using var fixture = CreateFixture();
        var data = new TheoryData<string, int>();
        foreach (var (faceName, worstCase) in GovernedWriteFaceWorstCases(fixture.Model))
        {
            data.Add(faceName, worstCase);
        }

        return data;
    }

    /// <summary>
    /// **受治理写面**的最坏情况，**全部由实跑真实拼接函数 + 从 EF 模型读回的列宽算出**，
    /// 没有任何手抄的长度数字，也没有手抄的前缀字面量（前缀开销靠「用空串跑一次真函数」测出来）。
    ///
    /// 「受治理」= 该写面至少有一段的取值由调用方控制且没有声明上界，因此需要用列宽兜住。
    /// 世界观历史 seed **不在**本集合里：它的三段输入全部来自它自己的定长生成器，
    /// 由 <see cref="The_world_history_seed_keys_fit_the_column_without_defining_it"/> 单独按实测长度看守。
    /// </summary>
    private static IReadOnlyDictionary<string, int> GovernedWriteFaceWorstCases(IModel model)
    {
        var organizationIdWidth = ColumnWidth(model, "organization_id");
        var environmentIdWidth = ColumnWidth(model, "environment_id");
        var sourceDocumentIdWidth = ColumnWidth(model, "source_document_id");

        // 首件：`{org}:{env}:` 之后的两段合起来就是同一行的 source_document_id，
        // 所以那两段可用的总长度 = source_document_id 列宽 − 复合身份自带的那个分隔符。
        var firstArticleOverhead = FirstArticleInspection.TriggerIdempotencyKey(
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty).Length;
        var compositeSeparatorLength = FirstArticleInspection.SourceDocumentId(string.Empty, string.Empty).Length;
        var firstArticleWorstCase = firstArticleOverhead
            + organizationIdWidth
            + environmentIdWidth
            + (sourceDocumentIdWidth - compositeSeparatorLength);

        // 周期检：闭集 kind + Guid("D") + long，三段都定长，直接实跑最宽取值即最坏情况。
        // long.MinValue 比真实序号（非负）多算一位符号，是故意的过近似。
        var widestPeriodicKind = new[] { PeriodicInspectionSourceLine.TimeKind, PeriodicInspectionSourceLine.QuantityKind }
            .MaxBy(kind => kind.Length)!;
        var periodicWorstCase = PeriodicInspectionSourceLine
            .TriggerIdempotencyKey(widestPeriodicKind, Guid.Empty, long.MinValue)
            .Length;

        return new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [nameof(FirstArticleInspection)] = firstArticleWorstCase,
            [nameof(PeriodicInspectionSourceLine)] = periodicWorstCase,
        };
    }

    private static InspectionTask CreatePendingWithTriggerKey(string triggerIdempotencyKey)
    {
        var createdAtUtc = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
        return InspectionTask.CreatePending(
            "org-001",
            "env-dev",
            new InspectionPlanId(Guid.CreateVersion7()),
            QualityInspectionSourceTypes.Receiving,
            QualityInspectionSourceServices.Wms,
            "WMS-IN-0001",
            "LINE-001",
            "SKU-0001",
            1m,
            "ea",
            batchNo: null,
            serialNo: null,
            createdAtUtc,
            createdAtUtc.AddHours(8),
            triggerIdempotencyKey);
    }

    private static IProperty[] TriggerKeyColumns(IModel model)
    {
        return model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties())
            .Where(property => string.Equals(property.GetColumnName(), TriggerKeyColumnName, StringComparison.Ordinal))
            .ToArray();
    }

    private static int ColumnWidth(IModel model)
    {
        return Assert.Single(TriggerKeyColumns(model)).GetMaxLength()!.Value;
    }

    private static int ColumnWidth(IModel model, string columnName)
    {
        var entityType = model.FindEntityType(typeof(InspectionTask))!;
        var property = Assert.Single(
            entityType.GetProperties(),
            candidate => string.Equals(candidate.GetColumnName(), columnName, StringComparison.Ordinal));
        return property.GetMaxLength()!.Value;
    }

    private static ModelFixture CreateFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
        });
        services.AddQualityPostgreSqlPersistence(
            "Host=unused;Database=nerv_iip_trigger_key_width_contract;Username=nerv;Password=nerv");
        return new ModelFixture(services.BuildServiceProvider());
    }

    private sealed class ModelFixture : IDisposable
    {
        private readonly ServiceProvider provider;
        private readonly IServiceScope scope;

        public ModelFixture(ServiceProvider provider)
        {
            this.provider = provider;
            scope = provider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Model = dbContext.GetService<IDesignTimeModel>().Model;
        }

        public IModel Model { get; }

        public void Dispose()
        {
            scope.Dispose();
            provider.Dispose();
        }
    }
}
