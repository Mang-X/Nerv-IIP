using System.Reflection;
using System.Text.Json;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class BarcodeLabelPostgresEvidenceGovernanceTests
{
    private const string ProfileClassName =
        "Nerv.IIP.Business.BarcodeLabel.Web.Tests.BarcodeLabelPostgresProfileTests";

    [Fact]
    public void Every_profile_test_is_registered_in_the_postgres_lane_and_evidence_policy()
    {
        var repositoryRoot = FindRepositoryRoot();
        var discovered = DiscoverProfileTestIdentities();
        var manifest = ReadRegisteredIdentities(
            Path.Combine(repositoryRoot, "scripts", "postgres-test-lane.json"),
            "members",
            "barcodelabel-postgres-profile",
            "expectedTestIdentities",
            expectedCountProperty: null);
        var policy = ReadRegisteredIdentities(
            Path.Combine(repositoryRoot, "scripts", "test-evidence-policy.json"),
            "rules",
            "barcodelabel-postgres-profile",
            "testIdentities",
            "expectedRuntimeTestCount");

        Assert.Equal(discovered, manifest);
        Assert.Equal(discovered, policy);
    }

    [Fact]
    public void Missing_registration_probe_reports_a_new_test_in_the_excluded_profile_class()
    {
        var registered = DiscoverProfileTestIdentities();
        var syntheticIdentity = $"{ProfileClassName}.New_unregistered_postgres_probe";

        var missing = FindMissingRegistrations([.. registered, syntheticIdentity], registered);

        Assert.Equal([syntheticIdentity], missing);
    }

    private static string[] DiscoverProfileTestIdentities() =>
        typeof(BarcodeLabelPostgresProfileTests)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes(inherit: true).Any(attribute => attribute is FactAttribute))
            .Select(method => $"{ProfileClassName}.{method.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string[] ReadRegisteredIdentities(
        string path,
        string collectionProperty,
        string entryId,
        string identitiesProperty,
        string? expectedCountProperty)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var entry = document.RootElement
            .GetProperty(collectionProperty)
            .EnumerateArray()
            .Single(candidate => candidate.GetProperty("id").GetString() == entryId);
        var identities = entry
            .GetProperty(identitiesProperty)
            .EnumerateArray()
            .Select(identity => identity.GetString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (expectedCountProperty is not null)
        {
            Assert.Equal(identities.Length, entry.GetProperty(expectedCountProperty).GetInt32());
        }

        return identities;
    }

    private static string[] FindMissingRegistrations(
        IEnumerable<string> discovered,
        IEnumerable<string> registered)
    {
        var registeredSet = registered.ToHashSet(StringComparer.Ordinal);
        return discovered
            .Where(identity => !registeredSet.Contains(identity))
            .Order(StringComparer.Ordinal)
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
