using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

public sealed class ProductEngineeringCommandKnownExceptionMessageArchitectureTests
{
    private const string ReleaseCommandsPath =
        "backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductEngineeringReleaseCommands.cs";
    private const string ScheduledReleasePath =
        "backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Scheduling/EngineeringChangeScheduledReleaseService.cs";
    private const string StandardOperationCommandsPath =
        "backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/StandardOperations/StandardOperationCommands.cs";
    private const string ArchiveProductionVersionCommandPath =
        "backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductionVersions/ArchiveProductionVersionCommand.cs";
    private const string CreateProductionVersionCommandPath =
        "backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductionVersions/CreateProductionVersionCommand.cs";
    private const string UpdateProductionVersionCommandPath =
        "backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductionVersions/UpdateProductionVersionCommand.cs";

    private static readonly IReadOnlyCollection<string> CommandSourcePaths =
    [
        ReleaseCommandsPath,
        ScheduledReleasePath,
        StandardOperationCommandsPath,
        ArchiveProductionVersionCommandPath,
        CreateProductionVersionCommandPath,
        UpdateProductionVersionCommandPath,
    ];

    private static readonly IReadOnlyCollection<ProductEngineeringCommandKnownExceptionSite> ExpectedSites =
    [
        // The six release handlers and the continuity validator are the static write surface.
        Target(ReleaseCommandsPath, "RegisterEngineeringDocumentCommandHandler", "Handle", 1),
        Target(ReleaseCommandsPath, "PublishSopDocumentCommandHandler", "Handle", 1),
        Target(ReleaseCommandsPath, "CreateEngineeringItemRevisionCommandHandler", "Handle", 1),
        Target(ReleaseCommandsPath, "ReleaseEngineeringBomCommandHandler", "Handle", 2, asKnownExceptionCallCount: 1),
        Target(ReleaseCommandsPath, "ReleaseManufacturingBomCommandHandler", "Handle", 3, asKnownExceptionCallCount: 1),
        Target(ReleaseCommandsPath, "ReleaseRoutingCommandHandler", "Handle", 4, asKnownExceptionCallCount: 1),
        Target(ReleaseCommandsPath, "ProductEngineeringReleaseValidation", "ValidateManufacturingBomMaterialContinuity", 2),

        // Standard-operation command handlers retain their six dynamic domain-error wrappers as exclusions.
        Target(StandardOperationCommandsPath, "CreateStandardOperationCommandHandler", "Handle", 2, asKnownExceptionCallCount: 1),
        Target(StandardOperationCommandsPath, "UpdateStandardOperationCommandHandler", "Handle", 1, asKnownExceptionCallCount: 1),
        Target(StandardOperationCommandsPath, "ArchiveStandardOperationCommandHandler", "Handle", 1, asKnownExceptionCallCount: 1),

        // Production-version command messages include the shared binding validator methods.
        Target(CreateProductionVersionCommandPath, "CreateProductionVersionCommandHandler", "Handle", 1),
        Target(CreateProductionVersionCommandPath, "ProductionVersionBindingValidator", "ResolveAsync", 4),
        Target(CreateProductionVersionCommandPath, "ProductionVersionBindingValidator", "EnsurePublishedAndEffective", 2),
        Target(UpdateProductionVersionCommandPath, "UpdateProductionVersionCommandHandler", "Handle", 2),
        Target(ArchiveProductionVersionCommandPath, "ArchiveProductionVersionCommandHandler", "Handle", 1),

        // Shared exception wrappers deliberately preserve domain exception.Message for the dynamic layer.
        Excluded(ReleaseCommandsPath, "ProductEngineeringReleaseValidation", "AsKnownException<T>", 2, "dynamic domain-message helper"),
        Excluded(ReleaseCommandsPath, "ProductEngineeringReleaseValidation", "AsKnownException", 2, "dynamic domain-message helper"),

        // The provider boundary is not part of this command-message layer.
        Excluded(ReleaseCommandsPath, "HttpProductEngineeringMasterDataReferenceValidator", "ValidateActiveReferencesAsync", 3, "provider boundary"),

        // Engineering Change public synchronous release and archive paths are the target layer.
        Target(ReleaseCommandsPath, "ReleaseEngineeringChangeCommandHandler", "Handle", 0, asKnownExceptionCallCount: 1),
        Target(ReleaseCommandsPath, "ReleaseEngineeringChangeCommandHandler", "ResolveAffectedVersionAsync", 1),
        Target(ReleaseCommandsPath, "ReleaseEngineeringChangeCommandHandler", "EnsureAcyclicSupersedeTopology", 3),
        Target(ReleaseCommandsPath, "ReleaseEngineeringChangeCommandHandler", "SupersedeCycleException", 1),
        Target(ReleaseCommandsPath, "ReleaseEngineeringChangeCommandHandler", "NormalizeRequired", 1),
        Target(ReleaseCommandsPath, "ReleaseEngineeringChangeCommandHandler", "GetSuccessorEngineeringBomAsync", 1),
        Target(ReleaseCommandsPath, "ReleaseEngineeringChangeCommandHandler", "GetSuccessorManufacturingBomAsync", 1),
        Target(ReleaseCommandsPath, "ReleaseEngineeringChangeCommandHandler", "GetSuccessorRoutingAsync", 1),
        Target(ReleaseCommandsPath, "ReleaseEngineeringChangeCommandHandler", "GetSuccessorProductionVersionAsync", 1),
        Target(ReleaseCommandsPath, "ReleaseEngineeringChangeCommandHandler", "GetSuccessorEngineeringDocumentAsync", 1),
        Target(ReleaseCommandsPath, "ReleaseEngineeringChangeCommandHandler", "ArchiveEngineeringBom", 1, asKnownExceptionCallCount: 1),
        Target(ReleaseCommandsPath, "ReleaseEngineeringChangeCommandHandler", "ArchiveManufacturingBom", 1, asKnownExceptionCallCount: 1),
        Target(ReleaseCommandsPath, "ReleaseEngineeringChangeCommandHandler", "ArchiveRouting", 1, asKnownExceptionCallCount: 1),
        Target(ReleaseCommandsPath, "ReleaseEngineeringChangeCommandHandler", "ArchiveProductionVersion", 1, asKnownExceptionCallCount: 2),
        Target(ReleaseCommandsPath, "ReleaseEngineeringChangeCommandHandler", "ArchiveEngineeringDocument", 1, asKnownExceptionCallCount: 1),
        Target(ReleaseCommandsPath, "ReleaseEngineeringChangeCommandHandler", "GetEngineeringDocumentRepository", 1),
        Target(ReleaseCommandsPath, "ReleaseEngineeringChangeCommandHandler", "EnsurePublishedSuccessor", 1),
        Target(ReleaseCommandsPath, "ReleaseEngineeringChangeCommandHandler", "EnsureActiveSuccessor", 1),
        // Scheduled promotion remains an internal background command and is excluded from this public layer.
        Excluded(ReleaseCommandsPath, "PromoteScheduledEngineeringChangeCommandHandler", "Handle", 1, "scheduled/internal command", asKnownExceptionCallCount: 1),
        // The archive resolver is only reached by the background scheduler and remains excluded.
        Excluded(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "ResolveAffectedVersionAsync", 1, "scheduler/background"),
        Excluded(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "GetSuccessorEngineeringBomAsync", 1, "scheduler/background"),
        Excluded(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "GetSuccessorManufacturingBomAsync", 1, "scheduler/background"),
        Excluded(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "GetSuccessorRoutingAsync", 1, "scheduler/background"),
        Excluded(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "GetSuccessorProductionVersionAsync", 1, "scheduler/background"),
        Excluded(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "GetSuccessorEngineeringDocumentAsync", 1, "scheduler/background"),
        Excluded(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "ArchiveEngineeringBom", 1, "scheduler/background", asKnownExceptionCallCount: 1),
        Excluded(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "ArchiveManufacturingBom", 1, "scheduler/background", asKnownExceptionCallCount: 1),
        Excluded(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "ArchiveRouting", 1, "scheduler/background", asKnownExceptionCallCount: 1),
        Excluded(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "ArchiveProductionVersion", 1, "scheduler/background", asKnownExceptionCallCount: 2),
        Excluded(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "ArchiveEngineeringDocument", 1, "scheduler/background", asKnownExceptionCallCount: 1),
        Excluded(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "GetEngineeringDocumentRepository", 1, "scheduler/background"),
        Excluded(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "EnsurePublishedSuccessor", 1, "scheduler/background"),
        Excluded(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "EnsureActiveSuccessor", 1, "scheduler/background"),
        Target(ReleaseCommandsPath, "CancelScheduledEngineeringChangeCommandHandler", "Handle", 1, asKnownExceptionCallCount: 1),
        Target(ReleaseCommandsPath, "RescheduleEngineeringChangeCommandHandler", "Handle", 1, asKnownExceptionCallCount: 1),
        // The fallback verifier has no public facade and remains explicitly excluded.
        Excluded(ReleaseCommandsPath, "RejectingEngineeringApprovalVerifier", "EnsureApprovedAsync", 1, "fallback/no-facade"),
        Target(ReleaseCommandsPath, "HttpEngineeringApprovalVerifier", "EnsureApprovedAsync", 3),
        Target(ReleaseCommandsPath, "HttpEngineeringApprovalVerifier", "ValidateApprovedChain", 1),
    ];

