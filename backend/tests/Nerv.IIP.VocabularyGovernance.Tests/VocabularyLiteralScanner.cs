using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Nerv.IIP.VocabularyGovernance.Tests;

/// <summary>
/// 一条白名单豁免：<see cref="Value"/> 是词表字面量取值，<see cref="RelativePath"/> 是被豁免文件
/// （相对 backend 根，正斜杠分隔），<see cref="Adjudication"/> 是中文裁决说明。
/// 豁免按（值 × 文件）二元组生效：同文件出现其他被守护值、或其他文件出现同值，仍然红。
/// </summary>
public sealed record VocabularyExemption(string Value, string RelativePath, string Adjudication);

/// <summary>
/// 扫描结果。<see cref="Violations"/> 是未被豁免的裸字面量违例；
/// <see cref="StaleExemptions"/> 是没有命中任何字面量的白名单条目——
/// 违例被真正修掉后必须同步删除豁免，否则红（防止白名单只进不出）。
/// </summary>
public sealed record VocabularyScanResult(
    IReadOnlyList<string> Violations,
    IReadOnlyList<string> StaleExemptions);

/// <summary>
/// 词表裸字面量扫描器：在给定源码文档里找出与任一被守护词表常量**取值完全相同**的字符串字面量。
///
/// 判定基于 Roslyn 语法树的字面量表达式节点（含 verbatim / raw / UTF-8 字面量，
/// 以及**无插值洞**的插值字符串），并用 token 的 <c>ValueText</c> 做序数比较：
/// 注释、XML doc、同名标识符、<c>nameof</c>、常量成员访问（即合规的常量引用形式）
/// 都不是字符串字面量节点，天然不会误报——这正是样板（MasterData 分析器）
/// 用语法/语义节点取代 <c>string.Contains</c> 文本匹配的原因。
/// </summary>
public static class VocabularyLiteralScanner
{
    public static VocabularyScanResult Scan(
        IReadOnlyCollection<VocabularyConstant> constants,
        IReadOnlyCollection<SourceDocument> documents,
        IReadOnlyCollection<VocabularyExemption> exemptions,
        IReadOnlyCollection<string> replicaFileNames)
    {
        var constantsByValue = constants
            .GroupBy(constant => constant.Value, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(constant => constant.Reference)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(reference => reference, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        var exemptionIndex = exemptions.ToDictionary(
            exemption => (exemption.Value, Normalize(exemption.RelativePath)),
            exemption => exemption);
        var usedExemptions = new HashSet<VocabularyExemption>();
        var violations = new List<(string Path, int Line, string Message)>();

        foreach (var document in documents)
        {
            var normalizedPath = Normalize(document.Path);
            if (replicaFileNames.Contains(GetFileName(normalizedPath), StringComparer.Ordinal))
            {
                // 跨服务逐字副本圈：副本一致性由 ReplicaConsistencyChecker 断言守护，
                // 副本内的字面量不按「禁止重复」处理（票面 #1703 (b) 类裁决）。
                continue;
            }

            var syntaxTree = CSharpSyntaxTree.ParseText(document.Text, path: normalizedPath);
            var root = syntaxTree.GetRoot();

            foreach (var (node, value) in EnumerateStringLiterals(root))
            {
                if (!constantsByValue.TryGetValue(value, out var references))
                {
                    continue;
                }

                if (exemptionIndex.TryGetValue((value, normalizedPath), out var exemption))
                {
                    usedExemptions.Add(exemption);
                    continue;
                }

                var line = syntaxTree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
                violations.Add((
                    normalizedPath,
                    line,
                    $"{normalizedPath}:{line}: 裸字面量 \"{value}\" 与词表常量同值"
                    + $"（候选：{string.Join("、", references)}）；"
                    + "同义请改为常量引用，同值不同义请在白名单登记中文裁决。"));
            }
        }

        var staleExemptions = exemptions
            .Where(exemption => !usedExemptions.Contains(exemption))
            .Select(exemption =>
                $"{Normalize(exemption.RelativePath)}: 白名单条目（值 \"{exemption.Value}\"）未命中任何字面量，"
                + $"违例已消失时必须同步删除豁免。裁决原文：{exemption.Adjudication}")
            .OrderBy(message => message, StringComparer.Ordinal)
            .ToArray();

        return new VocabularyScanResult(
            violations
                .OrderBy(violation => violation.Path, StringComparer.Ordinal)
                .ThenBy(violation => violation.Line)
                .ThenBy(violation => violation.Message, StringComparer.Ordinal)
                .Select(violation => violation.Message)
                .ToArray(),
            staleExemptions);
    }

    private static IEnumerable<(SyntaxNode Node, string Value)> EnumerateStringLiterals(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case LiteralExpressionSyntax literal
                    when literal.IsKind(SyntaxKind.StringLiteralExpression)
                        || literal.IsKind(SyntaxKind.Utf8StringLiteralExpression):
                    yield return (literal, literal.Token.ValueText);
                    break;
                case InterpolatedStringExpressionSyntax interpolated
                    when !interpolated.Contents.OfType<InterpolationSyntax>().Any():
                    // 无插值洞的插值字符串等价于普通字面量，纳入匹配堵住 $"..." 形式的绕过；
                    // 含插值洞的字符串没有静态完整取值，词表值作为拼接片段不在本门禁范围（见覆盖率声明）。
                    yield return (
                        interpolated,
                        string.Concat(
                            interpolated.Contents
                                .OfType<InterpolatedStringTextSyntax>()
                                .Select(content => content.TextToken.ValueText)));
                    break;
            }
        }
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string GetFileName(string normalizedPath)
    {
        var separatorIndex = normalizedPath.LastIndexOf('/');
        return separatorIndex < 0 ? normalizedPath : normalizedPath[(separatorIndex + 1)..];
    }
}
