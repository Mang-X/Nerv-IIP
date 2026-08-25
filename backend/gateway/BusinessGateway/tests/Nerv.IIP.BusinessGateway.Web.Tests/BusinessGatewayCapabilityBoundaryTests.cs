using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayCapabilityBoundaryTests
{
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

        Assert.Equal(ExpectedSharedTypeFiles.Count, violations.Count);
        Assert.All(violations, violation =>
            Assert.Contains("RenamedBusinessServiceClients.cs", violation, StringComparison.Ordinal));
    }

    [Fact]
    public void Boundary_analyzer_rejects_duplicate_shared_declarations_in_legacy_or_nested_types()
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
                "BusinessServiceClients.cs",
                "public sealed class LegacyOuter { " +
                "public sealed record BusinessServiceAuditContext {} } " +
                "public sealed class BusinessServiceProxyException {} " +
                "public abstract class BusinessServiceHttpClient {}"),
        };

        var violations = AnalyzeBoundary(documents, ExpectedSharedTypeFiles);

        Assert.Equal(ExpectedSharedTypeFiles.Count, violations.Count);
        Assert.All(violations, violation =>
            Assert.Contains("BusinessServiceClients.cs", violation, StringComparison.Ordinal));
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
                .Select(declaration => new TypeDeclaration(
                    document.RelativePath,
                    declaration.Identifier.ValueText)))
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

        return violations;
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
