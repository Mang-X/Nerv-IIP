using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Nerv.IIP.Business.Mes.Web.Tests;

// Contract: Governance + Regression. Authority: Issue #2234 and Review 5042969162.
public sealed class MesMaterialRequirementSnapshotBoundaryTests
{
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
        var violations = Directory.EnumerateFiles(applicationRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Seed{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(FindRawMaterialRequirementAccesses)
            .Where(access => !IsApprovedRawAccess(access))
            .OrderBy(access => access.Path, StringComparer.Ordinal)
            .ThenBy(access => access.Line)
            .Select(access => $"{Path.GetRelativePath(applicationRoot, access.Path)}:{access.Line}:{access.Method}")
            .ToArray();

        Assert.Empty(violations);
    }

    private static IEnumerable<RawAccess> FindRawMaterialRequirementAccesses(string path)
    {
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path);
        var root = tree.GetRoot();
        foreach (var access in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                     .Where(x => x.Name.Identifier.ValueText == "MaterialRequirements"))
        {
            var method = access.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            var line = tree.GetLineSpan(access.Span).StartLinePosition.Line + 1;
            yield return new RawAccess(path, line, method?.Identifier.ValueText ?? "<unknown>");
        }
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
        var normalized = access.Path.Replace(Path.DirectorySeparatorChar, '/');
        return normalized.EndsWith("/Readiness/MaterialRequirementSnapshotReader.cs", StringComparison.Ordinal)
            || (normalized.EndsWith("/Commands/Workbench/MesWorkbenchCommands.cs", StringComparison.Ordinal)
                && access.Method == "EnsureRequirementSnapshotsAsync")
            || (normalized.EndsWith("/IntegrationEventHandlers/EngineeringChangeReleasedIntegrationEventHandlerForMesWip.cs", StringComparison.Ordinal)
                && access.Method == "HandleValidEventCoreAsync");
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

    private sealed record RawAccess(string Path, int Line, string Method);
}
