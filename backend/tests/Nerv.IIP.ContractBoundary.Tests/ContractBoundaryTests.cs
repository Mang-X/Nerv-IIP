using System.Reflection;
using Nerv.IIP.AppHub.Domain;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Wms.Web.Endpoints.Wms;
using Nerv.IIP.Ops.Domain;

namespace Nerv.IIP.ContractBoundary.Tests;

public sealed class ContractBoundaryTests
{
    public static TheoryData<Assembly, string[]> DomainAssembliesWithForbiddenContracts => new()
    {
        {
            typeof(IAppHubStateStore).Assembly,
            ["Nerv.IIP.Contracts.AppHubQueries"]
        },
        {
            typeof(OperationTaskFact).Assembly,
            ["Nerv.IIP.Contracts.Ops"]
        },
        {
            typeof(SchedulePlan).Assembly,
            ["Nerv.IIP.Contracts.Scheduling"]
        }
    };

    [Theory]
    [MemberData(nameof(DomainAssembliesWithForbiddenContracts))]
    public void Domain_projects_do_not_reference_query_or_algorithm_contracts(
        Assembly domainAssembly,
        string[] forbiddenContractNames)
    {
        var referencedAssemblyNames = CollectReferencedAssemblyNames(domainAssembly);
        var offenders = forbiddenContractNames
            .Where(referencedAssemblyNames.Contains)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Domain assembly {domainAssembly.GetName().Name} must not reference query/read-model/algorithm contracts: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Wms_uses_public_inventory_contract_instead_of_local_inventory_dto_copy()
    {
        var wmsAssembly = typeof(WmsEndpoint<,>).Assembly;
        var referencedAssemblyNames = CollectReferencedAssemblyNames(wmsAssembly);
        var localDtoTypeNames = wmsAssembly
            .GetTypes()
            .Select(x => x.Name)
            .Where(typeName =>
                typeName is "IInventoryMovementClient"
                    or "PostStockMovementRequest"
                    or "PostStockMovementResponse")
            .ToArray();

        Assert.Contains("Nerv.IIP.Contracts.Inventory", referencedAssemblyNames);
        Assert.Empty(localDtoTypeNames);
    }

    private static HashSet<string> CollectReferencedAssemblyNames(Assembly rootAssembly)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        Collect(rootAssembly);
        return visited;

        void Collect(Assembly assembly)
        {
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (string.IsNullOrWhiteSpace(reference.Name) || !visited.Add(reference.Name))
                {
                    continue;
                }

                var localAssemblyPath = Path.Combine(AppContext.BaseDirectory, $"{reference.Name}.dll");
                if (File.Exists(localAssemblyPath))
                {
                    Collect(Assembly.LoadFrom(localAssemblyPath));
                }
            }
        }
    }
}
