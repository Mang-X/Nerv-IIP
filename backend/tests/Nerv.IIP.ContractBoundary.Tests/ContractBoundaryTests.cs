namespace Nerv.IIP.ContractBoundary.Tests;

public sealed class ContractBoundaryTests
{
    public static TheoryData<string, string[]> DomainProjectsWithForbiddenContracts => new()
    {
        {
            "backend/services/AppHub/src/Nerv.IIP.AppHub.Domain",
            ["Nerv.IIP.Contracts.AppHubQueries"]
        },
        {
            "backend/services/Ops/src/Nerv.IIP.Ops.Domain",
            ["Nerv.IIP.Contracts.Ops"]
        },
        {
            "backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain",
            ["Nerv.IIP.Contracts.Scheduling"]
        }
    };

    [Theory]
    [MemberData(nameof(DomainProjectsWithForbiddenContracts))]
    public void Domain_projects_do_not_reference_query_or_algorithm_contracts(
        string projectDirectory,
        string[] forbiddenContractNames)
    {
        var root = FindRepositoryRoot();
        var fullProjectDirectory = Path.Combine(root, projectDirectory);
        var projectFile = Directory.GetFiles(fullProjectDirectory, "*.csproj").Single();
        var projectText = File.ReadAllText(projectFile);
        var sourceText = string.Join(
            Environment.NewLine,
            Directory.GetFiles(fullProjectDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(file => !IsGeneratedBuildOutput(file))
                .Select(File.ReadAllText));

        var offenders = forbiddenContractNames
            .Where(contractName =>
                projectText.Contains($"{contractName}.csproj", StringComparison.Ordinal)
                || sourceText.Contains($"using {contractName};", StringComparison.Ordinal)
                || sourceText.Contains($"{contractName}.", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Domain project {projectDirectory} must not reference query/read-model/algorithm contracts: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Wms_uses_public_inventory_contract_instead_of_local_inventory_dto_copy()
    {
        var root = FindRepositoryRoot();
        var wmsWebDirectory = Path.Combine(root, "backend", "services", "Business", "Wms", "src", "Nerv.IIP.Business.Wms.Web");
        var projectText = File.ReadAllText(Path.Combine(wmsWebDirectory, "Nerv.IIP.Business.Wms.Web.csproj"));
        var sourceFiles = Directory.GetFiles(wmsWebDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsGeneratedBuildOutput(file))
            .ToArray();
        var sourceText = string.Join(Environment.NewLine, sourceFiles.Select(File.ReadAllText));

        Assert.Contains("Nerv.IIP.Contracts.Inventory.csproj", projectText);
        Assert.DoesNotContain(
            Path.Combine("Application", "Inventory", "IInventoryMovementClient.cs"),
            sourceFiles.Select(file => Path.GetRelativePath(wmsWebDirectory, file)));
        Assert.DoesNotContain("public interface IInventoryMovementClient", sourceText);
        Assert.DoesNotContain("public sealed record PostStockMovementRequest", sourceText);
        Assert.DoesNotContain("public sealed record PostStockMovementResponse", sourceText);
        Assert.Contains("using Nerv.IIP.Contracts.Inventory;", sourceText);
    }

    private static bool IsGeneratedBuildOutput(string file)
    {
        return file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md")) && Directory.Exists(Path.Combine(directory.FullName, "backend")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
