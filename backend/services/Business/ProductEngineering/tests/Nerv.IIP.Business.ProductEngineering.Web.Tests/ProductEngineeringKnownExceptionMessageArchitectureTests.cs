using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

public sealed class ProductEngineeringKnownExceptionMessageArchitectureTests
{
    private static readonly IReadOnlyCollection<ProductEngineeringExcludedQuerySite> ExcludedQuerySites =
    [
        new(
            "backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Queries/ProductEngineeringReleaseQueries.cs",
            "GetMasterDataWorkCenterUsageQueryHandler",
            "Handle",
            "internal work-center usage is outside this query layer"),
    ];

    public static TheoryData<string, string> EnglishUserMessageSources => new()
    {
        {
            "using NetCorePal.Extensions.Primitives; class Probe { void Run() { throw new KnownException(\"Unable to save\"); } }",
            "explicit KnownException construction"
        },
        {
            "using NetCorePal.Extensions.Primitives; class Probe { KnownException Create() => new(\"Unable to save\"); }",
            "expression-bodied target-typed construction"
        },
        {
            "using NetCorePal.Extensions.Primitives; class Probe { KnownException Create() { return new(\"Unable to save\"); } }",
            "block return target-typed construction"
        },
        {
            "using NetCorePal.Extensions.Primitives; class Probe { void Run() { KnownException error = new(\"Unable to save\"); } }",
            "local-variable target-typed construction"
        },
        {
            "using NetCorePal.Extensions.Primitives; class Probe { private readonly KnownException _error = new(\"Unable to save\"); }",
            "field target-typed construction"
        },
        {
            "using NetCorePal.Extensions.Primitives; class Probe { KnownException Error { get; } = new(\"Unable to save\"); }",
            "property target-typed construction"
        },
    };

    [Theory]
    [MemberData(nameof(EnglishUserMessageSources))]
    public void English_user_messages_are_reported(string source, string _)
    {
        var violations = AnalyzeProbe(source);

        Assert.Equal(["Probe.cs:1: 用户消息必须包含中文。"], violations);
    }

    [Fact]
    public void Non_static_user_messages_are_reported()
    {
        const string source =
            "using NetCorePal.Extensions.Primitives; class Probe { KnownException Create(string message) => new(message); }";

        var violations = AnalyzeProbe(source);

        Assert.Equal(["Probe.cs:1: 用户消息必须是可静态分析的字符串字面量或插值字符串。"], violations);
    }

    [Fact]
    public void User_messages_estimated_above_sixty_characters_are_reported()
    {
        const string source =
            "using NetCorePal.Extensions.Primitives; class Probe { KnownException Create(string code) => new($\"一二三四五六七八九十一二三四五六七八九十一二三四五六七八九十一二三四五六七八九十一二三四五六七八九十{code}\"); }";

        var violations = AnalyzeProbe(source);

        Assert.Equal(["Probe.cs:1: 用户消息估算长度不能超过 60 个字符。"], violations);
    }

    [Fact]
    public void Chinese_raw_string_user_messages_are_allowed()
    {
        const string source =
            "using NetCorePal.Extensions.Primitives; class Probe { KnownException Create() => new(\"\"\"无法保存，请稍后重试。\"\"\"); }";

        var violations = AnalyzeProbe(source);

        Assert.Empty(violations);
    }

    [Fact]
    public void Chinese_interpolated_user_messages_are_allowed()
    {
        const string source =
            "using NetCorePal.Extensions.Primitives; class Probe { KnownException Create(string code) => new($\"编码 {code} 无效，请检查后重试。\"); }";

        var violations = AnalyzeProbe(source);

        Assert.Empty(violations);
    }

    [Fact]
    public void Same_named_non_framework_exception_is_ignored()
    {
        const string source =
            "namespace Fake { public sealed class KnownException(string message) : System.Exception(message); } class Probe { void Run() { throw new Fake.KnownException(\"Unable to save\"); } }";

        var violations = AnalyzeProbe(source);

        Assert.Empty(violations);
    }

    [Fact]
    public void Excluded_query_sites_are_ignored_by_exact_file_and_method()
    {
        const string source =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle() { throw new KnownException(\"internal-code\"); } }";

        var violations = ProductEngineeringUserMessageSourceAnalyzer.Analyze(
            [new ProductEngineeringSourceDocument(
                "backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Queries/ProductEngineeringReleaseQueries.cs",
                source)],
            [new ProductEngineeringExcludedQuerySite(
                "backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Queries/ProductEngineeringReleaseQueries.cs",
                "Probe",
                "Handle",
                "explicitly excluded query method")]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Empty_source_collection_fails_closed()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ProductEngineeringUserMessageSourceAnalyzer.Analyze([], []));

        Assert.Contains("源集合不能为空", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductEngineering_exposed_query_user_messages_follow_the_architecture_rules()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(
            repositoryRoot,
            "backend",
            "services",
            "Business",
            "ProductEngineering",
            "src",
            "Nerv.IIP.Business.ProductEngineering.Web",
            "Application",
            "Queries");
        var relativePaths = new[]
        {
            "ProductEngineeringBomQueries.cs",
            "ProductEngineeringImpactQueries.cs",
            "ProductEngineeringReleaseQueries.cs",
            Path.Combine("ProductionVersions", "ResolveProductionVersionQuery.cs"),
            Path.Combine("StandardOperations", "StandardOperationQueries.cs"),
        };
        var documents = relativePaths
            .Select(relativePath => Path.Combine(sourceRoot, relativePath))
            .Select(file => new ProductEngineeringSourceDocument(
                Path.GetRelativePath(repositoryRoot, file),
                File.ReadAllText(file)))
            .ToArray();

        Assert.Equal(relativePaths.Length, documents.Length);
        var violations = ProductEngineeringUserMessageSourceAnalyzer.Analyze(documents, ExcludedQuerySites);

        Assert.NotEmpty(documents);
        Assert.True(
            violations.Count == 0,
            "ProductEngineering exposed query messages must be static, contain Chinese, and be at most 60 estimated characters. Offenders:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static IReadOnlyList<string> AnalyzeProbe(string source) =>
        ProductEngineeringUserMessageSourceAnalyzer.Analyze(
            [new ProductEngineeringSourceDocument("Probe.cs", source)],
            []);

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
