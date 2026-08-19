using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Nerv.IIP.VocabularyGovernance.Tests;

/// <summary>一条副本分裂：<see cref="MemberKey"/> 是「类型名.成员键」，<see cref="Message"/> 是中文说明。</summary>
public sealed record ReplicaDrift(string MemberKey, string Message);

/// <summary>
/// 跨服务逐字副本一致性检查器（票面 #1703 (b) 类：<c>WorldHistorySpec.cs</c> 等复制圈）。
///
/// 对同名副本文件按**成员**对齐比较：凡在两份以上副本中出现的同名成员
/// （方法按「名字 + 参数表」区分重载），其源码文本必须逐字相同——
/// 一侧改了另一侧没跟上即红（先例：<c>WorldHistoryShortageComponentGoldenVector.cs</c> 的 Digest 分叉思路）。
/// 允许单侧存在的服务专属成员（如 Mes 副本的 <c>PlanningSuggestionIdForSalesOrder</c>）
/// 与 namespace / using 行差异，因此比较单位是成员而不是整个文件。
/// 成员文本取 <c>SyntaxNode.ToString()</c>（不含前导 XML doc / 注释 trivia），
/// 即只钉语义载体，文档注释措辞差异不在断言范围。
/// </summary>
public static class ReplicaConsistencyChecker
{
    public static IReadOnlyList<ReplicaDrift> Check(IReadOnlyCollection<SourceDocument> replicaDocuments)
    {
        // key -> (成员文本 -> 出现该文本的文件列表)
        var membersByKey = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);

        foreach (var document in replicaDocuments)
        {
            var root = CSharpSyntaxTree.ParseText(document.Text, path: document.Path).GetRoot();
            foreach (var type in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (type.Parent is BaseTypeDeclarationSyntax)
                {
                    // 嵌套类型作为外层类型的成员整体比较，不再单独展开。
                    continue;
                }

                if (type is not TypeDeclarationSyntax typeDeclaration)
                {
                    RecordMember(membersByKey, MemberKey(type), type.ToString(), document.Path);
                    continue;
                }

                // 类型头（修饰符/关键字/类型参数/主构造参数表/基类表）单独作为伪成员比较：
                // 位置记录（positional record）没有花括号成员，参数表分叉只能在这里被看见。
                RecordMember(
                    membersByKey,
                    $"{typeDeclaration.Identifier.ValueText}.<类型头>",
                    TypeHeaderText(typeDeclaration),
                    document.Path);

                foreach (var member in typeDeclaration.Members)
                {
                    RecordMember(
                        membersByKey,
                        $"{typeDeclaration.Identifier.ValueText}.{MemberKey(member)}",
                        member.ToString(),
                        document.Path);
                }
            }
        }

        var drifts = new List<ReplicaDrift>();
        foreach (var (key, texts) in membersByKey.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (texts.Count <= 1)
            {
                continue;
            }

            var groups = texts
                .Select(entry => $"[{string.Join("、", entry.Value.OrderBy(path => path, StringComparer.Ordinal))}]")
                .OrderBy(group => group, StringComparer.Ordinal);
            drifts.Add(new ReplicaDrift(
                key,
                $"副本成员 {key} 在同名副本间出现 {texts.Count} 种不同文本，副本圈要求逐字相同；"
                + $"分裂面：{string.Join(" vs ", groups)}"));
        }

        return drifts;
    }

    private static void RecordMember(
        Dictionary<string, Dictionary<string, List<string>>> membersByKey,
        string key,
        string text,
        string path)
    {
        if (!membersByKey.TryGetValue(key, out var texts))
        {
            texts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            membersByKey[key] = texts;
        }

        if (!texts.TryGetValue(text, out var paths))
        {
            paths = [];
            texts[text] = paths;
        }

        paths.Add(path);
    }

    private static string TypeHeaderText(TypeDeclarationSyntax type) =>
        string.Concat(
            type.Modifiers.ToString(),
            " ",
            type.Keyword.ValueText,
            " ",
            type.Identifier.ValueText,
            type.TypeParameterList?.ToString(),
            type.ParameterList?.ToString(),
            type.BaseList?.ToString());

    private static string MemberKey(MemberDeclarationSyntax member) =>
        member switch
        {
            MethodDeclarationSyntax method => $"{method.Identifier.ValueText}{method.ParameterList}",
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            FieldDeclarationSyntax field => string.Join(
                ",",
                field.Declaration.Variables.Select(variable => variable.Identifier.ValueText)),
            BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
            ConstructorDeclarationSyntax constructor =>
                $"{constructor.Identifier.ValueText}{constructor.ParameterList}",
            _ => member.ToString(),
        };
}
