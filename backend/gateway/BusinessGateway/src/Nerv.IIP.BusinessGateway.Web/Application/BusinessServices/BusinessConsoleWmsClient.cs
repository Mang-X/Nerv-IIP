namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessWmsClient
{
    Task<BusinessConsoleWmsInboundOrderListResponse> ListInboundOrdersAsync(
        string internalBearerToken,
        BusinessConsoleWmsListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsOutboundOrderListResponse> ListOutboundOrdersAsync(
        string internalBearerToken,
        BusinessConsoleWmsListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsWcsTaskListResponse> ListWcsTasksAsync(
        string internalBearerToken,
        BusinessConsoleWmsWcsTaskListRequest request,
        CancellationToken cancellationToken);
}

public sealed class HttpBusinessWmsClient(HttpClient httpClient) : BusinessServiceHttpClient(httpClient), IBusinessWmsClient
{
    public async Task<BusinessConsoleWmsInboundOrderListResponse> ListInboundOrdersAsync(
        string internalBearerToken,
        BusinessConsoleWmsListRequest request,
        CancellationToken cancellationToken)
    {
        var items = await SendAsync<IReadOnlyCollection<BusinessConsoleWmsInboundOrderItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/wms/inbound-orders?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);
        return new BusinessConsoleWmsInboundOrderListResponse(items, null, "unsupported");
    }

    public async Task<BusinessConsoleWmsOutboundOrderListResponse> ListOutboundOrdersAsync(
        string internalBearerToken,
        BusinessConsoleWmsListRequest request,
        CancellationToken cancellationToken)
    {
        var items = await SendAsync<IReadOnlyCollection<BusinessConsoleWmsOutboundOrderItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/wms/outbound-orders?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);
        return new BusinessConsoleWmsOutboundOrderListResponse(items);
    }

    public Task<BusinessConsoleWmsWcsTaskListResponse> ListWcsTasksAsync(
        string internalBearerToken,
        BusinessConsoleWmsWcsTaskListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleWmsWcsTaskListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/wms/wcs-tasks?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("externalTaskId", request.ExternalTaskId),
                ("warehouseTaskId", request.WarehouseTaskId)),
            null,
            cancellationToken);

    private static string ContextQuery(string organizationId, string environmentId) =>
        Query(("organizationId", organizationId), ("environmentId", environmentId));
}
