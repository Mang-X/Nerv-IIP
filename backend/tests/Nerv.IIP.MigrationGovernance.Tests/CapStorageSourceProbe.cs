using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Nerv.IIP.MigrationGovernance.Tests;

/// <summary>一处源码违例：<see cref="Line"/> 是 1 基行号，<see cref="Shape"/> 是形状名。</summary>
internal sealed record SourceProbeFinding(int Line, string Shape, string Detail);

/// <summary>
/// #3124 源码面的语法分析。**用 Roslyn 而不是手写词法**：本仓已在 14 个测试项目里用
/// <c>Microsoft.CodeAnalysis.CSharp</c>，最近的邻居就是 <c>Nerv.IIP.VocabularyGovernance.Tests</c>
/// 的 <c>VocabularyLiteralScanner</c>（同样是「字面量取值 vs 注释/标识符」的判定）。
///
/// 上一版是手写字符级词法器，被复审实测出三类错判，其中两类在仓库里有真实实例：
///   - 插值洞没有按 hole 解析：洞里的嵌套字面量两面都查不到，洞里的成员访问反被当成字面量吞掉；
///   - <c>#region Don't touch</c> 里的单引号被当 char literal，吞到 EOF，整文件失防；
///   - <c>@"""</c> 因为「先数引号再看 verbatim」被误判成 raw string——
///     <c>MesInventoryLocationDeploymentConfigurationTests.cs</c> 正是这个形状。
/// 这些在 Roslyn 里不是「多覆盖几种」，而是本来就解析正确：
///   - 插值洞是 <see cref="InterpolationSyntax"/> 子节点，与文本段天然分开；
///   - verbatim / raw / 普通 / UTF-8 字面量各有自己的 <see cref="SyntaxKind"/>，不靠数引号；
///   - 预处理指令是 trivia，<c>DescendantNodes()</c> 默认不进 trivia，注释与 XML doc 同理；
///   - <c>Token.ValueText</c> 直接给还原后的取值，<c>\uXXXX</c> 一并解决。
///
/// 两个判定面**共用同一棵语法树**（因此不是互相独立的两道防线，见 <c>CoverageBoundaryNotice</c>），
/// 但解析本身是 Roslyn 的实现而不是本仓手写的。
/// </summary>
internal static class CapStorageSourceProbe
{
    internal static IReadOnlyList<SourceProbeFinding> Analyze(
        string path,
        string text,
        IReadOnlyCollection<string> tableNames,
        IReadOnlyCollection<string> dbSetPropertyNames,
        IReadOnlyCollection<string> capEntityTypeNames,
        IReadOnlyCollection<string> nonQueryDeclarationApis)
    {
        var tree = CSharpSyntaxTree.ParseText(text, path: path);
        var root = tree.GetRoot();
        var findings = new List<SourceProbeFinding>();

        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                // 面 1：表名以字符串字面量出现。含 verbatim、raw、UTF-8；插值字符串的文本段在下面单独取，
                // 插值洞里的嵌套字面量本身也是 LiteralExpressionSyntax，会在这里被独立命中。
                case LiteralExpressionSyntax literal
                    when literal.IsKind(SyntaxKind.StringLiteralExpression)
                        || literal.IsKind(SyntaxKind.Utf8StringLiteralExpression):
                    AddLiteralFinding(literal, literal.Token.ValueText);
                    break;

                // 面 2a：DbSet 属性访问（db.PublishedMessages）。标识符节点，注释里写同样的字样不会命中。
                case MemberAccessExpressionSyntax memberAccess
                    when dbSetPropertyNames.Contains(memberAccess.Name.Identifier.ValueText, StringComparer.Ordinal):
                    findings.Add(new SourceProbeFinding(
                        LineOf(memberAccess.Name),
                        "DbSet property access",
                        memberAccess.Name.Identifier.ValueText));
                    break;

                // 面 2b：泛型 Set<T>() 调用，要求有接收者——DbContext 自身实现接口时的裸调用是声明惯用法。
                // 类型实参取最右标识符，因此全限定写法也会命中。
                case MemberAccessExpressionSyntax { Name: GenericNameSyntax generic } genericAccess
                    when string.Equals(generic.Identifier.ValueText, "Set", StringComparison.Ordinal)
                        && generic.TypeArgumentList.Arguments.Count == 1
                        && capEntityTypeNames.Contains(
                            RightmostIdentifier(generic.TypeArgumentList.Arguments[0]),
                            StringComparer.Ordinal):
                    findings.Add(new SourceProbeFinding(
                        LineOf(genericAccess.Name),
                        "DbContext.Set<T>() access",
                        RightmostIdentifier(generic.TypeArgumentList.Arguments[0])));
                    break;
            }
        }

        // 插值字符串的文本段：$"...{schema}.cap_published_messages" 的表名活在这里。
        foreach (var textSegment in root.DescendantNodes().OfType<InterpolatedStringTextSyntax>())
        {
            AddLiteralFinding(textSegment, textSegment.TextToken.ValueText);
        }

        return findings;

        void AddLiteralFinding(SyntaxNode node, string value)
        {
            var matched = tableNames.FirstOrDefault(
                name => value.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (matched is null || IsNonQueryDeclarationArgument(node, nonQueryDeclarationApis))
            {
                return;
            }

            findings.Add(new SourceProbeFinding(LineOf(node), "string literal", matched));
        }
    }

    /// <summary>
    /// 字面量是否是某个非查询声明 API 的实参：取最近的 <see cref="InvocationExpressionSyntax"/> 祖先，
    /// 比对它的方法简单名。语法定位，不再靠「回退到最近的分号」那种文本启发式。
    /// </summary>
    private static bool IsNonQueryDeclarationArgument(
        SyntaxNode node,
        IReadOnlyCollection<string> nonQueryDeclarationApis)
    {
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null)
        {
            return false;
        }

        var callee = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            SimpleNameSyntax simpleName => simpleName.Identifier.ValueText,
            _ => null,
        };

        return callee is not null && nonQueryDeclarationApis.Contains(callee, StringComparer.Ordinal);
    }

    private static string RightmostIdentifier(TypeSyntax type) => type switch
    {
        QualifiedNameSyntax qualified => RightmostIdentifier(qualified.Right),
        AliasQualifiedNameSyntax aliased => RightmostIdentifier(aliased.Name),
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        _ => type.ToString(),
    };

    private static int LineOf(SyntaxNode node) =>
        node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
}
