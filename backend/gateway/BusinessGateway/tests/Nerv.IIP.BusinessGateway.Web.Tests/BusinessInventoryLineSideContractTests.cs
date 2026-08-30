using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Inventory;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessInventoryLineSideContractTests
{
    [Fact]
    public void Inventory_client_reuses_shared_line_side_wire_contract()
    {
        var method = typeof(IBusinessInventoryClient).GetMethod(
            nameof(IBusinessInventoryClient.ListLineSideBalancesAsync));

        Assert.NotNull(method);
        var requestType = method.GetParameters()[1].ParameterType;
        Assert.Equal(typeof(LineSideInventoryBalancesRequest), requestType);

        var responseType = Assert.Single(method.ReturnType.GetGenericArguments());
        Assert.Equal(typeof(LineSideInventoryBalancesResponse), responseType);
    }
}
