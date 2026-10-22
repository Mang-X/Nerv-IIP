namespace Nerv.IIP.VocabularyGovernance.Tests;

/// <summary>
/// #3292 · 「在查询树里对值转换过的强类型 Id 属性再取内部成员」的门禁（来源 #3098 验收条件第 3 条）。
///
/// #3098 把当时已知的 5 个位点逐处修掉并各配 SQLite 回归用例；本门禁解决的是「**下一处**怎么被挡住」。
/// 形状：<c>x.Id.Id == v</c>（不带 <c>.ToString()</c> 也一样）。SQLite 与真实 PostgreSQL 18 两个 provider
/// 实测一致报 <c>could not be translated</c>，运行时表现为真机 500。
///
/// ⛔ 本门禁**不是文本扫描**：#3176 / PR #3214 已实证同类文本护栏三轮 8 种绕法不收敛（护栏自身涨到
/// 1139 行、退化成手搓 C# 词法分析器，最终按裁定移除）。本形状的文本扫描难度更高——
/// <c>OperationTask.cs</c> 的内存集合谓词、<c>OperationTaskRepository.cs</c> 的
/// <c>ToListAsync</c> 之后排序，与致命位点**文本完全同形但合法**。
///
/// 判定走 Roslyn 语义模型，判据两条（见 <see cref="StronglyTypedIdQueryScanner"/>）：
/// 祖先链上是否存在 <c>Expression&lt;&gt;</c> lambda + 位置分类（谓词/排序/Join/分组/聚合 vs 终端投影）。
///
/// 覆盖边界（逐条都是跑过的断言，不是声明）：
/// <list type="bullet">
/// <item>已扫：<c>backend</c> 下**全部非测试项目**（路径段含 <c>tests</c> 的排除），
/// 按 ProjectReference 图的根分组编译。</item>
/// <item>未扫：测试代码本身（<c>backend/**/tests/**</c>）与前端；测试里的同形写法不构成真机 500。</item>
/// <item>判不出来一律 fail-closed：符号解析失败进 <c>unresolved</c>
/// （<see cref="Scanner_resolves_every_strongly_typed_id_inner_member_across_backend"/> 断言为 0），
/// LINQ 查询语法进 <c>UncoveredSyntaxForms</c>
/// （<see cref="Scanner_reports_no_site_hiding_in_linq_query_syntax"/> 断言为空），
/// 表达式树内运算符不可识别、投影后继运算符不可识别 ⇒ 判致命。</item>
/// </list>
///
/// <para>
/// ⭐ <b>已知缺口与绕法登记（PR #3448 复审实测，本票射程不含、⛔ 不在本票堵）。</b>
/// 护栏自称完备比有洞更坏 —— 下面每条都是量过的读数，不是猜测：
/// </para>
/// <list type="number">
/// <item><b>绕法：<c>HasQueryFilter(x =&gt; x.Id.Id != "…")</c> 判合法。</b>
/// 模型配置这个逃生口是按**命名空间** <c>Microsoft.EntityFrameworkCore.Metadata.Builders</c> 开的、
/// ⛔ 不是按 <c>HasConversion</c> 这个 API 开的，而 query filter 会被拼进每条查询翻译成 SQL。
/// 全仓当前 0 处使用。</item>
/// <item><b>绕法：<c>((IStronglyTypedId&lt;string&gt;)x.Id).Id</c> 判零命中。</b>
/// 强转成接口后 <c>symbol.ContainingType</c> 不再等于接收者类型，扫描器直接 return ——
/// **既不计位点、也不计 <c>unresolved</c>**。泛型约束上取 <c>.Id</c> 同理。</item>
/// <item><b>假阳族：闭包捕获。</b><c>Where(x =&gt; x.Foo == parent.Id.Id)</c> ——
/// <c>parent</c> 是闭包捕获的外部变量、运行时会被参数化，实际合法，但本扫描器按位置判致命。</item>
/// <item><b><c>UncoveredProjects == 0</c> 近乎同义反复。</b>
/// 新项目没人引用就自成新根、天然 covered，该集合非空只可能来自引用环；
/// <c>Assert.Equal(Roots.Count, CompilationCount)</c> 在 <c>Reading</c> 路径上 <c>rootFilter</c> 恒为 null，
/// 是纯同义反复。⇒ ⭐ <b>扫描面的覆盖性由「根集的可达闭包 = 全部非测试项目」这个结构性事实保证，
/// ⛔ 不由那两条断言保证</b>（扩展性本身经复审实测成立：新建一个零 wiring 的服务放一处
/// <c>Where(x =&gt; x.Id.Id == v)</c>，守护断言当场红并点名）。</item>
/// <item><b>⭐ <c>unresolved</c> fail-closed 的真实强度：四种退化里 3 种静默全绿。</b>
/// 复审实测：删 <c>Npgsql.EntityFrameworkCore.PostgreSQL</c> ⇒ 10/10 绿；
/// 删 <c>NetCorePal.Extensions.Domain.Abstractions</c> ⇒ 10/10 绿；
/// 删全部 56 个包 ⇒ 红 8/10、<c>unresolved=1454</c>；静默排除整个 FileStorage（3 项目 / 58 文件）⇒ 10/10 绿。
/// 真因：**删一条直接 <c>PackageReference</c> 并不会把程序集移出 TPA**（传递依赖照样带进来），扫描面根本没缩。
/// ⇒ ⛔「漏了哪个包就会报红」作为一般主张**不成立**；它只在「包真的从 TPA 里消失」时成立
/// （删全部 56 个那格、以及源生成器清单残缺那格 —— 后者在本 PR 实施中真发生过一次，当场红 22 条）。
/// ⚠️ 第四格是真缺口：<see cref="Scan_face_covers_every_non_test_backend_project"/> 的下界
/// （projects≥90 / files≥2000 / sites≥200，现读数 98 / 2262 / 248）余量足够吞掉一个中小服务。</item>
/// </list>
/// </summary>
public sealed class StronglyTypedIdQueryTranslationGovernanceTests
{
    /// <summary>
    /// head <c>654a3a6c4</c> 实读：backend 非测试源码里位于表达式树内的强类型 Id 内部成员共 8 处，
    /// **全部**在终端投影位（EF 允许客户端求值），因此全部合法。
    ///
    /// ⭐ 这 8 条是阴性对照的登记面：其中 7 处是票面点名的终端 <c>Select</c> 投影位，
    /// 第 8 处 <c>ListOperationTasksQuery.cs</c> 的 <c>a.Id.Id</c> 是**导航属性子查询**——
    /// 接收者是 <c>IReadOnlyCollection</c>、内层 lambda 被转成 <c>Func&lt;&gt;</c>，
    /// 票面原判据（按接收者类型判）会把它整族漏掉。
    ///
    /// ⚠️ 登记项按「路径 + 位置分类 + 代码片段」钉，不含行号：行号漂移不该让门禁报红，
    /// 但新增/改写一处表达式树内的位点必须回到这张表上登记。
    /// </summary>
    private static readonly IReadOnlyList<string> RegisteredExpressionTreeSites =
    [
        "services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Queries/ProductionVersions/ListProductionVersionsQuery.cs | Projection | x.Id.Id",
        "services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Queries/ProductionVersions/ResolveProductionVersionQuery.cs | Projection | x.Id.Id",
        "services/Ops/src/Nerv.IIP.Ops.Web/Application/Queries/ListAuditRecordsQuery.cs | Projection | x.Id.Id",
        "services/Ops/src/Nerv.IIP.Ops.Web/Application/Queries/ListAuditRecordsQuery.cs | Projection | x.OperationTaskId.Id",
        "services/Ops/src/Nerv.IIP.Ops.Web/Application/Queries/ListOperationTasksQuery.cs | Projection | a.Id.Id",
        "services/Ops/src/Nerv.IIP.Ops.Web/Application/Queries/ListOperationTasksQuery.cs | Projection | x.Id.Id",
        "services/Ops/src/Nerv.IIP.Ops.Web/Application/Queries/ValidateAuditIntegrityQuery.cs | Projection | x.Id.Id",
        "services/Ops/src/Nerv.IIP.Ops.Web/Application/Queries/ValidateAuditIntegrityQuery.cs | Projection | x.OperationTaskId.Id",
    ];

