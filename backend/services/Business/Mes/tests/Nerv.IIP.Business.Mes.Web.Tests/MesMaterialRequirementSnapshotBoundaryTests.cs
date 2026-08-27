using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Readiness;

namespace Nerv.IIP.Business.Mes.Web.Tests;

// Contract: Governance + Regression. Authority: Issue #2234 and Review 5042969162.
public sealed class MesMaterialRequirementSnapshotBoundaryTests
{
    [Fact]
    public void Material_requirement_source_root_scan_covers_the_entire_MES_Web_project()
    {
        var webRoot = FindMesWebRoot();
        var actual = EnumerateProductionScanPaths()
            .Select(path => Path.GetRelativePath(webRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Application/Readiness/MaterialRequirementSnapshotReader.cs", actual);
        Assert.Contains("Endpoints/Mes/MesEndpoints.cs", actual);
        Assert.Contains("Program.cs", actual);
    }

    [Fact]
    public void Canonical_reader_caller_inventory_uses_symbol_identity_instead_of_source_spelling()
    {
        const string source = """
            using SnapshotReader = Nerv.IIP.Business.Mes.Web.Application.Readiness.MaterialRequirementSnapshotReader;

            namespace Probe;

            internal sealed class AliasProbe
            {
                internal async Task Handle(
                    Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext dbContext,
                    CancellationToken cancellationToken)
                {
                    _ = await SnapshotReader.LoadLatestByWorkOrderAsync(
                        dbContext,
                        "org",
                        "env",
                        "wo",
                        cancellationToken);
                }
            }
            """;

        var actual = FindCanonicalReaderCallers("Application/Queries/AliasProbe.cs", source);

        Assert.Equal(["Probe.AliasProbe.Handle"], actual);
    }

    public static TheoryData<string, string> EfSourcesBehindStorageAndCastShapes => new()
    {
        {
            "field assignment",
            """
            private readonly IQueryable<MaterialRequirement> rows;

            internal BypassProbe(ApplicationDbContext dbContext)
            {
                rows = dbContext.MaterialRequirements;
            }
            """
        },
        {
            "property assignment",
            """
            private IQueryable<MaterialRequirement> Rows { get; }

            internal BypassProbe(ApplicationDbContext dbContext)
            {
                Rows = dbContext.Set<MaterialRequirement>();
            }
            """
        },
        {
            "assignment after non-generic return cast",
            """
            internal void Read(ApplicationDbContext dbContext)
            {
                object untyped = dbContext.MaterialRequirements;
                IQueryable<MaterialRequirement> rows = (IQueryable<MaterialRequirement>)untyped;
                _ = rows;
            }
            """
        },
    };

    [Fact]
    public void Every_online_snapshot_consumer_calls_the_canonical_reader_directly()
    {
        var actual = FindCanonicalReaderCallers(EnumerateProductionScanPaths())
            .ToHashSet(StringComparer.Ordinal);
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench.CreateMaterialIssueRequestCommandHandler.Handle",
            "Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench.CreateMaterialIssueRequestCommandHandler.ResolveFrozenMaterialSelectionAsync",
            "Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench.GetMesOverviewQueryHandler.CountReleasedWorkOrdersWithMaterialShortageAsync",
            "Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench.GetMaterialReadinessQueryHandler.Handle",
            "Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench.MaterialReadinessGuards.EnsureRequirementSnapshotsAsync",
            "Nerv.IIP.Business.Mes.Web.Application.Readiness.MesOperationTaskActionReadinessEvaluator.EvaluateManyAsync",
            "Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench.MaterialReadinessGuards.GetShortageReasonsAsync",
            "Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench.PrevalidateMaterialScanQueryHandler.Handle",
        };

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void MES_Web_material_requirement_EF_source_roots_are_exactly_owned()
    {
        var violations = FindRawMaterialRequirementAccesses(
                EnumerateProductionScanPaths())
            .Where(access => !IsApprovedRawAccess(access))
            .OrderBy(access => access.Path, StringComparer.Ordinal)
            .ThenBy(access => access.Line)
            .Select(access => $"{Path.GetRelativePath(FindMesWebRoot(), access.Path)}:{access.Line}:{access.ContainingType}.{access.Method}:{access.Kind}")
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Approved_non_consumer_EF_access_inventory_is_exact()
    {
        var actual = FindRawMaterialRequirementAccesses(
                EnumerateProductionScanPaths())
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
    [MemberData(
        nameof(MesMaterialRequirementEfSourceFixtures.EquivalentEfReadBypasses),
        MemberType = typeof(MesMaterialRequirementEfSourceFixtures))]
    public void Equivalent_EF_read_shapes_are_rejected(string _, string statement)
    {
        var source = $$"""
            using System;
            using System.Linq.Expressions;
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

            internal sealed class OtherEntity;
            internal sealed class OtherDynamicState
            {
                internal object[] Entries() => [];
            }
            """;

        var violations = FindRawMaterialRequirementAccesses("Application/Queries/BypassProbe.cs", source).ToArray();

        Assert.DoesNotContain(violations, violation => violation.Kind.StartsWith("CompilationError:", StringComparison.Ordinal));
        Assert.NotEmpty(violations);
    }

    [Theory]
    [MemberData(
        nameof(MesMaterialRequirementEfSourceFixtures.KnownNonTargetEfReadShapes),
        MemberType = typeof(MesMaterialRequirementEfSourceFixtures))]
    public void Known_non_target_EF_shapes_are_not_reported(string _, string statement)
    {
        var source = $$"""
            using System;
            using System.Linq.Expressions;
            using Microsoft.EntityFrameworkCore;
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

            internal sealed class OtherEntity;
            internal sealed class OtherDynamicState
            {
                internal object[] Entries() => [];
            }
            """;

        var violations = FindRawMaterialRequirementAccesses("Application/Queries/NonTargetProbe.cs", source).ToArray();

        Assert.DoesNotContain(violations, violation => violation.Kind.StartsWith("CompilationError:", StringComparison.Ordinal));
        Assert.Empty(violations);
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
    public void Material_requirement_type_metadata_without_EF_reflection_is_not_a_source_root()
    {
        const string source = """
            using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;

            namespace Probe;

            internal sealed class NonEfProbe
            {
                internal Type Read() => typeof(MaterialRequirement);
            }
            """;

        var violations = FindRawMaterialRequirementAccesses("Application/Queries/NonEfProbe.cs", source);

        Assert.DoesNotContain(violations, violation => violation.Kind.StartsWith("CompilationError:", StringComparison.Ordinal));
        Assert.Empty(violations);
    }

    [Theory]
    [MemberData(nameof(EfSourcesBehindStorageAndCastShapes))]
    public void EF_sources_are_rejected_at_the_root_after_storage_or_cast(string _, string members)
    {
        var source = $$"""
            using System.Linq;
            using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
            using Nerv.IIP.Business.Mes.Infrastructure;

            namespace Probe;

            internal sealed class BypassProbe
            {
                {{members}}
            }
            """;

        var violations = FindRawMaterialRequirementAccesses("Application/Queries/BypassProbe.cs", source);

        Assert.DoesNotContain(violations, violation => violation.Kind.StartsWith("CompilationError:", StringComparison.Ordinal));
        Assert.NotEmpty(violations);
    }

    [Fact]
    public void Direct_EF_source_outside_Application_is_rejected()
    {
        const string source = """
            using Nerv.IIP.Business.Mes.Infrastructure;

            namespace Nerv.IIP.Business.Mes.Web.Endpoints;

            internal sealed class BypassProbe
            {
                internal void Read(ApplicationDbContext dbContext)
                {
                    _ = dbContext.MaterialRequirements;
                }
            }
            """;

        var violations = FindRawMaterialRequirementAccesses("Endpoints/BypassProbe.cs", source);

        Assert.DoesNotContain(violations, violation => violation.Kind.StartsWith("CompilationError:", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Kind == "DbSetProperty");
    }

    [Fact]
    public void Typed_carriers_without_a_MES_Web_EF_source_are_not_reported_as_raw_accesses()
    {
        const string source = """
            using System.Linq;
            using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;

            namespace Probe;

            internal sealed class BypassProbe
            {
                private IQueryable<MaterialRequirement> Rows { get; set; } = default!;

                internal void Read(object untyped)
                {
                    Rows = (IQueryable<MaterialRequirement>)untyped;
                    _ = Rows;
                }
            }
            """;

        var violations = FindRawMaterialRequirementAccesses("Application/Queries/BypassProbe.cs", source);

        Assert.DoesNotContain(violations, violation => violation.Kind.StartsWith("CompilationError:", StringComparison.Ordinal));
        Assert.Empty(violations);
    }

    [Fact]
    public void Canonical_reader_exposes_only_materialized_snapshot_values()
    {
        var methods = typeof(MaterialRequirementSnapshotReader)
            .GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Where(method => method.Name.StartsWith("LoadLatest", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(methods);
        Assert.All(methods, method =>
        {
            var exposedTypes = FlattenType(method.ReturnType).ToArray();
            Assert.DoesNotContain(typeof(MaterialRequirement), exposedTypes);
            Assert.DoesNotContain(exposedTypes, type =>
                type.IsGenericType
                && type.GetGenericTypeDefinition() is { } definition
                && (definition == typeof(IQueryable<>)
                    || definition == typeof(IAsyncEnumerable<>)
                    || definition == typeof(DbSet<>)));
        });
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
        var projectTrees = CreateMesWebProjectTrees();
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
    }

    private static string? ClassifyOperation(IOperation operation) => operation switch
    {
        IPropertyReferenceOperation property when IsMaterialRequirementSet(property.Property.Type) =>
            "DbSetProperty",
        IInvocationOperation invocation when IsMaterialRequirementEfRoot(invocation.TargetMethod, invocation) =>
            $"Invocation:{invocation.TargetMethod.Name}",
        IInvocationOperation invocation when IsMaterialRequirementEfReflectionRoot(invocation) =>
            "Reflection:Root",
        IMethodReferenceOperation methodReference when IsMaterialRequirementEfRoot(methodReference.Method, null) =>
            $"MethodReference:{methodReference.Method.Name}",
        IDynamicInvocationOperation dynamicInvocation when IsDynamicMaterialRequirementEfInvocation(dynamicInvocation, operation.SemanticModel!) =>
            "DynamicInvocation",
        _ => null,
    };

    private static bool IsMaterialRequirementEfRoot(IMethodSymbol method, IInvocationOperation? invocation)
    {
        if (!MesEfSourceRootApiSurfaceTests.TryGetRule(method, out var rule)
            || rule.Category != MesEfSourceRootApiSurfaceTests.ApiCategory.Root)
        {
            return false;
        }

        return EvaluateEvidence(
            rule.Evidence,
            method.TypeArguments,
            invocation?.Arguments,
            invocation?.SemanticModel);
    }

    private static bool EvaluateEvidence(
        MesEfSourceRootApiSurfaceTests.EntityEvidencePolicy evidence,
        IReadOnlyList<ITypeSymbol> typeArguments,
        IReadOnlyList<IArgumentOperation>? arguments,
        SemanticModel? semanticModel)
    {
        if (evidence.Kind == MesEfSourceRootApiSurfaceTests.EntityEvidenceKind.AllTrackedEntities)
        {
            return true;
        }

        if (evidence.Kind == MesEfSourceRootApiSurfaceTests.EntityEvidenceKind.ClosedGenericTypeArgument)
        {
            return evidence.Position >= 0
                && evidence.Position < typeArguments.Count
                && IsMaterialRequirement(typeArguments[evidence.Position]);
        }

        if (evidence.Kind != MesEfSourceRootApiSurfaceTests.EntityEvidenceKind.RuntimeTypeArgument
            || arguments is null)
        {
            return evidence.UnknownFailClosed;
        }

        var argument = arguments.FirstOrDefault(item => item.Parameter?.Ordinal == evidence.Position);
        return ResolveRuntimeType(argument?.Value, semanticModel, allowLocal: true) switch
        {
            RuntimeTypeEvidence.MaterialRequirement => true,
            RuntimeTypeEvidence.Other => false,
            _ => evidence.UnknownFailClosed,
        };
    }

    private static bool IsDynamicMaterialRequirementEfInvocation(
        IDynamicInvocationOperation invocation,
        SemanticModel semanticModel)
    {
        if (invocation.Syntax is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } syntax
            || ResolveDynamicOwner(member.Expression, semanticModel, allowLocal: true) is not { } owner)
        {
            return false;
        }

        var genericArguments = member.Name is GenericNameSyntax generic
            ? generic.TypeArgumentList.Arguments.Select(type => semanticModel.GetTypeInfo(type).Type).ToArray()
            : [];
        if (!MesEfSourceRootApiSurfaceTests.TryGetDynamicRule(
                owner,
                member.Name.Identifier.ValueText,
                genericArguments.Length,
                syntax.ArgumentList.Arguments.Count,
                out var rule))
        {
            return false;
        }

        if (rule.Evidence.Kind == MesEfSourceRootApiSurfaceTests.EntityEvidenceKind.AllTrackedEntities)
        {
            return true;
        }

        if (rule.Evidence.Kind == MesEfSourceRootApiSurfaceTests.EntityEvidenceKind.ClosedGenericTypeArgument)
        {
            return rule.Evidence.Position < genericArguments.Length
                && IsMaterialRequirement(genericArguments[rule.Evidence.Position]);
        }

        var argument = rule.Evidence.Position < syntax.ArgumentList.Arguments.Count
            ? semanticModel.GetOperation(syntax.ArgumentList.Arguments[rule.Evidence.Position].Expression)
            : null;
        return ResolveRuntimeType(argument, semanticModel, allowLocal: true) switch
        {
            RuntimeTypeEvidence.MaterialRequirement => true,
            RuntimeTypeEvidence.Other => false,
            _ => rule.Evidence.UnknownFailClosed,
        };
    }

    private static bool IsMaterialRequirementEfReflectionRoot(IInvocationOperation invocation)
    {
        if (invocation.TargetMethod.Name != nameof(System.Reflection.MethodInfo.MakeGenericMethod)
            || invocation.TargetMethod.ContainingType.ToDisplayString() != "System.Reflection.MethodInfo")
        {
            return false;
        }

        var closesOverMaterialRequirement = invocation.Arguments
            .SelectMany(argument => argument.Value.DescendantsAndSelf())
            .OfType<ITypeOfOperation>()
            .Any(typeOf => IsMaterialRequirement(typeOf.TypeOperand));
        if (!closesOverMaterialRequirement)
        {
            return false;
        }

        return invocation.Instance?.DescendantsAndSelf()
            .OfType<IInvocationOperation>()
            .Any(getMethod =>
                getMethod.TargetMethod.Name == nameof(Type.GetMethod)
                && getMethod.TargetMethod.ContainingType.ToDisplayString() == typeof(Type).FullName
                && getMethod.Instance?.DescendantsAndSelf().OfType<ITypeOfOperation>().FirstOrDefault() is { } ownerType
                && getMethod.Arguments.FirstOrDefault()?.Value.ConstantValue is { HasValue: true, Value: string methodName }
                && MesEfSourceRootApiSurfaceTests.HasReflectedGenericRoot(
                    MapOwner(ownerType.TypeOperand as INamedTypeSymbol) ?? string.Empty,
                    methodName,
                    1)) == true;
    }

    private static string? ResolveDynamicOwner(ExpressionSyntax expression, SemanticModel semanticModel, bool allowLocal)
    {
        while (expression is ParenthesizedExpressionSyntax
               || expression is CastExpressionSyntax { Type: IdentifierNameSyntax { Identifier.ValueText: "dynamic" } })
        {
            expression = expression switch
            {
                ParenthesizedExpressionSyntax parenthesized => parenthesized.Expression,
                CastExpressionSyntax cast => cast.Expression,
                _ => expression,
            };
        }

        if (MapOwner(semanticModel.GetTypeInfo(expression).Type as INamedTypeSymbol) is { } direct)
        {
            return direct;
        }

        if (!allowLocal || expression is not IdentifierNameSyntax identifier
            || semanticModel.GetSymbolInfo(identifier).Symbol is not ILocalSymbol local
            || local.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax() is not VariableDeclaratorSyntax declarator
            || declarator.Initializer?.Value is not { } initializer
            || HasLocalWrites(local, declarator, semanticModel))
        {
            return null;
        }

        return ResolveDynamicOwner(initializer, semanticModel, allowLocal: false);
    }

    private static string? MapOwner(INamedTypeSymbol? type)
    {
        if (IsOrInheritsFrom(type, typeof(DbContext).FullName!))
        {
            return typeof(DbContext).FullName;
        }

        return type?.ToDisplayString() == "Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker"
            ? type.ToDisplayString()
            : null;
    }

    private static RuntimeTypeEvidence ResolveRuntimeType(IOperation? operation, SemanticModel? semanticModel, bool allowLocal)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        if (operation is ITypeOfOperation typeOf)
        {
            return IsMaterialRequirement(typeOf.TypeOperand)
                ? RuntimeTypeEvidence.MaterialRequirement
                : RuntimeTypeEvidence.Other;
        }

        if (!allowLocal || semanticModel is null || operation is not ILocalReferenceOperation localReference
            || localReference.Local.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax() is not VariableDeclaratorSyntax declarator
            || declarator.Initializer?.Value is not { } initializer
            || HasLocalWrites(localReference.Local, declarator, semanticModel))
        {
            return RuntimeTypeEvidence.Unknown;
        }

        return ResolveRuntimeType(semanticModel.GetOperation(initializer), semanticModel, allowLocal: false);
    }

    private static bool HasLocalWrites(ILocalSymbol local, VariableDeclaratorSyntax declarator, SemanticModel semanticModel)
    {
        var method = declarator.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        return method?.DescendantNodes().OfType<AssignmentExpressionSyntax>()
            .SelectMany(assignment => assignment.Left.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            .Any(identifier => SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(identifier).Symbol, local)) == true;
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

    private static IEnumerable<Type> FlattenType(Type type)
    {
        yield return type;
        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var nested in FlattenType(argument))
            {
                yield return nested;
            }
        }

        if (type.HasElementType && type.GetElementType() is { } element)
        {
            foreach (var nested in FlattenType(element))
            {
                yield return nested;
            }
        }
    }

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

    private static string FindMesWebRoot() => Path.Combine(
        FindRepositoryRoot(),
        "backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web");

    private static IEnumerable<string> EnumerateProductionScanPaths()
    {
        return Directory.EnumerateFiles(FindMesWebRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path));
    }

    private static IEnumerable<string> FindCanonicalReaderCallers(IEnumerable<string> paths)
    {
        var projectTrees = CreateMesWebProjectTrees();
        var scanPaths = paths.ToHashSet(StringComparer.Ordinal);
        var compilation = CreateCompilation(
            projectTrees,
            "Nerv.IIP.Business.Mes.Web",
            OutputKind.ConsoleApplication);
        return FindCanonicalReaderCallers(
            projectTrees.Where(tree => scanPaths.Contains(tree.FilePath)),
            compilation);
    }

    private static IEnumerable<string> FindCanonicalReaderCallers(string path, string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, ProductionParseOptions, path: path);
        var projectTrees = CreateMesWebProjectTrees();
        var compilation = CreateCompilation(
            projectTrees.Append(tree),
            "MesMaterialRequirementSnapshotCallerFixture",
            OutputKind.ConsoleApplication);
        return FindCanonicalReaderCallers([tree], compilation);
    }

    private static IEnumerable<string> FindCanonicalReaderCallers(
        IEnumerable<SyntaxTree> trees,
        CSharpCompilation compilation)
    {
        foreach (var tree in trees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var reported = new HashSet<TextSpan>();
            foreach (var node in tree.GetRoot().DescendantNodesAndSelf())
            {
                var operation = semanticModel.GetOperation(node);
                var target = operation switch
                {
                    IInvocationOperation invocation => invocation.TargetMethod,
                    IMethodReferenceOperation methodReference => methodReference.Method,
                    _ => null,
                };
                if (target is null || !IsCanonicalReaderMethod(target) || !reported.Add(operation!.Syntax.Span))
                {
                    continue;
                }

                var ownerSyntax = operation.Syntax.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                var owner = ownerSyntax is null ? null : semanticModel.GetDeclaredSymbol(ownerSyntax);
                if (owner?.ContainingType.ToDisplayString()
                    == "Nerv.IIP.Business.Mes.Web.Application.Readiness.MaterialRequirementSnapshotReader")
                {
                    continue;
                }

                yield return owner is null
                    ? "<unknown>"
                    : $"{owner.ContainingType.ToDisplayString()}.{owner.Name}";
            }
        }
    }

    private static bool IsCanonicalReaderMethod(IMethodSymbol method) =>
        method.ContainingType.ToDisplayString()
            == "Nerv.IIP.Business.Mes.Web.Application.Readiness.MaterialRequirementSnapshotReader"
        && method.Name.StartsWith("LoadLatest", StringComparison.Ordinal);

    private static SyntaxTree[] CreateMesWebProjectTrees() =>
        Directory.EnumerateFiles(FindMesWebRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(path => CSharpSyntaxTree.ParseText(
                File.ReadAllText(path),
                ProductionParseOptions,
                path: path))
            .ToArray();

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

    private enum RuntimeTypeEvidence
    {
        Unknown,
        MaterialRequirement,
        Other,
    }

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
