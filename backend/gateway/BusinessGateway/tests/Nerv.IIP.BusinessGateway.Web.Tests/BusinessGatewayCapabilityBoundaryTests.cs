using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
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
    private static readonly IReadOnlySet<string> ExpectedLegacyGovernedTypeNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "BusinessGatewayInventoryForwardedPermissionOptions",
            "HttpBusinessApprovalClient",
            "HttpBusinessBarcodeLabelClient",
            "HttpBusinessErpClient",
            "HttpBusinessFileStorageClient",
            "HttpBusinessIndustrialTelemetryClient",
            "HttpBusinessInventoryClient",
            "HttpBusinessMaintenanceClient",
            "HttpBusinessMasterDataClient",
            "HttpBusinessMesClient",
            "HttpBusinessNotificationClient",
            "HttpBusinessPlanningClient",
            "HttpBusinessProductEngineeringClient",
            "HttpBusinessQualityClient",
            "HttpBusinessSchedulingClient",
            "IBusinessApprovalClient",
            "IBusinessBarcodeLabelClient",
            "IBusinessErpClient",
            "IBusinessFileStorageClient",
            "IBusinessIndustrialTelemetryClient",
            "IBusinessInventoryClient",
            "IBusinessMaintenanceClient",
            "IBusinessMasterDataClient",
            "IBusinessMesClient",
            "IBusinessNotificationClient",
            "IBusinessPlanningClient",
            "IBusinessProductEngineeringClient",
            "IBusinessQualityClient",
            "IBusinessSchedulingClient",
        };
    private static readonly IReadOnlySet<string> NoLegacyGovernedTypes =
        new HashSet<string>(StringComparer.Ordinal);

    [Fact]
    public void Shared_client_infrastructure_has_one_real_declaration_in_each_expected_file()
    {
        var businessServicesDirectory = LocateBusinessServicesDirectory();

        var violations = AnalyzeBoundary(
            businessServicesDirectory,
            ExpectedSharedTypeFiles,
            ExpectedLegacyGovernedTypeNames);

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

        var violations = AnalyzeBoundary(
            documents,
            ExpectedSharedTypeFiles,
            NoLegacyGovernedTypes);

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

        var violations = AnalyzeBoundary(
            documents,
            ExpectedSharedTypeFiles,
            NoLegacyGovernedTypes);

        Assert.Contains(violations, violation =>
            violation.Contains("ReplacementClients.cs", StringComparison.Ordinal) &&
            violation.Contains("Inventory", StringComparison.Ordinal) &&
            violation.Contains("Quality", StringComparison.Ordinal));
    }

    [Fact]
    public void Boundary_analyzer_rejects_nested_client_declarations_in_the_legacy_monolith()
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
                LegacyClientMonolithFileName,
                "public interface IBusinessInventoryClient {} " +
                "public sealed class HttpBusinessInventoryClient { " +
                "public interface IBusinessBoundaryMutationClient {} " +
                "public sealed class HttpBusinessBoundaryMutationClient {} }"),
        };
        IReadOnlySet<string> expectedLegacyTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "IBusinessInventoryClient",
            "HttpBusinessInventoryClient",
        };

        var violations = AnalyzeBoundary(documents, ExpectedSharedTypeFiles, expectedLegacyTypes);

        Assert.Contains(violations, violation =>
            violation.Contains(LegacyClientMonolithFileName, StringComparison.Ordinal) &&
            violation.Contains("HttpBusinessBoundaryMutationClient", StringComparison.Ordinal) &&
            violation.Contains("IBusinessBoundaryMutationClient", StringComparison.Ordinal));
    }

    [Fact]
    public void Boundary_analyzer_rejects_new_top_level_client_declarations_in_the_legacy_monolith()
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
                LegacyClientMonolithFileName,
                "public interface IBusinessInventoryClient {} public sealed class HttpBusinessInventoryClient {} " +
                "public interface IBusinessBoundaryMutationClient {} public sealed class HttpBusinessBoundaryMutationClient {}"),
        };
        IReadOnlySet<string> expectedLegacyTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "IBusinessInventoryClient",
            "HttpBusinessInventoryClient",
        };

        var violations = AnalyzeBoundary(documents, ExpectedSharedTypeFiles, expectedLegacyTypes);

        Assert.Contains(violations, violation =>
            violation.Contains(LegacyClientMonolithFileName, StringComparison.Ordinal) &&
            violation.Contains("HttpBusinessBoundaryMutationClient", StringComparison.Ordinal) &&
            violation.Contains("IBusinessBoundaryMutationClient", StringComparison.Ordinal));
    }

    [Fact]
    public void Boundary_analyzer_rejects_allowlisted_clients_duplicated_under_distinct_outer_types()
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
                LegacyClientMonolithFileName,
                "public sealed class FirstOuter { " +
                "public interface IBusinessInventoryClient {} " +
                "public sealed class HttpBusinessInventoryClient {} } " +
                "public sealed class SecondOuter { " +
                "public interface IBusinessInventoryClient {} " +
                "public sealed class HttpBusinessInventoryClient {} }"),
        };
        IReadOnlySet<string> expectedLegacyTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "IBusinessInventoryClient",
            "HttpBusinessInventoryClient",
        };

        var violations = AnalyzeBoundary(documents, ExpectedSharedTypeFiles, expectedLegacyTypes);

        Assert.Contains(violations, violation =>
            violation.Contains("FirstOuter.IBusinessInventoryClient", StringComparison.Ordinal) &&
            violation.Contains("SecondOuter.IBusinessInventoryClient", StringComparison.Ordinal));
    }

    [Fact]
    public void Boundary_analyzer_rejects_nonconventional_type_derived_from_the_shared_client_base()
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
                LegacyClientMonolithFileName,
                "public interface IBusinessInventoryClient {} " +
                "public sealed class HttpBusinessInventoryClient {} " +
                "public sealed class InventoryTransport : BusinessServiceHttpClient {}"),
        };
        IReadOnlySet<string> expectedLegacyTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "IBusinessInventoryClient",
            "HttpBusinessInventoryClient",
        };

        var violations = AnalyzeBoundary(documents, ExpectedSharedTypeFiles, expectedLegacyTypes);

        Assert.Contains(violations, violation =>
            violation.Contains("InventoryTransport", StringComparison.Ordinal));
    }

    [Fact]
    public void Boundary_analyzer_rejects_nonconventional_type_implementing_a_managed_client_interface()
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
                LegacyClientMonolithFileName,
                "public interface IBusinessInventoryClient {} " +
                "public sealed class HttpBusinessInventoryClient {} " +
                "public sealed class InventoryTransport : IBusinessInventoryClient {}"),
        };
        IReadOnlySet<string> expectedLegacyTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "IBusinessInventoryClient",
            "HttpBusinessInventoryClient",
        };

        var violations = AnalyzeBoundary(documents, ExpectedSharedTypeFiles, expectedLegacyTypes);

        Assert.Contains(violations, violation =>
            violation.Contains("InventoryTransport", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> AnalyzeBoundary(
        string businessServicesDirectory,
        IReadOnlyDictionary<string, string> expectedFiles,
        IReadOnlySet<string> expectedLegacyGovernedTypeNames) =>
        AnalyzeBoundary(
            Directory.EnumerateFiles(businessServicesDirectory, "*.cs", SearchOption.AllDirectories)
                .Select(path => new SourceDocument(
                    Path.GetRelativePath(businessServicesDirectory, path).Replace('\\', '/'),
                    File.ReadAllText(path))),
            expectedFiles,
            expectedLegacyGovernedTypeNames);

    private static IReadOnlyList<string> AnalyzeBoundary(
        IEnumerable<SourceDocument> documents,
        IReadOnlyDictionary<string, string> expectedFiles,
        IReadOnlySet<string> expectedLegacyGovernedTypeNames)
    {
        var syntaxTrees = documents
            .Select(document => CSharpSyntaxTree.ParseText(document.Source, path: document.RelativePath))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "BusinessGatewayBoundaryAnalysis",
            syntaxTrees,
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var declarations = syntaxTrees
            .SelectMany(tree =>
            {
                var semanticModel = compilation.GetSemanticModel(tree);
                return tree.GetRoot()
                    .DescendantNodes()
                    .OfType<BaseTypeDeclarationSyntax>()
                    .Select(declaration => new TypeDeclaration(
                        tree.FilePath,
                        declaration.Identifier.ValueText,
                        DeclarationIdentity(declaration),
                        semanticModel.GetDeclaredSymbol(declaration)!));
            })
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

        var legacyDeclarations = declarations
            .Where(declaration => declaration.RelativePath == LegacyClientMonolithFileName)
            .ToArray();
        var sharedClientBase = declarations.SingleOrDefault(declaration =>
            declaration.RelativePath == "Shared/BusinessServiceHttpClient.cs" &&
            declaration.TypeName == "BusinessServiceHttpClient");
        var expectedClientInterfaces = legacyDeclarations
            .Where(declaration =>
                expectedLegacyGovernedTypeNames.Contains(declaration.DeclarationIdentity) &&
                declaration.Symbol.TypeKind == TypeKind.Interface)
            .Select(declaration => declaration.Symbol)
            .ToHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var semanticClientClasses = legacyDeclarations
            .Where(declaration =>
                declaration.Symbol.TypeKind == TypeKind.Class &&
                (DerivesFrom(declaration.Symbol, sharedClientBase?.Symbol) ||
                 declaration.Symbol.AllInterfaces.Any(expectedClientInterfaces.Contains)))
            .Select(declaration => declaration.Symbol)
            .ToHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var semanticClientInterfaces = semanticClientClasses
            .SelectMany(symbol => symbol.AllInterfaces)
            .ToHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var actualLegacyGovernedDeclarations = legacyDeclarations
            .Where(declaration =>
                expectedLegacyGovernedTypeNames.Contains(declaration.DeclarationIdentity) ||
                CapabilityClientName(declaration.TypeName) is not null ||
                declaration.TypeName.EndsWith("Options", StringComparison.Ordinal) ||
                semanticClientClasses.Contains(declaration.Symbol) ||
                semanticClientInterfaces.Contains(declaration.Symbol))
            .ToArray();
        var declarationCounts = actualLegacyGovernedDeclarations
            .GroupBy(declaration => declaration.DeclarationIdentity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var legacyDeclarationDifferences = expectedLegacyGovernedTypeNames
            .Concat(declarationCounts.Keys)
            .Distinct(StringComparer.Ordinal)
            .Select(identity => new
            {
                Identity = identity,
                Expected = expectedLegacyGovernedTypeNames.Contains(identity) ? 1 : 0,
                Actual = declarationCounts.GetValueOrDefault(identity),
            })
            .Where(entry => entry.Expected != entry.Actual)
            .OrderBy(entry => entry.Identity, StringComparer.Ordinal)
            .Select(entry => $"{entry.Identity} (expected {entry.Expected}, actual {entry.Actual})")
            .ToArray();
        if (legacyDeclarationDifferences.Length > 0)
        {
            violations.Add(
                $"{LegacyClientMonolithFileName} client/config declarations differ from the managed migration allowlist; " +
                $"differences: {string.Join(", ", legacyDeclarationDifferences)}.");
        }

        foreach (var file in declarations
                     .Where(declaration => declaration.RelativePath != LegacyClientMonolithFileName)
                     .GroupBy(declaration => declaration.RelativePath))
        {
            var capabilities = file
                .SelectMany(CapabilityClientNames)
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

    private static string DeclarationIdentity(BaseTypeDeclarationSyntax declaration) =>
        string.Join(
            ".",
            declaration.Ancestors()
                .OfType<BaseTypeDeclarationSyntax>()
                .Reverse()
                .Select(ancestor => ancestor.Identifier.ValueText)
                .Append(declaration.Identifier.ValueText));

    private static bool DerivesFrom(INamedTypeSymbol symbol, INamedTypeSymbol? expectedBase)
    {
        if (expectedBase is null)
        {
            return false;
        }

        for (var baseType = symbol.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(baseType, expectedBase))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> CapabilityClientNames(TypeDeclaration declaration)
    {
        var declaredCapability = CapabilityClientName(declaration.TypeName);
        if (declaredCapability is not null)
        {
            yield return declaredCapability;
        }

        foreach (var capability in declaration.Symbol.AllInterfaces
                     .Select(@interface => CapabilityClientName(@interface.Name))
                     .Where(capability => capability is not null)
                     .Cast<string>())
        {
            yield return capability;
        }
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

    private sealed record TypeDeclaration(
        string RelativePath,
        string TypeName,
        string DeclarationIdentity,
        INamedTypeSymbol Symbol);
}
