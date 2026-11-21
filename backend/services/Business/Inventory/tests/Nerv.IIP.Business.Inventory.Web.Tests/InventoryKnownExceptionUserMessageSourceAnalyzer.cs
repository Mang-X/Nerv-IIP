using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

internal sealed record InventoryKnownExceptionSourceDocument(string Path, string Text);

internal sealed record InventoryExcludedMessageSite(string Path, string TypeName, string MethodName, string Reason)
{
    public string Key => $"{Path}|{TypeName}|{MethodName}";
}

internal static class InventoryKnownExceptionUserMessageSourceAnalyzer
{
    private const string KnownExceptionTypeName = "NetCorePal.Extensions.Primitives.KnownException";
    private const string InventoryPostingRejectedExceptionTypeName =
        "Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements.InventoryPostingRejectedException";
    private const int MaximumMessageLength = 60;

    public static IReadOnlyList<string> AnalyzeKnownExceptionMessages(
        IReadOnlyCollection<InventoryKnownExceptionSourceDocument> documents,
        IReadOnlyCollection<InventoryExcludedMessageSite> excludedSites)
    {
        if (documents.Count == 0)
        {
            throw new InvalidOperationException("Inventory KnownException 源集合不能为空。");
        }

        var sourceTrees = ParseTrees(documents);
        var compilation = CreateCompilation(sourceTrees, "InventoryKnownExceptionMessageAnalysis");
        var excludedSiteKeys = excludedSites
            .Select(site => site.Key)
            .ToHashSet(StringComparer.Ordinal);
        var violations = new List<Violation>();

        foreach (var syntaxTree in sourceTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            foreach (var creation in syntaxTree.GetRoot().DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
            {
                if (!IsTransportVisibleKnownExceptionType(semanticModel, creation))
                {
                    continue;
                }

                var key = GetSiteKey(syntaxTree, creation);
                if (key is null || excludedSiteKeys.Contains(key.Key))
                {
                    continue;
                }

                var firstArgument = creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
                if (firstArgument is null)
                {
                    violations.Add(new Violation(
                        NormalizePath(syntaxTree.FilePath),
                        syntaxTree.GetLineSpan(creation.Span).StartLinePosition.Line + 1,
                        creation.SpanStart,
                        "用户消息必须提供可静态分析的首个参数。"));
                    continue;
                }

                AddMessageViolations(syntaxTree, firstArgument, violations);
            }
        }

        return FormatViolations(violations);
    }

    public static IReadOnlyList<string> AnalyzePostingRejectedMessages(
        IReadOnlyCollection<InventoryKnownExceptionSourceDocument> documents,
        IReadOnlyCollection<InventoryExcludedMessageSite> excludedSites)
    {
        if (documents.Count == 0)
        {
            throw new InvalidOperationException("InventoryPostingRejectedException 源集合不能为空。");
        }

        var sourceTrees = ParseTrees(documents);
        var compilation = CreateCompilation(sourceTrees, "InventoryPostingRejectedMessageAnalysis");
        var excludedSiteKeys = excludedSites
            .Select(site => site.Key)
            .ToHashSet(StringComparer.Ordinal);
        var violations = new List<Violation>();

        foreach (var syntaxTree in sourceTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            foreach (var creation in syntaxTree.GetRoot().DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
            {
                if (!IsType(semanticModel, creation, InventoryPostingRejectedExceptionTypeName))
                {
                    continue;
                }

                var key = GetSiteKey(syntaxTree, creation);
                if (key is null || excludedSiteKeys.Contains(key.Key))
                {
                    continue;
                }

                var messageArgument = creation.ArgumentList?.Arguments.Skip(1).FirstOrDefault()?.Expression;
                if (messageArgument is null)
                {
                    violations.Add(new Violation(
                        NormalizePath(syntaxTree.FilePath),
                        syntaxTree.GetLineSpan(creation.Span).StartLinePosition.Line + 1,
                        creation.SpanStart,
                        "库存过账拒绝必须提供可静态分析的失败消息。"));
                    continue;
                }

                AddMessageViolations(syntaxTree, messageArgument, violations);
            }
        }

        return FormatViolations(violations);
    }

    private static void AddMessageViolations(
        SyntaxTree syntaxTree,
        ExpressionSyntax messageExpression,
        ICollection<Violation> violations)
    {
        var line = syntaxTree.GetLineSpan(messageExpression.Span).StartLinePosition.Line + 1;
        var path = NormalizePath(syntaxTree.FilePath);

        if (!UserMessageText.TryExtractMessage(messageExpression, out var message, out var estimatedLength))
        {
            violations.Add(new Violation(
                path,
                line,
                messageExpression.SpanStart,
                "用户消息必须是可静态分析的字符串字面量或插值字符串。"));
            return;
        }

        if (!UserMessageText.ContainsChinese(message))
        {
            violations.Add(new Violation(path, line, messageExpression.SpanStart, "用户消息必须包含中文。"));
        }

        if (estimatedLength > MaximumMessageLength)
        {
            violations.Add(new Violation(path, line, messageExpression.SpanStart, "用户消息估算长度不能超过 60 个字符。"));
        }

        if (message.Any(character => char.IsControl(character) || character is '<' or '>' or '{' or '}' or '/' or '\\'))
        {
            violations.Add(new Violation(path, line, messageExpression.SpanStart, "用户消息不能包含控制字符或不安全分隔符。"));
        }
    }

    private static SyntaxTree[] ParseTrees(IReadOnlyCollection<InventoryKnownExceptionSourceDocument> documents) =>
        documents
            .Select(document => CSharpSyntaxTree.ParseText(document.Text, path: NormalizePath(document.Path)))
            .ToArray();

    private static CSharpCompilation CreateCompilation(
        IReadOnlyCollection<SyntaxTree> sourceTrees,
        string assemblyName)
    {
        var syntaxTrees = sourceTrees
            .Append(CSharpSyntaxTree.ParseText(
                "global using NetCorePal.Extensions.Primitives;",
                path: "__InventoryKnownExceptionGlobalUsings.g.cs"))
            .ToArray();
        return CSharpCompilation.Create(assemblyName, syntaxTrees, CreateMetadataReferences());
    }

    private static bool IsType(
        SemanticModel semanticModel,
        BaseObjectCreationExpressionSyntax creation,
        string typeName) =>
        semanticModel.GetTypeInfo(creation).Type?.ToDisplayString() == typeName;

    private static bool IsTransportVisibleKnownExceptionType(
        SemanticModel semanticModel,
        BaseObjectCreationExpressionSyntax creation)
    {
        var type = semanticModel.GetTypeInfo(creation).Type as INamedTypeSymbol;
        if (type is null || type.ToDisplayString() == InventoryPostingRejectedExceptionTypeName)
        {
            return false;
        }

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == KnownExceptionTypeName)
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetContainingTypeName(SyntaxNode node) =>
        node.Ancestors()
            .OfType<TypeDeclarationSyntax>()
            .Select(type => type.Identifier.ValueText)
            .FirstOrDefault();

    private static SiteKey? GetSiteKey(SyntaxTree syntaxTree, SyntaxNode node)
    {
        var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        var typeName = method is null ? null : GetContainingTypeName(method);
        if (method is null || typeName is null)
        {
            return null;
        }

        var path = NormalizePath(syntaxTree.FilePath);
        return new SiteKey(path, typeName, MethodName(method));
    }

    private static string MethodName(MethodDeclarationSyntax method) =>
        method.Identifier.ValueText + (method.TypeParameterList is null ? string.Empty : method.TypeParameterList.ToString());

    private static IReadOnlyCollection<MetadataReference> CreateMetadataReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var assemblyPaths = trustedPlatformAssemblies?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(typeof(KnownException).Assembly.Location)
            .Append(typeof(InventoryPostingRejectedException).Assembly.Location)
            .Distinct(StringComparer.Ordinal)
            ?? [
                typeof(object).Assembly.Location,
                typeof(KnownException).Assembly.Location,
                typeof(InventoryPostingRejectedException).Assembly.Location,
            ];

        return assemblyPaths
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static IReadOnlyList<string> FormatViolations(IEnumerable<Violation> violations) =>
        violations
            .OrderBy(violation => violation.Path, StringComparer.Ordinal)
            .ThenBy(violation => violation.Line)
            .ThenBy(violation => violation.Position)
            .ThenBy(violation => violation.Reason, StringComparer.Ordinal)
            .Select(violation => $"{violation.Path}:{violation.Line}: {violation.Reason}")
            .ToArray();

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private sealed record SiteKey(string Path, string TypeName, string MethodName)
    {
        public string Key => $"{Path}|{TypeName}|{MethodName}";
    }

    private sealed record Violation(string Path, int Line, int Position, string Reason);
}
