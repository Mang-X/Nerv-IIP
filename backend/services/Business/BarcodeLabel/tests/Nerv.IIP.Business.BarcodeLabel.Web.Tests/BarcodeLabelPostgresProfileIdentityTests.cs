using System.Reflection;
using System.Text.Json;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class BarcodeLabelPostgresProfileIdentityTests
{
    private const string ProfileId = "barcodelabel-postgres-profile";

    [Fact]
    public void Runtime_facts_exactly_match_the_postgres_manifest_and_evidence_policy()
    {
        var runtimeIdentities = typeof(BarcodeLabelPostgresProfileTests)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributesData()
                .Any(attribute => typeof(FactAttribute).IsAssignableFrom(attribute.AttributeType)))
            .Select(method => $"{method.DeclaringType!.FullName}.{method.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        var repositoryRoot = FindRepositoryRoot();
        using var manifest = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(repositoryRoot, "scripts", "postgres-test-lane.json")));
        var manifestMember = Assert.Single(
            manifest.RootElement.GetProperty("members").EnumerateArray(),
            candidate => string.Equals(
                candidate.GetProperty("id").GetString(),
                ProfileId,
                StringComparison.Ordinal));
        var manifestIdentities = ReadSortedIdentities(manifestMember, "expectedTestIdentities");

        using var policy = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(repositoryRoot, "scripts", "test-evidence-policy.json")));
        var policyRule = Assert.Single(
            policy.RootElement.GetProperty("rules").EnumerateArray(),
            candidate => string.Equals(
                candidate.GetProperty("id").GetString(),
                ProfileId,
                StringComparison.Ordinal));
        var policyIdentities = ReadSortedIdentities(policyRule, "testIdentities");

        Assert.Equal(runtimeIdentities, manifestIdentities);
        Assert.Equal(runtimeIdentities, policyIdentities);
        Assert.Equal(runtimeIdentities.Length, policyRule.GetProperty("expectedRuntimeTestCount").GetInt32());
    }

    private static string[] ReadSortedIdentities(JsonElement owner, string propertyName) =>
        owner.GetProperty(propertyName)
            .EnumerateArray()
            .Select(identity => identity.GetString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "scripts", "postgres-test-lane.json")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Cannot locate the repository root from the test output directory.");
    }
}