    private const string OperationTaskRepositoryPath =
        "services/Ops/src/Nerv.IIP.Ops.Infrastructure/Repositories/OperationTaskRepository.cs";

    private const string OperationTaskPath =
        "services/Ops/src/Nerv.IIP.Ops.Domain/AggregatesModel/OperationTaskAggregate/OperationTask.cs";

    /// <summary>
    /// <c>OperationTaskRepository</c> 里那条 <c>IQueryable</c> 谓词（#3292 交回点名的 <c>:119</c>）。
    /// 阳性对照靠**往它里面注入**一处 <c>x.Id.Id</c> 取数——main 上不存在致命位点，
    /// 不注入就量不到判定力。⛔ 注入只在内存里做，不落盘。
    /// </summary>
    private const string QueryablePredicateAnchor =
        """.Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Status == "dispatched")""";

    private static readonly Lazy<BackendStronglyTypedIdScan.BackendScanReading> OpsWithInjectedPredicate =
        new(() => ScanOpsWith(text => Inject(
            text,
            QueryablePredicateAnchor,
            """.Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Status == "dispatched" && x.Id.Id != "probe")""")));

    private static readonly Lazy<BackendStronglyTypedIdScan.BackendScanReading> OpsWithInjectedNavigationSubquery =
        new(() => ScanOpsWith(text => Inject(
            text,
            QueryablePredicateAnchor,
            """.Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Attempts.Any(a => a.Id.Id != "probe"))""")));

