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
    private const string CommandHandlerTypeName = "NetCorePal.Extensions.Primitives.ICommandHandler`1";
    private const string CommandHandlerWithResultTypeName = "NetCorePal.Extensions.Primitives.ICommandHandler`2";
    private const int InterpolationEstimatedLength = 12;
    private const int MaximumMessageLength = 60;

    public static IReadOnlyList<BarcodeLabelKnownExceptionSite> Discover(
        IReadOnlyCollection<BarcodeLabelSourceDocument> documents,
        IReadOnlyCollection<string>? commandHandlerTypeNames = null,
        bool requireSuccessfulCompilation = true)
    {
        if (documents.Count == 0)
        {
            throw new InvalidOperationException("BarcodeLabel KnownException 源集合不能为空。");
        }

        var sourceTrees = ParseTrees(documents);
        var compilation = CreateCompilation(sourceTrees, requireSuccessfulCompilation);
        var sites = new Dictionary<string, BarcodeLabelKnownExceptionSite>(StringComparer.Ordinal);

        foreach (var syntaxTree in sourceTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();
            foreach (var invocation in FindKnownExceptionInvocations(root, semanticModel))
            {
                var member = invocation.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().First();
                var identity = GetMemberIdentity(semanticModel, member, invocation, syntaxTree);
                var path = NormalizePath(syntaxTree.FilePath);
                var key = $"{path}|{identity.TypeName}|{identity.MemberName}";
                if (sites.TryGetValue(key, out var existing))
                {
                    sites[key] = existing with { DirectKnownExceptionCount = existing.DirectKnownExceptionCount + 1 };
                }
                else
                {
                    sites.Add(key, new BarcodeLabelKnownExceptionSite(
                        path,
                        identity.TypeName,
                        identity.MemberName,
                        1,
                        BarcodeLabelKnownExceptionSiteKind.Target,
                        "discovered BarcodeLabel KnownException site"));
                }
            }
        }

        commandHandlerTypeNames ??= [CommandHandlerTypeName, CommandHandlerWithResultTypeName];
        var commandHandlerDefinitions = commandHandlerTypeNames
            .Select(compilation.GetTypeByMetadataName)
            .Where(symbol => symbol is not null)
            .Cast<INamedTypeSymbol>()
            .ToArray();
        if (commandHandlerDefinitions.Length != 2)
        {
            throw new InvalidOperationException("BarcodeLabel ICommandHandler 合同类型无法完整解析。");
        }

        var declaredTypes = sourceTrees
            .SelectMany(syntaxTree => syntaxTree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            .Select(type => (Syntax: type, Symbol: compilation.GetSemanticModel(type.SyntaxTree).GetDeclaredSymbol(type) as INamedTypeSymbol))
            .Where(pair => pair.Symbol is not null)
            .GroupBy(pair => pair.Symbol!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .Select(group => group.OrderBy(pair => NormalizePath(pair.Syntax.SyntaxTree.FilePath), StringComparer.Ordinal).First());
        foreach (var (syntax, typeSymbol) in declaredTypes)
        {
            var handlerInterfaces = typeSymbol!.AllInterfaces
                .Where(@interface => commandHandlerDefinitions.Any(definition =>
                    SymbolEqualityComparer.Default.Equals(@interface.OriginalDefinition, definition)))
                .ToArray();
            if (handlerInterfaces.Length == 0)
            {
                continue;
            }

            var handlerInterfaceMembers = handlerInterfaces
                .SelectMany(@interface => @interface.AllInterfaces.Prepend(@interface))
                .SelectMany(@interface => @interface.GetMembers("Handle"))
                .OfType<IMethodSymbol>()
                .ToArray();
            var handleDeclarations = handlerInterfaceMembers
                .Select(member => typeSymbol.FindImplementationForInterfaceMember(member))
                .OfType<IMethodSymbol>()
                .SelectMany(method => method.DeclaringSyntaxReferences)
                .Select(reference => reference.GetSyntax())
                .OfType<MethodDeclarationSyntax>()
                .Cast<SyntaxNode>()
                .ToArray();
            if (handleDeclarations.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{NormalizePath(syntax.SyntaxTree.FilePath)}: BarcodeLabel ICommandHandler {typeSymbol.Name} 的 Handle 合同声明无法解析。");
            }

            foreach (var handleDeclaration in handleDeclarations)
            {
                var path = NormalizePath(handleDeclaration.SyntaxTree.FilePath);
                var key = $"{path}|{typeSymbol.Name}|Handle";
                if (sites.TryGetValue(key, out var existing))
                {
                    sites[key] = existing with { IsCommandHandler = true };
                }
                else
                {
                    sites.Add(key, new BarcodeLabelKnownExceptionSite(
                        path,
                        typeSymbol.Name,
                        "Handle",
                        0,
                        BarcodeLabelKnownExceptionSiteKind.Target,
                        "discovered BarcodeLabel command handler",
                        true));
                }
            }
        }

        return sites.Values
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

        violations.AddRange(exclusions.Select(exclusion => $"BarcodeLabel KnownException 台账不允许豁免：{exclusion.Key}。"));

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
        IReadOnlyCollection<BarcodeLabelSourceDocument> documents,
        bool requireSuccessfulCompilation = true)
    {
        if (documents.Count == 0)
        {
            throw new InvalidOperationException("BarcodeLabel 用户消息源集合不能为空。");
        }

        var sourceTrees = ParseTrees(documents);
        var compilation = CreateCompilation(sourceTrees, requireSuccessfulCompilation);
        var violations = new List<Violation>();

        foreach (var syntaxTree in sourceTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            foreach (var invocation in FindKnownExceptionInvocations(syntaxTree.GetRoot(), semanticModel))
            {
                var firstArgument = GetFirstArgument(invocation);
                if (firstArgument is null)
                {
                    violations.Add(new Violation(
                        syntaxTree.FilePath,
                        GetLine(syntaxTree, invocation),
                        invocation.SpanStart,
                        "用户消息必须提供可静态分析的首个参数。"));
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

    private static SyntaxTree[] ParseTrees(IReadOnlyCollection<BarcodeLabelSourceDocument> documents) =>
        documents
            .Select(document => CSharpSyntaxTree.ParseText(document.Text, path: NormalizePath(document.Path)))
            .ToArray();

    private static CSharpCompilation CreateCompilation(
        IReadOnlyCollection<SyntaxTree> sourceTrees,
        bool requireSuccessfulCompilation)
    {
        var syntaxTrees = sourceTrees
            .Append(CSharpSyntaxTree.ParseText(
                "global using NetCorePal.Extensions.Primitives;",
                path: "__BarcodeLabelGlobalUsings.g.cs"))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "BarcodeLabelUserMessageArchitecture",
            syntaxTrees,
            CreateMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .OrderBy(diagnostic => NormalizePath(diagnostic.Location.SourceTree?.FilePath ?? string.Empty), StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .ToArray();
        if (requireSuccessfulCompilation && errors.Length != 0)
        {
            throw new InvalidOperationException(
                "BarcodeLabel 用户消息源码无法编译："
                + Environment.NewLine
                + string.Join(Environment.NewLine, errors.Select(diagnostic => diagnostic.ToString())));
        }

        return compilation;
    }

    private static IEnumerable<SyntaxNode> FindKnownExceptionInvocations(
        SyntaxNode root,
        SemanticModel semanticModel)
    {
        foreach (var creation in root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
        {
            if (IsKnownExceptionType(semanticModel.GetTypeInfo(creation).Type))
            {
                yield return creation;
            }
        }

        foreach (var constructor in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
        {
            if (constructor.Initializer is { } initializer
                && semanticModel.GetSymbolInfo(initializer).Symbol is IMethodSymbol symbol
                && IsKnownExceptionType(symbol.ContainingType))
            {
                yield return initializer;
            }
        }

        foreach (var baseType in root.DescendantNodes().OfType<PrimaryConstructorBaseTypeSyntax>())
        {
            if (semanticModel.GetSymbolInfo(baseType).Symbol is IMethodSymbol symbol
                && IsKnownExceptionType(symbol.ContainingType))
            {
                yield return baseType;
            }
        }
    }

    private static bool IsKnownExceptionType(ITypeSymbol? type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == KnownExceptionTypeName)
            {
                return true;
            }
        }

        return false;
    }

    private static ExpressionSyntax? GetFirstArgument(SyntaxNode invocation) => invocation switch
    {
        BaseObjectCreationExpressionSyntax creation => creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression,
        ConstructorInitializerSyntax initializer => initializer.ArgumentList.Arguments.FirstOrDefault()?.Expression,
        PrimaryConstructorBaseTypeSyntax baseType => baseType.ArgumentList.Arguments.FirstOrDefault()?.Expression,
        _ => null,
    };

    private static MemberIdentity GetMemberIdentity(
        SemanticModel semanticModel,
        MemberDeclarationSyntax member,
        SyntaxNode invocation,
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
            return new MemberIdentity(
                namedType.Name,
                invocation is PrimaryConstructorBaseTypeSyntax ? ".ctor" : ".type");
        }

        var containingType = symbol.ContainingType
            ?? throw new InvalidOperationException(
                $"{NormalizePath(syntaxTree.FilePath)}:{GetLine(syntaxTree, member)}: KnownException 所在成员缺少包含类型。");
        var memberName = member switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            _ => symbol switch
            {
                IMethodSymbol { MethodKind: MethodKind.Constructor } => ".ctor",
                IMethodSymbol method => method.Name,
                _ => symbol.MetadataName,
            },
        };
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

    private sealed record MemberIdentity(string TypeName, string MemberName);
    private sealed record Violation(string Path, int Line, int Position, string Reason);
}
