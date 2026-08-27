using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Tests;

// Contract: Governance + Regression. Authority: Issue #2234 and Review 5042969162.
public sealed class MesMaterialRequirementSnapshotBoundaryTests
{
    public static TheoryData<string, string> EquivalentEfReadBypasses => new()
    {
        {
            "DbSet property through receiver alias",
            "var context = dbContext; var rows = context.MaterialRequirements;"
        },
        {
            "generic Set",
            "var rows = dbContext.Set<MaterialRequirement>();"
        },
        {
            "generic Set through receiver and entity aliases",
            "DbContext context = dbContext; var rows = context.Set<Requirement>();"
        },
        {
            "dynamic receiver with a resolved entity alias",
            "dynamic context = dbContext; var rows = context.Set<Requirement>();"
        },
        {
            "shared type Set",
            "var rows = dbContext.Set<MaterialRequirement>(\"material-requirements\");"
        },
        {
            "Set Type reflection",
            "var rows = typeof(DbContext).GetMethod(nameof(DbContext.Set))!.MakeGenericMethod(typeof(MaterialRequirement));"
        },
        {
            "typed raw query",
            "var rows = dbContext.Database.SqlQuery<MaterialRequirement>($\"SELECT * FROM mes.material_requirements\");"
        },
        {
            "FromSql rooted at generic Set",
            "var rows = dbContext.Set<MaterialRequirement>().FromSqlRaw(\"SELECT * FROM mes.material_requirements\");"
        },
    };

    [Fact]
    public void Every_online_snapshot_consumer_calls_the_canonical_reader_directly()
    {
        var applicationRoot = Path.Combine(
            FindRepositoryRoot(),
            "backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application");
        var actual = Directory.EnumerateFiles(applicationRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(FindCanonicalReaderCallers)
            .ToHashSet(StringComparer.Ordinal);
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "CreateMaterialIssueRequestCommandHandler.Handle",
            "CreateMaterialIssueRequestCommandHandler.ResolveFrozenMaterialSelectionAsync",
            "GetMesOverviewQueryHandler.CountReleasedWorkOrdersWithMaterialShortageAsync",
            "GetMaterialReadinessQueryHandler.Handle",
            "MaterialReadinessGuards.EnsureRequirementSnapshotsAsync",
            "MesOperationTaskActionReadinessEvaluator.EvaluateManyAsync",
            "MaterialReadinessGuards.GetShortageReasonsAsync",
            "PrevalidateMaterialScanQueryHandler.Handle",
        };

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Online_material_requirement_consumers_cannot_bypass_the_canonical_reader()
    {
        var applicationRoot = Path.Combine(
            FindRepositoryRoot(),
            "backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application");
        var violations = FindRawMaterialRequirementAccesses(
                Directory.EnumerateFiles(applicationRoot, "*.cs", SearchOption.AllDirectories))
            .Where(access => !IsApprovedRawAccess(access))
            .OrderBy(access => access.Path, StringComparer.Ordinal)
            .ThenBy(access => access.Line)
            .Select(access => $"{Path.GetRelativePath(applicationRoot, access.Path)}:{access.Line}:{access.Method}")
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Approved_non_consumer_EF_access_inventory_is_exact()
    {
        var applicationRoot = Path.Combine(
            FindRepositoryRoot(),
            "backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application");
        var actual = FindRawMaterialRequirementAccesses(
                Directory.EnumerateFiles(applicationRoot, "*.cs", SearchOption.AllDirectories))
            .Where(IsApprovedRawAccess)
            .GroupBy(access => access.ApprovalKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var expected = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Readiness/MaterialRequirementSnapshotReader.cs|LoadLatestCapturesAsync|DbSetProperty"] = 2,
            ["Commands/Workbench/MesWorkbenchCommands.cs|EnsureRequirementSnapshotsAsync|DbSetProperty"] = 1,
            ["IntegrationEventHandlers/EngineeringChangeReleasedIntegrationEventHandlerForMesWip.cs|HandleValidEventCoreAsync|DbSetProperty"] = 2,
            ["Seed/WorldHistorySeedService.cs|WriteMaterialFacts|DbSetProperty"] = 1,
            ["Seed/WorldHistoryConsistencyValidator.cs|LoadKittingCountsAsync|DbSetProperty"] = 1,
            ["Seed/WorldHistoryConsistencyValidator.cs|LoadKittingGapsAsync|DbSetProperty"] = 1,
        };

        Assert.Equal(
            expected.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}"),
            actual.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}"));
    }

    [Theory]
    [MemberData(nameof(EquivalentEfReadBypasses))]
    public void Equivalent_EF_read_shapes_are_rejected(string _, string statement)
    {
        var source = $$"""
            using Microsoft.EntityFrameworkCore;
            using Requirement = Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate.MaterialRequirement;
            using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
            using Nerv.IIP.Business.Mes.Infrastructure;

            namespace Probe;

            internal sealed class BypassProbe
            {
                internal void Read(ApplicationDbContext dbContext)
                {
                    {{statement}}
                }
            }
            """;

        var violations = FindRawMaterialRequirementAccesses("Application/Queries/BypassProbe.cs", source).ToArray();

        Assert.NotEmpty(violations);
    }

    [Fact]
    public void Unrelated_same_named_property_is_not_an_EF_access()
    {
        const string source = """
            namespace Probe;

            internal sealed class OtherState
            {
                internal int MaterialRequirements { get; init; }
            }

            internal sealed class NonEfProbe
            {
                internal int Read(OtherState state) => state.MaterialRequirements;
            }
            """;

        var violations = FindRawMaterialRequirementAccesses("Application/Queries/NonEfProbe.cs", source);

        Assert.Empty(violations);
    }

    private static IEnumerable<RawAccess> FindRawMaterialRequirementAccesses(IEnumerable<string> paths)
    {
        var trees = paths
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "MesMaterialRequirementSnapshotBoundary",
            trees,
            CreateMetadataReferences());
        return trees.SelectMany(tree => FindRawMaterialRequirementAccesses(tree, compilation.GetSemanticModel(tree)));
    }

