using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

public sealed class DemandPlanningKnownExceptionMessageArchitectureTests
{
    private const string AcceptPlanningSuggestionPath =
        "backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Commands/AcceptPlanningSuggestionCommand.cs";
    private const string CancelDemandSourcePath =
        "backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Commands/CancelDemandSourceCommand.cs";
    private const string CreateOrUpdateDemandSourcePath =
        "backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Commands/CreateOrUpdateDemandSourceCommand.cs";
    private const string MasterProductionSchedulePath =
        "backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Commands/MasterProductionScheduleCommands.cs";
    private const string RejectPlanningSuggestionPath =
        "backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Commands/RejectPlanningSuggestionCommand.cs";
    private const string RunMrpPath =
        "backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Commands/RunMrpCommand.cs";
    private const string PlanningSuggestionDownstreamBridgePath =
        "backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Planning/PlanningSuggestionDownstreamBridge.cs";
    private const string DemandPlanningCodingPath =
        "backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Commands/DemandPlanningCodingService.cs";
    private const string SharedCodeAllocatorPath =
        "backend/common/Coding/Nerv.IIP.Coding/CodeAllocator.cs";

    private static readonly IReadOnlyCollection<string> SourcePaths =
    [
        AcceptPlanningSuggestionPath,
        CancelDemandSourcePath,
        CreateOrUpdateDemandSourcePath,
        MasterProductionSchedulePath,
        RejectPlanningSuggestionPath,
        RunMrpPath,
        PlanningSuggestionDownstreamBridgePath,
    ];

    private static readonly IReadOnlyCollection<DemandPlanningKnownExceptionSite> ExpectedSites =
    [
        Target(CancelDemandSourcePath, "CancelDemandSourceCommandHandler", "Handle", 1),
        Target(CreateOrUpdateDemandSourcePath, "CreateOrUpdateDemandSourceCommandHandler", "Handle", 1),

        Target(AcceptPlanningSuggestionPath, "UnsupportedPlanningSuggestionDownstreamBridge", "CreateDownstreamAsync", 1),
        Target(AcceptPlanningSuggestionPath, "AcceptPlanningSuggestionCommandHandler", "Handle", 2),
        Target(AcceptPlanningSuggestionPath, "AcceptPlanningSuggestionCommandHandler", "ResolveDownstreamReferenceAsync", 1),
        Target(AcceptPlanningSuggestionPath, "AcceptPlanningSuggestionCommandHandler", "EnsureCanCreateDownstreamReference", 1),

        Target(RejectPlanningSuggestionPath, "RejectPlanningSuggestionCommandHandler", "Handle", 2),

        Target(MasterProductionSchedulePath, "CreateMasterProductionScheduleBucketCommandHandler", "Handle", 1),
        Target(MasterProductionSchedulePath, "MasterProductionScheduleCommandLoader", "LoadBucketAsync", 1),
        Target(MasterProductionSchedulePath, "UpdateMasterProductionScheduleBucketCommandHandler", "Handle", 1),
        Target(MasterProductionSchedulePath, "ReviewMasterProductionScheduleBucketCommandHandler", "Handle", 1),
        Target(MasterProductionSchedulePath, "ReleaseMasterProductionScheduleBucketCommandHandler", "Handle", 1),

        Target(PlanningSuggestionDownstreamBridgePath, "HttpPlanningSuggestionDownstreamBridge", "CreateDownstreamAsync", 1),
        Target(PlanningSuggestionDownstreamBridgePath, "HttpMesPlanningSuggestionDownstreamBridge", "CreateDownstreamAsync", 3),
        Target(PlanningSuggestionDownstreamBridgePath, "HttpErpPlanningSuggestionDownstreamBridge", "CreateDownstreamAsync", 3),
        Target(PlanningSuggestionDownstreamBridgePath, "HttpErpPlanningSuggestionDownstreamBridge", "ReadResponseDataAsync", 2),

        Excluded(RunMrpPath, "MarkMrpRunRunningCommandHandler", "Handle", 2, "后台 worker 状态转换，不返回原始 HTTP 请求"),
        Excluded(RunMrpPath, "ExecuteMrpRunCommandHandler", "Handle", 2, "后台 worker 计算事务，不返回原始 HTTP 请求"),
        Excluded(RunMrpPath, "MarkMrpRunFailedCommandHandler", "Handle", 1, "后台 worker 补偿事务，不返回原始 HTTP 请求"),
    ];

