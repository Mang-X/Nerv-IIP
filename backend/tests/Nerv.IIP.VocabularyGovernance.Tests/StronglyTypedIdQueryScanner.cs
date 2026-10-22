using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Nerv.IIP.VocabularyGovernance.Tests;

/// <summary>取到强类型 Id 内部成员的那一处代码在查询里的位置分类。</summary>
public enum QueryPosition
{
    /// <summary>不在任何 <c>Expression&lt;&gt;</c> lambda 内：内存集合/委托，EF 不参与，合法。</summary>
    OutsideExpressionTree,

    /// <summary>
    /// **终端**投影位（<c>Select</c> / <c>SelectMany</c> 的选择器，且该投影之后链上没有会被翻译的后继运算符）：
    /// EF 允许终端投影客户端求值，合法。
    /// </summary>
    Projection,

    /// <summary>
    /// **非终端**投影位：<c>Select</c> 之后链上还有 <c>Where</c> / <c>OrderBy</c> 等会被翻译的运算符。
    /// ⭐ EF 会把非终端 <c>Select</c> 提升进后继运算符一起翻译 ——
    /// 实测 <c>.Select(x =&gt; x.Id.Id).Where(…)</c> 与 <c>.Select(x =&gt; x.Id.Id).OrderBy(…)</c>
    /// 都抛 <c>could not be translated</c>，而 <c>.Select(x =&gt; x.Id.Id)</c> 终端不抛。致命。
    /// </summary>
    NonTerminalProjection,

    /// <summary>谓词位（<c>Where</c> / <c>Any</c> / <c>All</c> / <c>First</c>… 的谓词）：必须被翻译，致命。</summary>
    Predicate,

    /// <summary>排序位（<c>OrderBy</c> / <c>ThenBy</c>…）：必须被翻译，致命。</summary>
    Ordering,

    /// <summary>Join 位（<c>Join</c> / <c>GroupJoin</c> 的键选择器）：必须被翻译，致命。</summary>
    Join,

    /// <summary>分组键位（<c>GroupBy</c>）：必须被翻译，致命。</summary>
    Grouping,

    /// <summary>聚合选择器位（<c>Sum</c> / <c>Min</c> / <c>Max</c> / <c>Average</c>）：必须被翻译，致命。</summary>
    Aggregate,

    /// <summary>
    /// EF 模型配置位（<c>Microsoft.EntityFrameworkCore.Metadata.Builders.*</c> 上的 lambda，
    /// 典型是 <c>HasConversion(x =&gt; x.Id, x =&gt; new XId(x))</c> 的值转换器声明本身）。
    /// 这里的表达式**不进查询翻译**，它定义的正是「Id 怎么转成列值」，合法。
    /// </summary>
    ModelConfiguration,

    /// <summary>
    /// 在表达式树内，但外层没有可识别的 LINQ 运算符（例如整棵树先存进变量再传给 <c>Where</c>）。
    /// ⭐ fail-closed 判致命：判不出来时按最坏情况报红，而不是放过。
    /// </summary>
    UnclassifiedInsideExpressionTree,
}

/// <summary>位置分类到「是否致命」的唯一换算口径。</summary>
public static class QueryPositionExtensions
{
    /// <summary>
    /// 致命 = 该表达式必须被查询提供程序翻译成 SQL，而值转换过的强类型 Id 取内部成员翻译不了（#3098）。
    /// ⭐ 默认值是致命：新增的分类若忘了在这里登记，会走 fail-closed 分支报红而不是被放过。
    /// </summary>
    public static bool IsFatal(this QueryPosition position) => position switch
    {
        QueryPosition.OutsideExpressionTree => false,
        QueryPosition.Projection => false,
        QueryPosition.ModelConfiguration => false,
        _ => true,
    };
}

/// <summary>一处「对强类型 Id 再取内部成员」的位点。</summary>
public sealed record StronglyTypedIdInnerMemberSite(
    string Path,
    int Line,
    int Column,
    string Snippet,
    string IdType,
    string Member,
    QueryPosition Position,
    string OperatorChain)
{
    public bool InExpressionTree => Position != QueryPosition.OutsideExpressionTree;

    /// <summary>是否是 #3098 那个形状（查询树里必须被翻译的位置），即会真机 500 的那类。</summary>
    public bool Fatal => Position.IsFatal();

    public string Describe() =>
        $"{Path}:{Line}:{Column} [{Position}] {IdType}.{Member} — {Snippet}"
        + (OperatorChain.Length == 0 ? string.Empty : $"（运算符链：{OperatorChain}）");
}