    [Fact]
    public void Command_known_exception_messages_have_a_closed_static_and_excluded_ledger()
    {
        var repositoryRoot = FindRepositoryRoot();
        var documents = CommandSourcePaths
            .Select(path => Path.Combine(repositoryRoot, path.Replace('/', Path.DirectorySeparatorChar)))
            .Select(file => new ProductEngineeringSourceDocument(
                Path.GetRelativePath(repositoryRoot, file).Replace(Path.DirectorySeparatorChar, '/'),
                File.Exists(file) ? File.ReadAllText(file) : string.Empty))
            .ToArray();

        Assert.Equal(CommandSourcePaths.Count, documents.Length);
        Assert.All(documents, document => Assert.False(string.IsNullOrEmpty(document.Text), $"命令源文件缺失或为空：{document.Path}"));

        var expectedKeys = ExpectedSites.Select(site => site.Key).ToArray();
        Assert.Equal(expectedKeys.Length, expectedKeys.Distinct(StringComparer.Ordinal).Count());

        var discovered = ProductEngineeringCommandKnownExceptionSourceAnalyzer.Discover(documents);
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
            Assert.True(
                expected.AsKnownExceptionCallCount == matches[0].AsKnownExceptionCallCount,
                $"{expected.Key} 的 AsKnownException 调用数不匹配：期望 {expected.AsKnownExceptionCallCount}，实际 {matches[0].AsKnownExceptionCallCount}。");
        }