    [Fact]
    public void Known_exception_sites_have_a_closed_target_and_exclusion_ledger()
    {
        var repositoryRoot = FindRepositoryRoot();
        var documents = SourcePaths
            .Select(path => Path.Combine(repositoryRoot, path.Replace('/', Path.DirectorySeparatorChar)))
            .Select(file => new DemandPlanningSourceDocument(
                Path.GetRelativePath(repositoryRoot, file).Replace(Path.DirectorySeparatorChar, '/'),
                File.Exists(file) ? File.ReadAllText(file) : string.Empty))
            .ToArray();

        Assert.Equal(SourcePaths.Count, documents.Length);
        Assert.All(documents, document => Assert.False(string.IsNullOrEmpty(document.Text), $"命令源文件缺失或为空：{document.Path}"));
        var sourceRoot = Path.Combine(
            repositoryRoot,
            "backend/services/Business/DemandPlanning/src".Replace('/', Path.DirectorySeparatorChar));
        var allSourceDocuments = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Select(file => new DemandPlanningSourceDocument(
                Path.GetRelativePath(repositoryRoot, file).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(file)))
            .ToArray();
        var discoveredSourcePaths = DemandPlanningKnownExceptionSourceAnalyzer.Discover(allSourceDocuments)
            .Select(site => site.Path)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            SourcePaths.OrderBy(path => path, StringComparer.Ordinal),
            discoveredSourcePaths);

        var expectedKeys = ExpectedSites.Select(site => site.Key).ToArray();
        Assert.Equal(expectedKeys.Length, expectedKeys.Distinct(StringComparer.Ordinal).Count());

        var discovered = DemandPlanningKnownExceptionSourceAnalyzer.Discover(documents);
        var duplicateDiscoveredKeys = discovered
            .GroupBy(site => site.Key, StringComparer.Ordinal)
            .Where(group => group.Count() != 1)
            .Select(group => $"{group.Key} ({group.Count()})")
            .ToArray();
        Assert.Empty(duplicateDiscoveredKeys);

        var expectedKeySet = expectedKeys.ToHashSet(StringComparer.Ordinal);
        var unclassified = discovered
            .Where(site => !expectedKeySet.Contains(site.Key))
            .Select(site => site.Key)
            .ToArray();
        Assert.Empty(unclassified);

        foreach (var expected in ExpectedSites)
        {
            var matches = discovered.Where(site => site.Key == expected.Key).ToArray();
            Assert.Single(matches);
            Assert.Equal(expected.DirectKnownExceptionCount, matches[0].DirectKnownExceptionCount);
        }

        var excludedSites = ExpectedSites
            .Where(site => site.Kind == DemandPlanningKnownExceptionSiteKind.Excluded)
            .Select(site => new DemandPlanningExcludedSite(site.Path, site.TypeName, site.MethodName, site.Reason))
            .ToArray();
        var violations = DemandPlanningKnownExceptionMessageAnalyzer.Analyze(documents, excludedSites);