    // ── 门禁本体 ──────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ 守护断言：backend 非测试源码里不许出现「查询树里必须被翻译的位置上取强类型 Id 内部成员」。
    /// 新写一处 <c>Where(x =&gt; x.Id.Id == v)</c> 就会在这里红。
    /// </summary>
    [Fact]
    public void Backend_has_no_strongly_typed_id_inner_member_in_a_translated_query_position()
    {
        var reading = BackendStronglyTypedIdScan.Reading;

        Assert.True(
            reading.Sites.All(site => !site.Fatal),
            "查询树里对值转换过的强类型 Id 再取内部成员会翻译失败（#3098，真机 500）。"
            + "谓词/排序/Join/分组/聚合位请先把强类型 Id 物化再直接比较，例如 "
            + "`var id = new OperationTaskId(raw); query.Where(x => x.Id == id)`。违例位点："
            + Environment.NewLine
            + string.Join(Environment.NewLine, reading.Sites.Where(site => site.Fatal).Select(site => "  " + site.Describe())));
    }

    /// <summary>
    /// ⭐ fail-closed：每一处「接收者是强类型 Id」的取成员都必须解析出符号，<c>unresolved</c> 必须为 0。
    ///
    /// 这条是宿主「中央包并集」那份清单的解药：清单漏了哪个包、或 netcorepal 源生成器没跑起来，
    /// 目标类型/生成成员就解析不出来，读数会从 0 跳到非 0 而**不是静默少扫**。
    /// 哨兵读数见 <see cref="StronglyTypedIdQueryScannerProbeTests.Sentinel_disabling_source_generators_turns_every_site_into_an_unresolved_reading"/>。
    /// </summary>
    [Fact]
    public void Scanner_resolves_every_strongly_typed_id_inner_member_across_backend()
    {
        var reading = BackendStronglyTypedIdScan.Reading;

        Assert.Empty(ScannerCompilationFactory.SourceGenerators.LoadFailures);
        Assert.NotEmpty(ScannerCompilationFactory.SourceGenerators.Generators);

        Assert.True(
            reading.Unresolved.Count == 0,
            $"{reading.Unresolved.Count} 处强类型 Id 内部成员没能解析出符号 —— 扫描面已经被静默缩小，"
            + "请先补齐宿主 csproj 的中央包并集 / 确认源生成器清单，再看门禁结论："
            + Environment.NewLine
            + string.Join(Environment.NewLine, reading.Unresolved.Take(20).Select(site => "  " + site.Describe())));
    }

    /// <summary>覆盖边界写成断言：LINQ 查询语法判不了，出现即报红，⛔ 不静默放过。</summary>
    [Fact]
    public void Scanner_reports_no_site_hiding_in_linq_query_syntax()
    {
        var reading = BackendStronglyTypedIdScan.Reading;

        Assert.True(
            reading.UncoveredSyntaxForms.Count == 0,
            "下列位点写在 LINQ 查询语法里，本扫描器判不了："
            + Environment.NewLine
            + string.Join(Environment.NewLine, reading.UncoveredSyntaxForms.Select(text => "  " + text)));
    }

    // ── 扫描面：不许静默缩小 ──────────────────────────────────────────────