/// <summary>判不出来的位点。⭐ 这个集合非空 ⇒ 门禁报红（fail-closed），⛔ 不允许静默少扫。</summary>
public sealed record UnresolvedInnerMemberSite(string Path, int Line, int Column, string Reason, string Snippet)
{
    public string Describe() => $"{Path}:{Line}:{Column} [{Reason}] {Snippet}";
}

public sealed record StronglyTypedIdScanResult(
    IReadOnlyList<StronglyTypedIdInnerMemberSite> Sites,
    IReadOnlyList<UnresolvedInnerMemberSite> Unresolved,
    IReadOnlyList<string> UncoveredSyntaxForms)
{
    public IReadOnlyList<StronglyTypedIdInnerMemberSite> Fatal =>
        Sites.Where(site => site.Fatal).ToArray();

    public IReadOnlyList<StronglyTypedIdInnerMemberSite> InExpressionTree =>
        Sites.Where(site => site.InExpressionTree).ToArray();
}

/// <summary>
/// #3292 · 「在查询树里对值转换过的强类型 Id 属性再取内部成员」的 symbol 级判定。
///
/// 判据（两条，取代票面原写的「接收者是 <c>IQueryable&lt;T&gt;</c> 还是 <c>IEnumerable&lt;T&gt;</c>」——
/// 那条已被实测打穿两头：导航属性子查询整族假阴、终端投影位 7 处假阳）：
/// <list type="number">
/// <item>该节点是否位于**任一祖先** <c>Expression&lt;&gt;</c> lambda 内（沿祖先链全走到方法边界，
/// ⛔ 不是只看最近一层：<c>Where(x =&gt; x.Attempts.Any(a =&gt; a.Id.Id == v))</c> 的内层 lambda
/// 被转成 <c>Func&lt;&gt;</c>，但整棵树仍是表达式树）。</item>
/// <item>位置分类：谓词 / 排序 / Join / 分组 / 聚合位 **vs** 投影位 —— 只有前者致命。
/// 沿祖先链把每一层 LINQ 运算符都分类，**任一层非投影即致命**；全是投影才判合法。</item>
/// </list>
///
/// 覆盖边界（写成断言而非声明，见 <see cref="UncoveredSyntaxForms"/>）：
/// <list type="bullet">
/// <item>已覆盖：<c>a.b.Id</c>（SimpleMemberAccess）与 <c>a?.Id</c>（ConditionalAccess / MemberBinding）。</item>
/// <item>未覆盖：LINQ **查询语法**（<c>from … where … select …</c>）。backend 非测试源码当前零处使用；
/// 一旦出现，扫描器把它记进 <see cref="UncoveredSyntaxForms"/> 让门禁报红，⛔ 不是静默放过。</item>
/// </list>
/// </summary>
public static class StronglyTypedIdQueryScanner
{
    private const string StronglyTypedIdNamespace = "NetCorePal.Extensions.Domain";
    private const string ExpressionMetadataName = "System.Linq.Expressions.Expression`1";

    /// <summary>
    /// 会把 lambda 交给查询提供程序翻译的运算符族。⭐ 名单是「哪些类型的方法算查询运算符」，
    /// 不是「哪些位点豁免」；族外一律 fail-closed 判致命。
    /// </summary>
    private static readonly HashSet<string> QueryOperatorContainingTypes = new(StringComparer.Ordinal)
    {
        "System.Linq.Queryable",
        "System.Linq.Enumerable",
        "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions",
        "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions",
    };

    /// <summary>EF 模型配置 API 所在命名空间；这里的表达式定义映射本身，不进查询翻译。</summary>
    private static readonly string[] ModelConfigurationNamespaces =
    [
        "Microsoft.EntityFrameworkCore.Metadata.Builders",
        "Microsoft.EntityFrameworkCore.ChangeTracking",
    ];

    private static readonly HashSet<string> ProjectionOperators = new(StringComparer.Ordinal)
    {
        "Select", "SelectMany",
    };

    private static readonly HashSet<string> PredicateOperators = new(StringComparer.Ordinal)
    {
        "Where", "Any", "All", "Count", "LongCount", "First", "FirstOrDefault",
        "Single", "SingleOrDefault", "Last", "LastOrDefault", "SkipWhile", "TakeWhile",
    };

    private static readonly HashSet<string> OrderingOperators = new(StringComparer.Ordinal)
    {
        "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending",
    };

