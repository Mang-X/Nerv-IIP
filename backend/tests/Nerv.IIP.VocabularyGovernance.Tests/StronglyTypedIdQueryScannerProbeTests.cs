namespace Nerv.IIP.VocabularyGovernance.Tests;

/// <summary>
/// #3292 · <see cref="StronglyTypedIdQueryScanner"/> 自身的 probe 用例。
/// 扫描器没有自测就等于鉴别力未知：这里用内存源码串逐条钉住「什么算致命 / 什么算合法」，
/// 并带哨兵格（关掉源生成器必须由绿变红）与 CONTROL 无害变异（只验跑法，必须仍绿）。
/// </summary>
public sealed class StronglyTypedIdQueryScannerProbeTests
{
    /// <summary>
    /// 探针领域模型。<c>ThingId</c> 的 <c>Id</c> 成员由 netcorepal 源生成器产出，源码里没有——
    /// 哨兵格关掉生成器后这个成员就不存在，扫描器必须把它记进 unresolved 而不是静默判无违例。
    /// </summary>
    private const string DomainSource = """
        using System.Collections.Generic;
        using NetCorePal.Extensions.Domain;

        namespace Probe;

        public partial record ThingId : IStringStronglyTypedId;

        public partial record AttemptId : IStringStronglyTypedId;

        public sealed class Attempt
        {
            public AttemptId Id { get; set; } = new("a");

            public string Status { get; set; } = string.Empty;
        }

        public sealed class Thing
        {
            public ThingId Id { get; set; } = new("t");

            public string Status { get; set; } = string.Empty;

            public List<Attempt> Attempts { get; } = [];
        }
        """;

    private const string ProbeSource = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Linq.Expressions;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;

        namespace Probe;

        public static class Probes
        {
            // ── 致命：查询树里必须被翻译的位置 ───────────────────────────────
            public static Thing? Predicate(IQueryable<Thing> query, string value) =>
                query.Where(x => x.Id.Id == value).FirstOrDefault();

            public static List<Thing> Ordering(IQueryable<Thing> query) =>
                query.OrderBy(x => x.Id.Id).ToList();

            public static List<Thing> NavigationSubqueryPredicate(IQueryable<Thing> query, string value) =>
                query.Where(x => x.Attempts.Any(a => a.Id.Id == value)).ToList();

            public static List<Thing> JoinKey(IQueryable<Thing> query, IQueryable<Attempt> attempts) =>
                query.Join(attempts, x => x.Id.Id, y => y.Id.Id, (x, y) => x).ToList();

            public static readonly Expression<Func<Thing, string>> Detached = x => x.Id.Id;

            // ── 合法：终端投影位 / 内存集合 / EF 模型配置 ────────────────────
            public static List<string> TerminalProjection(IQueryable<Thing> query) =>
                query.Select(x => x.Id.Id).ToList();

            public static List<string> NonTerminalProjectionBeforePredicate(IQueryable<Thing> query) =>
                query.Select(x => x.Id.Id).Where(s => s != "zz").ToList();

            public static List<string> NonTerminalProjectionBeforeOrdering(IQueryable<Thing> query) =>
                query.Select(x => x.Id.Id).OrderBy(s => s).ToList();

            public static string? TerminalProjectionThenMaterialiser(IQueryable<Thing> query) =>
                query.Select(x => x.Id.Id).FirstOrDefault();

            public static List<string?> NavigationSubqueryProjection(IQueryable<Thing> query) =>
                query
                    .Select(x => x.Attempts.OrderByDescending(a => a.Status).Select(a => a.Id.Id).FirstOrDefault())
                    .ToList();

            public static Thing? InMemoryPredicate(List<Thing> items, string value) =>
                items.SingleOrDefault(x => x.Id.Id == value);

            public static List<Thing> AfterMaterialisation(List<Thing> items) =>
                items.OrderBy(x => x.Status).ThenBy(x => x.Id.Id).ToList();