    /// <summary>
    /// 扫描面是结构性枚举而不是白名单：backend 下全部非测试项目都必须落在某个 ProjectReference 根的闭包里。
    /// 新增一个谁都不引用的项目会自成新根；新增一个被引用的项目自动进入上级闭包。
    /// </summary>
    [Fact]
    public void Scan_face_covers_every_non_test_backend_project()
    {
        var reading = BackendStronglyTypedIdScan.Reading;

        Assert.Empty(reading.Graph.UncoveredProjects);
        Assert.Equal(reading.Graph.Roots.Count, reading.CompilationCount);

        // 下界不是为了钉死数字，是为了让「枚举静默塌掉」变成红：
        // 枚举一旦断掉，unresolved 会跟着变 0，只有对着规模下界才看得出来。
        // head 654a3a6c4 实读：98 个非测试项目 / 25 个根 / 2262 个被扫文件 / 248 处位点。
        Assert.True(reading.Graph.Projects.Count >= 90, $"非测试项目只枚举到 {reading.Graph.Projects.Count} 个。");
        Assert.True(reading.ScannedFileCount >= 2000, $"只扫到 {reading.ScannedFileCount} 个源文件。");
        Assert.True(reading.Sites.Count >= 200, $"只判出 {reading.Sites.Count} 处强类型 Id 内部成员。");
    }

    /// <summary>
    /// main 上位于表达式树内的位点集合必须与登记面逐条相等：
    /// 多出一条（新写法）或少一条（改写/删除）都要回到 <see cref="RegisteredExpressionTreeSites"/> 对账。
    /// </summary>
    [Fact]
    public void Expression_tree_sites_on_main_match_the_registered_terminal_projections()
    {
        var reading = BackendStronglyTypedIdScan.Reading;

        var actual = reading.Sites
            .Where(site => site.InExpressionTree && site.Position != QueryPosition.ModelConfiguration)
            .Select(site => $"{site.Path} | {site.Position} | {site.Snippet}")
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(RegisteredExpressionTreeSites.OrderBy(text => text, StringComparer.Ordinal).ToArray(), actual);
    }

    // ── 阴性对照：票面点名的两处合法写法 ──────────────────────────────────

    /// <summary>
    /// <c>OperationTask.RecordResult</c> 里 <c>_attempts.SingleOrDefault(x =&gt; x.Id.Id == …)</c>：
    /// <c>_attempts</c> 是内存集合，lambda 转成 <c>Func&lt;&gt;</c>，EF 不参与 ⇒ 合法，⛔ 不许误报。
    /// </summary>
    [Fact]
    public void In_memory_collection_predicate_in_the_aggregate_is_not_reported()
    {
        var site = SingleSiteWith(BackendStronglyTypedIdScan.Reading, OperationTaskPath, "SingleOrDefault");

        Assert.Equal(QueryPosition.OutsideExpressionTree, site.Position);
        Assert.False(site.Fatal);
    }

    /// <summary>
    /// <c>OperationTaskRepository</c> 里 <c>ToListAsync</c> 之后的 <c>ThenBy(x =&gt; x.Id.Id)</c>：
    /// 已经物化成内存列表 ⇒ 合法，⛔ 不许误报。
    /// </summary>
    [Fact]
    public void Ordering_after_materialisation_in_the_repository_is_not_reported()
    {
        var site = SingleSiteWith(BackendStronglyTypedIdScan.Reading, OperationTaskRepositoryPath, "ThenBy");

        Assert.Equal(QueryPosition.OutsideExpressionTree, site.Position);
        Assert.False(site.Fatal);
    }

    // ── 阳性对照 + 鉴别力：同文件同方法内把两类分开 ──────────────────────

    /// <summary>
    /// ⭐ 鉴别力最强的一格：在 <c>OperationTaskRepository.GetExpiredLeasesAsync</c> 里注入一处
    /// <c>IQueryable</c> 谓词位的 <c>x.Id.Id</c>（对应 <c>:119</c>）。
    /// 它必须被判致命，而**同文件同方法、14 行之下**的 <c>ThenBy(x =&gt; x.Id.Id)</c>（<c>:134</c>）
    /// 必须仍判合法。两类在同一处被分开，才说明判定靠的是上下文而不是文本。
    /// </summary>
    [Fact]
    public void Injected_queryable_predicate_is_fatal_while_the_sibling_ordering_in_the_same_method_stays_legal()
    {
        var reading = OpsWithInjectedPredicate.Value;

        var injected = Assert.Single(
            reading.Sites,
            site => site.Path == OperationTaskRepositoryPath && site.Fatal);
        Assert.Equal(QueryPosition.Predicate, injected.Position);
        Assert.Equal("Where", injected.OperatorChain);

        var sibling = SingleSiteWith(reading, OperationTaskRepositoryPath, "ThenBy");
        Assert.Equal(QueryPosition.OutsideExpressionTree, sibling.Position);
        Assert.True(
            sibling.Line > injected.Line,
            $"阴性对照应在注入点之后：注入 {injected.Line} 行 / 对照 {sibling.Line} 行。");
        Assert.Empty(reading.Unresolved);
    }

