using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayCapabilityBoundaryTests
{
    private const string LegacyClientMonolithFileName = "BusinessServiceClients.cs";
    private const string BusinessServicesNamespace =
        "Nerv.IIP.BusinessGateway.Web.Application.BusinessServices";
    private static readonly IReadOnlyDictionary<string, string> ExpectedSharedTypeFiles =
        new Dictionary<string, string>
        {
            ["BusinessServiceAuditContext"] = "BusinessServiceAuditContext.cs",
            ["BusinessServiceProxyException"] = "BusinessServiceProxyException.cs",
            ["BusinessServiceHttpClient"] = "BusinessServiceHttpClient.cs",
        };
    private static readonly IReadOnlySet<TypeDeclarationIdentity> ExpectedLegacyGovernedDeclarations =
        CreateExpectedLegacyDeclarations(
            BusinessServicesNamespace,
            [
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
            ]);
    private static readonly IReadOnlySet<string> NoLegacyGovernedTypes =
        new HashSet<string>(StringComparer.Ordinal);

    [Fact]
    public void Shared_client_infrastructure_has_one_real_declaration_in_each_expected_file()
    {
        var businessServicesDirectory = LocateBusinessServicesDirectory();

        var violations = AnalyzeBoundary(
            businessServicesDirectory,
            ExpectedSharedTypeFiles,
            ExpectedLegacyGovernedDeclarations);

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
            violation.Contains("FirstOuter", StringComparison.Ordinal) &&
            violation.Contains("SecondOuter", StringComparison.Ordinal) &&
            violation.Contains("IBusinessInventoryClient", StringComparison.Ordinal));
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

    [Fact]
    public void Boundary_analyzer_rejects_nonconventional_interface_derived_from_a_managed_client_interface()
    {
        var documents = CreateBoundaryDocuments(
            "public interface IBusinessInventoryClient {} " +
            "public sealed class HttpBusinessInventoryClient {} " +
            "public interface EvidenceAxisInventoryTransport : IBusinessInventoryClient {}");
        IReadOnlySet<string> expectedLegacyTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "IBusinessInventoryClient",
            "HttpBusinessInventoryClient",
        };

        var violations = AnalyzeBoundary(documents, ExpectedSharedTypeFiles, expectedLegacyTypes);

        Assert.Contains(violations, violation =>
            violation.Contains("EvidenceAxisInventoryTransport", StringComparison.Ordinal));
    }

    [Fact]
    public void Boundary_analyzer_rejects_multiple_semantic_clients_in_a_nonlegacy_file()
    {
        var documents = CreateBoundaryDocuments(
            "public interface IBusinessInventoryClient {} public sealed class HttpBusinessInventoryClient {}",
            new SourceDocument(
                "EvidenceAxisReplacement.cs",
                "public abstract class EvidenceAxisInventoryTransport : BusinessServiceHttpClient {} " +
                "public abstract class EvidenceAxisQualityTransport : BusinessServiceHttpClient {}"));
        IReadOnlySet<string> expectedLegacyTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "IBusinessInventoryClient",
            "HttpBusinessInventoryClient",
        };

        var violations = AnalyzeBoundary(documents, ExpectedSharedTypeFiles, expectedLegacyTypes);

        Assert.Contains(violations, violation =>
            violation.Contains("EvidenceAxisReplacement.cs", StringComparison.Ordinal) &&
            violation.Contains("EvidenceAxisInventoryTransport", StringComparison.Ordinal) &&
            violation.Contains("EvidenceAxisQualityTransport", StringComparison.Ordinal));
    }

    [Fact]
    public void Boundary_analyzer_rejects_legacy_declaration_kind_changes()
    {
        var documents = CreateBoundaryDocuments(
            "public interface IBusinessInventoryClient {} " +
            "public sealed class HttpBusinessInventoryClient {} " +
            "public sealed record BusinessGatewayInventoryForwardedPermissionOptions {}");
        IReadOnlySet<string> expectedLegacyTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "IBusinessInventoryClient",
            "HttpBusinessInventoryClient",
            "BusinessGatewayInventoryForwardedPermissionOptions",
        };

        var violations = AnalyzeBoundary(documents, ExpectedSharedTypeFiles, expectedLegacyTypes);

        Assert.Contains(violations, violation =>
            violation.Contains("record", StringComparison.Ordinal));
    }

    [Fact]
    public void Boundary_analyzer_rejects_legacy_declaration_arity_changes()
    {
        var documents = CreateBoundaryDocuments(
            "public interface IBusinessInventoryClient<T> {} public sealed class HttpBusinessInventoryClient {}");
        IReadOnlySet<string> expectedLegacyTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "IBusinessInventoryClient",
            "HttpBusinessInventoryClient",
        };

        var violations = AnalyzeBoundary(documents, ExpectedSharedTypeFiles, expectedLegacyTypes);

        Assert.Contains(violations, violation =>
            violation.Contains("arity", StringComparison.Ordinal));
    }

    [Fact]
    public void Boundary_analyzer_rejects_legacy_declaration_namespace_changes()
    {
        var documents = CreateBoundaryDocuments(
            "namespace MutatedBoundary { " +
            "public interface IBusinessInventoryClient {} " +
            "public sealed class HttpBusinessInventoryClient {} }");
        IReadOnlySet<string> expectedLegacyTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "IBusinessInventoryClient",
            "HttpBusinessInventoryClient",
        };

        var violations = AnalyzeBoundary(documents, ExpectedSharedTypeFiles, expectedLegacyTypes);

        Assert.Contains(violations, violation =>
            violation.Contains("MutatedBoundary", StringComparison.Ordinal));
    }

    private static IReadOnlyList<SourceDocument> CreateBoundaryDocuments(
        string legacySource,
        params SourceDocument[] additionalDocuments) =>
        [
            new SourceDocument(
                "Shared/BusinessServiceAuditContext.cs",
                "public sealed record BusinessServiceAuditContext {}"),
            new SourceDocument(
                "Shared/BusinessServiceProxyException.cs",
                "public sealed class BusinessServiceProxyException {}"),
            new SourceDocument(
                "Shared/BusinessServiceHttpClient.cs",
                "public abstract class BusinessServiceHttpClient {}"),
            new SourceDocument(LegacyClientMonolithFileName, legacySource),
            .. additionalDocuments,
        ];

    private static IReadOnlyList<string> AnalyzeBoundary(
        string businessServicesDirectory,
        IReadOnlyDictionary<string, string> expectedFiles,
        IReadOnlySet<TypeDeclarationIdentity> expectedLegacyGovernedDeclarations) =>
        AnalyzeBoundary(
            Directory.EnumerateFiles(businessServicesDirectory, "*.cs", SearchOption.AllDirectories)
                .Select(path => new SourceDocument(
                    Path.GetRelativePath(businessServicesDirectory, path).Replace('\\', '/'),
                    File.ReadAllText(path))),
            expectedFiles,
            expectedLegacyGovernedDeclarations);

    private static IReadOnlyList<string> AnalyzeBoundary(
        IEnumerable<SourceDocument> documents,
        IReadOnlyDictionary<string, string> expectedFiles,
        IReadOnlySet<string> expectedLegacyGovernedTypeNames) =>
        AnalyzeBoundary(
            documents,
            expectedFiles,
            CreateExpectedLegacyDeclarations(string.Empty, expectedLegacyGovernedTypeNames));

    private static IReadOnlyList<string> AnalyzeBoundary(
        IEnumerable<SourceDocument> documents,
        IReadOnlyDictionary<string, string> expectedFiles,
        IReadOnlySet<TypeDeclarationIdentity> expectedLegacyGovernedDeclarations)
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
                    .Select(declaration =>
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(declaration)!;
                        return new TypeDeclaration(
                            tree.FilePath,
                            declaration.Identifier.ValueText,
                            CreateDeclarationIdentity(declaration, symbol),
                            symbol);
                    });
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
        var clientClassification = ClassifyClients(declarations, sharedClientBase?.Symbol);
        var actualLegacyGovernedDeclarations = legacyDeclarations
            .Where(declaration =>
                expectedLegacyGovernedDeclarations.Contains(declaration.Identity) ||
                declaration.TypeName.EndsWith("Options", StringComparison.Ordinal) ||
                clientClassification.ClientSymbols.Contains(declaration.Symbol))
            .ToArray();
        var declarationCounts = actualLegacyGovernedDeclarations
            .GroupBy(declaration => declaration.Identity)
            .ToDictionary(group => group.Key, group => group.Count());
        var legacyDeclarationDifferences = expectedLegacyGovernedDeclarations
            .Concat(declarationCounts.Keys)
            .Distinct()
            .Select(identity => new
            {
                Identity = identity,
                Expected = expectedLegacyGovernedDeclarations.Contains(identity) ? 1 : 0,
                Actual = declarationCounts.GetValueOrDefault(identity),
            })
            .Where(entry => entry.Expected != entry.Actual)
            .OrderBy(entry => FormatIdentity(entry.Identity), StringComparer.Ordinal)
            .Select(entry =>
                $"{FormatIdentity(entry.Identity)} (expected {entry.Expected}, actual {entry.Actual})")
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
            var clientBoundaries = file
                .Where(declaration => clientClassification.ClientSymbols.Contains(declaration.Symbol))
                .SelectMany(declaration => ClientBoundaryKeys(declaration, clientClassification.Capabilities))
                .GroupBy(boundary => boundary.Key, StringComparer.Ordinal)
                .Select(group => new
                {
                    Key = group.Key,
                    Declarations = group
                        .Select(boundary => boundary.Declaration)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray(),
                })
                .OrderBy(boundary => boundary.Key, StringComparer.Ordinal)
                .ToArray();
            if (clientBoundaries.Length > 1)
            {
                violations.Add(
                    $"{file.Key} declares multiple client boundaries: " +
                    string.Join(
                        "; ",
                        clientBoundaries.Select(boundary =>
                            $"{boundary.Key} => {string.Join(", ", boundary.Declarations)}")) +
                    ".");
            }
        }

        return violations;
    }

    private static IReadOnlySet<TypeDeclarationIdentity> CreateExpectedLegacyDeclarations(
        string namespaceName,
        IEnumerable<string> typeNames) =>
        typeNames
            .Select(typeName => new TypeDeclarationIdentity(
                namespaceName,
                string.Empty,
                typeName.StartsWith("IBusiness", StringComparison.Ordinal) ? "interface" : "class",
                typeName,
                0,
                Accessibility.Public))
            .ToHashSet();

    private static TypeDeclarationIdentity CreateDeclarationIdentity(
        BaseTypeDeclarationSyntax declaration,
        INamedTypeSymbol symbol) =>
        new(
            symbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : symbol.ContainingNamespace.ToDisplayString(),
            ContainingTypePath(symbol.ContainingType),
            DeclarationKind(declaration),
            symbol.Name,
            symbol.Arity,
            symbol.DeclaredAccessibility);

    private static string ContainingTypePath(INamedTypeSymbol? containingType)
    {
        var containingTypes = new Stack<string>();
        for (var current = containingType; current is not null; current = current.ContainingType)
        {
            containingTypes.Push($"{current.Name}`{current.Arity}");
        }

        return string.Join(".", containingTypes);
    }

    private static string DeclarationKind(BaseTypeDeclarationSyntax declaration) =>
        declaration switch
        {
            RecordDeclarationSyntax recordDeclaration when
                recordDeclaration.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) => "record struct",
            RecordDeclarationSyntax => "record class",
            ClassDeclarationSyntax => "class",
            InterfaceDeclarationSyntax => "interface",
            StructDeclarationSyntax => "struct",
            EnumDeclarationSyntax => "enum",
            _ => declaration.Kind().ToString(),
        };

    private static string FormatIdentity(TypeDeclarationIdentity identity)
    {
        var namespaceName = string.IsNullOrEmpty(identity.NamespaceName)
            ? "<global>"
            : identity.NamespaceName;
        var containingType = string.IsNullOrEmpty(identity.ContainingTypePath)
            ? string.Empty
            : $"{identity.ContainingTypePath}.";
        return $"{identity.Accessibility.ToString().ToLowerInvariant()} {identity.DeclarationKind} " +
               $"{namespaceName}.{containingType}{identity.TypeName} (arity {identity.Arity})";
    }

    private static ClientClassification ClassifyClients(
        IReadOnlyCollection<TypeDeclaration> declarations,
        INamedTypeSymbol? sharedClientBase)
    {
        var clientSymbols = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var capabilities = new Dictionary<INamedTypeSymbol, HashSet<string>>(SymbolEqualityComparer.Default);

        foreach (var declaration in declarations)
        {
            var capability = CapabilityClientName(declaration.TypeName);
            if (capability is not null)
            {
                clientSymbols.Add(declaration.Symbol);
                GetCapabilities(capabilities, declaration.Symbol).Add(capability);
            }

            if (DerivesFrom(declaration.Symbol, sharedClientBase))
            {
                clientSymbols.Add(declaration.Symbol);
            }
        }

        bool changed;
        do
        {
            changed = false;
            foreach (var declaration in declarations)
            {
                var symbol = declaration.Symbol;
                var relatedSymbols = symbol.Interfaces
                    .Concat(symbol.BaseType is null ? [] : [symbol.BaseType]);
                foreach (var relatedSymbol in relatedSymbols.Where(clientSymbols.Contains))
                {
                    changed |= clientSymbols.Add(symbol);
                    foreach (var capability in GetCapabilities(capabilities, relatedSymbol))
                    {
                        changed |= GetCapabilities(capabilities, symbol).Add(capability);
                    }
                }
            }
        }
        while (changed);

        return new ClientClassification(clientSymbols, capabilities);
    }

    private static HashSet<string> GetCapabilities(
        IDictionary<INamedTypeSymbol, HashSet<string>> capabilities,
        INamedTypeSymbol symbol)
    {
        if (!capabilities.TryGetValue(symbol, out var result))
        {
            result = new HashSet<string>(StringComparer.Ordinal);
            capabilities.Add(symbol, result);
        }

        return result;
    }

    private static IEnumerable<ClientBoundary> ClientBoundaryKeys(
        TypeDeclaration declaration,
        IReadOnlyDictionary<INamedTypeSymbol, HashSet<string>> capabilities)
    {
        var declarationDisplay = FormatIdentity(declaration.Identity);
        if (!capabilities.TryGetValue(declaration.Symbol, out var knownCapabilities) ||
            knownCapabilities.Count == 0)
        {
            yield return new ClientBoundary($"unattributed:{declarationDisplay}", declarationDisplay);
            yield break;
        }

        foreach (var capability in knownCapabilities)
        {
            yield return new ClientBoundary($"capability:{capability}", declarationDisplay);
        }
    }

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
        TypeDeclarationIdentity Identity,
        INamedTypeSymbol Symbol);

    private sealed record TypeDeclarationIdentity(
        string NamespaceName,
        string ContainingTypePath,
        string DeclarationKind,
        string TypeName,
        int Arity,
        Accessibility Accessibility);

    private sealed record ClientClassification(
        IReadOnlySet<INamedTypeSymbol> ClientSymbols,
        IReadOnlyDictionary<INamedTypeSymbol, HashSet<string>> Capabilities);

    private sealed record ClientBoundary(string Key, string Declaration);
}