            public static void ValueConversion(EntityTypeBuilder<Thing> builder) =>
                builder.Property(x => x.Id).HasConversion(x => x.Id, x => new ThingId(x));
        }
        """;

    private const string ProbePath = "probe/Probes.cs";

    private static StronglyTypedIdScanResult Scan(string probeSource = ProbeSource, bool runGenerators = true)
    {
        var compilation = ScannerCompilationFactory.Create(
            "Probe",
            [new SourceDocument("probe/Domain.cs", DomainSource), new SourceDocument(ProbePath, probeSource)],
            web: false,
            runGenerators: runGenerators);
        return StronglyTypedIdQueryScanner.Scan(compilation, [ProbePath, "probe/Domain.cs"]);
    }

    private static QueryPosition PositionIn(StronglyTypedIdScanResult result, string methodName)
    {
        var lines = ProbeSource.Split('\n');
        var declarationLine = Array.FindIndex(lines, line => line.Contains($" {methodName}(", StringComparison.Ordinal)
            || line.Contains($" {methodName} =", StringComparison.Ordinal));
        Assert.True(declarationLine >= 0, $"探针源码里找不到 {methodName}。");

        var nextDeclaration = Array.FindIndex(lines, declarationLine + 1, line =>
            line.StartsWith("    public ", StringComparison.Ordinal));
        var end = nextDeclaration < 0 ? lines.Length : nextDeclaration;

        var sites = result.Sites
            .Where(site => site.Path == ProbePath && site.Line > declarationLine && site.Line <= end)
            .ToArray();
        Assert.True(sites.Length > 0, $"{methodName} 里没有扫到任何强类型 Id 内部成员位点。");

        // 同一个方法里若出现多个位点（Join 的两个键选择器），判定必须一致。
        Assert.Single(sites.Select(site => site.Position).Distinct());
        return sites[0].Position;
    }

    // ── 阳性对照：查询树里必须被翻译的位置 ────────────────────────────────

    [Theory]
    [InlineData("Predicate", QueryPosition.Predicate)]
    [InlineData("Ordering", QueryPosition.Ordering)]
    [InlineData("NavigationSubqueryPredicate", QueryPosition.Predicate)]
    [InlineData("JoinKey", QueryPosition.Join)]
    [InlineData("Detached", QueryPosition.UnclassifiedInsideExpressionTree)]
    [InlineData("NonTerminalProjectionBeforePredicate", QueryPosition.NonTerminalProjection)]
    [InlineData("NonTerminalProjectionBeforeOrdering", QueryPosition.NonTerminalProjection)]
    public void Translated_query_positions_are_reported_as_fatal(string methodName, QueryPosition expected)
    {
        var result = Scan();

        Assert.Equal(expected, PositionIn(result, methodName));
        Assert.Contains(result.Fatal, site => site.Path == ProbePath);
    }

    /// <summary>
    /// ⭐ 导航属性子查询这一族：接收者 <c>x.Attempts</c> 是 <c>IReadOnlyCollection</c>、内层 lambda 被转成
    /// <c>Func&lt;&gt;</c>，票面原判据（按最近一层的接收者类型判）会把**整族**漏掉。
    /// 判据①要求沿祖先链全走，这一格钉的就是「不在第一层停下」。
    /// </summary>
    [Fact]
    public void Navigation_subquery_inside_a_predicate_is_not_missed_by_the_inner_delegate_lambda()
    {
        var result = Scan();

        var site = Assert.Single(result.Sites, candidate => candidate.Snippet == "a.Id.Id" && candidate.Fatal);
        Assert.Equal(QueryPosition.Predicate, site.Position);
        Assert.Equal("Any ← Where", site.OperatorChain);
    }

    // ── 阴性对照：合法写法不许误判 ────────────────────────────────────────

    [Theory]
    [InlineData("TerminalProjection", QueryPosition.Projection)]
    [InlineData("TerminalProjectionThenMaterialiser", QueryPosition.Projection)]
    [InlineData("NavigationSubqueryProjection", QueryPosition.Projection)]
    [InlineData("InMemoryPredicate", QueryPosition.OutsideExpressionTree)]
    [InlineData("AfterMaterialisation", QueryPosition.OutsideExpressionTree)]
    [InlineData("ValueConversion", QueryPosition.ModelConfiguration)]
    public void Legal_positions_are_not_reported(string methodName, QueryPosition expected)
    {
        var result = Scan();

        var position = PositionIn(result, methodName);
        Assert.Equal(expected, position);
        Assert.False(position.IsFatal(), $"{methodName} 的位置分类 {position} 被判致命，属于误报。");
    }

    /// <summary>
    /// ⭐ 终端 vs 非终端投影是**同一条 <c>Select</c> 上的成对格**：
    /// 终端投影是活在 main 上的合法写法（少了这一格，护栏一上线就对 8 处合法代码报红）；
    /// 而 <c>Select</c> 之后再接 <c>Where</c> / <c>OrderBy</c> 时 EF 会把投影提升进后继运算符一起翻译，
    /// 实测抛 <c>could not be translated</c> —— 只按「是不是 Select」判会放过真机必炸的那一半。
    /// </summary>
    [Fact]
    public void Terminal_and_non_terminal_projections_of_the_same_operator_are_separated()
    {
        var result = Scan();

        Assert.False(PositionIn(result, "TerminalProjection").IsFatal());
        Assert.False(PositionIn(result, "TerminalProjectionThenMaterialiser").IsFatal());
        Assert.False(PositionIn(result, "NavigationSubqueryProjection").IsFatal());

        Assert.True(PositionIn(result, "NonTerminalProjectionBeforePredicate").IsFatal());
        Assert.True(PositionIn(result, "NonTerminalProjectionBeforeOrdering").IsFatal());
    }

    // ── 哨兵格：证明变异真生效、探针不是恒真 ──────────────────────────────

    /// <summary>
    /// ⭐ 哨兵：关掉源生成器（其余不变）⇒ 强类型 Id 的 <c>Id</c> 成员在语义模型里不存在 ⇒
    /// <c>unresolved</c> 必须从 0 变成非 0、判出的位点必须归零。
    /// 这一格证明「unresolved == 0」不是恒真断言。
    /// </summary>
    [Fact]
    public void Sentinel_disabling_source_generators_turns_every_site_into_an_unresolved_reading()
    {
        var withGenerators = Scan();
        var withoutGenerators = Scan(runGenerators: false);

        Assert.Empty(withGenerators.Unresolved);
        Assert.NotEmpty(withGenerators.Sites);

        Assert.Empty(withoutGenerators.Sites);
        Assert.NotEmpty(withoutGenerators.Unresolved);
        Assert.All(
            withoutGenerators.Unresolved,
            site => Assert.Equal("member-symbol-unresolved", site.Reason));
    }

    /// <summary>
    /// CONTROL 无害变异（只改 lambda 形参名，不改语义）⇒ 全部判定必须逐条不变。
    /// 这一格只验跑法：证明上面那些格子的红绿是被变异内容驱动的，不是被「改没改文件」驱动的。
    /// </summary>
    [Fact]
    public void Control_renaming_a_lambda_parameter_changes_no_verdict()
    {
        var baseline = Scan();
        var renamed = Scan(ProbeSource.Replace(
            "query.Where(x => x.Id.Id == value).FirstOrDefault()",
            "query.Where(entity => entity.Id.Id == value).FirstOrDefault()",
            StringComparison.Ordinal));

        Assert.Equal(
            baseline.Sites.Select(site => $"{site.Line}:{site.Position}").ToArray(),
            renamed.Sites.Select(site => $"{site.Line}:{site.Position}").ToArray());
        Assert.Empty(renamed.Unresolved);
    }

    /// <summary>
    /// 覆盖边界写成断言：LINQ 查询语法不在判定面内，遇到必须记进
    /// <see cref="StronglyTypedIdScanResult.UncoveredSyntaxForms"/> 让门禁报红，⛔ 不是静默放过。
    /// </summary>
    [Fact]
    public void Query_syntax_is_declared_out_of_scope_and_reported_instead_of_silently_skipped()
    {
        const string querySyntax = """
            using System.Collections.Generic;
            using System.Linq;

            namespace Probe;

            public static class QuerySyntaxProbe
            {
                public static List<string> Run(IQueryable<Thing> query) =>
                    (from thing in query where thing.Id.Id != "" select thing.Status).ToList();
            }
            """;

        var result = Scan(querySyntax);

        Assert.Empty(result.Sites);
        var reported = Assert.Single(result.UncoveredSyntaxForms);
        Assert.Contains("LINQ 查询语法", reported, StringComparison.Ordinal);
    }

}