    /// <summary>
    /// ⭐ 阳性对照第二类：注入一处**导航属性子查询**谓词（<c>x.Attempts.Any(a =&gt; a.Id.Id != …)</c>）。
    /// 接收者 <c>x.Attempts</c> 是 <c>IReadOnlyCollection</c>、内层 lambda 是 <c>Func&lt;&gt;</c>，
    /// 票面原判据（按接收者类型判）会把这一族整族漏掉；判据①沿祖先链全走才抓得到。
    /// </summary>
    [Fact]
    public void Injected_navigation_subquery_predicate_is_fatal()
    {
        var reading = OpsWithInjectedNavigationSubquery.Value;

        var injected = Assert.Single(
            reading.Sites,
            site => site.Path == OperationTaskRepositoryPath && site.Fatal);
        Assert.Equal(QueryPosition.Predicate, injected.Position);
        Assert.Equal("Any ← Where", injected.OperatorChain);
        Assert.Empty(reading.Unresolved);
    }

    /// <summary>
    /// CONTROL 无害变异：把注入锚点原样替换成它自己（文件确实被改写过，语义没变）⇒ 判定必须与未注入一致。
    /// 这一格只验跑法：证明上面两格的红是注入内容带来的，不是「动过这个文件」带来的。
    /// </summary>
    [Fact]
    public void Control_rewriting_the_anchor_to_itself_changes_no_verdict()
    {
        var control = ScanOpsWith(text => Inject(text, QueryablePredicateAnchor, QueryablePredicateAnchor));

        Assert.DoesNotContain(control.Sites, site => site.Fatal);
        Assert.Empty(control.Unresolved);
        Assert.Equal(
            BackendStronglyTypedIdScan.Reading.Sites
                .Where(site => site.Path == OperationTaskRepositoryPath)
                .Select(site => $"{site.Line}:{site.Position}")
                .ToArray(),
            control.Sites
                .Where(site => site.Path == OperationTaskRepositoryPath)
                .Select(site => $"{site.Line}:{site.Position}")
                .ToArray());
    }

    // ── 取数辅助 ──────────────────────────────────────────────────────────

    private static StronglyTypedIdInnerMemberSite SingleSiteWith(
        BackendStronglyTypedIdScan.BackendScanReading reading,
        string path,
        string operatorName) =>
        Assert.Single(
            reading.Sites,
            site => site.Path == path && site.OperatorChain.Split(" ← ").Contains(operatorName, StringComparer.Ordinal));

    /// <summary>只编 Ops 这一个根（阳性对照不需要全仓 25 个根，省掉约 50 秒）。</summary>
    private static BackendStronglyTypedIdScan.BackendScanReading ScanOpsWith(Func<string, string> rewrite) =>
        BackendStronglyTypedIdScan.Run(
            new Dictionary<string, Func<string, string>>(StringComparer.Ordinal)
            {
                [OperationTaskRepositoryPath] = rewrite,
            },
            rootFilter: root => root.RelativePath == "services/Ops/src/Nerv.IIP.Ops.Web/Nerv.IIP.Ops.Web.csproj");

    /// <summary>按唯一锚点替换。锚点不唯一就抛——源码漂移必须显式报错，⛔ 不许静默不注入。</summary>
    private static string Inject(string text, string anchor, string replacement)
    {
        var occurrences = 0;
        var index = 0;
        while ((index = text.IndexOf(anchor, index, StringComparison.Ordinal)) >= 0)
        {
            occurrences++;
            index += anchor.Length;
        }

        Assert.True(occurrences == 1, $"注入锚点在源码里出现了 {occurrences} 次（应为 1 次）：{anchor}");
        return text.Replace(anchor, replacement, StringComparison.Ordinal);
    }
}
