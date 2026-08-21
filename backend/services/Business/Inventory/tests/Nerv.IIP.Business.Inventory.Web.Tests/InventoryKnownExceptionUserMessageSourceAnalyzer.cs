using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

internal sealed record InventoryKnownExceptionSourceDocument(string Path, string Text);

internal sealed record InventoryKnownExceptionSite(
    string Path,
    string TypeName,
    string MethodName,
    int DirectKnownExceptionCount,
    InventoryKnownExceptionSiteKind Kind,
    string Reason)
{
    public string Key => $"{Path}|{TypeName}|{MethodName}";
}

internal enum InventoryKnownExceptionSiteKind
{
    Target,
    Excluded,
}

internal sealed record InventoryPostingRejectedSite(
    string Path,
    string TypeName,
    string MethodName,
    int DirectConstructionCount,
    int FromDomainCallCount,
    InventoryKnownExceptionSiteKind Kind,
    string Reason)
{
    public string Key => $"{Path}|{TypeName}|{MethodName}";
}

internal static class InventoryKnownExceptionUserMessageSourceAnalyzer
{
    private const string KnownExceptionTypeName = "NetCorePal.Extensions.Primitives.KnownException";
    private const string InventoryPostingRejectedExceptionTypeName =
        "Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements.InventoryPostingRejectedException";
    private const int InterpolationEstimatedLength = 12;
    private const int MaximumMessageLength = 60;

    public static IReadOnlyList<InventoryKnownExceptionSite> DiscoverKnownExceptions(
        IReadOnlyCollection<InventoryKnownExceptionSourceDocument> documents)
    {
        if (documents.Count == 0)
        {
            throw new InvalidOperationException("Inventory KnownException 源集合不能为空。");
        }

        var sourceTrees = ParseTrees(documents);
        var compilation = CreateCompilation(sourceTrees, "InventoryKnownExceptionDiscovery");
        var discovered = new List<InventoryKnownExceptionSite>();

        foreach (var syntaxTree in sourceTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            foreach (var method in syntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var typeName = GetContainingTypeName(method);
                if (typeName is null)
                {
                    continue;
                }

                var directKnownExceptionCount = method.DescendantNodes()
                    .OfType<BaseObjectCreationExpressionSyntax>()
                    .Count(creation =>
                        creation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() == method
                        && IsTransportVisibleKnownExceptionType(semanticModel, creation));
                if (directKnownExceptionCount == 0)
                {
                    continue;
                }

                discovered.Add(new InventoryKnownExceptionSite(
                    NormalizePath(syntaxTree.FilePath),
                    typeName,
                    MethodName(method),
                    directKnownExceptionCount,
                    InventoryKnownExceptionSiteKind.Excluded,
                    "discovered Inventory KnownException site"));
            }
        }

        return discovered;
    }

    public static IReadOnlyList<InventoryPostingRejectedSite> DiscoverPostingRejectedCalls(
        IReadOnlyCollection<InventoryKnownExceptionSourceDocument> documents)
    {
        if (documents.Count == 0)
        {
            throw new InvalidOperationException("InventoryPostingRejectedException 源集合不能为空。");
        }

        var sourceTrees = ParseTrees(documents);
        var compilation = CreateCompilation(sourceTrees, "InventoryPostingRejectedDiscovery");
        var discovered = new Dictionary<string, InventoryPostingRejectedSiteBuilder>(StringComparer.Ordinal);

        foreach (var syntaxTree in sourceTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();
            foreach (var creation in root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
            {
                if (!IsType(semanticModel, creation, InventoryPostingRejectedExceptionTypeName))
                {
                    continue;
                }

                var key = GetSiteKey(syntaxTree, creation);
                if (key is null)
                {
                    continue;
                }

                if (!discovered.TryGetValue(key.Key, out var builder))
                {
                    builder = new InventoryPostingRejectedSiteBuilder(key.Path, key.TypeName, key.MethodName);
                    discovered.Add(key.Key, builder);
                }
                builder.DirectConstructionCount++;
            }

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (symbol is null
                    || symbol.Name != "FromDomain"
                    || symbol.ContainingType.ToDisplayString() != InventoryPostingRejectedExceptionTypeName)
                {
                    continue;
                }

                var key = GetSiteKey(syntaxTree, invocation);
                if (key is null)
                {
                    continue;
                }

                if (!discovered.TryGetValue(key.Key, out var builder))
                {
                    builder = new InventoryPostingRejectedSiteBuilder(key.Path, key.TypeName, key.MethodName);
                    discovered.Add(key.Key, builder);
                }
                builder.FromDomainCallCount++;
            }
        }

        return discovered.Values
            .Select(builder => new InventoryPostingRejectedSite(
                builder.Path,
                builder.TypeName,
                builder.MethodName,
                builder.DirectConstructionCount,
                builder.FromDomainCallCount,
                InventoryKnownExceptionSiteKind.Excluded,
                "discovered InventoryPostingRejectedException call site"))
            .OrderBy(site => site.Key, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string> AnalyzeKnownExceptionMessages(
        IReadOnlyCollection<InventoryKnownExceptionSourceDocument> documents,
        IReadOnlyCollection<InventoryKnownExceptionSite> excludedSites)
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
        IReadOnlyCollection<InventoryPostingRejectedSite> excludedSites)
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

        if (!TryExtractMessage(messageExpression, out var message, out var estimatedLength))
        {
            violations.Add(new Violation(
                path,
                line,
                messageExpression.SpanStart,
                "用户消息必须是可静态分析的字符串字面量或插值字符串。"));
            return;
        }

        if (!ContainsChinese(message))
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

    private static bool TryExtractMessage(
        ExpressionSyntax expression,
        out string message,
        out int estimatedLength)
    {
        if (expression is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            message = literal.Token.ValueText;
            estimatedLength = message.Length;
            return true;
        }

        if (expression is InterpolatedStringExpressionSyntax interpolated)
        {
            var fixedText = interpolated.Contents
                .OfType<InterpolatedStringTextSyntax>()
                .Select(content => content.TextToken.ValueText)
                .ToArray();
            message = string.Concat(fixedText);
            estimatedLength = fixedText.Sum(text => text.Length)
                + (interpolated.Contents.OfType<InterpolationSyntax>().Count() * InterpolationEstimatedLength);
            return true;
        }

        message = string.Empty;
        estimatedLength = 0;
        return false;
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

    private static bool ContainsChinese(string message) =>
        message.Any(character => character is >= '\u3400' and <= '\u9fff');

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

    private sealed class InventoryPostingRejectedSiteBuilder(string path, string typeName, string methodName)
    {
        public string Path { get; } = path;
        public string TypeName { get; } = typeName;
        public string MethodName { get; } = methodName;
        public int DirectConstructionCount { get; set; }
        public int FromDomainCallCount { get; set; }
    }

    private sealed record Violation(string Path, int Line, int Position, string Reason);
}