        var excludedSites = ExpectedSites
            .Where(site => site.Kind != ProductEngineeringCommandSiteKind.Target)
            .Select(site => new ProductEngineeringExcludedQuerySite(
                site.Path,
                site.TypeName,
                site.MethodName.Split('<')[0],
                site.Reason))
            .ToArray();
        var violations = ProductEngineeringUserMessageSourceAnalyzer.Analyze(documents, excludedSites);

        Assert.True(
            violations.Count == 0,
            "ProductEngineering command user messages must be static, contain Chinese, and be at most 60 estimated characters. Offenders:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static ProductEngineeringCommandKnownExceptionSite Target(
        string path,
        string typeName,
        string methodName,
        int directKnownExceptionCount,
        int asKnownExceptionCallCount = 0) =>
        new(path, typeName, methodName, directKnownExceptionCount, asKnownExceptionCallCount, ProductEngineeringCommandSiteKind.Target, "static command write layer");

    private static ProductEngineeringCommandKnownExceptionSite Excluded(
        string path,
        string typeName,
        string methodName,
        int directKnownExceptionCount,
        string reason,
        int asKnownExceptionCallCount = 0) =>
        new(path, typeName, methodName, directKnownExceptionCount, asKnownExceptionCallCount, ProductEngineeringCommandSiteKind.Excluded, reason);

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

internal enum ProductEngineeringCommandSiteKind
{
    Target,
    Excluded,
}

internal sealed record ProductEngineeringCommandKnownExceptionSite(
    string Path,
    string TypeName,
    string MethodName,
    int DirectKnownExceptionCount,
    int AsKnownExceptionCallCount,
    ProductEngineeringCommandSiteKind Kind,
    string Reason)
{
    public string Key => $"{Path}|{TypeName}|{MethodName}";
}

internal static class ProductEngineeringCommandKnownExceptionSourceAnalyzer
{
    private const string KnownExceptionTypeName = "NetCorePal.Extensions.Primitives.KnownException";

    public static IReadOnlyList<ProductEngineeringCommandKnownExceptionSite> Discover(
        IReadOnlyCollection<ProductEngineeringSourceDocument> documents)
    {
        if (documents.Count == 0)
        {
            throw new InvalidOperationException("ProductEngineering 命令用户消息源集合不能为空。");
        }

        var sourceTrees = documents
            .Select(document => CSharpSyntaxTree.ParseText(document.Text, path: document.Path))
            .ToArray();
        var syntaxTrees = sourceTrees
            .Append(CSharpSyntaxTree.ParseText(
                "global using NetCorePal.Extensions.Primitives;",
                path: "__ProductEngineeringGlobalUsings.g.cs"))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "ProductEngineeringCommandKnownExceptionArchitecture",
            syntaxTrees,
            CreateMetadataReferences());
        var discovered = new List<ProductEngineeringCommandKnownExceptionSite>();

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

                var methodName = MethodName(method);
                var directKnownExceptionCount = method.DescendantNodes()
                    .OfType<BaseObjectCreationExpressionSyntax>()
                    .Count(creation =>
                        creation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() == method
                        && IsKnownException(semanticModel, creation));
                var asKnownExceptionCallCount = method.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Count(invocation =>
                        invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() == method
                        && IsAsKnownException(invocation));

                if (directKnownExceptionCount == 0 && asKnownExceptionCallCount == 0)
                {
                    continue;
                }

                discovered.Add(new ProductEngineeringCommandKnownExceptionSite(
                    syntaxTree.FilePath.Replace('\\', '/'),
                    typeName,
                    methodName,
                    directKnownExceptionCount,
                    asKnownExceptionCallCount,
                    ProductEngineeringCommandSiteKind.Excluded,
                    "discovered command site"));
            }
        }

        return discovered;
    }

    private static bool IsKnownException(
        SemanticModel semanticModel,
        BaseObjectCreationExpressionSyntax creation) =>
        semanticModel.GetTypeInfo(creation).Type?.ToDisplayString() == KnownExceptionTypeName;

    private static bool IsAsKnownException(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax memberAccess
        && memberAccess.Name.Identifier.ValueText == "AsKnownException"
        && memberAccess.Expression.ToString().EndsWith("ProductEngineeringReleaseValidation", StringComparison.Ordinal);

    private static string MethodName(MethodDeclarationSyntax method) =>
        method.Identifier.ValueText + (method.TypeParameterList is null ? string.Empty : method.TypeParameterList.ToString());

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
}
