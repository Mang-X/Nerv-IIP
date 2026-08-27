using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayCapabilityBoundaryTests
{
    private const string BusinessServicesNamespace =
        "Nerv.IIP.BusinessGateway.Web.Application.BusinessServices";

    private static readonly IReadOnlyDictionary<string, string> ExpectedSharedTypeFiles =
        new Dictionary<string, string>
        {
            ["BusinessServiceAuditContext"] = "BusinessServiceAuditContext.cs",
            ["BusinessServiceProxyException"] = "BusinessServiceProxyException.cs",
            ["BusinessServiceHttpClient"] = "BusinessServiceHttpClient.cs",
        };

    private static readonly CapabilityBoundaryContract ProductionContract =
        CreateProductionContract();

    [Fact]
    public void Shared_client_infrastructure_has_one_real_declaration_in_each_expected_file()
    {
        var businessServicesDirectory = LocateBusinessServicesDirectory();

        var violations = AnalyzeSharedBoundary(
            businessServicesDirectory,
            ExpectedSharedTypeFiles);

        Assert.Empty(violations);
    }

    [Fact]
    public void Boundary_analyzer_rejects_comment_placeholders_and_relocated_real_declarations()
    {
        var documents = new[]
        {
            new SourceDocument(
                "Shared/BusinessServiceAuditContext.cs",
                "// public sealed record BusinessServiceAuditContext"),
            new SourceDocument(
                "Shared/BusinessServiceProxyException.cs",
                "// public sealed class BusinessServiceProxyException"),
            new SourceDocument(
                "Shared/BusinessServiceHttpClient.cs",
                "// public abstract class BusinessServiceHttpClient"),
            new SourceDocument(
                "RenamedBusinessServiceClients.cs",
                "public sealed record BusinessServiceAuditContext {} " +
                "public sealed class BusinessServiceProxyException {} " +
                "public abstract class BusinessServiceHttpClient {}"),
        };

        var violations = AnalyzeSharedBoundary(documents, ExpectedSharedTypeFiles);

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

        var violations = AnalyzeSharedBoundary(documents, ExpectedSharedTypeFiles);

        Assert.Equal(ExpectedSharedTypeFiles.Count, violations.Count);
        Assert.All(violations, violation =>
            Assert.Contains("BusinessServiceClients.cs", violation, StringComparison.Ordinal));
    }

    [Fact]
    public void Capability_client_declarations_match_the_registered_legacy_and_directory_contract()
    {
        var violations = AnalyzeCapabilityBoundary(
            LoadProductionDocuments(),
            ProductionContract);

        Assert.Empty(violations);
    }

    [Fact]
    public void Approval_client_declarations_live_together_in_its_capability_file()
    {
        var declarations = BuildSnapshot(LoadProductionDocuments()).Declarations;
        var expectedPath = "Capabilities/Approval/BusinessApprovalClient.cs";

        foreach (var identity in new[]
        {
            Identity("Interface", "IBusinessApprovalClient"),
            Identity("Class", "HttpBusinessApprovalClient"),
        })
        {
            var owners = declarations
                .Where(declaration => declaration.Identity == identity)
                .Select(declaration => declaration.RelativePath)
                .ToArray();

            Assert.Single(owners);
            Assert.Equal(expectedPath, owners[0]);
        }
    }

    [Fact]
    public void Notification_client_declarations_live_together_in_its_capability_file()
    {
        var declarations = BuildSnapshot(LoadProductionDocuments()).Declarations;
        var expectedPath = "Capabilities/Notification/BusinessNotificationClient.cs";

        foreach (var identity in new[]
        {
            Identity("Interface", "IBusinessNotificationClient"),
            Identity("Class", "HttpBusinessNotificationClient"),
        })
        {
            var owners = declarations
                .Where(declaration => declaration.Identity == identity)
                .Select(declaration => declaration.RelativePath)
                .ToArray();

            Assert.Single(owners);
            Assert.Equal(expectedPath, owners[0]);
        }
    }

    [Fact]
    public void MasterData_client_declarations_live_together_in_its_capability_file()
    {
        var declarations = BuildSnapshot(LoadProductionDocuments()).Declarations;
        var expectedPath = "Capabilities/MasterData/BusinessMasterDataClient.cs";

        foreach (var identity in new[]
        {
            Identity("Interface", "IBusinessMasterDataClient"),
            Identity("Class", "HttpBusinessMasterDataClient"),
        })
        {
            var owners = declarations
                .Where(declaration => declaration.Identity == identity)
                .Select(declaration => declaration.RelativePath)
                .ToArray();

            Assert.Single(owners);
            Assert.Equal(expectedPath, owners[0]);
        }
    }

    [Fact]
    public void Quality_client_declarations_live_together_in_its_capability_file()
    {
        var declarations = BuildSnapshot(LoadProductionDocuments()).Declarations;
        var expectedPath = "Capabilities/Quality/BusinessQualityClient.cs";

        foreach (var identity in new[]
        {
            Identity("Interface", "IBusinessQualityClient"),
            Identity("Class", "HttpBusinessQualityClient"),
        })
        {
            var owners = declarations
                .Where(declaration => declaration.Identity == identity)
                .Select(declaration => declaration.RelativePath)
                .ToArray();

            Assert.Single(owners);
            Assert.Equal(expectedPath, owners[0]);
        }
    }

    [Fact]
    public void FileStorage_client_declarations_live_together_in_its_capability_file()
    {
        var declarations = BuildSnapshot(LoadProductionDocuments()).Declarations;
        var expectedPath = "Capabilities/FileStorage/BusinessFileStorageClient.cs";

        foreach (var identity in new[]
        {
            Identity("Interface", "IBusinessFileStorageClient"),
            Identity("Class", "HttpBusinessFileStorageClient"),
        })
        {
            var owners = declarations
                .Where(declaration => declaration.Identity == identity)
                .Select(declaration => declaration.RelativePath)
                .ToArray();

            Assert.Single(owners);
            Assert.Equal(expectedPath, owners[0]);
        }
    }

    [Fact]
    public void ProductEngineering_client_declarations_live_together_in_its_capability_file()
    {
        var declarations = BuildSnapshot(LoadProductionDocuments()).Declarations;
        var expectedPath = "Capabilities/ProductEngineering/BusinessProductEngineeringClient.cs";

        foreach (var identity in new[]
        {
            Identity("Interface", "IBusinessProductEngineeringClient"),
            Identity("Class", "HttpBusinessProductEngineeringClient"),
        })
        {
            var owners = declarations
                .Where(declaration => declaration.Identity == identity)
                .Select(declaration => declaration.RelativePath)
                .ToArray();

            Assert.Single(owners);
            Assert.Equal(expectedPath, owners[0]);
        }
    }

    [Fact]
    public void Planning_client_declarations_live_together_in_its_capability_file()
    {
        var declarations = BuildSnapshot(LoadProductionDocuments()).Declarations;
        var expectedPath = "Capabilities/Planning/BusinessPlanningClient.cs";

        foreach (var identity in new[]
        {
            Identity("Interface", "IBusinessPlanningClient"),
            Identity("Class", "HttpBusinessPlanningClient"),
        })
        {
            var owners = declarations
                .Where(declaration => declaration.Identity == identity)
                .Select(declaration => declaration.RelativePath)
                .ToArray();

            Assert.Single(owners);
            Assert.Equal(expectedPath, owners[0]);
        }
    }

    [Fact]
    public void Scheduling_client_declarations_live_together_in_its_capability_file()
    {
        var declarations = BuildSnapshot(LoadProductionDocuments()).Declarations;
        var expectedPath = "Capabilities/Scheduling/BusinessSchedulingClient.cs";

        foreach (var identity in new[]
        {
            Identity("Interface", "IBusinessSchedulingClient"),
            Identity("Class", "HttpBusinessSchedulingClient"),
        })
        {
            var owners = declarations
                .Where(declaration => declaration.Identity == identity)
                .Select(declaration => declaration.RelativePath)
                .ToArray();

            Assert.Single(owners);
            Assert.Equal(expectedPath, owners[0]);
        }
    }

    [Fact]
    public void Capability_boundary_mutation_matrix_rejects_escapes_and_preserves_non_clients()
    {
        var baseDocuments = new[]
        {
            new SourceDocument(
                "BusinessServiceClients.cs",
                $"namespace {BusinessServicesNamespace}; " +
                "public partial interface IBusinessInventoryClient {}"),
            new SourceDocument(
                "Shared/BusinessServiceHttpClient.cs",
                $"namespace {BusinessServicesNamespace}; " +
                "public abstract class BusinessServiceHttpClient {}"),
            new SourceDocument(
                "Capabilities/Inventory/BusinessInventoryClient.cs",
                $"namespace {BusinessServicesNamespace}; " +
                "public partial class HttpBusinessInventoryClient : " +
                "BusinessServiceHttpClient, IBusinessInventoryClient {}"),
            new SourceDocument(
                "Capabilities/Inventory/InventoryWireDto.cs",
                $"namespace {BusinessServicesNamespace}; " +
                "public sealed record InventoryWireDto(string Value);"),
            new SourceDocument(
                "BusinessServiceClients.cs.config.cs",
                $"namespace {BusinessServicesNamespace}; " +
                "public sealed class InventoryOptions {} " +
                "public sealed class NestedHolder { public sealed class NestedDto {} }"),
        };
        var contract = CreateFixtureContract(
            legacy: new Dictionary<string, LegacyDeclarationContract>
            {
                [Identity("Interface", "IBusinessInventoryClient")] =
                    new("Inventory", ["BusinessServiceClients.cs"]),
            });

        Assert.Empty(AnalyzeCapabilityBoundary(baseDocuments, contract));

        var mutations = new[]
        {
            new SourceDocument(
                "BusinessServiceClients.cs.new-client.cs",
                $"namespace {BusinessServicesNamespace}; " +
                "public sealed class UnnamedInventoryClient : IBusinessInventoryClient {}"),
            new SourceDocument(
                "BusinessServiceClients.cs.duplicate-interface.cs",
                $"namespace {BusinessServicesNamespace}; " +
                "public partial interface IBusinessInventoryClient {}"),
            new SourceDocument(
                "BusinessServiceClients.cs.partial.cs",
                $"namespace {BusinessServicesNamespace}; " +
                "public partial class HttpBusinessInventoryClient {}"),
            new SourceDocument(
                "BusinessServiceClients.cs.nested.cs",
                $"namespace {BusinessServicesNamespace}; " +
                "public sealed class LegacyOuter { " +
                "public sealed class NestedInventoryClient : IBusinessInventoryClient {} }"),
            new SourceDocument(
                "Capabilities/Quality/WrongInventoryClient.cs",
                $"namespace {BusinessServicesNamespace}; " +
                "public sealed class WrongDirectoryInventoryClient : IBusinessInventoryClient {}"),
            new SourceDocument(
                "BusinessServiceClients.cs.bidirectional.cs",
                $"namespace {BusinessServicesNamespace}; " +
                "public interface IDerivedInventoryClient : IBusinessInventoryClient {} " +
                "public sealed class NonConventionalInventoryClient : IDerivedInventoryClient {}"),
            new SourceDocument(
                "BusinessServiceClients.cs.indirect-base-chain.cs",
                $"namespace {BusinessServicesNamespace}; " +
                "public abstract class NonConventionalBase : " +
                "HttpBusinessInventoryClient {} " +
                "public sealed class LegacyDerivedInventoryClient : " +
                "NonConventionalBase {}"),
            new SourceDocument(
                "BusinessServiceClients.cs.unassigned-client.cs",
                $"namespace {BusinessServicesNamespace}; " +
                "public sealed class UnassignedInventoryClient : BusinessServiceHttpClient {}"),
        };

        foreach (var mutation in mutations)
        {
            var violations = AnalyzeCapabilityBoundary(
                baseDocuments.Append(mutation),
                contract);

            Assert.True(
                violations.Count > 0,
                $"Mutation {mutation.RelativePath} was accepted by the boundary contract.");
            Assert.Contains(
                mutation.RelativePath,
                string.Join(Environment.NewLine, violations),
                StringComparison.Ordinal);
        }

        var indirectBaseViolations = AnalyzeCapabilityBoundary(
            baseDocuments.Append(mutations.Single(mutation =>
                mutation.RelativePath == "BusinessServiceClients.cs.indirect-base-chain.cs")),
            contract);
        var indirectBaseReport = string.Join(
            Environment.NewLine,
            indirectBaseViolations);
        Assert.Contains(
            Identity("Class", "LegacyDerivedInventoryClient"),
            indirectBaseReport,
            StringComparison.Ordinal);
        Assert.Contains(
            "must be under Capabilities/Inventory",
            indirectBaseReport,
            StringComparison.Ordinal);

        var falsePositiveViolations = AnalyzeCapabilityBoundary(
            baseDocuments.Append(
                new SourceDocument(
                    "BusinessServiceClients.cs.legitimate.cs",
                    $"namespace {BusinessServicesNamespace}; " +
                    "public sealed class InventoryOptionsDto {}")),
            contract);

        Assert.Empty(falsePositiveViolations);

        var crossCapabilityDocuments = baseDocuments
            .Append(
                new SourceDocument(
                    "BusinessServiceClients.cs.quality.cs",
                    $"namespace {BusinessServicesNamespace}; " +
                    "public interface IBusinessQualityClient {}"))
            .Append(
                new SourceDocument(
                    "Capabilities/Inventory/CrossCapabilityClient.cs",
                    $"namespace {BusinessServicesNamespace}; " +
                    "public sealed class CrossCapabilityClient : " +
                    "IBusinessInventoryClient, IBusinessQualityClient {}"));
        var crossCapabilityContract = CreateFixtureContract(
            legacy: new Dictionary<string, LegacyDeclarationContract>
            {
                [Identity("Interface", "IBusinessInventoryClient")] =
                    new("Inventory", ["BusinessServiceClients.cs"]),
                [Identity("Interface", "IBusinessQualityClient")] =
                    new("Quality", ["BusinessServiceClients.cs.quality.cs"]),
            },
            managedSeedCapabilities: new Dictionary<string, string>
            {
                [Identity("Interface", "IBusinessInventoryClient")] = "Inventory",
                [Identity("Interface", "IBusinessQualityClient")] = "Quality",
            },
            capabilityDirectories: new Dictionary<string, string>
            {
                ["Inventory"] = "Capabilities/Inventory",
                ["Quality"] = "Capabilities/Quality",
            });

        var crossCapabilityViolations = AnalyzeCapabilityBoundary(
            crossCapabilityDocuments,
            crossCapabilityContract);

        Assert.Contains(
            "CrossCapabilityClient.cs",
            string.Join(Environment.NewLine, crossCapabilityViolations),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Erp_client_declarations_live_together_in_its_capability_file()
    {
        var declarations = BuildSnapshot(LoadProductionDocuments()).Declarations;
        var expectedPath = "Capabilities/Erp/BusinessErpClient.cs";

        foreach (var identity in new[]
        {
            Identity("Interface", "IBusinessErpClient"),
            Identity("Class", "HttpBusinessErpClient"),
        })
        {
            var owners = declarations
                .Where(declaration => declaration.Identity == identity)
                .Select(declaration => declaration.RelativePath)
                .ToArray();

            Assert.Single(owners);
            Assert.Equal(expectedPath, owners[0]);
        }
    }

    [Fact]
    public void BarcodeLabel_client_declarations_live_together_in_its_capability_file()
    {
        var declarations = BuildSnapshot(LoadProductionDocuments()).Declarations;
        var expectedPath = "Capabilities/BarcodeLabel/BusinessBarcodeLabelClient.cs";

        foreach (var identity in new[]
        {
            Identity("Interface", "IBusinessBarcodeLabelClient"),
            Identity("Class", "HttpBusinessBarcodeLabelClient"),
        })
        {
            var owners = declarations
                .Where(declaration => declaration.Identity == identity)
                .Select(declaration => declaration.RelativePath)
                .ToArray();

            Assert.Single(owners);
            Assert.Equal(expectedPath, owners[0]);
        }
    }

    private static IReadOnlyList<string> AnalyzeSharedBoundary(
        string businessServicesDirectory,
        IReadOnlyDictionary<string, string> expectedFiles) =>
        AnalyzeSharedBoundary(
            Directory.EnumerateFiles(businessServicesDirectory, "*.cs", SearchOption.AllDirectories)
                .Select(path => new SourceDocument(
                    Path.GetRelativePath(businessServicesDirectory, path).Replace('\\', '/'),
                    File.ReadAllText(path))),
            expectedFiles);

    private static IReadOnlyList<string> AnalyzeSharedBoundary(
        IEnumerable<SourceDocument> documents,
        IReadOnlyDictionary<string, string> expectedFiles)
    {
        var declarations = documents
            .SelectMany(document => CSharpSyntaxTree
                .ParseText(document.Source, path: document.RelativePath)
                .GetRoot()
                .DescendantNodes()
                .OfType<BaseTypeDeclarationSyntax>()
                .Select(declaration => new SharedTypeDeclaration(
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

    private static IReadOnlyList<string> AnalyzeCapabilityBoundary(
        IEnumerable<SourceDocument> documents,
        CapabilityBoundaryContract contract)
    {
        var snapshot = BuildSnapshot(documents);
        var sourceIdentities = snapshot.Declarations
            .Select(declaration => declaration.Identity)
            .ToHashSet(StringComparer.Ordinal);
        var graph = sourceIdentities.ToDictionary(
            identity => identity,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);

        foreach (var declaration in snapshot.Declarations)
        {
            foreach (var relatedIdentity in declaration.RelatedIdentities)
            {
                if (!sourceIdentities.Contains(relatedIdentity) ||
                    contract.IgnoredSharedIdentities.Contains(relatedIdentity))
                {
                    continue;
                }

                graph[declaration.Identity].Add(relatedIdentity);
                graph[relatedIdentity].Add(declaration.Identity);
            }
        }

        var violations = new List<string>();
        var capabilitiesByIdentity = new Dictionary<string, HashSet<string>>(
            StringComparer.Ordinal);

        foreach (var (seedIdentity, capability) in contract.ManagedSeedCapabilities)
        {
            if (!sourceIdentities.Contains(seedIdentity))
            {
                violations.Add($"Missing managed seed {seedIdentity} for capability {capability}.");
                continue;
            }

            foreach (var identity in Traverse(graph, seedIdentity))
            {
                if (!capabilitiesByIdentity.TryGetValue(identity, out var capabilities))
                {
                    capabilities = new HashSet<string>(StringComparer.Ordinal);
                    capabilitiesByIdentity.Add(identity, capabilities);
                }

                capabilities.Add(capability);
            }
        }

        var declarationsByIdentity = snapshot.Declarations
            .GroupBy(declaration => declaration.Identity, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<TypeDeclaration>)group.ToArray(),
                StringComparer.Ordinal);
        foreach (var identity in declarationsByIdentity.Keys)
        {
            if (capabilitiesByIdentity.ContainsKey(identity) ||
                contract.IgnoredSharedIdentities.Contains(identity) ||
                !DerivesFromSharedClientBase(
                    identity,
                    declarationsByIdentity,
                    contract.SharedClientBaseIdentities,
                    new HashSet<string>(StringComparer.Ordinal)))
            {
                continue;
            }

            violations.Add(
                $"Unassigned capability client {identity} must be registered with a capability; " +
                $"declarations: {FormatPaths(declarationsByIdentity[identity])}.");
        }

        foreach (var (identity, capabilities) in capabilitiesByIdentity)
        {
            var declarations = snapshot.Declarations
                .Where(declaration => declaration.Identity == identity)
                .ToArray();
            if (capabilities.Count != 1)
            {
                violations.Add(
                    $"Client symbol {identity} belongs to multiple capabilities: " +
                    string.Join(", ", capabilities.OrderBy(value => value, StringComparer.Ordinal)) +
                    $"; declarations: {FormatPaths(declarations)}");
                continue;
            }

            var capability = capabilities.Single();
            if (contract.LegacyDeclarations.TryGetValue(identity, out var legacy))
            {
                if (!string.Equals(legacy.Capability, capability, StringComparison.Ordinal))
                {
                    violations.Add(
                        $"Legacy symbol {identity} is registered for {legacy.Capability} but resolves to " +
                        $"{capability}; declarations: {FormatPaths(declarations)}");
                }

                var actualPaths = declarations
                    .Select(declaration => declaration.RelativePath)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray();
                var expectedPaths = legacy.ExpectedPaths
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray();
                if (!actualPaths.SequenceEqual(expectedPaths, StringComparer.Ordinal))
                {
                    violations.Add(
                        $"Legacy symbol {identity} declaration locations changed; expected " +
                        $"[{string.Join(", ", expectedPaths)}], found [{string.Join(", ", actualPaths)}].");
                }

                continue;
            }

            if (!contract.CapabilityDirectories.TryGetValue(capability, out var expectedDirectory))
            {
                violations.Add($"Capability {capability} has no registered directory for {identity}.");
                continue;
            }

            var expectedPrefix = expectedDirectory.TrimEnd('/') + "/";
            foreach (var declaration in declarations)
            {
                if (!declaration.RelativePath.StartsWith(expectedPrefix, StringComparison.Ordinal))
                {
                    violations.Add(
                        $"Client symbol {identity} for {capability} must be under {expectedDirectory}; " +
                        $"found {declaration.RelativePath}.");
                }
            }
        }

        foreach (var (identity, legacy) in contract.LegacyDeclarations)
        {
            var declarations = snapshot.Declarations
                .Where(declaration => declaration.Identity == identity)
                .Select(declaration => declaration.RelativePath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            var expectedPaths = legacy.ExpectedPaths
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (!declarations.SequenceEqual(expectedPaths, StringComparer.Ordinal))
            {
                violations.Add(
                    $"Registered legacy symbol {identity} has an incomplete declaration inventory; expected " +
                    $"[{string.Join(", ", expectedPaths)}], found [{string.Join(", ", declarations)}].");
            }
        }

        return violations;
    }

    private static IReadOnlyList<SourceDocument> LoadProductionDocuments()
    {
        var directory = LocateBusinessServicesDirectory();
        return Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Select(path => new SourceDocument(
                Path.GetRelativePath(directory, path).Replace('\\', '/'),
                File.ReadAllText(path)))
            .ToArray();
    }

    private static CompilationSnapshot BuildSnapshot(IEnumerable<SourceDocument> documents)
    {
        var sourceDocuments = documents.ToArray();
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var trees = sourceDocuments
            .Select(document => CSharpSyntaxTree.ParseText(
                document.Source,
                parseOptions,
                document.RelativePath))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            assemblyName: "BusinessGatewayCapabilityBoundaryFixture",
            syntaxTrees: trees,
            references: TrustedPlatformReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var declarations = new List<TypeDeclaration>();

        for (var index = 0; index < trees.Length; index++)
        {
            var tree = trees[index];
            var document = sourceDocuments[index];
            var semanticModel = compilation.GetSemanticModel(tree);
            foreach (var syntax in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (semanticModel.GetDeclaredSymbol(syntax) is not INamedTypeSymbol symbol)
                {
                    continue;
                }

                declarations.Add(new TypeDeclaration(
                    document.RelativePath,
                    CanonicalIdentity(symbol),
                    symbol.BaseType is null ? null : CanonicalIdentity(symbol.BaseType),
                    symbol.Interfaces.Select(CanonicalIdentity).ToArray()));
            }
        }

        return new CompilationSnapshot(declarations);
    }

    private static IEnumerable<string> Traverse(
        IReadOnlyDictionary<string, HashSet<string>> graph,
        string seedIdentity)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>([seedIdentity]);
        while (pending.Count > 0)
        {
            var identity = pending.Dequeue();
            if (!visited.Add(identity))
            {
                continue;
            }

            yield return identity;
            foreach (var relatedIdentity in graph[identity])
            {
                pending.Enqueue(relatedIdentity);
            }
        }
    }

    private static bool DerivesFromSharedClientBase(
        string identity,
        IReadOnlyDictionary<string, IReadOnlyList<TypeDeclaration>> declarationsByIdentity,
        IReadOnlySet<string> sharedClientBaseIdentities,
        ISet<string> visiting)
    {
        if (!visiting.Add(identity) ||
            !declarationsByIdentity.TryGetValue(identity, out var declarations))
        {
            return false;
        }

        foreach (var declaration in declarations)
        {
            if (declaration.BaseIdentity is not null &&
                (sharedClientBaseIdentities.Contains(declaration.BaseIdentity) ||
                 DerivesFromSharedClientBase(
                     declaration.BaseIdentity,
                     declarationsByIdentity,
                     sharedClientBaseIdentities,
                     visiting)))
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatPaths(IEnumerable<TypeDeclaration> declarations) =>
        string.Join(", ", declarations.Select(declaration => declaration.RelativePath));

    private static CapabilityBoundaryContract CreateProductionContract()
    {
        var capabilities = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MasterData"] = "MasterData",
            ["Inventory"] = "Inventory",
            ["Quality"] = "Quality",
            ["FileStorage"] = "FileStorage",
            ["ProductEngineering"] = "ProductEngineering",
            ["Planning"] = "Planning",
            ["Scheduling"] = "Scheduling",
            ["Erp"] = "Erp",
            ["BarcodeLabel"] = "BarcodeLabel",
            ["IndustrialTelemetry"] = "IndustrialTelemetry",
            ["Maintenance"] = "Maintenance",
            ["Approval"] = "Approval",
            ["Notification"] = "Notification",
            ["Mes"] = "Mes",
            ["Wms"] = "Wms",
        };
        var seedCapabilities = new Dictionary<string, string>(StringComparer.Ordinal);
        var legacyDeclarations = new Dictionary<string, LegacyDeclarationContract>(StringComparer.Ordinal);
        foreach (var (clientName, capability) in capabilities)
        {
            var sourcePath = capability == "Wms"
                ? "BusinessConsoleWmsClient.cs"
                : "BusinessServiceClients.cs";
            AddManagedType(
                seedCapabilities,
                legacyDeclarations,
                "Interface",
                $"IBusiness{clientName}Client",
                capability,
                sourcePath,
                includeInLegacy: capability != "ProductEngineering" &&
                    capability != "Planning" &&
                    capability != "Scheduling" &&
                    capability != "FileStorage" &&
                    capability != "Erp" &&
                    capability != "MasterData" &&
                    capability != "Inventory" &&
                    capability != "Quality" &&
                    capability != "Approval" &&
                    capability != "Notification" &&
                    capability != "BarcodeLabel");
            AddManagedType(
                seedCapabilities,
                legacyDeclarations,
                "Class",
                $"HttpBusiness{clientName}Client",
                capability,
                sourcePath,
                includeInLegacy: capability != "ProductEngineering" &&
                    capability != "Planning" &&
                    capability != "Scheduling" &&
                    capability != "FileStorage" &&
                    capability != "Erp" &&
                    capability != "MasterData" &&
                    capability != "Inventory" &&
                    capability != "Quality" &&
                    capability != "Approval" &&
                    capability != "Notification" &&
                    capability != "BarcodeLabel");
        }

        seedCapabilities.Add(
            Identity("Interface", "IBusinessQualityScrapReasonCodeClient"),
            "Quality");
        seedCapabilities.Add(
            Identity("Class", "HttpBusinessQualityScrapReasonCodeClient"),
            "Quality");
        seedCapabilities[Identity("Interface", "IBusinessBarcodeResolverClient")] = "BarcodeLabel";
        seedCapabilities[Identity("Class", "HttpBusinessBarcodeResolverClient")] = "BarcodeLabel";
        AddManagedType(
            seedCapabilities,
            legacyDeclarations,
            "Interface",
            "IBusinessMesWorkOrderTransformationClient",
            "Mes",
            "Capabilities/Mes/BusinessMesWorkOrderTransformationClient.cs",
            includeInLegacy: false);
        AddManagedType(
            seedCapabilities,
            legacyDeclarations,
            "Class",
            "HttpBusinessMesWorkOrderTransformationClient",
            "Mes",
            "Capabilities/Mes/BusinessMesWorkOrderTransformationClient.cs",
            includeInLegacy: false);

        AddManagedType(
            seedCapabilities,
            legacyDeclarations,
            "Class",
            "BusinessGatewayInventoryForwardedPermissionOptions",
            "Inventory",
            "BusinessServiceClients.cs",
            includeInLegacy: false);

        var capabilityDirectories = capabilities.Values
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                capability => capability,
                capability => $"Capabilities/{capability}",
                StringComparer.Ordinal);
        var ignoredSharedIdentities = new HashSet<string>(StringComparer.Ordinal)
        {
            Identity("Class", "BusinessServiceAuditContext"),
            Identity("Class", "BusinessServiceProxyException"),
            Identity("Class", "BusinessServiceHttpClient"),
        };

        var sharedClientBaseIdentities = new HashSet<string>(StringComparer.Ordinal)
        {
            Identity("Class", "BusinessServiceHttpClient"),
        };

        return new CapabilityBoundaryContract(
            capabilityDirectories,
            seedCapabilities,
            legacyDeclarations,
            ignoredSharedIdentities,
            sharedClientBaseIdentities);
    }

    private static CapabilityBoundaryContract CreateFixtureContract(
        IReadOnlyDictionary<string, LegacyDeclarationContract> legacy,
        IReadOnlyDictionary<string, string>? managedSeedCapabilities = null,
        IReadOnlyDictionary<string, string>? capabilityDirectories = null)
    {
        return new CapabilityBoundaryContract(
            capabilityDirectories ??
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Inventory"] = "Capabilities/Inventory",
            },
            managedSeedCapabilities ??
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Identity("Interface", "IBusinessInventoryClient")] = "Inventory",
            },
            legacy,
            new HashSet<string>(StringComparer.Ordinal)
            {
                Identity("Class", "BusinessServiceHttpClient"),
            },
            new HashSet<string>(StringComparer.Ordinal)
            {
                Identity("Class", "BusinessServiceHttpClient"),
            });
    }

    private static void AddManagedType(
        IDictionary<string, string> seedCapabilities,
        IDictionary<string, LegacyDeclarationContract> legacyDeclarations,
        string kind,
        string name,
        string capability,
        string sourcePath,
        bool includeInLegacy = true)
    {
        var identity = Identity(kind, name);
        seedCapabilities.Add(identity, capability);
        if (includeInLegacy)
        {
            legacyDeclarations.Add(
                identity,
                new LegacyDeclarationContract(capability, [sourcePath]));
        }
    }

    private static string Identity(string kind, string name) =>
        $"{BusinessServicesNamespace}::{kind}:{name}`0";

    private static string CanonicalIdentity(INamedTypeSymbol symbol)
    {
        var containingTypes = new Stack<string>();
        for (var containing = symbol.ContainingType;
             containing is not null;
             containing = containing.ContainingType)
        {
            containingTypes.Push($"{containing.Name}`{containing.Arity}");
        }

        var containingPrefix = containingTypes.Count == 0
            ? string.Empty
            : string.Join("+", containingTypes) + "+";
        var namespaceName = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return $"{namespaceName}::{symbol.TypeKind}:{containingPrefix}{symbol.Name}`{symbol.Arity}";
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        IEnumerable<string> assemblyPaths = trustedAssemblies?.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries) ??
            new[] { typeof(object).Assembly.Location };
        return assemblyPaths.Select(path => MetadataReference.CreateFromFile(path));
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

    private sealed record SharedTypeDeclaration(string RelativePath, string TypeName);

    private sealed record TypeDeclaration(
        string RelativePath,
        string Identity,
        string? BaseIdentity,
        IReadOnlyList<string> InterfaceIdentities)
    {
        public IEnumerable<string> RelatedIdentities =>
            BaseIdentity is null
                ? InterfaceIdentities
                : new[] { BaseIdentity }.Concat(InterfaceIdentities);
    }

    private sealed record CompilationSnapshot(IReadOnlyList<TypeDeclaration> Declarations);

    private sealed record LegacyDeclarationContract(
        string Capability,
        IReadOnlyList<string> ExpectedPaths);

    private sealed record CapabilityBoundaryContract(
        IReadOnlyDictionary<string, string> CapabilityDirectories,
        IReadOnlyDictionary<string, string> ManagedSeedCapabilities,
        IReadOnlyDictionary<string, LegacyDeclarationContract> LegacyDeclarations,
        IReadOnlySet<string> IgnoredSharedIdentities,
        IReadOnlySet<string> SharedClientBaseIdentities);
}
