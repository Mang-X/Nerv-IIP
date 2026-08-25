using System.Runtime.CompilerServices;
using Xunit;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayCapabilityBoundaryTests
{
    [Fact]
    public void Shared_client_infrastructure_has_a_dedicated_physical_boundary()
    {
        var businessServicesDirectory = LocateBusinessServicesDirectory();
        var legacySource = File.ReadAllText(
            Path.Combine(businessServicesDirectory, "BusinessServiceClients.cs"));
        var expectedFiles = new Dictionary<string, string>
        {
            ["BusinessServiceAuditContext.cs"] = "public sealed record BusinessServiceAuditContext(",
            ["BusinessServiceProxyException.cs"] = "public sealed class BusinessServiceProxyException",
            ["BusinessServiceHttpClient.cs"] = "public abstract class BusinessServiceHttpClient",
        };

        foreach (var (fileName, declaration) in expectedFiles)
        {
            var sourcePath = Path.Combine(businessServicesDirectory, "Shared", fileName);
            Assert.True(File.Exists(sourcePath), $"Expected shared client source at '{sourcePath}'.");
            Assert.Contains(declaration, File.ReadAllText(sourcePath), StringComparison.Ordinal);
            Assert.DoesNotContain(declaration, legacySource, StringComparison.Ordinal);
        }
    }

    private static string LocateBusinessServicesDirectory([CallerFilePath] string sourcePath = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourcePath)!,
            "..",
            "..",
            "src",
            "Nerv.IIP.BusinessGateway.Web",
            "Application",
            "BusinessServices"));
}