        Assert.True(
            violations.Count == 0,
            "DemandPlanning transport-visible KnownException 消息必须是含中文、长度不超过 60 的静态安全文案。违规位点："
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Demand_source_validation_message_is_chinese_and_actionable()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repositoryRoot, CreateOrUpdateDemandSourcePath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Contains("sales-order 类型由集成流程维护，不能手工创建。", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Demand type 'sales-order' is integration-owned and cannot be created manually.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Known_exception_interpolation_cannot_include_catch_diagnostics()
    {
        var documents = new[]
        {
            new DemandPlanningSourceDocument(
                "synthetic/DynamicKnownException.cs",
                """
                using NetCorePal.Extensions.Primitives;

                public sealed class SyntheticDynamicKnownException
                {
                    public void Handle()
                    {
                        try
                        {
                        }
                        catch (InvalidOperationException ex)
                        {
                            throw new KnownException($"操作失败：{ex.Message}");
                        }
                    }
                }
                """),
        };

        var violations = DemandPlanningKnownExceptionMessageAnalyzer.Analyze(documents, []);

        Assert.Contains(
            violations,
            violation => violation.Contains("不能透传动态异常文本", StringComparison.Ordinal));
    }

    [Fact]
    public void Shared_code_allocator_is_registered_only_through_the_demand_planning_call_root()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sharedAllocator = Path.Combine(repositoryRoot, SharedCodeAllocatorPath.Replace('/', Path.DirectorySeparatorChar));
        var sourceRoot = Path.Combine(
            repositoryRoot,
            "backend/services/Business/DemandPlanning/src".Replace('/', Path.DirectorySeparatorChar));
        var documents = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Select(file => new DemandPlanningSourceDocument(
                Path.GetRelativePath(repositoryRoot, file).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(file)))
            .ToArray();
        var syntaxTrees = documents
            .Select(document => CSharpSyntaxTree.ParseText(document.Text, path: document.Path))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "DemandPlanningCodeAllocatorArchitecture",
            syntaxTrees,
            CreateMetadataReferences(typeof(Nerv.IIP.Coding.CodeAllocator)));
        var allocatorSites = syntaxTrees
            .SelectMany(syntaxTree =>
            {
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                return syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<BaseObjectCreationExpressionSyntax>()
                    .Where(creation => semanticModel.GetTypeInfo(creation).Type?.ToDisplayString() == "Nerv.IIP.Coding.CodeAllocator")
                    .Select(creation => new
                    {
                        Path = syntaxTree.FilePath.Replace('\\', '/'),
                        TypeName = creation.Ancestors()
                            .OfType<TypeDeclarationSyntax>()
                            .Select(type => type.Identifier.ValueText)
                            .FirstOrDefault(),
                        ConstructorName = creation.Ancestors()
                            .OfType<ConstructorDeclarationSyntax>()
                            .Select(constructor => constructor.Identifier.ValueText)
                            .FirstOrDefault(),
                    });
            })
            .ToArray();

        Assert.Equal(2, allocatorSites.Length);
        Assert.All(allocatorSites, site =>
        {
            Assert.Equal(DemandPlanningCodingPath, site.Path);
            Assert.Equal("DemandPlanningCodingService", site.TypeName);
            Assert.Equal("DemandPlanningCodingService", site.ConstructorName);
        });
        Assert.True(File.Exists(sharedAllocator));
    }

    private static IReadOnlyCollection<MetadataReference> CreateMetadataReferences(params Type[] additionalTypes)
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var assemblyPaths = trustedPlatformAssemblies?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(additionalTypes.Select(type => type.Assembly.Location))
            .Distinct(StringComparer.Ordinal)
            ?? additionalTypes.Select(type => type.Assembly.Location).ToArray();

        return assemblyPaths
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static DemandPlanningKnownExceptionSite Target(string path, string typeName, string methodName, int count) =>
        new(path, typeName, methodName, count, DemandPlanningKnownExceptionSiteKind.Target, "transport-visible 同步公开根");

    private static DemandPlanningKnownExceptionSite Excluded(string path, string typeName, string methodName, int count, string reason) =>
        new(path, typeName, methodName, count, DemandPlanningKnownExceptionSiteKind.Excluded, reason);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "backend")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}

internal enum DemandPlanningKnownExceptionSiteKind
{
    Target,
    Excluded,
}

internal sealed record DemandPlanningKnownExceptionSite(
    string Path,
    string TypeName,
    string MethodName,
    int DirectKnownExceptionCount,
    DemandPlanningKnownExceptionSiteKind Kind,
    string Reason)
{
    public string Key => $"{Path}|{TypeName}|{MethodName}";
}

internal sealed record DemandPlanningSourceDocument(string Path, string Text);

internal sealed record DemandPlanningExcludedSite(string Path, string TypeName, string MethodName, string Reason);

internal sealed record DemandPlanningDiscoveredKnownExceptionSite(
    string Path,
    string TypeName,
    string MethodName,
    int DirectKnownExceptionCount)
{
    public string Key => $"{Path}|{TypeName}|{MethodName}";
}

internal static class DemandPlanningKnownExceptionSourceAnalyzer
{
    private const string KnownExceptionTypeName = "NetCorePal.Extensions.Primitives.KnownException";

    public static IReadOnlyList<DemandPlanningDiscoveredKnownExceptionSite> Discover(
        IReadOnlyCollection<DemandPlanningSourceDocument> documents)
    {
        if (documents.Count == 0)
        {
            throw new InvalidOperationException("DemandPlanning KnownException 源集合不能为空。");
        }

        var sourceTrees = documents
            .Select(document => CSharpSyntaxTree.ParseText(document.Text, path: document.Path))
            .ToArray();
        var syntaxTrees = sourceTrees
            .Append(CSharpSyntaxTree.ParseText(
                "global using NetCorePal.Extensions.Primitives;",
                path: "__DemandPlanningGlobalUsings.g.cs"))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "DemandPlanningKnownExceptionArchitecture",
            syntaxTrees,
            CreateMetadataReferences());
        var discovered = new List<DemandPlanningDiscoveredKnownExceptionSite>();

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