    private static IEnumerable<RawAccess> FindRawMaterialRequirementAccesses(string path, string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: path);
        var compilation = CSharpCompilation.Create(
            "MesMaterialRequirementSnapshotBoundary",
            [tree],
            CreateMetadataReferences());
        return FindRawMaterialRequirementAccesses(tree, compilation.GetSemanticModel(tree));
    }

    private static IEnumerable<RawAccess> FindRawMaterialRequirementAccesses(
        SyntaxTree tree,
        SemanticModel semanticModel)
    {
        var path = tree.FilePath;
        foreach (var access in FindMaterialRequirementEfAccesses(tree, semanticModel))
        {
            var method = access.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            var line = tree.GetLineSpan(access.Span).StartLinePosition.Line + 1;
            var kind = access switch
            {
                MemberAccessExpressionSyntax => "DbSetProperty",
                InvocationExpressionSyntax invocation => $"Invocation:{GetInvocationName(invocation)}",
                TypeOfExpressionSyntax => "TypeOf",
                _ => access.Kind().ToString(),
            };
            yield return new RawAccess(path, line, method?.Identifier.ValueText ?? "<unknown>", kind);
        }
    }

    private static string GetInvocationName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        _ => invocation.Expression.ToString(),
    };

    private static IEnumerable<SyntaxNode> FindMaterialRequirementEfAccesses(
        SyntaxTree tree,
        SemanticModel semanticModel)
    {
        var reported = new HashSet<TextSpan>();
        foreach (var memberAccess in tree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            var symbol = semanticModel.GetSymbolInfo(memberAccess).Symbol;
            if (((symbol is IPropertySymbol property && IsMaterialRequirementSet(property.Type))
                    || (symbol is null && memberAccess.Name.Identifier.ValueText == "MaterialRequirements"))
                && reported.Add(memberAccess.Span))
            {
                yield return memberAccess;
            }
        }

        foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            var isRelevant = symbol is not null
                ? IsMaterialRequirementEfMethod(symbol)
                : IsUnresolvedMaterialRequirementEfInvocation(invocation, semanticModel);

            if (isRelevant && reported.Add(invocation.Span))
            {
                yield return invocation;
            }
        }

        foreach (var typeOf in tree.GetRoot().DescendantNodes().OfType<TypeOfExpressionSyntax>())
        {
            if (IsMaterialRequirement(semanticModel.GetTypeInfo(typeOf.Type).Type)
                && reported.Add(typeOf.Span))
            {
                yield return typeOf;
            }
        }
    }

    private static bool IsMaterialRequirementEfMethod(IMethodSymbol method)
    {
        var original = method.ReducedFrom ?? method;
        if (method.TypeArguments.Concat(original.TypeArguments).Any(IsMaterialRequirement))
        {
            return original.Name is "Set" or "SqlQuery" or "SqlQueryRaw" or "FromSql" or "FromSqlRaw" or "FromSqlInterpolated";
        }

        return false;
    }

    private static bool IsUnresolvedMaterialRequirementEfInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        if (GetInvocationName(invocation) is not ("Set" or "SqlQuery" or "SqlQueryRaw" or "FromSql" or "FromSqlRaw" or "FromSqlInterpolated"))
        {
            return false;
        }

        return invocation.Expression.DescendantNodesAndSelf()
            .OfType<GenericNameSyntax>()
            .SelectMany(generic => generic.TypeArgumentList.Arguments)
            .Any(type => IsMaterialRequirement(semanticModel.GetTypeInfo(type).Type));
    }

    private static bool IsMaterialRequirementSet(ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && named.Name == nameof(DbSet<MaterialRequirement>)
        && named.ContainingNamespace.ToDisplayString() == "Microsoft.EntityFrameworkCore"
        && named.TypeArguments.Length == 1
        && IsMaterialRequirement(named.TypeArguments[0]);

    private static bool IsMaterialRequirement(ITypeSymbol? type) =>
        type?.ToDisplayString() == typeof(MaterialRequirement).FullName;

    private static IReadOnlyCollection<MetadataReference> CreateMetadataReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var paths = trustedPlatformAssemblies?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                .Select(assembly => assembly.Location))
            .Append(typeof(ApplicationDbContext).Assembly.Location)
            .Append(typeof(MaterialRequirement).Assembly.Location)
            .Append(typeof(DbContext).Assembly.Location)
            .Append(typeof(RelationalDatabaseFacadeExtensions).Assembly.Location)
            .Distinct(StringComparer.Ordinal)
            ?? [typeof(object).Assembly.Location];

        return paths.Select(path => MetadataReference.CreateFromFile(path)).ToArray();
    }

    private static IEnumerable<string> FindCanonicalReaderCallers(string path)
    {
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetRoot();
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                     .Where(x => x.Expression.ToString().StartsWith("MaterialRequirementSnapshotReader.LoadLatest", StringComparison.Ordinal)))
        {
            var method = invocation.Ancestors().OfType<MethodDeclarationSyntax>().First();
            var type = method.Ancestors().OfType<TypeDeclarationSyntax>().First();
            yield return $"{type.Identifier.ValueText}.{method.Identifier.ValueText}";
        }
    }

    private static bool IsApprovedRawAccess(RawAccess access)
    {
        if (access.Kind != "DbSetProperty")
        {
            return false;
        }

        var normalized = access.Path.Replace(Path.DirectorySeparatorChar, '/');
        return normalized.EndsWith("/Readiness/MaterialRequirementSnapshotReader.cs", StringComparison.Ordinal)
            || (normalized.EndsWith("/Commands/Workbench/MesWorkbenchCommands.cs", StringComparison.Ordinal)
                && access.Method == "EnsureRequirementSnapshotsAsync")
            || (normalized.EndsWith("/IntegrationEventHandlers/EngineeringChangeReleasedIntegrationEventHandlerForMesWip.cs", StringComparison.Ordinal)
                && access.Method == "HandleValidEventCoreAsync")
            || (normalized.EndsWith("/Seed/WorldHistorySeedService.cs", StringComparison.Ordinal)
                && access.Method == "WriteMaterialFacts")
            || (normalized.EndsWith("/Seed/WorldHistoryConsistencyValidator.cs", StringComparison.Ordinal)
                && access.Method is "LoadKittingCountsAsync" or "LoadKittingGapsAsync");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "NuGet.config"))
                && File.Exists(Path.Combine(current.FullName, "backend", "Nerv.IIP.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed record RawAccess(string Path, int Line, string Method, string Kind)
    {
        public string ApprovalKey
        {
            get
            {
                const string marker = "/Application/";
                var normalized = Path.Replace(System.IO.Path.DirectorySeparatorChar, '/');
                var markerIndex = normalized.LastIndexOf(marker, StringComparison.Ordinal);
                var relative = markerIndex >= 0 ? normalized[(markerIndex + marker.Length)..] : normalized;
                return $"{relative}|{Method}|{Kind}";
            }
        }
    }
}
