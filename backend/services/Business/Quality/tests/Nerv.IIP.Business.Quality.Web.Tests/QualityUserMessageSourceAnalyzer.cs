using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed record QualitySourceDocument(string Path, string Text);

public sealed record QualityKnownExceptionSite(
    string Path,
    string TypeName,
    string MethodName,
    int DirectKnownExceptionCount,
    QualityKnownExceptionSiteKind Kind,
    string Reason)
{
    public string Key => $"{Path}|{TypeName}|{MethodName}";
}

public sealed record QualityExcludedSite(string Path, string TypeName, string MethodName, string Reason);

public enum QualityKnownExceptionSiteKind
{
    Target,
    Excluded,
}

public static class QualityUserMessageSourceAnalyzer
{
    private const string KnownExceptionTypeName = "NetCorePal.Extensions.Primitives.KnownException";

    public static IReadOnlyList<QualityKnownExceptionSite> Discover(
        IReadOnlyCollection<QualitySourceDocument> documents)
    {
        if (documents.Count == 0)
        {
            throw new InvalidOperationException("Quality KnownException 源集合不能为空。");
        }

        var sourceTrees = ParseTrees(documents);
        var compilation = CreateCompilation(sourceTrees);
        var discovered = new List<QualityKnownExceptionSite>();

        foreach (var syntaxTree in sourceTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            foreach (var method in syntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var typeName = method.Ancestors()
                    .OfType<TypeDeclarationSyntax>()
                    .Select(type => type.Identifier.ValueText)
                    .FirstOrDefault();
                if (typeName is null)
                {
                    continue;
                }

                var directKnownExceptionCount = method.DescendantNodes()
                    .OfType<BaseObjectCreationExpressionSyntax>()
                    .Count(creation =>
                        creation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() == method
                        && IsKnownException(semanticModel, creation));
                if (directKnownExceptionCount == 0)
                {
                    continue;
                }

                discovered.Add(new QualityKnownExceptionSite(
                    NormalizePath(syntaxTree.FilePath),
                    typeName,
                    method.Identifier.ValueText,
                    directKnownExceptionCount,
                    QualityKnownExceptionSiteKind.Excluded,
                    "discovered Quality KnownException site"));
            }
        }

        return discovered;
    }

    public static IReadOnlyList<string> Analyze(
        IReadOnlyCollection<QualitySourceDocument> documents,
        IReadOnlyCollection<QualityExcludedSite> excludedSites)
    {
        if (documents.Count == 0)
        {
            throw new InvalidOperationException("Quality 用户消息源集合不能为空。");
        }

        var sourceTrees = ParseTrees(documents);
        var compilation = CreateCompilation(sourceTrees);
        var excludedSiteKeys = excludedSites
            .Select(site => new ExcludedSiteKey(NormalizePath(site.Path), site.TypeName, site.MethodName))
            .ToHashSet();
        var violations = new List<Violation>();

        foreach (var syntaxTree in sourceTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            foreach (var creation in syntaxTree.GetRoot().DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
            {
                if (!IsKnownException(semanticModel, creation))
                {
                    continue;
                }

                var method = creation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                var methodName = method?.Identifier.ValueText ?? "<global>";
                var typeName = method?.Ancestors()
                    .OfType<TypeDeclarationSyntax>()
                    .Select(type => type.Identifier.ValueText)
                    .FirstOrDefault()
                    ?? "<global>";
                var siteKey = new ExcludedSiteKey(NormalizePath(syntaxTree.FilePath), typeName, methodName);
                if (excludedSiteKeys.Contains(siteKey))
                {
                    continue;
                }

                var firstArgument = creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
                if (firstArgument is not null)
                {
                    AddViolations(syntaxTree, firstArgument, violations);
                }
            }
        }

        return violations
            .OrderBy(violation => violation.Path, StringComparer.Ordinal)
            .ThenBy(violation => violation.Line)
            .ThenBy(violation => violation.Position)
            .ThenBy(violation => violation.Reason, StringComparer.Ordinal)
            .Select(violation => $"{violation.Path}:{violation.Line}: {violation.Reason}")
            .ToArray();
    }

    private static SyntaxTree[] ParseTrees(IReadOnlyCollection<QualitySourceDocument> documents) =>
        documents
            .Select(document => CSharpSyntaxTree.ParseText(document.Text, path: NormalizePath(document.Path)))
            .ToArray();

    private static CSharpCompilation CreateCompilation(IReadOnlyCollection<SyntaxTree> sourceTrees)
    {
        var syntaxTrees = sourceTrees
            .Append(CSharpSyntaxTree.ParseText(
                "global using NetCorePal.Extensions.Primitives;",
                path: "__QualityGlobalUsings.g.cs"))
            .ToArray();
        return CSharpCompilation.Create(
            "QualityUserMessageArchitecture",
            syntaxTrees,
            CreateMetadataReferences());
    }

    private static bool IsKnownException(
        SemanticModel semanticModel,
        BaseObjectCreationExpressionSyntax creation) =>
        semanticModel.GetTypeInfo(creation).Type?.ToDisplayString() == KnownExceptionTypeName;

    private static void AddViolations(
        SyntaxTree syntaxTree,
        ExpressionSyntax messageExpression,
        ICollection<Violation> violations)
    {
        var line = syntaxTree.GetLineSpan(messageExpression.Span).StartLinePosition.Line + 1;
        var path = syntaxTree.FilePath;
        if (!TryExtractMessage(messageExpression, out var message))
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
            violations.Add(new Violation(
                path,
                line,
                messageExpression.SpanStart,
                "用户消息必须包含中文。"));
        }
    }

    private static bool TryExtractMessage(ExpressionSyntax expression, out string message)
    {
        if (expression is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            message = literal.Token.ValueText;
            return true;
        }

        if (expression is InterpolatedStringExpressionSyntax interpolated)
        {
            message = string.Concat(interpolated.Contents
                .OfType<InterpolatedStringTextSyntax>()
                .Select(content => content.TextToken.ValueText));
            return true;
        }

        message = string.Empty;
        return false;
    }

    private static bool ContainsChinese(string message) =>
        message.Any(character => character is >= '\u3400' and <= '\u9fff');

    private static IReadOnlyCollection<MetadataReference> CreateMetadataReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var assemblyPaths = trustedPlatformAssemblies?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(typeof(KnownException).Assembly.Location)
            .Distinct(StringComparer.Ordinal)
            ?? [
                typeof(object).Assembly.Location,
                typeof(KnownException).Assembly.Location,
            ];

        return assemblyPaths
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private sealed record ExcludedSiteKey(string Path, string TypeName, string MethodName);

    private sealed record Violation(string Path, int Line, int Position, string Reason);
}
