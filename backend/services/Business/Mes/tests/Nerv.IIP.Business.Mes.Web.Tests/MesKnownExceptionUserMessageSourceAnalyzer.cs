using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed record MesSourceDocument(string Path, string Text);

public sealed record MesKnownExceptionSite(
    string Path,
    string TypeName,
    string MemberName,
    int DirectKnownExceptionCount,
    MesKnownExceptionSiteKind Kind,
    string Reason)
{
    public string Key => $"{Path}|{TypeName}|{MemberName}";
}

public enum MesKnownExceptionSiteKind
{
    Target,
    Excluded,
}

public static class MesKnownExceptionUserMessageSourceAnalyzer
{
    private const string KnownExceptionTypeName = "NetCorePal.Extensions.Primitives.KnownException";
    private const int InterpolationEstimatedLength = 12;
    private const int MaximumMessageLength = 60;

    public static IReadOnlyList<MesKnownExceptionSite> Discover(
        IReadOnlyCollection<MesSourceDocument> documents)
    {
        if (documents.Count == 0)
        {
            throw new InvalidOperationException("MES KnownException 源集合不能为空。");
        }

        var sourceTrees = ParseTrees(documents);
        var compilation = CreateCompilation(sourceTrees);
        var discovered = new Dictionary<string, MesKnownExceptionSite>(StringComparer.Ordinal);

        foreach (var syntaxTree in sourceTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            foreach (var invocation in FindKnownExceptionInvocations(syntaxTree, semanticModel))
            {
                var member = FindMember(invocation);
                var typeName = member?.AncestorsAndSelf()
                    .OfType<TypeDeclarationSyntax>()
                    .Select(type => type.Identifier.ValueText)
                    .FirstOrDefault() ?? "<global>";
                var memberName = GetMemberName(member);
                var path = NormalizePath(syntaxTree.FilePath);
                var key = $"{path}|{typeName}|{memberName}";
                if (discovered.TryGetValue(key, out var existing))
                {
                    discovered[key] = existing with
                    {
                        DirectKnownExceptionCount = existing.DirectKnownExceptionCount + 1,
                    };
                }
                else
                {
                    discovered[key] = new MesKnownExceptionSite(
                        path,
                        typeName,
                        memberName,
                        1,
                        MesKnownExceptionSiteKind.Excluded,
                        "discovered MES KnownException site");
                }
            }
        }

        return discovered.Values
            .OrderBy(site => site.Path, StringComparer.Ordinal)
            .ThenBy(site => site.TypeName, StringComparer.Ordinal)
            .ThenBy(site => site.MemberName, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string> Analyze(
        IReadOnlyCollection<MesSourceDocument> documents,
        IReadOnlyCollection<MesKnownExceptionSite> excludedSites)
    {
        if (documents.Count == 0)
        {
            throw new InvalidOperationException("MES 用户消息源集合不能为空。");
        }

        var sourceTrees = ParseTrees(documents);
        var compilation = CreateCompilation(sourceTrees);
        var excludedSiteKeys = excludedSites
            .Select(site => site.Key)
            .ToHashSet(StringComparer.Ordinal);
        var violations = new List<Violation>();

        foreach (var syntaxTree in sourceTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            foreach (var invocation in FindKnownExceptionInvocations(syntaxTree, semanticModel))
            {
                var member = FindMember(invocation);
                var typeName = member?.AncestorsAndSelf()
                    .OfType<TypeDeclarationSyntax>()
                    .Select(type => type.Identifier.ValueText)
                    .FirstOrDefault() ?? "<global>";
                var siteKey = $"{NormalizePath(syntaxTree.FilePath)}|{typeName}|{GetMemberName(member)}";
                if (excludedSiteKeys.Contains(siteKey))
                {
                    continue;
                }

                var firstArgument = GetFirstArgument(invocation);
                if (firstArgument is null)
                {
                    AddViolation(
                        syntaxTree,
                        invocation,
                        "用户消息必须提供可静态分析的首个参数。",
                        violations);
                    continue;
                }

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

    private static IEnumerable<SyntaxNode> FindKnownExceptionInvocations(
        SyntaxTree syntaxTree,
        SemanticModel semanticModel)
    {
        foreach (var creation in syntaxTree.GetRoot().DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
        {
            if (semanticModel.GetTypeInfo(creation).Type?.ToDisplayString() == KnownExceptionTypeName)
            {
                yield return creation;
            }
        }

        foreach (var constructor in syntaxTree.GetRoot().DescendantNodes().OfType<ConstructorDeclarationSyntax>())
        {
            var initializer = constructor.Initializer;
            if (initializer is not null &&
                semanticModel.GetSymbolInfo(initializer).Symbol is IMethodSymbol symbol &&
                symbol.ContainingType.ToDisplayString() == KnownExceptionTypeName)
            {
                yield return initializer;
            }
        }

        foreach (var baseType in syntaxTree.GetRoot().DescendantNodes().OfType<PrimaryConstructorBaseTypeSyntax>())
        {
            if (semanticModel.GetSymbolInfo(baseType).Symbol is IMethodSymbol symbol &&
                symbol.ContainingType.ToDisplayString() == KnownExceptionTypeName)
            {
                yield return baseType;
            }
        }
    }

    private static ExpressionSyntax? GetFirstArgument(SyntaxNode invocation) => invocation switch
    {
        BaseObjectCreationExpressionSyntax creation => creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression,
        ConstructorInitializerSyntax initializer => initializer.ArgumentList.Arguments.FirstOrDefault()?.Expression,
        PrimaryConstructorBaseTypeSyntax baseType => baseType.ArgumentList.Arguments.FirstOrDefault()?.Expression,
        _ => null,
    };

    private static MemberDeclarationSyntax? FindMember(SyntaxNode invocation) => invocation
        .AncestorsAndSelf()
        .OfType<MemberDeclarationSyntax>()
        .FirstOrDefault();

    private static string GetMemberName(MemberDeclarationSyntax? member) => member switch
    {
        MethodDeclarationSyntax method => method.Identifier.ValueText,
        ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
        TypeDeclarationSyntax => ".ctor",
        PropertyDeclarationSyntax property => property.Identifier.ValueText,
        FieldDeclarationSyntax field => field.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText ?? "<field>",
        _ => "<global>",
    };

    private static SyntaxTree[] ParseTrees(IReadOnlyCollection<MesSourceDocument> documents) => documents
        .Select(document => CSharpSyntaxTree.ParseText(document.Text, path: NormalizePath(document.Path)))
        .ToArray();

    private static CSharpCompilation CreateCompilation(IReadOnlyCollection<SyntaxTree> sourceTrees)
    {
        var syntaxTrees = sourceTrees
            .Append(CSharpSyntaxTree.ParseText(
                "global using NetCorePal.Extensions.Primitives;",
                path: "__MesKnownExceptionGlobalUsings.g.cs"))
            .ToArray();
        return CSharpCompilation.Create(
            "MesKnownExceptionMessageArchitecture",
            syntaxTrees,
            CreateMetadataReferences());
    }

    private static void AddViolation(
        SyntaxTree syntaxTree,
        SyntaxNode node,
        string reason,
        ICollection<Violation> violations)
    {
        var line = syntaxTree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
        violations.Add(new Violation(
            syntaxTree.FilePath,
            line,
            node.SpanStart,
            reason));
    }

    private static void AddViolations(
        SyntaxTree syntaxTree,
        ExpressionSyntax messageExpression,
        ICollection<Violation> violations)
    {
        var line = syntaxTree.GetLineSpan(messageExpression.Span).StartLinePosition.Line + 1;
        var path = syntaxTree.FilePath;

        if (!TryExtractMessage(messageExpression, out var message, out var estimatedLength))
        {
            AddViolation(syntaxTree, messageExpression, "用户消息必须是可静态分析的字符串字面量或插值字符串。", violations);
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
        if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
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
                + interpolated.Contents.OfType<InterpolationSyntax>().Count() * InterpolationEstimatedLength;
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
            ?? [typeof(object).Assembly.Location, typeof(KnownException).Assembly.Location];

        return assemblyPaths.Select(path => MetadataReference.CreateFromFile(path)).ToArray();
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private sealed record Violation(string Path, int Line, int Position, string Reason);
}