                discovered.Add(new DemandPlanningDiscoveredKnownExceptionSite(
                    syntaxTree.FilePath.Replace('\\', '/'),
                    typeName,
                    method.Identifier.ValueText,
                    directKnownExceptionCount));
            }
        }

        return discovered;
    }

    private static bool IsKnownException(
        SemanticModel semanticModel,
        BaseObjectCreationExpressionSyntax creation) =>
        semanticModel.GetTypeInfo(creation).Type?.ToDisplayString() == KnownExceptionTypeName;

    private static IReadOnlyCollection<MetadataReference> CreateMetadataReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var assemblyPaths = trustedPlatformAssemblies?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(typeof(KnownException).Assembly.Location)
            .Distinct(StringComparer.Ordinal)
            ?? [typeof(object).Assembly.Location, typeof(KnownException).Assembly.Location];

        return assemblyPaths
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}

internal static class DemandPlanningKnownExceptionMessageAnalyzer
{
    private const string KnownExceptionTypeName = "NetCorePal.Extensions.Primitives.KnownException";
    private const int InterpolationEstimatedLength = 12;
    private const int MaximumMessageLength = 60;

    public static IReadOnlyList<string> Analyze(
        IReadOnlyCollection<DemandPlanningSourceDocument> documents,
        IReadOnlyCollection<DemandPlanningExcludedSite> excludedSites)
    {
        var sourceTrees = documents
            .Select(document => CSharpSyntaxTree.ParseText(document.Text, path: NormalizePath(document.Path)))
            .ToArray();
        var syntaxTrees = sourceTrees
            .Append(CSharpSyntaxTree.ParseText(
                "global using NetCorePal.Extensions.Primitives;",
                path: "__DemandPlanningGlobalUsings.g.cs"))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "DemandPlanningKnownExceptionMessageArchitecture",
            syntaxTrees,
            CreateMetadataReferences());
        var excludedKeys = excludedSites
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
                if (excludedKeys.Contains(new ExcludedSiteKey(NormalizePath(syntaxTree.FilePath), typeName, methodName)))
                {
                    continue;
                }

                var expression = creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
                if (expression is not null)
                {
                    AddViolations(syntaxTree, expression, violations);
                }
                else
                {
                    var line = syntaxTree.GetLineSpan(creation.Span).StartLinePosition.Line + 1;
                    violations.Add(new Violation(syntaxTree.FilePath, line, creation.SpanStart, "用户消息不能为空。"));
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

    private static void AddViolations(SyntaxTree syntaxTree, ExpressionSyntax expression, ICollection<Violation> violations)
    {
        var line = syntaxTree.GetLineSpan(expression.Span).StartLinePosition.Line + 1;
        if (expression is InterpolatedStringExpressionSyntax interpolated
            && ContainsDiagnosticInterpolation(interpolated))
        {
            violations.Add(new Violation(syntaxTree.FilePath, line, expression.SpanStart, "用户消息不能透传动态异常文本。"));
            return;
        }

        if (!TryExtractMessage(expression, out var message, out var estimatedLength))
        {
            violations.Add(new Violation(syntaxTree.FilePath, line, expression.SpanStart, "用户消息必须是静态字符串或插值字符串，不能透传动态异常文本。"));
            return;
        }

        if (!ContainsChinese(message))
        {
            violations.Add(new Violation(syntaxTree.FilePath, line, expression.SpanStart, "用户消息必须包含中文。"));
        }

        if (estimatedLength > MaximumMessageLength)
        {
            violations.Add(new Violation(syntaxTree.FilePath, line, expression.SpanStart, "用户消息估算长度不能超过 60 个字符。"));
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

    private static bool TryExtractMessage(ExpressionSyntax expression, out string message, out int estimatedLength)
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
                + interpolated.Contents.OfType<InterpolationSyntax>().Count() * InterpolationEstimatedLength;
            return true;
        }

        message = string.Empty;
        estimatedLength = 0;
        return false;
    }

    private static bool IsKnownException(SemanticModel semanticModel, BaseObjectCreationExpressionSyntax creation) =>
        semanticModel.GetTypeInfo(creation).Type?.ToDisplayString() == KnownExceptionTypeName;

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

        return assemblyPaths
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private sealed record ExcludedSiteKey(string Path, string TypeName, string MethodName);

    private sealed record Violation(string Path, int Line, int Position, string Reason);
}
