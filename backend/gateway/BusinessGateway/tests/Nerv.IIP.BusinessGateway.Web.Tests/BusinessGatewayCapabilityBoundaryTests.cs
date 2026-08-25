using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayCapabilityBoundaryTests
{
    private const string LegacyClientMonolithFileName = "BusinessServiceClients.cs";
    private static readonly IReadOnlyDictionary<string, string> ExpectedSharedTypeFiles =
        new Dictionary<string, string>
        {
            ["BusinessServiceAuditContext"] = "BusinessServiceAuditContext.cs",
            ["BusinessServiceProxyException"] = "BusinessServiceProxyException.cs",
            ["BusinessServiceHttpClient"] = "BusinessServiceHttpClient.cs",
        };

    [Fact]
    public void Shared_client_infrastructure_has_one_real_declaration_in_each_expected_file()
    {
        var businessServicesDirectory = LocateBusinessServicesDirectory();

        var violations = AnalyzeBoundary(businessServicesDirectory, ExpectedSharedTypeFiles);

        Assert.Empty(violations);
    }

    [Fact]
    public void Boundary_analyzer_rejects_comment_placeholders_and_relocated_real_declarations()
    {
        var documents = new[]
        {
            new SourceDocument("Shared/BusinessServiceAuditContext.cs", "// public sealed record BusinessServiceAuditContext"),
            new SourceDocument("Shared/BusinessServiceProxyException.cs", "// public sealed class BusinessServiceProxyException"),
            new SourceDocument("Shared/BusinessServiceHttpClient.cs", "// public abstract class BusinessServiceHttpClient"),
            new SourceDocument(
                "RenamedBusinessServiceClients.cs",
                "public sealed record BusinessServiceAuditContext {} " +
                "public sealed class BusinessServiceProxyException {} " +
                "public abstract class BusinessServiceHttpClient {}"),
        };

        var violations = AnalyzeBoundary(documents, ExpectedSharedTypeFiles);

        Assert.Contains(violations, violation => violation.Contains("BusinessServiceAuditContext.cs", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains("RenamedBusinessServiceClients.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Boundary_analyzer_rejects_a_renamed_multi_capability_client_monolith()
    {
        var documents = new[]
        {
            new SourceDocument(
                "Shared/BusinessServiceAuditContext.cs",
                "public sealed record BusinessServiceAuditContext {}"),
            new SourceDocument(
                "Shared/BusinessServiceProxyException.cs",
                "public sealed class BusinessServiceProxyException {}"),
            new SourceDocument(
                "Shared/BusinessServiceHttpClient.cs",
                "public abstract class BusinessServiceHttpClient {}"),
            new SourceDocument(
                "ReplacementClients.cs",
                "public interface IBusinessInventoryClient {} public sealed class HttpBusinessInventoryClient {} " +
                "public interface IBusinessQualityClient {} public sealed class HttpBusinessQualityClient {}"),
        };

        var violations = AnalyzeBoundary(documents, ExpectedSharedTypeFiles);

        Assert.Contains(violations, violation =>
            violation.Contains("ReplacementClients.cs", StringComparison.Ordinal) &&
            violation.Contains("Inventory", StringComparison.Ordinal) &&
            violation.Contains("Quality", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> AnalyzeBoundary(
        string businessServicesDirectory,
        IReadOnlyDictionary<string, string> expectedFiles) =>
        AnalyzeBoundary(
            Directory.EnumerateFiles(businessServicesDirectory, "*.cs", SearchOption.AllDirectories)
                .Select(path => new SourceDocument(
                    Path.GetRelativePath(businessServicesDirectory, path).Replace('\\', '/'),
                    File.ReadAllText(path))),
            expectedFiles);

    private static IReadOnlyList<string> AnalyzeBoundary(
        IEnumerable<SourceDocument> documents,
        IReadOnlyDictionary<string, string> expectedFiles)
    {
        var declarations = documents
            .SelectMany(document => CSharpSyntaxTree
                .ParseText(document.Source, path: document.RelativePath)
                .GetRoot()
                .DescendantNodes()
                .OfType<BaseTypeDeclarationSyntax>()
                .Select(declaration => new TypeDeclaration(document.RelativePath, declaration.Identifier.ValueText)))
            .ToArray();
        var violations = new List<string>();

        foreach (var (typeName, expectedFileName) in expectedFiles)
        {
            var owners = declarations
                .Where(declaration => declaration.TypeName == typeName)
                .Select(declaration => declaration.RelativePath)
                .ToArray();
            var expectedPath = $"Shared/{expectedFileName}";
            if (owners.Length != 1 || owners[0] != expectedPath)
            {
                violations.Add(
                    $"Expected exactly one real declaration of {typeName} in {expectedPath}; found: " +
                    (owners.Length == 0 ? "none" : string.Join(", ", owners)));
            }
        }

        foreach (var file in declarations
                     .Where(declaration => declaration.RelativePath != LegacyClientMonolithFileName)
                     .GroupBy(declaration => declaration.RelativePath))
        {
            var capabilities = file
                .Select(declaration => CapabilityClientName(declaration.TypeName))
                .Where(capability => capability is not null)
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (capabilities.Length > 1)
            {
                violations.Add(
                    $"{file.Key} declares clients for multiple capabilities: {string.Join(", ", capabilities)}.");
            }
        }

        return violations;
    }

    private static string? CapabilityClientName(string typeName)
    {
        const string interfacePrefix = "IBusiness";
        const string implementationPrefix = "HttpBusiness";
        const string suffix = "Client";
        var prefix = typeName.StartsWith(interfacePrefix, StringComparison.Ordinal)
            ? interfacePrefix
            : typeName.StartsWith(implementationPrefix, StringComparison.Ordinal)
                ? implementationPrefix
                : null;
        return prefix is not null && typeName.EndsWith(suffix, StringComparison.Ordinal)
            ? typeName[prefix.Length..^suffix.Length]
            : null;
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

    private sealed record SourceDocument(string RelativePath, string Source);

    private sealed record TypeDeclaration(string RelativePath, string TypeName);
}