    private static readonly HashSet<string> JoinOperators = new(StringComparer.Ordinal)
    {
        "Join", "GroupJoin",
    };

    private static readonly HashSet<string> GroupingOperators = new(StringComparer.Ordinal)
    {
        "GroupBy",
    };

    private static readonly HashSet<string> AggregateOperators = new(StringComparer.Ordinal)
    {
        "Sum", "Min", "Max", "Average", "MinBy", "MaxBy", "Aggregate",
    };

    /// <summary>
    /// 物化运算符：跟在投影之后**不会**把投影推进 SQL 翻译面。
    /// ⚠️ 只在「该次调用不带 lambda 实参」时才算物化——<c>FirstOrDefault(s =&gt; s == v)</c>
    /// 是对投影结果做谓词，仍要翻译。
    /// </summary>
    private static readonly HashSet<string> MaterializingOperators = new(StringComparer.Ordinal)
    {
        "ToList", "ToListAsync", "ToArray", "ToArrayAsync", "ToHashSet", "ToHashSetAsync",
        "AsEnumerable", "AsAsyncEnumerable", "ToAsyncEnumerable",
        "First", "FirstAsync", "FirstOrDefault", "FirstOrDefaultAsync",
        "Single", "SingleAsync", "SingleOrDefault", "SingleOrDefaultAsync",
        "Last", "LastAsync", "LastOrDefault", "LastOrDefaultAsync",
    };

    public static StronglyTypedIdScanResult Scan(
        CSharpCompilation compilation,
        IReadOnlyCollection<string> scannedPaths)
    {
        var paths = new HashSet<string>(scannedPaths, StringComparer.Ordinal);
        var sites = new List<StronglyTypedIdInnerMemberSite>();
        var unresolved = new List<UnresolvedInnerMemberSite>();
        var uncovered = new List<string>();

        foreach (var syntaxTree in compilation.SyntaxTrees.Where(tree => paths.Contains(tree.FilePath)))
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            foreach (var node in root.DescendantNodes())
            {
                var access = MemberAccess.From(node);
                if (access is null)
                {
                    continue;
                }

                Inspect(compilation, semanticModel, syntaxTree, access, sites, unresolved, uncovered);
            }
        }

