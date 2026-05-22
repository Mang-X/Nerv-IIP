using Nerv.IIP.Business.Mes.Web.Endpoints.Mes;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesEndpointContractTests
{
    [Fact]
    public void MesEndpointContracts_ExposeRescheduleAndRushOrderRoutes()
    {
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/schedules/run"
            && x.OperationId == "runBusinessMesSchedule");

        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/work-orders/rush"
            && x.OperationId == "createBusinessMesRushWorkOrder");
    }
}
