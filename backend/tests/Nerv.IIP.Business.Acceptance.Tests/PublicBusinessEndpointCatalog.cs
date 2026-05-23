using Nerv.IIP.Business.DemandPlanning.Web.Endpoints.Planning;
using Nerv.IIP.Business.Erp.Web.Endpoints.Erp;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Endpoints.Iiot;
using Nerv.IIP.Business.Inventory.Web.Endpoints.Inventory;
using Nerv.IIP.Business.Maintenance.Web.Endpoints.Maintenance;
using Nerv.IIP.Business.MasterData.Web.Endpoints.MasterData;
using Nerv.IIP.Business.Mes.Web.Endpoints.Mes;
using Nerv.IIP.Business.ProductEngineering.Web.Endpoints.ProductEngineering;
using Nerv.IIP.Business.ProductEngineering.Web.Endpoints.ProductionVersions;
using Nerv.IIP.Business.Quality.Web.Endpoints.InspectionPlans;
using Nerv.IIP.Business.Quality.Web.Endpoints.InspectionRecords;
using Nerv.IIP.Business.Quality.Web.Endpoints.NonconformanceReports;
using Nerv.IIP.Business.Wms.Web.Endpoints.Wms;

namespace Nerv.IIP.Business.Acceptance.Tests;

public static class PublicBusinessEndpointCatalog
{
    public static IReadOnlyCollection<EndpointSurface> All { get; } =
    [
        .. MasterDataEndpointContracts.All.Select(x => Endpoint("BusinessMasterData", x.HttpMethod, x.Route, x.OperationId)),
        .. ProductEngineeringEndpointContracts.All.Select(x => Endpoint("BusinessProductEngineering", x.HttpMethod, x.Route, x.OperationId)),
        .. ProductionVersionEndpointContracts.All.Select(x => Endpoint("BusinessProductEngineering", x.HttpMethod, x.Route, x.OperationId)),
        .. DemandPlanningEndpointContracts.All.Select(x => Endpoint("BusinessDemandPlanning", x.HttpMethod, x.Route, x.OperationId)),
        .. MesEndpointContracts.All.Select(x => Endpoint("BusinessMes", x.HttpMethod, x.Route, x.OperationId)),
        .. ErpProcurementEndpointContracts.All.Select(x => Endpoint("BusinessErp", x.HttpMethod, x.Route, x.OperationId)),
        .. ErpSalesEndpointContracts.All.Select(x => Endpoint("BusinessErp", x.HttpMethod, x.Route, x.OperationId)),
        .. ErpFinanceEndpointContracts.All.Select(x => Endpoint("BusinessErp", x.HttpMethod, x.Route, x.OperationId)),
        .. QualityEndpointContracts.All.Select(x => Endpoint("BusinessQuality", x.HttpMethod, x.Route, x.OperationId)),
        .. QualityInspectionEndpointContracts.All.Select(x => Endpoint("BusinessQuality", x.HttpMethod, x.Route, x.OperationId)),
        .. WmsEndpointContracts.All.Select(x => Endpoint("BusinessWms", x.HttpMethod, x.Route, x.OperationId)),
        .. InventoryEndpointContracts.All.Select(x => Endpoint("BusinessInventory", x.HttpMethod, x.Route, x.OperationId)),
        .. IndustrialTelemetryEndpointContracts.All.Select(x => Endpoint("BusinessIndustrialTelemetry", x.HttpMethod, x.Route, x.OperationId)),
        .. MaintenanceEndpointContracts.All.Select(x => Endpoint("BusinessMaintenance", x.HttpMethod, x.Route, x.OperationId)),
    ];

    private static EndpointSurface Endpoint(string service, string httpMethod, string route, string operationId)
    {
        return new EndpointSurface(service, httpMethod, route, operationId);
    }
}
