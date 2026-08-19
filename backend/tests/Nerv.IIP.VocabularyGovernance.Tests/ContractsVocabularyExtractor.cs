using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Nerv.IIP.VocabularyGovernance.Tests;

/// <summary>一份参与扫描的源码文档（磁盘文件或 probe 用例里的内存源码串）。</summary>
public sealed record SourceDocument(string Path, string Text);

/// <summary>
/// 一条被守护的词表常量：<see cref="Reference"/> 是常量的完全限定引用
/// （如 <c>Nerv.IIP.Contracts.Approval.ApprovalSourceServices.BusinessErp</c>），
/// <see cref="Value"/> 是它的字面量取值。
/// </summary>
public sealed record VocabularyConstant(string Reference, string Value);

/// <summary>
/// 词表常量抽取结果。<see cref="Errors"/> 非空表示存在无法静态求值或取值为空串的常量——
/// 抽取失败必须显式红掉，不允许静默缩小守护集合（fail-closed）。
/// </summary>
public sealed record VocabularyExtractionResult(
    IReadOnlyList<VocabularyConstant> Constants,
    IReadOnlyList<string> Errors);

/// <summary>
/// 从 <c>Nerv.IIP.Contracts.*</c> 源码里按**类型系统**穷举被守护的词表常量：
/// 凡「public static class 中 public const string 字段」一律入守护集合，
/// 不维护任何「要查哪些值」的手写清单——新增词表常量自动进入守护范围。
/// 求值走 Roslyn 语义模型（<c>IFieldSymbol.ConstantValue</c>），
/// 因此常量拼接（如 <c>Prefix + "-x"</c>）也能得到最终取值。
/// </summary>
public static class ContractsVocabularyExtractor
{
    private const string ContractsNamespacePrefix = "Nerv.IIP.Contracts";

    public static VocabularyExtractionResult Extract(IReadOnlyCollection<SourceDocument> documents)
    {
        var syntaxTrees = documents
            .Select(document => CSharpSyntaxTree.ParseText(document.Text, path: document.Path))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "ContractsVocabulary",
            syntaxTrees,
            CreateMetadataReferences());

        var constants = new List<VocabularyConstant>();
        var errors = new List<string>();

        foreach (var syntaxTree in syntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
            {
                foreach (var variable in field.Declaration.Variables)
                {
                    if (semanticModel.GetDeclaredSymbol(variable) is not IFieldSymbol symbol)
                    {
                        continue;
                    }

                    if (!IsGuardedVocabularyConstant(symbol))
                    {
                        continue;
                    }

                    var reference = $"{symbol.ContainingType.ToDisplayString()}.{symbol.Name}";
                    if (!symbol.HasConstantValue || symbol.ConstantValue is not string value)
                    {
                        errors.Add($"{reference}: 词表常量必须能被静态求值为字符串。");
                        continue;
                    }

                    if (value.Length == 0)
                    {
                        errors.Add($"{reference}: 词表常量不允许取值为空串（空串会让扫描匹配失去意义）。");
                        continue;
                    }

                    constants.Add(new VocabularyConstant(reference, value));
                }
            }
        }

        return new VocabularyExtractionResult(
            constants
                .OrderBy(constant => constant.Reference, StringComparer.Ordinal)
                .ToArray(),
            errors
                .OrderBy(error => error, StringComparer.Ordinal)
                .ToArray());
    }

    private static bool IsGuardedVocabularyConstant(IFieldSymbol symbol) =>
        symbol.IsConst
        && symbol.DeclaredAccessibility == Accessibility.Public
        && symbol.Type.SpecialType == SpecialType.System_String
        && symbol.ContainingType is { IsStatic: true, DeclaredAccessibility: Accessibility.Public }
        && IsContractsNamespace(symbol.ContainingType.ContainingNamespace);

    private static bool IsContractsNamespace(INamespaceSymbol? namespaceSymbol)
    {
        if (namespaceSymbol is null || namespaceSymbol.IsGlobalNamespace)
        {
            return false;
        }

        var display = namespaceSymbol.ToDisplayString();
        return display == ContractsNamespacePrefix
            || display.StartsWith(ContractsNamespacePrefix + ".", StringComparison.Ordinal);
    }

    private static IReadOnlyCollection<MetadataReference> CreateMetadataReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var assemblyPaths = trustedPlatformAssemblies?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            ?? [typeof(object).Assembly.Location];

        return assemblyPaths
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}
