using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
    public void Endpoint_provider_boundary_analyzer_uses_web_global_usings_and_fails_closed_on_diagnostics()
    {
        const string implicitUsingSource = """
            sealed class Probe(IConfiguration configuration)
            {
                private string? Provider => configuration["Persistence:Provider"];
            }
            """;
        const string invalidSource = """
            sealed class InvalidProbe(UnknownProviderType provider);
            """;

        var violations = AnalyzeEndpointProviderBoundary(
        [
            new SourceDocument("ImplicitUsingProbe.cs", implicitUsingSource),
            new SourceDocument("InvalidProbe.cs", invalidSource)
        ]);

        Assert.Contains(violations, violation =>
            violation.Contains("ImplicitUsingProbe.cs", StringComparison.Ordinal)
            && violation.Contains("Persistence:Provider", StringComparison.Ordinal));
        Assert.Contains(violations, violation =>
            violation.Contains("InvalidProbe.cs", StringComparison.Ordinal)
            && violation.Contains("CS0246", StringComparison.Ordinal));
    }

    [Fact]
    public void Endpoint_provider_boundary_analyzer_reports_conditional_configuration_lookups()
    {
        const string source = """
            using Microsoft.Extensions.Configuration;

            sealed class Probe(IConfiguration? configuration)
            {
                private string? Indexed => configuration?["Persistence:Provider"];
                private string? Extended => configuration?.GetValue<string>("Persistence:Provider");
            }
            """;

        var violations = AnalyzeEndpointProviderBoundary([new SourceDocument("Probe.cs", source)]);

        Assert.Equal(2, violations.Count(violation =>
            violation.Contains("Persistence:Provider", StringComparison.Ordinal)));
    }

    [Fact]
    public void Endpoint_provider_boundary_analyzer_resolves_constant_configuration_keys()
    {
        const string source = """
            using Microsoft.Extensions.Configuration;

            sealed class Probe(IConfiguration configuration)
            {
                private const string ProviderKey = "Persistence:Provider";
                private string? Provider => configuration[ProviderKey];
            }
            """;

        var violations = AnalyzeEndpointProviderBoundary([new SourceDocument("Probe.cs", source)]);

        Assert.Single(violations, violation =>
            violation.Contains("Persistence:Provider", StringComparison.Ordinal));
    }

    [Fact]
    public void Endpoint_provider_boundary_analyzer_reports_provider_method_groups()
    {
        const string source = """
            using System;

            sealed class Probe
            {
                private readonly Func<bool> check = ProviderProbe.IsInMemory;
            }

            static class ProviderProbe
            {
                public static bool IsInMemory() => true;
            }
            """;

        var violations = AnalyzeEndpointProviderBoundary([new SourceDocument("Probe.cs", source)]);

        Assert.Single(violations, violation =>
            violation.Contains("IsInMemory", StringComparison.Ordinal));
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
    private const string WebGlobalUsings = """
        global using Microsoft.AspNetCore.Builder;
        global using Microsoft.AspNetCore.Hosting;
        global using Microsoft.AspNetCore.Http;
        global using Microsoft.AspNetCore.Routing;
        global using Microsoft.Extensions.Configuration;
        global using Microsoft.Extensions.DependencyInjection;
        global using Microsoft.Extensions.Hosting;
        global using Microsoft.Extensions.Logging;
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Net.Http.Json;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    private static readonly Lazy<IReadOnlyCollection<MetadataReference>> MetadataReferences =
        new(CreateMetadataReferences);

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
        var globalUsingsTree = CSharpSyntaxTree.ParseText(WebGlobalUsings, path: "WebGlobalUsings.g.cs");
        var compilation = CSharpCompilation.Create(
            "IamEndpointProviderBoundary",
            syntaxTrees.Prepend(globalUsingsTree),
            MetadataReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var forbiddenTypes = ForbiddenEndpointTypeNames
            .Select(typeName => compilation.GetTypeByMetadataName(typeName)
                ?? throw new InvalidOperationException($"Could not resolve forbidden IAM type '{typeName}'."))
            .ToArray();
        var configurationType = compilation.GetTypeByMetadataName(ConfigurationTypeName)
            ?? throw new InvalidOperationException($"Could not resolve '{ConfigurationTypeName}'.");
        var violations = new List<SourceViolation>();

        foreach (var diagnostic in compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Location.SourceTree is not null
                && syntaxTrees.Contains(diagnostic.Location.SourceTree)))
        {
            var line = diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;
            violations.Add(new SourceViolation(
                diagnostic.Location.SourceTree!.FilePath,
                line,
                diagnostic.Location.SourceSpan.Start,
                $"compilation error {diagnostic.Id}: {diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}"));
        }

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

            foreach (var name in root.DescendantNodes().OfType<SimpleNameSyntax>())
            {
                if (name.Ancestors().Any(ancestor => ancestor is UsingDirectiveSyntax)
                    || (name.Parent is MemberAccessExpressionSyntax memberAccess
                        && memberAccess.Name != name))
                {
                    continue;
                }

                var method = semanticModel.GetSymbolInfo(name).Symbol as IMethodSymbol;
                if (method is null
                    || !ForbiddenProviderMethodNames.Contains(method.Name, StringComparer.Ordinal))
                {
                    continue;
                }

                AddViolation(
                    syntaxTree,
                    name,
                    $"references provider detection method '{method.Name}'",
                    violations);
            }

            foreach (var lookup in ConfigurationLookups(root))
            {
                var constant = semanticModel.GetConstantValue(lookup.Key);
                if (!constant.HasValue
                    || constant.Value is not string key
                    || key != PersistenceProviderConfigurationKey
                    || !IsConfigurationType(
                        semanticModel.GetTypeInfo(lookup.Receiver).Type,
                        configurationType))
                {
                    continue;
                }

                AddViolation(
                    syntaxTree,
                    lookup.Key,
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

    private static IEnumerable<ConfigurationLookup> ConfigurationLookups(SyntaxNode root)
    {
        foreach (var elementAccess in root.DescendantNodes().OfType<ElementAccessExpressionSyntax>())
        {
            foreach (var argument in elementAccess.ArgumentList.Arguments)
            {
                yield return new ConfigurationLookup(elementAccess.Expression, argument.Expression);
            }
        }

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                foreach (var argument in invocation.ArgumentList.Arguments)
                {
                    yield return new ConfigurationLookup(memberAccess.Expression, argument.Expression);
                }
            }
        }

        foreach (var conditionalAccess in root.DescendantNodes().OfType<ConditionalAccessExpressionSyntax>())
        {
            if (conditionalAccess.WhenNotNull is ElementBindingExpressionSyntax elementBinding)
            {
                foreach (var argument in elementBinding.ArgumentList.Arguments)
                {
                    yield return new ConfigurationLookup(conditionalAccess.Expression, argument.Expression);
                }
            }
            else if (conditionalAccess.WhenNotNull is InvocationExpressionSyntax
            {
                Expression: MemberBindingExpressionSyntax
            } conditionalInvocation)
            {
                foreach (var argument in conditionalInvocation.ArgumentList.Arguments)
                {
                    yield return new ConfigurationLookup(conditionalAccess.Expression, argument.Expression);
                }
            }
        }
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
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES is required for Roslyn boundary analysis.");
        var assemblyPaths = trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll", SearchOption.TopDirectoryOnly))
            .OrderBy(path => path, StringComparer.Ordinal)
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());

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

    private sealed record ConfigurationLookup(ExpressionSyntax Receiver, ExpressionSyntax Key);
}