        return new StronglyTypedIdScanResult(
            sites.OrderBy(site => site.Path, StringComparer.Ordinal).ThenBy(site => site.Line).ThenBy(site => site.Column).ToArray(),
            unresolved.OrderBy(site => site.Path, StringComparer.Ordinal).ThenBy(site => site.Line).ThenBy(site => site.Column).ToArray(),
            uncovered.OrderBy(text => text, StringComparer.Ordinal).ToArray());
    }

    private static void Inspect(
        CSharpCompilation compilation,
        SemanticModel semanticModel,
        SyntaxTree syntaxTree,
        MemberAccess access,
        List<StronglyTypedIdInnerMemberSite> sites,
        List<UnresolvedInnerMemberSite> unresolved,
        List<string> uncovered)
    {
        var receiverType = semanticModel.GetTypeInfo(access.Receiver).Type;

        // ⭐ fail-closed 第一路：接收者类型压根没解析出来（宿主包并集漏了哪个包就会长这样）。
        // 只对「成员名是 Id」的节点计数——强类型 Id 的内部成员一律叫 Id（四个 netcorepal 变体都是）。
        if (receiverType is null or IErrorTypeSymbol)
        {
            if (access.MemberName == "Id")
            {
                unresolved.Add(Unresolved(syntaxTree, access, "receiver-type-unresolved"));
            }

            return;
        }

        if (!IsStronglyTypedId(receiverType))
        {
            return;
        }

        // ⭐ fail-closed 第二路：接收者确实是强类型 Id，但成员符号解析不出来 ——
        // 这正是「源生成器没跑」的形态（Id 成员是生成的，源码里没有）。哨兵用例钉的就是这一路。
        var symbol = semanticModel.GetSymbolInfo(access.Node).Symbol;
        if (symbol is null)
        {
            unresolved.Add(Unresolved(syntaxTree, access, "member-symbol-unresolved"));
            return;
        }

        if (symbol is not (IPropertySymbol or IFieldSymbol))
        {
            return;
        }

        // 只认**声明在强类型 Id 类型自己身上**的成员；object / IEquatable 上的成员不算「取内部成员」。
        if (!SymbolEqualityComparer.Default.Equals(symbol.ContainingType, receiverType))
        {
            return;
        }

        // ⭐ 覆盖边界写成断言：LINQ 查询语法里的位点判不了，记进 uncovered 让门禁报红，⛔ 不静默放过。
        if (access.Node.Ancestors().OfType<QueryExpressionSyntax>().Any())
        {
            uncovered.Add(
                $"{Location(syntaxTree, access.Node)}: LINQ 查询语法里的强类型 Id 内部成员不在本扫描器判定面内"
                + $"（{Snippet(access.Node)}）；请改写成方法语法，或扩展扫描器覆盖查询语法。");
            return;
        }

        var (position, chain) = Classify(compilation, semanticModel, access.Node);
        var lineSpan = syntaxTree.GetLineSpan(access.Node.Span);
        sites.Add(new StronglyTypedIdInnerMemberSite(
            syntaxTree.FilePath,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1,
            Snippet(access.Node),
            receiverType.ToDisplayString(),
            symbol.Name,
            position,
            chain));
    }

    /// <summary>
    /// 判据①+②：沿祖先链走到方法边界，收集每一层 lambda 的转换类型与它所在的 LINQ 运算符位置。
    /// ⛔ 不在第一层 lambda 处停下。
    /// </summary>
    private static (QueryPosition Position, string OperatorChain) Classify(
        CSharpCompilation compilation,
        SemanticModel semanticModel,
        SyntaxNode node)
    {
        var expressionType = compilation.GetTypeByMetadataName(ExpressionMetadataName);
        var inExpressionTree = false;
        var positions = new List<QueryPosition>();
        var chain = new List<string>();

        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is MemberDeclarationSyntax or LocalFunctionStatementSyntax)
            {
                break;
            }

            if (ancestor is not AnonymousFunctionExpressionSyntax lambda)
            {
                continue;
            }

            var converted = semanticModel.GetTypeInfo(lambda).ConvertedType;
            if (IsExpressionOfT(converted, expressionType))
            {
                inExpressionTree = true;
            }

            var (position, name) = ClassifyLambdaPosition(semanticModel, lambda);
            positions.Add(position);
            chain.Add(name);
        }

        if (!inExpressionTree)
        {
            return (QueryPosition.OutsideExpressionTree, string.Join(" ← ", chain));
        }

        var operatorChain = string.Join(" ← ", chain);

        if (positions.Count == 0)
        {
            return (QueryPosition.UnclassifiedInsideExpressionTree, operatorChain);
        }

        // 任一层既非投影也非模型配置 ⇒ 致命；全是投影/模型配置 ⇒ 合法。
        var verdict = positions.FirstOrDefault(
            candidate => candidate is not (QueryPosition.Projection or QueryPosition.ModelConfiguration),
            positions[0]);
        return (verdict, operatorChain);
    }

    private static (QueryPosition Position, string Name) ClassifyLambdaPosition(
        SemanticModel semanticModel,
        AnonymousFunctionExpressionSyntax lambda)
    {
        if (lambda.Parent is not ArgumentSyntax argument
            || argument.Parent is not ArgumentListSyntax argumentList
            || argumentList.Parent is not InvocationExpressionSyntax invocation)
        {
            return (QueryPosition.UnclassifiedInsideExpressionTree, "<非运算符实参>");
        }

        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return (QueryPosition.UnclassifiedInsideExpressionTree, "<运算符未解析>");
        }

        var containingType = method.ContainingType?.ToDisplayString() ?? string.Empty;
        var containingNamespace = method.ContainingType?.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        // EF 模型配置（值转换器声明、属性/键选择器）不是查询翻译面。
        if (ModelConfigurationNamespaces.Any(prefix =>
                containingNamespace == prefix
                || containingNamespace.StartsWith(prefix + ".", StringComparison.Ordinal)))
        {
            return (QueryPosition.ModelConfiguration, $"{method.Name}@{containingType}");
        }

        // ⭐ 只有「真的会被查询提供程序翻译」的运算符族才按名字分类；
        // ⛔ 族外的 API（自定义仓储的 Expression 形参、Moq、FluentValidation…）一律走 fail-closed 的
        // UnclassifiedInsideExpressionTree 分支判致命，不假定它安全。
        if (!QueryOperatorContainingTypes.Contains(containingType))
        {
            return (QueryPosition.UnclassifiedInsideExpressionTree, $"{method.Name}@{containingType}");
        }

        var name = method.Name;

        if (ProjectionOperators.Contains(name))
        {
            return IsTerminalProjection(semanticModel, invocation)
                ? (QueryPosition.Projection, name)
                : (QueryPosition.NonTerminalProjection, name);
        }

        if (PredicateOperators.Contains(name))
        {
            return (QueryPosition.Predicate, name);
        }

        if (OrderingOperators.Contains(name))
        {
            return (QueryPosition.Ordering, name);
        }

        if (JoinOperators.Contains(name))
        {
            return (QueryPosition.Join, name);
        }

        if (GroupingOperators.Contains(name))
        {
            return (QueryPosition.Grouping, name);
        }

        if (AggregateOperators.Contains(name))
        {
            return (QueryPosition.Aggregate, name);
        }

        return (QueryPosition.UnclassifiedInsideExpressionTree, name);
    }

    /// <summary>
    /// 该投影是不是**终端**投影：沿方法链往后走，只要还遇到一个会被翻译的查询运算符就不是。
    /// 允许继续往后走的只有「不带 lambda 实参的物化运算符」（<c>ToListAsync</c> / <c>FirstOrDefault()</c> …），
    /// 它们不会把投影推进 SQL。
    /// ⭐ 后继运算符判不出来（不在查询运算符族里、符号解析不了）一律按**非终端**处理，fail-closed。
    /// </summary>
    private static bool IsTerminalProjection(SemanticModel semanticModel, InvocationExpressionSyntax invocation)
    {
        ExpressionSyntax current = invocation;
        while (true)
        {
            if (current.Parent is not MemberAccessExpressionSyntax memberAccess
                || memberAccess.Expression.Span != current.Span)
            {
                // 链到头：这个投影就是最后一步。
                return true;
            }

            if (memberAccess.Parent is not InvocationExpressionSyntax next)
            {
                // 后面是属性访问（`.Length` 之类），不再有查询运算符。
                return true;
            }

            if (semanticModel.GetSymbolInfo(next).Symbol is not IMethodSymbol method
                || !QueryOperatorContainingTypes.Contains(method.ContainingType?.ToDisplayString() ?? string.Empty))
            {
                return false;
            }

            if (!MaterializingOperators.Contains(method.Name)
                || next.ArgumentList.Arguments.Any(argument => argument.Expression is AnonymousFunctionExpressionSyntax))
            {
                return false;
            }

            current = next;
        }
    }

    private static bool IsExpressionOfT(ITypeSymbol? type, INamedTypeSymbol? expressionType) =>
        type is INamedTypeSymbol named
        && expressionType is not null
        && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, expressionType);

    private static bool IsStronglyTypedId(ITypeSymbol type) =>
        type.AllInterfaces.Any(@interface =>
            @interface.Name.EndsWith("StronglyTypedId", StringComparison.Ordinal)
            && @interface.ContainingNamespace?.ToDisplayString() == StronglyTypedIdNamespace);

    private static UnresolvedInnerMemberSite Unresolved(SyntaxTree syntaxTree, MemberAccess access, string reason)
    {
        var lineSpan = syntaxTree.GetLineSpan(access.Node.Span);
        return new UnresolvedInnerMemberSite(
            syntaxTree.FilePath,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1,
            reason,
            Snippet(access.Node));
    }

    private static string Location(SyntaxTree syntaxTree, SyntaxNode node)
    {
        var lineSpan = syntaxTree.GetLineSpan(node.Span);
        return $"{syntaxTree.FilePath}:{lineSpan.StartLinePosition.Line + 1}";
    }

    private static string Snippet(SyntaxNode node)
    {
        var text = string.Join(' ', node.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= 120 ? text : text[..120] + "…";
    }

    /// <summary>统一 <c>a.b.Id</c> 与 <c>a?.Id</c> 两种取成员语法。</summary>
    private sealed record MemberAccess(SyntaxNode Node, ExpressionSyntax Receiver, string MemberName)
    {
        public static MemberAccess? From(SyntaxNode node) => node switch
        {
            MemberAccessExpressionSyntax memberAccess
                when memberAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression) =>
                new MemberAccess(memberAccess, memberAccess.Expression, memberAccess.Name.Identifier.ValueText),
            MemberBindingExpressionSyntax memberBinding
                when memberBinding.Ancestors().OfType<ConditionalAccessExpressionSyntax>().FirstOrDefault() is { } conditional =>
                new MemberAccess(memberBinding, conditional.Expression, memberBinding.Name.Identifier.ValueText),
            _ => null,
        };
    }
}
