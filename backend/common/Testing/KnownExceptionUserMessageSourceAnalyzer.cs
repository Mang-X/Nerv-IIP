using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Testing;

internal sealed record UserMessageSourceDocument(string Path, string Text);

internal sealed record ExcludedUserMessageSite(string Path, string TypeName, string MethodName, string Reason);

internal enum UserMessagePolicy
{
    StaticChinese,
    SafeText,
    RequiredSafeText,
    NoDownstreamDiagnostics,
}

internal static class KnownExceptionUserMessageSourceAnalyzer
{
    private const string KnownExceptionTypeName = "NetCorePal.Extensions.Primitives.KnownException";
    private const int InterpolationEstimatedLength = 12;
    private const int MaximumMessageLength = 60;

    public static IReadOnlyList<string> Analyze(
        IReadOnlyCollection<UserMessageSourceDocument> documents,
        IReadOnlyCollection<ExcludedUserMessageSite> excludedSites,
        UserMessagePolicy policy = UserMessagePolicy.SafeText)
    {
        if (documents.Count == 0)
        {
            throw new InvalidOperationException("用户消息源集合不能为空。");
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
            var root = syntaxTree.GetRoot();
            foreach (var creation in root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
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
                    AddViolations(syntaxTree, firstArgument, policy, violations);
                }
                else if (policy is UserMessagePolicy.NoDownstreamDiagnostics or UserMessagePolicy.RequiredSafeText)
                {
                    var line = syntaxTree.GetLineSpan(creation.Span).StartLinePosition.Line + 1;
                    violations.Add(new Violation(
                        syntaxTree.FilePath,
                        line,
                        creation.SpanStart,
                        policy == UserMessagePolicy.RequiredSafeText
                            ? "用户消息必须提供可静态分析的首个参数。"
                            : "用户消息不能为空。"));
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

    private static SyntaxTree[] ParseTrees(IReadOnlyCollection<UserMessageSourceDocument> documents) =>
        documents
            .Select(document => CSharpSyntaxTree.ParseText(document.Text, path: NormalizePath(document.Path)))
            .ToArray();

    private static CSharpCompilation CreateCompilation(IReadOnlyCollection<SyntaxTree> sourceTrees)
    {
        var syntaxTrees = sourceTrees
            .Append(CSharpSyntaxTree.ParseText(
                "global using NetCorePal.Extensions.Primitives;",
                path: "__UserMessageGlobalUsings.g.cs"))
            .ToArray();
        return CSharpCompilation.Create(
            "KnownExceptionUserMessageArchitecture",
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
        UserMessagePolicy policy,
        ICollection<Violation> violations)
    {
        var line = syntaxTree.GetLineSpan(messageExpression.Span).StartLinePosition.Line + 1;
        var path = syntaxTree.FilePath;

        if (policy == UserMessagePolicy.NoDownstreamDiagnostics
            && messageExpression is InterpolatedStringExpressionSyntax interpolated
            && ContainsDiagnosticInterpolation(interpolated))
        {
            violations.Add(new Violation(path, line, messageExpression.SpanStart, "用户消息不能透传动态异常文本。"));
            return;
        }

        if (!TryExtractMessage(messageExpression, out var message, out var estimatedLength))
        {
            violations.Add(new Violation(
                path,
                line,
                messageExpression.SpanStart,
                policy == UserMessagePolicy.NoDownstreamDiagnostics
                    ? "用户消息必须是静态字符串或插值字符串，不能透传动态异常文本。"
                    : "用户消息必须是可静态分析的字符串字面量或插值字符串。"));
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

        if (policy == UserMessagePolicy.StaticChinese)
        {
            return;
        }

        if (estimatedLength > MaximumMessageLength)
        {
            violations.Add(new Violation(
                path,
                line,
                messageExpression.SpanStart,
                "用户消息估算长度不能超过 60 个字符。"));
        }

        if (policy is not (UserMessagePolicy.SafeText or UserMessagePolicy.RequiredSafeText))
        {
            return;
        }

        if (message.Any(char.IsControl))
        {
            violations.Add(new Violation(
                path,
                line,
                messageExpression.SpanStart,
                "用户消息不能包含控制字符。"));
        }

        if (message.Any(character => character is '<' or '>' or '{' or '}' or '/' or '\\'))
        {
            violations.Add(new Violation(
                path,
                line,
                messageExpression.SpanStart,
                "用户消息不能包含不安全字符。"));
        }
    }

    private static bool ContainsDiagnosticInterpolation(InterpolatedStringExpressionSyntax interpolated)
    {
        var method = interpolated.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        var catchVariables = method?.DescendantNodes()
            .OfType<CatchDeclarationSyntax>()
            .Select(catchDeclaration => catchDeclaration.Identifier.ValueText)
            .Where(identifier => !string.IsNullOrEmpty(identifier))
            .ToHashSet(StringComparer.Ordinal)
            ?? [];

        foreach (var interpolation in interpolated.Contents.OfType<InterpolationSyntax>())
        {
            if (interpolation.Expression
                .DescendantNodesAndSelf()
                .OfType<IdentifierNameSyntax>()
                .Any(identifier => catchVariables.Contains(identifier.Identifier.ValueText)))
            {
                return true;
            }

            if (interpolation.Expression
                .DescendantNodesAndSelf()
                .OfType<MemberAccessExpressionSyntax>()
                .Any(member => member.Name.Identifier.ValueText is "Message" or "Content" or "ReasonPhrase" or "StatusCode" or "InnerException"))
            {
                return true;
            }
        }

        return false;
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

    private sealed record ExcludedSiteKey(string Path, string TypeName, string MethodName);

    private sealed record Violation(string Path, int Line, int Position, string Reason);
}
