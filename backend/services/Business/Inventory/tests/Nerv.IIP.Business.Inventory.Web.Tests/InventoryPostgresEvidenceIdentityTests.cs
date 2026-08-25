using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

public sealed class InventoryPostgresEvidenceIdentityTests
{
    private const string LineSidePolicyId = "inventory-line-side-balance-external";
    private const string InventoryManifestMemberId = "inventory-postgres-profile";

    [Fact]
    public void Line_side_provider_method_uses_its_registered_postgres_evidence_identity()
    {
        var method = typeof(InventoryDirectoryPostgresTests).GetMethod(
            nameof(InventoryDirectoryPostgresTests.Line_side_balance_grouping_and_age_completeness_execute_on_postgres),
            BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);

        var factAttribute = Assert.Single(method.GetCustomAttributes(inherit: false).OfType<FactAttribute>());
        Assert.IsType<LineSideInventoryBalanceExternalPostgresFactAttribute>(factAttribute);

        var identity = $"{method.DeclaringType!.FullName}.{method.Name}";
        var repoRoot = FindRepoRoot();
        using var policy = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(repoRoot, "scripts", "test-evidence-policy.json")));
        var rule = Assert.Single(
            policy.RootElement.GetProperty("rules").EnumerateArray(),
            candidate => string.Equals(
                candidate.GetProperty("id").GetString(),
                LineSidePolicyId,
                StringComparison.Ordinal));

        Assert.Equal(LineSidePolicyId, rule.GetProperty("sourceId").GetString());
        Assert.Equal(
            new[] { identity },
            rule.GetProperty("testIdentities").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(1, rule.GetProperty("expectedRuntimeTestCount").GetInt32());

        var skipReason = LineSideInventoryBalanceExternalPostgresFactAttribute.GetSkipReason(null);
        Assert.NotNull(skipReason);
        Assert.Matches(
            new Regex(rule.GetProperty("reasonPattern").GetString()!, RegexOptions.CultureInvariant),
            skipReason);
        Assert.Null(LineSideInventoryBalanceExternalPostgresFactAttribute.GetSkipReason(
            "Host=localhost;Database=inventory"));

        using var manifest = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(repoRoot, "scripts", "postgres-test-lane.json")));
        var member = Assert.Single(
            manifest.RootElement.GetProperty("members").EnumerateArray(),
            candidate => string.Equals(
                candidate.GetProperty("id").GetString(),
                InventoryManifestMemberId,
                StringComparison.Ordinal));
        Assert.Contains(
            identity,
            member.GetProperty("expectedTestIdentities").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains($"FullyQualifiedName~{identity}", member.GetProperty("filter").GetString(), StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "scripts", "test-evidence-policy.json")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Cannot locate the repository root from the test output directory.");
    }
}
