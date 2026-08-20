using System.Reflection;
using Nerv.IIP.AppHub.Domain;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Wms.Web.Endpoints.Wms;
using Nerv.IIP.Ops.Domain;

namespace Nerv.IIP.ContractBoundary.Tests;

public sealed class ContractBoundaryTests
{
    /// <summary>
    /// 领域层禁引面登记表：一条 = 一个领域程序集 × 它不得引用的查询 / 读模型 / 算法契约。
    ///
    /// 口径是「Domain 不得引**查询 / 读模型 / 算法**契约」，不是「Domain 不得引任何契约」——
    /// 领域引词表类契约（<c>Contracts.Approval</c> / <c>Contracts.Wms</c> /
    /// <c>Contracts.ConnectorProtocol</c>）是既有且允许的形态。
    ///
    /// **凡是给某个领域程序集开了「可以引契约」这道门，必须同时在本表登记它的禁引面**
    /// （#1857 走查：Approval.Domain 当时开了门却没登记，禁引面上零条断言——
    /// 文档强度强于实现强度）。
    /// </summary>
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
        },
        {
            // #1857：Approval.Domain 为引用审批词表契约（Nerv.IIP.Contracts.Approval）开了门，
            // 禁引面照抄上面三组的口径一并钉死。
            typeof(ApprovalChain).Assembly,
            ["Nerv.IIP.Contracts.AppHubQueries", "Nerv.IIP.Contracts.Ops", "Nerv.IIP.Contracts.Scheduling"]
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
