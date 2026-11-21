using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

public sealed class DemandPlanningKnownExceptionMessageArchitectureTests
{
    private const string SourceRoot = "backend/services/Business/DemandPlanning/src";

    private const string CreateOrUpdateDemandSourcePath =
        SourceRoot + "/Nerv.IIP.Business.DemandPlanning.Web/Application/Commands/CreateOrUpdateDemandSourceCommand.cs";

    private const string RunMrpPath =
        SourceRoot + "/Nerv.IIP.Business.DemandPlanning.Web/Application/Commands/RunMrpCommand.cs";

    private const string DemandPlanningCodingPath =
        "backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Commands/DemandPlanningCodingService.cs";
    private const string SharedCodeAllocatorPath =
        "backend/common/Coding/Nerv.IIP.Coding/CodeAllocator.cs";

    private static readonly IReadOnlyCollection<ExcludedUserMessageSite> ExcludedSites =
    [
        new(RunMrpPath, "MarkMrpRunRunningCommandHandler", "Handle", "后台 worker 状态转换，不返回原始 HTTP 请求"),
        new(RunMrpPath, "ExecuteMrpRunCommandHandler", "Handle", "后台 worker 计算事务，不返回原始 HTTP 请求"),
        new(RunMrpPath, "MarkMrpRunFailedCommandHandler", "Handle", "后台 worker 补偿事务，不返回原始 HTTP 请求"),
    ];

    [Fact]
    public void Known_exception_messages_are_safe_for_transport()
    {
        var repositoryRoot = FindRepositoryRoot();
        var violations = KnownExceptionUserMessageSourceAnalyzer.Analyze(
            LoadDocuments(repositoryRoot), ExcludedSites, UserMessagePolicy.NoDownstreamDiagnostics);

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

    [Theory]
    [InlineData("public void Handle()", "ex.Message")]
    [InlineData("public SyntheticDynamicKnownException()", "ex")]
    public void Known_exception_interpolation_cannot_include_catch_diagnostics(string declaration, string diagnostic)
    {
        var documents = new[]
        {
            new UserMessageSourceDocument(
                "synthetic/DynamicKnownException.cs",
                $$"""
                using System;
                using NetCorePal.Extensions.Primitives;

                public sealed class SyntheticDynamicKnownException
                {
                    {{declaration}}
                    {
                        try
                        {
                        }
                        catch (InvalidOperationException ex)
                        {
                            throw new KnownException($"操作失败：{ {{diagnostic}} }");
                        }
                    }
                }
                """),
        };

        var violations = KnownExceptionUserMessageSourceAnalyzer.Analyze(documents, [], UserMessagePolicy.NoDownstreamDiagnostics);

        Assert.Contains(
            violations,
            violation => violation.Contains("不能透传动态异常文本", StringComparison.Ordinal));
    }

    [Fact]
    public void Shared_code_allocator_is_registered_only_through_the_demand_planning_call_root()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sharedAllocator = Path.Combine(repositoryRoot, SharedCodeAllocatorPath.Replace('/', Path.DirectorySeparatorChar));
        var documents = LoadDocuments(repositoryRoot);
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

    private static UserMessageSourceDocument[] LoadDocuments(string repositoryRoot)
    {
        var sourceRoot = Path.Combine(repositoryRoot, SourceRoot.Replace('/', Path.DirectorySeparatorChar));
        return Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(sourceRoot, file)
                .Split(Path.DirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj"))
            .Select(file => new UserMessageSourceDocument(
                Path.GetRelativePath(repositoryRoot, file).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(file)))
            .ToArray();
    }

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
