using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Web.Tests;

public sealed record NotificationSourceDocument(string Path, string Text);

public sealed record NotificationKnownExceptionSite(
    string Path,
    string TypeName,
    string MethodName,
    int DirectKnownExceptionCount,
    string Reason)
{
    public string Key => $"{Path}|{TypeName}|{MethodName}";
}

public sealed record NotificationExcludedKnownExceptionSite(
    string Path,
    string TypeName,
    string MethodName,
    int DirectKnownExceptionCount,
    string Reason)
{
    public string Key => $"{Path}|{TypeName}|{MethodName}";
}

public static class NotificationUserMessageSourceAnalyzer
{
    private const string KnownExceptionTypeName = "NetCorePal.Extensions.Primitives.KnownException";
    private const int InterpolationEstimatedLength = 12;
    private const int MaximumMessageLength = 60;

    public static IReadOnlyList<NotificationKnownExceptionSite> Discover(
        IReadOnlyCollection<NotificationSourceDocument> documents)
    {
        if (documents.Count == 0)
        {
            throw new InvalidOperationException("Notification 用户消息源集合不能为空。");
        }

        var sourceTrees = documents
            .Select(document => CSharpSyntaxTree.ParseText(document.Text, path: NormalizePath(document.Path)))
            .ToArray();
        var syntaxTrees = sourceTrees
            .Append(CSharpSyntaxTree.ParseText(
                "global using NetCorePal.Extensions.Primitives;",
                path: "__NotificationGlobalUsings.g.cs"))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "NotificationKnownExceptionMessageArchitecture",
            syntaxTrees,
            CreateMetadataReferences());

        var sites = new Dictionary<SiteKey, int>();
        foreach (var syntaxTree in sourceTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();
            foreach (var creation in root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
            {
                if (!IsKnownException(semanticModel, creation))
                {
                    continue;
                }

                var site = GetSite(syntaxTree, creation);
                if (site is null)
                {
                    continue;
                }

                sites[site.Value] = sites.TryGetValue(site.Value, out var count) ? count + 1 : 1;
            }
        }

        return sites
            .OrderBy(pair => pair.Key.Path, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.TypeName, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.MethodName, StringComparer.Ordinal)
            .Select(pair => new NotificationKnownExceptionSite(
                pair.Key.Path,
                pair.Key.TypeName,
                pair.Key.MethodName,
                pair.Value,
                "direct KnownException construction"))
            .ToArray();
    }

    public static IReadOnlyList<string> Analyze(
        IReadOnlyCollection<NotificationSourceDocument> documents,
        IReadOnlyCollection<NotificationExcludedKnownExceptionSite> excludedSites)
    {
        if (documents.Count == 0)
        {
            throw new InvalidOperationException("Notification 用户消息源集合不能为空。");
        }

        var sourceTrees = documents
            .Select(document => CSharpSyntaxTree.ParseText(document.Text, path: NormalizePath(document.Path)))
            .ToArray();
        var syntaxTrees = sourceTrees
            .Append(CSharpSyntaxTree.ParseText(
                "global using NetCorePal.Extensions.Primitives;",
                path: "__NotificationGlobalUsings.g.cs"))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "NotificationKnownExceptionMessageArchitecture",
            syntaxTrees,
            CreateMetadataReferences());
        var excludedSiteKeys = excludedSites
            .Select(site => NormalizeSiteKey(site.Key))
            .ToHashSet(StringComparer.Ordinal);
        var violations = new List<Violation>();

        foreach (var syntaxTree in sourceTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();
            foreach (var creation in root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
            {
                if (!IsKnownException(semanticModel, creation))
                {
                    continue;
                }

                var site = GetSite(syntaxTree, creation);
                if (site is not null && excludedSiteKeys.Contains(site.Value.Key))
                {
                    continue;
                }

                var firstArgument = creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
                AddViolations(syntaxTree, firstArgument, violations);
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

    private static bool IsKnownException(
        SemanticModel semanticModel,
        BaseObjectCreationExpressionSyntax creation) =>
        semanticModel.GetTypeInfo(creation).Type?.ToDisplayString() == KnownExceptionTypeName;

    private static SiteKey? GetSite(SyntaxTree syntaxTree, BaseObjectCreationExpressionSyntax creation)
    {
        var method = creation.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
        if (method is null)
        {
            return null;
        }

        var typeName = method.Ancestors()
            .OfType<TypeDeclarationSyntax>()
            .Select(type => type.Identifier.ValueText)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        return new SiteKey(
            NormalizePath(syntaxTree.FilePath),
            typeName,
            GetMethodName(method));
    }

    private static string GetMethodName(BaseMethodDeclarationSyntax method) =>
        method switch
        {
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
            DestructorDeclarationSyntax destructor => $"~{destructor.Identifier.ValueText}",
            OperatorDeclarationSyntax @operator => @operator.OperatorToken.ValueText,
            ConversionOperatorDeclarationSyntax conversion => conversion.Type.ToString(),
            _ => ((MethodDeclarationSyntax)method).Identifier.ValueText,
        };

    private static void AddViolations(
        SyntaxTree syntaxTree,
        ExpressionSyntax? messageExpression,
        ICollection<Violation> violations)
    {
        var span = messageExpression?.Span ?? new TextSpan(0, 0);
        var line = syntaxTree.GetLineSpan(span).StartLinePosition.Line + 1;
        var path = NormalizePath(syntaxTree.FilePath);

        if (messageExpression is null
            || !TryExtractMessage(messageExpression, out var message, out var estimatedLength))
        {
            violations.Add(new Violation(
                path,
                line,
                span.Start,
                "用户消息必须是可静态分析的字符串字面量或插值字符串。"));
            return;
        }

        if (!ContainsChinese(message))
        {
            violations.Add(new Violation(
                path,
                line,
                span.Start,
                "用户消息必须包含中文。"));
        }

        if (estimatedLength > MaximumMessageLength)
        {
            violations.Add(new Violation(
                path,
                line,
                span.Start,
                "用户消息估算长度不能超过 60 个字符。"));
        }

        if (message.Any(char.IsControl))
        {
            violations.Add(new Violation(
                path,
                line,
                span.Start,
                "用户消息不得包含控制字符。"));
        }

        if (message.Any(character => character is '<' or '>' or '{' or '}' or '/' or '\\'))
        {
            violations.Add(new Violation(
                path,
                line,
                span.Start,
                "用户消息不得包含不安全符号。"));
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

    private static string NormalizeSiteKey(string key) => key.Replace('\\', '/');

    private readonly record struct SiteKey(string Path, string TypeName, string MethodName)
    {
        public string Key => $"{Path}|{TypeName}|{MethodName}";
    }

    private sealed record Violation(string Path, int Line, int Position, string Reason);
}
