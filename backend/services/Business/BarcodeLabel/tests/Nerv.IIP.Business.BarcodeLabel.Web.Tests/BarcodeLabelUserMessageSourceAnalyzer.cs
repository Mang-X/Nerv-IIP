using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed record BarcodeLabelSourceDocument(string Path, string Text);

public sealed record BarcodeLabelKnownExceptionSite(
    string Path,
    string TypeName,
    string MethodName,
    int DirectKnownExceptionCount,
    BarcodeLabelKnownExceptionSiteKind Kind,
    string Reason,
    bool IsCommandHandler = false)
{
    public string Key => $"{Path}|{TypeName}|{MethodName}";
}

public enum BarcodeLabelKnownExceptionSiteKind
{
    Target,
    Excluded,
}

public static class BarcodeLabelUserMessageSourceAnalyzer
{
    private const string KnownExceptionTypeName = "NetCorePal.Extensions.Primitives.KnownException";
    private const int InterpolationEstimatedLength = 12;
    private const int MaximumMessageLength = 60;

    public static IReadOnlyList<BarcodeLabelKnownExceptionSite> Discover(
        IReadOnlyCollection<BarcodeLabelSourceDocument> documents)
    {
        if (documents.Count == 0)
        {
            throw new InvalidOperationException("BarcodeLabel KnownException 源集合不能为空。");
        }

        var sourceTrees = ParseTrees(documents);
        var compilation = CreateCompilation(sourceTrees);
        var discovered = new List<BarcodeLabelKnownExceptionSite>();

        foreach (var syntaxTree in sourceTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();
            var sites = new Dictionary<MemberDeclarationSyntax, DiscoveredMember>();
            foreach (var creation in root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
            {
                if (!IsKnownException(semanticModel, creation))
                {
                    continue;
                }

                var member = creation.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault()
                    ?? throw new InvalidOperationException(
                        $"{NormalizePath(syntaxTree.FilePath)}:{GetLine(syntaxTree, creation)}: KnownException 无法归属到成员。");
                var identity = GetMemberIdentity(semanticModel, member, syntaxTree);
                if (sites.TryGetValue(member, out var existing))
                {
                    sites[member] = existing with { DirectKnownExceptionCount = existing.DirectKnownExceptionCount + 1 };
                }
                else
                {
                    sites.Add(member, new DiscoveredMember(identity.TypeName, identity.MemberName, 1, false));
                }
            }

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var containingType = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                if (containingType is null)
                {
                    continue;
                }

                var typeSymbol = semanticModel.GetDeclaredSymbol(containingType) as INamedTypeSymbol;
                var isCommandHandlerHandle = method.Identifier.ValueText == "Handle"
                    && typeSymbol is not null
                    && typeSymbol.AllInterfaces.Any(@interface =>
                        @interface.Name == "ICommandHandler"
                        && @interface.Arity is 1 or 2);
                if (!isCommandHandlerHandle)
                {
                    continue;
                }

                var identity = GetMemberIdentity(semanticModel, method, syntaxTree);
                if (sites.TryGetValue(method, out var existing))
                {
                    sites[method] = existing with { IsCommandHandler = true };
                }
                else
                {
                    sites.Add(method, new DiscoveredMember(identity.TypeName, identity.MemberName, 0, true));
                }
            }

            discovered.AddRange(sites.Values.Select(site => new BarcodeLabelKnownExceptionSite(
                NormalizePath(syntaxTree.FilePath),
                site.TypeName,
                site.MemberName,
                site.DirectKnownExceptionCount,
                BarcodeLabelKnownExceptionSiteKind.Excluded,
                "discovered BarcodeLabel KnownException site",
                site.IsCommandHandler)));
        }

        return discovered
            .OrderBy(site => site.Path, StringComparer.Ordinal)
            .ThenBy(site => site.TypeName, StringComparer.Ordinal)
            .ThenBy(site => site.MethodName, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string> ValidateLedger(
        IReadOnlyCollection<BarcodeLabelKnownExceptionSite> discovered,
        IReadOnlyCollection<BarcodeLabelKnownExceptionSite> expected,
        int expectedExclusionCount)
    {
        var violations = new List<string>();
        var expectedGroups = expected.GroupBy(site => site.Key, StringComparer.Ordinal).ToArray();
        violations.AddRange(expectedGroups
            .Where(group => group.Count() != 1)
            .Select(group => $"期望台账键重复：{group.Key}。"));
        var discoveredGroups = discovered.GroupBy(site => site.Key, StringComparer.Ordinal).ToArray();
        violations.AddRange(discoveredGroups
            .Where(group => group.Count() != 1)
            .Select(group => $"发现位点键重复：{group.Key}。"));

        var exclusions = expected.Where(site => site.Kind == BarcodeLabelKnownExceptionSiteKind.Excluded).ToArray();
        if (exclusions.Length != expectedExclusionCount)
        {
            violations.Add($"豁免数量必须为 {expectedExclusionCount}，实际为 {exclusions.Length}。");
        }

        var discoveredByKey = discoveredGroups
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        foreach (var exclusion in exclusions)
        {
            if (discoveredByKey.TryGetValue(exclusion.Key, out var site) && site.IsCommandHandler)
            {
                violations.Add($"ICommandHandler 不得豁免：{exclusion.Key}。");
            }
        }

        var expectedKeys = expected.Select(site => site.Key).ToHashSet(StringComparer.Ordinal);
        var discoveredKeys = discovered.Select(site => site.Key).ToHashSet(StringComparer.Ordinal);
        violations.AddRange(discoveredKeys.Except(expectedKeys, StringComparer.Ordinal)
            .Select(key => $"位点未分类：{key}。"));
        violations.AddRange(expectedKeys.Except(discoveredKeys, StringComparer.Ordinal)
            .Select(key => $"台账位点不存在：{key}。"));
        return violations;
    }

    public static IReadOnlyList<string> Analyze(
        IReadOnlyCollection<BarcodeLabelSourceDocument> documents)
    {
        if (documents.Count == 0)
        {
            throw new InvalidOperationException("BarcodeLabel 用户消息源集合不能为空。");
        }

        var sourceTrees = ParseTrees(documents);
        var compilation = CreateCompilation(sourceTrees);
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

    private static SyntaxTree[] ParseTrees(IReadOnlyCollection<BarcodeLabelSourceDocument> documents) =>
        documents
            .Select(document => CSharpSyntaxTree.ParseText(document.Text, path: NormalizePath(document.Path)))
            .ToArray();

    private static CSharpCompilation CreateCompilation(IReadOnlyCollection<SyntaxTree> sourceTrees)
    {
        var syntaxTrees = sourceTrees
            .Append(CSharpSyntaxTree.ParseText(
                "global using NetCorePal.Extensions.Primitives;",
                path: "__BarcodeLabelGlobalUsings.g.cs"))
            .ToArray();
        return CSharpCompilation.Create(
            "BarcodeLabelUserMessageArchitecture",
            syntaxTrees,
            CreateMetadataReferences());
    }

    private static bool IsKnownException(
        SemanticModel semanticModel,
        BaseObjectCreationExpressionSyntax creation) =>
        semanticModel.GetTypeInfo(creation).Type?.ToDisplayString() == KnownExceptionTypeName;

    private static MemberIdentity GetMemberIdentity(
        SemanticModel semanticModel,
        MemberDeclarationSyntax member,
        SyntaxTree syntaxTree)
    {
        var symbol = semanticModel.GetDeclaredSymbol(member)
            ?? member.DescendantNodes()
                .Select(node => semanticModel.GetDeclaredSymbol(node))
                .FirstOrDefault(candidate => candidate is IFieldSymbol or IEventSymbol);
        if (symbol is null)
        {
            throw new InvalidOperationException(
                $"{NormalizePath(syntaxTree.FilePath)}:{GetLine(syntaxTree, member)}: KnownException 无法归属到成员：成员符号无法解析。");
        }

        if (symbol is INamedTypeSymbol namedType)
        {
            return new MemberIdentity(namedType.Name, ".type");
        }

        var containingType = symbol.ContainingType
            ?? throw new InvalidOperationException(
                $"{NormalizePath(syntaxTree.FilePath)}:{GetLine(syntaxTree, member)}: KnownException 所在成员缺少包含类型。");
        var memberName = symbol is IMethodSymbol { MethodKind: MethodKind.Constructor }
            ? ".ctor"
            : symbol.MetadataName;
        return new MemberIdentity(containingType.Name, memberName);
    }

    private static int GetLine(SyntaxTree syntaxTree, SyntaxNode node) =>
        syntaxTree.GetLineSpan(node.Span).StartLinePosition.Line + 1;

    private static void AddViolations(
        SyntaxTree syntaxTree,
        ExpressionSyntax messageExpression,
        ICollection<Violation> violations)
    {
        var line = syntaxTree.GetLineSpan(messageExpression.Span).StartLinePosition.Line + 1;
        var path = syntaxTree.FilePath;

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
            violations.Add(new Violation(
                path,
                line,
                messageExpression.SpanStart,
                "用户消息必须包含中文。"));
        }

        if (estimatedLength > MaximumMessageLength)
        {
            violations.Add(new Violation(
                path,
                line,
                messageExpression.SpanStart,
                "用户消息估算长度不能超过 60 个字符。"));
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

    private sealed record DiscoveredMember(
        string TypeName,
        string MemberName,
        int DirectKnownExceptionCount,
        bool IsCommandHandler);
    private sealed record MemberIdentity(string TypeName, string MemberName);
    private sealed record Violation(string Path, int Line, int Position, string Reason);
}
