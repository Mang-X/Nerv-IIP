using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
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
        {
            "generic Set method group",
            "Func<DbSet<MaterialRequirement>> read = dbContext.Set<MaterialRequirement>; var rows = read();"
        },
        {
            "ChangeTracker entries",
            "var rows = dbContext.ChangeTracker.Entries<MaterialRequirement>();"
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
            .Select(access => $"{Path.GetRelativePath(applicationRoot, access.Path)}:{access.Line}:{access.ContainingType}.{access.Method}:{access.Kind}")
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
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
            ["Readiness/MaterialRequirementSnapshotReader.cs|Nerv.IIP.Business.Mes.Web.Application.Readiness.MaterialRequirementSnapshotReader|LoadLatestCapturesAsync|DbSetProperty"] = 2,
            ["Commands/Workbench/MesWorkbenchCommands.cs|Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench.MaterialReadinessGuards|EnsureRequirementSnapshotsAsync|DbSetProperty"] = 1,
            ["IntegrationEventHandlers/EngineeringChangeReleasedIntegrationEventHandlerForMesWip.cs|Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers.EngineeringChangeReleasedIntegrationEventHandlerForMesWip|HandleValidEventCoreAsync|DbSetProperty"] = 2,
            ["Seed/WorldHistorySeedService.cs|Nerv.IIP.Business.Mes.Web.Application.Seed.WorldHistorySeedService|WriteMaterialFacts|DbSetProperty"] = 1,
            ["Seed/WorldHistoryConsistencyValidator.cs|Nerv.IIP.Business.Mes.Web.Application.Seed.WorldHistoryConsistencyValidator|LoadKittingCountsAsync|DbSetProperty"] = 1,
            ["Seed/WorldHistoryConsistencyValidator.cs|Nerv.IIP.Business.Mes.Web.Application.Seed.WorldHistoryConsistencyValidator|LoadKittingGapsAsync|DbSetProperty"] = 1,
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
            using System;
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

        Assert.DoesNotContain(violations, violation => violation.Kind.StartsWith("CompilationError:", StringComparison.Ordinal));
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

    [Fact]
    public void Typed_query_carrier_injected_into_a_consumer_is_rejected()
    {
        const string source = """
            using System.Linq;
            using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;

            namespace Probe;

            internal sealed class BypassProbe
            {
                internal void Read(IQueryable<MaterialRequirement> requirements)
                {
                    _ = requirements;
                }
            }
            """;

        var violations = FindRawMaterialRequirementAccesses("Application/Queries/BypassProbe.cs", source);

        Assert.DoesNotContain(violations, violation => violation.Kind.StartsWith("CompilationError:", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Kind == "CarrierParameter");
    }

    [Fact]
    public void Typed_query_carrier_returned_by_a_consumer_is_rejected()
    {
        const string source = """
            using System.Linq;
            using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;

            namespace Probe;

            internal sealed class BypassProbe
            {
                internal IQueryable<MaterialRequirement> Read() => default!;
            }
            """;

        var violations = FindRawMaterialRequirementAccesses("Application/Queries/BypassProbe.cs", source);

        Assert.DoesNotContain(violations, violation => violation.Kind.StartsWith("CompilationError:", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Kind == "CarrierReturn");
    }

    [Fact]
    public void Retyped_query_carrier_cannot_cross_a_non_generic_helper()
    {
        const string source = """
            using System.Linq;
            using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;

            namespace Probe;

            internal sealed class BypassProbe
            {
                internal void Read(IQueryable requirements)
                {
                    var typed = requirements.OfType<MaterialRequirement>().AsQueryable();
                    Consume((IQueryable)typed);
                }

                private static void Consume(IQueryable requirements) => _ = requirements;
            }
            """;

        var violations = FindRawMaterialRequirementAccesses("Application/Queries/BypassProbe.cs", source);

        Assert.DoesNotContain(violations, violation => violation.Kind.StartsWith("CompilationError:", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Kind == "CarrierArgument");
    }

    [Fact]
    public void Invalid_compilation_fails_closed_before_semantic_analysis()
    {
        const string source = """
            namespace Probe;

            internal sealed class BypassProbe
            {
                internal void Read(MissingMaterialRequirementContext context) => _ = context;
            }
            """;

        var violations = FindRawMaterialRequirementAccesses("Application/Queries/InvalidProbe.cs", source);

        Assert.Contains(violations, violation => violation.Kind.StartsWith("CompilationError:CS0246:", StringComparison.Ordinal));
    }

    [Fact]
    public void Approved_method_name_in_an_unapproved_containing_type_is_rejected()
    {
        const string source = """
            using Nerv.IIP.Business.Mes.Infrastructure;

            namespace Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

            internal sealed class ApprovalCollision
            {
                internal void EnsureRequirementSnapshotsAsync(ApplicationDbContext dbContext)
                {
                    _ = dbContext.MaterialRequirements;
                }
            }
            """;
        var repositoryRoot = FindRepositoryRoot();
        var path = Path.Combine(
            repositoryRoot,
            "backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs");
        var violations = FindRawMaterialRequirementAccesses(path, source)
            .ToArray();

        Assert.DoesNotContain(violations, violation => violation.Kind.StartsWith("CompilationError:", StringComparison.Ordinal));
        Assert.Contains(violations, access =>
            access.Kind == "DbSetProperty"
            && access.ContainingType == "Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench.ApprovalCollision"
            && !IsApprovedRawAccess(access));
    }

    private static IEnumerable<RawAccess> FindRawMaterialRequirementAccesses(IEnumerable<string> paths)
    {
        var scanPaths = paths.ToHashSet(StringComparer.Ordinal);
        var webRoot = Path.Combine(
            FindRepositoryRoot(),
            "backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web");
        var projectTrees = Directory.EnumerateFiles(webRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), ProductionParseOptions, path: path))
            .ToArray();
        var scanTrees = projectTrees.Where(tree => scanPaths.Contains(tree.FilePath)).ToArray();
        var compilation = CreateCompilation(
            projectTrees,
            "Nerv.IIP.Business.Mes.Web",
            OutputKind.ConsoleApplication);
        return FindRawMaterialRequirementAccesses(scanTrees, projectTrees, compilation);
    }

    private static IEnumerable<RawAccess> FindRawMaterialRequirementAccesses(string path, string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, ProductionParseOptions, path: path);
        var compilation = CreateCompilation(
            [tree],
            "MesMaterialRequirementSnapshotBoundaryFixture",
            OutputKind.DynamicallyLinkedLibrary);
        return FindRawMaterialRequirementAccesses([tree], [tree], compilation);
    }

    private static IEnumerable<RawAccess> FindRawMaterialRequirementAccesses(
        IReadOnlyCollection<SyntaxTree> trees,
        IReadOnlyCollection<SyntaxTree> diagnosticTrees,
        CSharpCompilation compilation)
    {
        var diagnostics = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Location.SourceTree is not null
                && diagnosticTrees.Contains(diagnostic.Location.SourceTree))
            .OrderBy(diagnostic => diagnostic.Location.SourceTree!.FilePath, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .ToArray();
        if (diagnostics.Length > 0)
        {
            return diagnostics.Select(diagnostic =>
            {
                var tree = diagnostic.Location.SourceTree!;
                var line = diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;
                return new RawAccess(
                    tree.FilePath,
                    line,
                    "<compilation>",
                    "<compilation>",
                    $"CompilationError:{diagnostic.Id}:{diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}");
            }).ToArray();
        }

        return trees.SelectMany(tree =>
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            return FindMaterialRequirementEfAccesses(tree, semanticModel).Select(access =>
            {
                var methodSyntax = access.Node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                var method = methodSyntax is null ? null : semanticModel.GetDeclaredSymbol(methodSyntax);
                var line = tree.GetLineSpan(access.Node.Span).StartLinePosition.Line + 1;
                return new RawAccess(
                    tree.FilePath,
                    line,
                    method?.ContainingType.ToDisplayString() ?? "<unknown>",
                    method?.Name ?? "<unknown>",
                    access.Kind);
            });
        }).ToArray();
    }

    private static string GetInvocationName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        _ => invocation.Expression.ToString(),
    };

    private static IEnumerable<SemanticAccess> FindMaterialRequirementEfAccesses(
        SyntaxTree tree,
        SemanticModel semanticModel)
    {
        var reported = new HashSet<TextSpan>();
        foreach (var node in tree.GetRoot().DescendantNodesAndSelf())
        {
            var operation = semanticModel.GetOperation(node);
            var kind = operation is null ? null : ClassifyOperation(operation);
            if (kind is not null && reported.Add(node.Span))
            {
                yield return new SemanticAccess(node, kind);
            }
        }

        foreach (var parameter in tree.GetRoot().DescendantNodes().OfType<ParameterSyntax>())
        {
            var symbol = semanticModel.GetDeclaredSymbol(parameter);
            if (symbol is not null && IsMaterialRequirementQueryCarrier(symbol.Type) && reported.Add(parameter.Span))
            {
                yield return new SemanticAccess(parameter, "CarrierParameter");
            }
        }

        foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var symbol = semanticModel.GetDeclaredSymbol(method);
            if (symbol is not null && IsMaterialRequirementQueryCarrier(symbol.ReturnType) && reported.Add(method.ReturnType.Span))
            {
                yield return new SemanticAccess(method.ReturnType, "CarrierReturn");
            }
        }
    }

    private static string? ClassifyOperation(IOperation operation) => operation switch
    {
        IPropertyReferenceOperation property when IsMaterialRequirementSet(property.Property.Type) =>
            "DbSetProperty",
        IInvocationOperation invocation when IsMaterialRequirementEfMethod(invocation.TargetMethod) =>
            $"Invocation:{invocation.TargetMethod.Name}",
        IMethodReferenceOperation methodReference when IsMaterialRequirementEfMethod(methodReference.Method) =>
            $"MethodReference:{methodReference.Method.Name}",
        ITypeOfOperation typeOf when IsMaterialRequirement(typeOf.TypeOperand) =>
            "TypeOf",
        IDynamicInvocationOperation dynamicInvocation when IsDynamicMaterialRequirementEfInvocation(dynamicInvocation, operation.SemanticModel!) =>
            "DynamicInvocation",
        IInvocationOperation invocation when IsMaterialRequirementCarrierRetype(invocation.TargetMethod) =>
            $"CarrierRetype:{invocation.TargetMethod.Name}",
        IArgumentOperation argument when ContainsMaterialRequirementCarrier(argument.Value)
            && !IsQueryPreservingMethod(argument.Parent as IInvocationOperation) =>
            "CarrierArgument",
        _ => null,
    };

    private static bool IsMaterialRequirementEfMethod(IMethodSymbol method)
    {
        var original = method.ReducedFrom ?? method;
        if (!method.TypeArguments.Concat(original.TypeArguments).Any(IsMaterialRequirement))
        {
            return false;
        }

        var containingType = original.ContainingType.ToDisplayString();
        return (original.Name == "Set" && IsOrInheritsFrom(original.ContainingType, typeof(DbContext).FullName!))
            || (original.Name == "Entries" && containingType == "Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker")
            || (original.Name is "SqlQuery" or "SqlQueryRaw"
                && containingType == "Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions")
            || (original.Name is "FromSql" or "FromSqlRaw" or "FromSqlInterpolated"
                && containingType == "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions");
    }

    private static bool IsDynamicMaterialRequirementEfInvocation(
        IDynamicInvocationOperation invocation,
        SemanticModel semanticModel)
    {
        if (invocation.Syntax is not InvocationExpressionSyntax syntax
            || GetInvocationName(syntax) is not ("Set" or "SqlQuery" or "SqlQueryRaw" or "FromSql" or "FromSqlRaw" or "FromSqlInterpolated"))
        {
            return false;
        }

        return syntax.Expression.DescendantNodesAndSelf()
            .OfType<GenericNameSyntax>()
            .SelectMany(generic => generic.TypeArgumentList.Arguments)
            .Any(type => IsMaterialRequirement(semanticModel.GetTypeInfo(type).Type));
    }

    private static bool IsMaterialRequirementCarrierRetype(IMethodSymbol method)
    {
        var original = method.ReducedFrom ?? method;
        return original.ContainingType.ToDisplayString() == "System.Linq.Queryable"
            && original.Name is "Cast" or "OfType" or "AsQueryable"
            && (method.TypeArguments.Concat(original.TypeArguments).Any(IsMaterialRequirement)
                || IsMaterialRequirementQueryCarrier(method.ReturnType));
    }

    private static bool IsQueryPreservingMethod(IInvocationOperation? invocation)
    {
        if (invocation is null)
        {
            return false;
        }

        var original = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        var containingType = original.ContainingType.ToDisplayString();
        return containingType is "System.Linq.Queryable"
            or "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions"
            or "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions";
    }

    private static bool ContainsMaterialRequirementCarrier(IOperation operation)
    {
        for (var current = operation; current is not null; current = current switch
        {
            IConversionOperation conversion => conversion.Operand,
            IParenthesizedOperation parenthesized => parenthesized.Operand,
            _ => null,
        })
        {
            if (IsMaterialRequirementQueryCarrier(current.Type))
            {
                return true;
            }
        }

        return operation.Descendants().Any(descendant => IsMaterialRequirementQueryCarrier(descendant.Type));
    }

    private static bool IsMaterialRequirementQueryCarrier(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (named.IsGenericType
            && named.TypeArguments.Any(IsMaterialRequirement)
            && named.ConstructedFrom.ToDisplayString() is "System.Linq.IQueryable<T>"
                or "System.Collections.Generic.IAsyncEnumerable<T>"
                or "Microsoft.EntityFrameworkCore.DbSet<TEntity>"
                or "Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>")
        {
            return true;
        }

        return named.AllInterfaces.Any(IsMaterialRequirementQueryCarrier);
    }

    private static bool IsOrInheritsFrom(INamedTypeSymbol? type, string metadataName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == metadataName)
            {
                return true;
            }
        }

        return false;
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
            .Concat(Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll", SearchOption.TopDirectoryOnly))
            .Append(typeof(ApplicationDbContext).Assembly.Location)
            .Append(typeof(MaterialRequirement).Assembly.Location)
            .Append(typeof(DbContext).Assembly.Location)
            .Append(typeof(RelationalDatabaseFacadeExtensions).Assembly.Location)
            .Where(path => Path.GetFileName(path) is not "Nerv.IIP.Business.Mes.Web.dll"
                and not "Nerv.IIP.Business.Mes.Web.Tests.dll")
            .Distinct(StringComparer.Ordinal)
            ?? [typeof(object).Assembly.Location];

        return paths.Select(path => MetadataReference.CreateFromFile(path)).ToArray();
    }

    private static CSharpCompilation CreateCompilation(
        IEnumerable<SyntaxTree> sourceTrees,
        string assemblyName,
        OutputKind outputKind)
    {
        var globalUsings = CSharpSyntaxTree.ParseText(
            WebGlobalUsings,
            ProductionParseOptions,
            path: "MesWebGlobalUsings.g.cs");
        return CSharpCompilation.Create(
            assemblyName,
            sourceTrees.Prepend(globalUsings),
            CreateMetadataReferences(),
            new CSharpCompilationOptions(
                outputKind,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static bool IsBuildOutput(string path)
    {
        var normalized = path.Replace(Path.DirectorySeparatorChar, '/');
        return normalized.Contains("/bin/", StringComparison.Ordinal)
            || normalized.Contains("/obj/", StringComparison.Ordinal);
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
        return (normalized.EndsWith("/Readiness/MaterialRequirementSnapshotReader.cs", StringComparison.Ordinal)
                && access.ContainingType == "Nerv.IIP.Business.Mes.Web.Application.Readiness.MaterialRequirementSnapshotReader"
                && access.Method == "LoadLatestCapturesAsync")
            || (normalized.EndsWith("/Commands/Workbench/MesWorkbenchCommands.cs", StringComparison.Ordinal)
                && access.ContainingType == "Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench.MaterialReadinessGuards"
                && access.Method == "EnsureRequirementSnapshotsAsync")
            || (normalized.EndsWith("/IntegrationEventHandlers/EngineeringChangeReleasedIntegrationEventHandlerForMesWip.cs", StringComparison.Ordinal)
                && access.ContainingType == "Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers.EngineeringChangeReleasedIntegrationEventHandlerForMesWip"
                && access.Method == "HandleValidEventCoreAsync")
            || (normalized.EndsWith("/Seed/WorldHistorySeedService.cs", StringComparison.Ordinal)
                && access.ContainingType == "Nerv.IIP.Business.Mes.Web.Application.Seed.WorldHistorySeedService"
                && access.Method == "WriteMaterialFacts")
            || (normalized.EndsWith("/Seed/WorldHistoryConsistencyValidator.cs", StringComparison.Ordinal)
                && access.ContainingType == "Nerv.IIP.Business.Mes.Web.Application.Seed.WorldHistoryConsistencyValidator"
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

    private static readonly CSharpParseOptions ProductionParseOptions = new(LanguageVersion.Preview);

    private const string WebGlobalUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Net.Http.Json;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using Microsoft.AspNetCore.Builder;
        global using Microsoft.AspNetCore.Hosting;
        global using Microsoft.AspNetCore.Http;
        global using Microsoft.AspNetCore.Routing;
        global using Microsoft.Extensions.Configuration;
        global using Microsoft.Extensions.DependencyInjection;
        global using Microsoft.Extensions.Hosting;
        global using Microsoft.Extensions.Logging;
        global using NetCorePal.Extensions.DependencyInjection;
        global using NetCorePal.Extensions.Primitives;
        """;

    private sealed record SemanticAccess(SyntaxNode Node, string Kind);

    private sealed record RawAccess(string Path, int Line, string ContainingType, string Method, string Kind)
    {
        public string ApprovalKey
        {
            get
            {
                const string marker = "/Application/";
                var normalized = Path.Replace(System.IO.Path.DirectorySeparatorChar, '/');
                var markerIndex = normalized.LastIndexOf(marker, StringComparison.Ordinal);
                var relative = markerIndex >= 0 ? normalized[(markerIndex + marker.Length)..] : normalized;
                return $"{relative}|{ContainingType}|{Method}|{Kind}";
            }
        }
    }
}
