using MediatR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Nerv.IIP.Iam.Infrastructure;

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamProviderBranchBoundaryTests
{
    [Fact]
    public void Endpoint_sources_do_not_branch_on_persistence_provider_or_touch_store_implementations()
    {
        var documents = SourceFiles("src/Nerv.IIP.Iam.Web/Endpoints")
            .Select(file => new SourceDocument(Relative(file), File.ReadAllText(file)))
            .ToArray();

        var violations = AnalyzeEndpointProviderBoundary(documents);

        Assert.True(
            violations.Count == 0,
            "IAM endpoints must not inspect the persistence provider or reference store implementations. Offenders:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Endpoint_provider_boundary_analyzer_reports_structured_violations()
    {
        const string source = """
            using Microsoft.Extensions.Configuration;
            using StoreAlias = Nerv.IIP.Iam.Infrastructure.InMemoryIamStore;

            sealed class Probe(IConfiguration configuration)
            {
                private StoreAlias? Store { get; }
                private global::Nerv.IIP.Iam.Infrastructure.ApplicationDbContext? Context { get; }
                private string? Provider => configuration["Persistence:Provider"];
                private bool UsesProvider(ProviderProbe provider) => provider.IsInMemory();
            }

            sealed class ProviderProbe { public bool IsInMemory() => true; }
            """;

        var violations = AnalyzeEndpointProviderBoundary([new SourceDocument("Probe.cs", source)]);

        Assert.Equal(4, violations.Count);
        Assert.Contains(violations, violation => violation.Contains("InMemoryIamStore", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains("ApplicationDbContext", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains("Persistence:Provider", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains("IsInMemory", StringComparison.Ordinal));
    }

    [Fact]
    public void Endpoint_provider_boundary_analyzer_ignores_comments_and_string_literals()
    {
        const string source = """
            sealed class Probe
            {
                // IsPostgreSql IsInMemory InMemoryIamStore ApplicationDbContext Persistence:Provider
                private const string Documentation = "IsPostgreSql IsInMemory InMemoryIamStore ApplicationDbContext Persistence:Provider";
            }
            """;

        var violations = AnalyzeEndpointProviderBoundary([new SourceDocument("Probe.cs", source)]);

        Assert.Empty(violations);
    }

    [Fact]
    public void User_application_handlers_use_persistence_abstractions_instead_of_provider_detection()
    {
        var violations = SourceFiles("src/Nerv.IIP.Iam.Web/Application")
            .Where(file => file.Contains($"{Path.DirectorySeparatorChar}Commands{Path.DirectorySeparatorChar}Users{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}Queries{Path.DirectorySeparatorChar}Users{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(file => ForbiddenApplicationTokens
                .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Relative(file)} contains '{token}'"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Auth_roles_and_sessions_application_services_do_not_use_ef_core_or_manual_transactions()
    {
        var applicationDirectories = new[]
        {
            $"Application{Path.DirectorySeparatorChar}Auth",
            $"Application{Path.DirectorySeparatorChar}Roles",
            $"Application{Path.DirectorySeparatorChar}Sessions"
        };

        var violations = SourceFiles("src/Nerv.IIP.Iam.Web/Application")
            .Where(file => applicationDirectories.Any(directory => file.Contains(directory, StringComparison.Ordinal)))
            .SelectMany(file => ForbiddenPostgresApplicationTokens
                .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Relative(file)} contains '{token}'"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Role_endpoints_do_not_use_exceptions_as_not_implemented_control_flow()
    {
        var violations = SourceFiles("src/Nerv.IIP.Iam.Web/Endpoints/Roles")
            .SelectMany(file => ForbiddenNotImplementedControlFlowTokens
                .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Relative(file)} contains '{token}'"))
            .ToArray();

        Assert.Empty(violations);
    }

    private static readonly string[] ForbiddenEndpointTypeNames =
    [
        "Nerv.IIP.Iam.Infrastructure.InMemoryIamStore",
        "Nerv.IIP.Iam.Infrastructure.ApplicationDbContext"
    ];

    private static readonly string[] ForbiddenProviderMethodNames =
    [
        "IsPostgreSql",
        "IsInMemory"
    ];

    private const string PersistenceProviderConfigurationKey = "Persistence:Provider";
    private const string ConfigurationTypeName = "Microsoft.Extensions.Configuration.IConfiguration";

    private static readonly string[] ForbiddenApplicationTokens =
    [
        "IServiceProvider",
        "GetService<IUserRepository>",
        "GetRequiredService<InMemoryIamStore>",
        "InMemoryIamStore"
    ];

    private static readonly string[] ForbiddenPostgresApplicationTokens =
    [
        "ApplicationDbContext",
        "Microsoft.EntityFrameworkCore",
        "SaveChangesAsync",
        "BeginTransaction",
        "ExecuteUpdateAsync",
        ".Database"
    ];

    private static readonly string[] ForbiddenNotImplementedControlFlowTokens =
    [
        "NotImplementedException"
    ];

    private static IReadOnlyList<string> AnalyzeEndpointProviderBoundary(
        IReadOnlyCollection<SourceDocument> documents)
    {
        var syntaxTrees = documents
            .Select(document => CSharpSyntaxTree.ParseText(document.Text, path: document.Path))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "IamEndpointProviderBoundary",
            syntaxTrees,
            CreateMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var forbiddenTypes = ForbiddenEndpointTypeNames
            .Select(typeName => compilation.GetTypeByMetadataName(typeName)
                ?? throw new InvalidOperationException($"Could not resolve forbidden IAM type '{typeName}'."))
            .ToArray();
        var configurationType = compilation.GetTypeByMetadataName(ConfigurationTypeName)
            ?? throw new InvalidOperationException($"Could not resolve '{ConfigurationTypeName}'.");
        var violations = new List<SourceViolation>();

        foreach (var syntaxTree in syntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            foreach (var identifier in root.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (identifier.Ancestors().Any(ancestor => ancestor is UsingDirectiveSyntax))
                {
                    continue;
                }

                var symbol = semanticModel.GetAliasInfo(identifier)?.Target
                    ?? semanticModel.GetSymbolInfo(identifier).Symbol;
                if (symbol is IAliasSymbol alias)
                {
                    symbol = alias.Target;
                }

                if (symbol is not INamedTypeSymbol type
                    || !forbiddenTypes.Any(forbidden =>
                        SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, forbidden)))
                {
                    continue;
                }

                AddViolation(
                    syntaxTree,
                    identifier,
                    $"references forbidden type '{type.ToDisplayString()}'",
                    violations);
            }

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var method = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (method is null
                    || !ForbiddenProviderMethodNames.Contains(method.Name, StringComparer.Ordinal))
                {
                    continue;
                }

                AddViolation(
                    syntaxTree,
                    invocation,
                    $"calls provider detection method '{method.Name}'",
                    violations);
            }

            foreach (var literal in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
            {
                if (!literal.IsKind(SyntaxKind.StringLiteralExpression)
                    || literal.Token.ValueText != PersistenceProviderConfigurationKey
                    || !IsConfigurationLookup(literal, semanticModel, configurationType))
                {
                    continue;
                }

                AddViolation(
                    syntaxTree,
                    literal,
                    $"reads configuration key '{PersistenceProviderConfigurationKey}'",
                    violations);
            }
        }

        return violations
            .Distinct()
            .OrderBy(violation => violation.Path, StringComparer.Ordinal)
            .ThenBy(violation => violation.Line)
            .ThenBy(violation => violation.Position)
            .Select(violation => $"{violation.Path}:{violation.Line}: {violation.Reason}")
            .ToArray();
    }

    private static bool IsConfigurationLookup(
        LiteralExpressionSyntax literal,
        SemanticModel semanticModel,
        INamedTypeSymbol configurationType)
    {
        if (literal.Parent?.Parent is BracketedArgumentListSyntax bracketedArguments
            && bracketedArguments.Parent is ElementAccessExpressionSyntax elementAccess
            && IsConfigurationType(
                semanticModel.GetTypeInfo(elementAccess.Expression).Type,
                configurationType))
        {
            return true;
        }

        if (literal.Parent is not ArgumentSyntax argument
            || argument.Parent?.Parent is not InvocationExpressionSyntax invocation)
        {
            return false;
        }

        var receiver = invocation.Expression is MemberAccessExpressionSyntax memberAccess
            ? memberAccess.Expression
            : null;
        return receiver is not null
            && IsConfigurationType(semanticModel.GetTypeInfo(receiver).Type, configurationType);
    }

    private static bool IsConfigurationType(ITypeSymbol? type, INamedTypeSymbol configurationType) =>
        type is not null
        && (SymbolEqualityComparer.Default.Equals(type, configurationType)
            || type.AllInterfaces.Any(@interface =>
                SymbolEqualityComparer.Default.Equals(@interface, configurationType)));

    private static void AddViolation(
        SyntaxTree syntaxTree,
        SyntaxNode node,
        string reason,
        ICollection<SourceViolation> violations)
    {
        var line = syntaxTree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
        violations.Add(new SourceViolation(syntaxTree.FilePath, line, node.SpanStart, reason));
    }

    private static IReadOnlyCollection<MetadataReference> CreateMetadataReferences()
    {
        _ = typeof(ApplicationDbContext).Assembly;
        _ = typeof(IConfiguration).Assembly;
        _ = typeof(DbContext).Assembly;
        _ = typeof(IMediator).Assembly;

        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var assemblyPaths = trustedPlatformAssemblies?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                .Select(assembly => assembly.Location))
            .Distinct(StringComparer.Ordinal)
            ?? [
                typeof(object).Assembly.Location,
                typeof(IConfiguration).Assembly.Location,
                typeof(ApplicationDbContext).Assembly.Location,
            ];

        return assemblyPaths
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static IEnumerable<string> SourceFiles(string relativeDirectory)
    {
        return Directory.EnumerateFiles(Path.Combine(IamServiceRoot(), relativeDirectory), "*.cs", SearchOption.AllDirectories);
    }

    private static string IamServiceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "backend", "services", "Iam");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate backend/services/Iam from test output directory.");
    }

    private static string Relative(string file)
    {
        return Path.GetRelativePath(IamServiceRoot(), file);
    }

    private sealed record SourceDocument(string Path, string Text);

    private sealed record SourceViolation(string Path, int Line, int Position, string Reason);
}
