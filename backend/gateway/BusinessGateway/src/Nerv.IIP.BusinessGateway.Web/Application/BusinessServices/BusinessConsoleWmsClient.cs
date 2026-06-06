namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessWmsClient
{
    Task<BusinessConsoleCreateWmsInboundOrderResponse> CreateInboundOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateWmsInboundOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsInboundOrderListResponse> ListInboundOrdersAsync(
        string internalBearerToken,
        BusinessConsoleWmsListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateWmsWarehouseTaskResponse> CreatePutawayTaskAsync(
        string internalBearerToken,
        string inboundOrderId,
        BusinessConsoleCreateWmsPutawayTaskRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCompleteWmsMovementResponse> CompleteInboundOrderAsync(
        string internalBearerToken,
        string inboundOrderId,
        BusinessConsoleCompleteWmsInboundOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateWmsOutboundOrderResponse> CreateOutboundOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateWmsOutboundOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsOutboundOrderListResponse> ListOutboundOrdersAsync(
        string internalBearerToken,
        BusinessConsoleWmsListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateWmsWarehouseTaskResponse> CreatePickingTaskAsync(
        string internalBearerToken,
        string outboundOrderId,
        BusinessConsoleCreateWmsPickingTaskRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCompleteWmsMovementResponse> CompleteOutboundOrderAsync(
        string internalBearerToken,
        string outboundOrderId,
        BusinessConsoleCompleteWmsOutboundOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateWmsCountExecutionResponse> CreateCountExecutionAsync(
        string internalBearerToken,
        BusinessConsoleCreateWmsCountExecutionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCompleteWmsMovementResponse> CompleteCountExecutionAsync(
        string internalBearerToken,
        string countExecutionId,
        BusinessConsoleCompleteWmsCountExecutionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleDispatchWmsWcsTaskResponse> DispatchWcsTaskAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessConsoleDispatchWmsWcsTaskRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> FailWcsTaskAsync(
        string internalBearerToken,
        string externalTaskId,
        BusinessConsoleFailWmsWcsTaskRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> CompleteWcsTaskAsync(
        string internalBearerToken,
        string externalTaskId,
        BusinessConsoleCompleteWmsWcsTaskRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsWcsTaskListResponse> ListWcsTasksAsync(
        string internalBearerToken,
        BusinessConsoleWmsWcsTaskListRequest request,
        CancellationToken cancellationToken);
}

public sealed class HttpBusinessWmsClient(HttpClient httpClient) : BusinessServiceHttpClient(httpClient), IBusinessWmsClient
{
    public Task<BusinessConsoleCreateWmsInboundOrderResponse> CreateInboundOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateWmsInboundOrderRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateWmsInboundOrderResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/wms/inbound-orders",
            request,
            cancellationToken);

    public async Task<BusinessConsoleWmsInboundOrderListResponse> ListInboundOrdersAsync(
        string internalBearerToken,
        BusinessConsoleWmsListRequest request,
        CancellationToken cancellationToken)
    {
        var page = await SendAsync<BusinessConsoleWmsInboundOrderDownstreamListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/wms/inbound-orders?" + WmsListQuery(request),
            null,
            cancellationToken);
        return new BusinessConsoleWmsInboundOrderListResponse(page.Items, page.Total, null, "unsupported");
    }

    public Task<BusinessConsoleCreateWmsWarehouseTaskResponse> CreatePutawayTaskAsync(
        string internalBearerToken,
        string inboundOrderId,
        BusinessConsoleCreateWmsPutawayTaskRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateWmsWarehouseTaskResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/wms/inbound-orders/{Uri.EscapeDataString(inboundOrderId)}/putaway-tasks",
            request,
            cancellationToken);

    public Task<BusinessConsoleCompleteWmsMovementResponse> CompleteInboundOrderAsync(
        string internalBearerToken,
        string inboundOrderId,
        BusinessConsoleCompleteWmsInboundOrderRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCompleteWmsMovementResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/wms/inbound-orders/{Uri.EscapeDataString(inboundOrderId)}/complete",
            request,
            cancellationToken);

    public Task<BusinessConsoleCreateWmsOutboundOrderResponse> CreateOutboundOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateWmsOutboundOrderRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateWmsOutboundOrderResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/wms/outbound-orders",
            request,
            cancellationToken);

    public async Task<BusinessConsoleWmsOutboundOrderListResponse> ListOutboundOrdersAsync(
        string internalBearerToken,
        BusinessConsoleWmsListRequest request,
        CancellationToken cancellationToken)
    {
        return await SendAsync<BusinessConsoleWmsOutboundOrderListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/wms/outbound-orders?" + WmsListQuery(request),
            null,
            cancellationToken);
    }

    public Task<BusinessConsoleCreateWmsWarehouseTaskResponse> CreatePickingTaskAsync(
        string internalBearerToken,
        string outboundOrderId,
        BusinessConsoleCreateWmsPickingTaskRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateWmsWarehouseTaskResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/wms/outbound-orders/{Uri.EscapeDataString(outboundOrderId)}/picking-tasks",
            request,
            cancellationToken);

    public Task<BusinessConsoleCompleteWmsMovementResponse> CompleteOutboundOrderAsync(
        string internalBearerToken,
        string outboundOrderId,
        BusinessConsoleCompleteWmsOutboundOrderRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCompleteWmsMovementResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/wms/outbound-orders/{Uri.EscapeDataString(outboundOrderId)}/complete",
            request,
            cancellationToken);

    public Task<BusinessConsoleCreateWmsCountExecutionResponse> CreateCountExecutionAsync(
        string internalBearerToken,
        BusinessConsoleCreateWmsCountExecutionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateWmsCountExecutionResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/wms/count-executions",
            request,
            cancellationToken);

    public Task<BusinessConsoleCompleteWmsMovementResponse> CompleteCountExecutionAsync(
        string internalBearerToken,
        string countExecutionId,
        BusinessConsoleCompleteWmsCountExecutionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCompleteWmsMovementResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/wms/count-executions/{Uri.EscapeDataString(countExecutionId)}/complete",
            request,
            cancellationToken);

    public Task<BusinessConsoleDispatchWmsWcsTaskResponse> DispatchWcsTaskAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessConsoleDispatchWmsWcsTaskRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleDispatchWmsWcsTaskResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/wms/wcs-tasks/{Uri.EscapeDataString(warehouseTaskId)}/dispatch",
            request,
            cancellationToken);

    public async Task<BusinessConsoleAcceptedResponse> FailWcsTaskAsync(
        string internalBearerToken,
        string externalTaskId,
        BusinessConsoleFailWmsWcsTaskRequest request,
        CancellationToken cancellationToken)
    {
        await SendAsync<object>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/wms/wcs-tasks/{Uri.EscapeDataString(externalTaskId)}/fail",
            request,
            cancellationToken);
        return new BusinessConsoleAcceptedResponse(true);
    }

    public async Task<BusinessConsoleAcceptedResponse> CompleteWcsTaskAsync(
        string internalBearerToken,
        string externalTaskId,
        BusinessConsoleCompleteWmsWcsTaskRequest request,
        CancellationToken cancellationToken)
    {
        await SendAsync<object>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/wms/wcs-tasks/{Uri.EscapeDataString(externalTaskId)}/complete",
            request,
            cancellationToken);
        return new BusinessConsoleAcceptedResponse(true);
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
                ("warehouseTaskId", request.WarehouseTaskId),
                ("skip", request.Skip),
                ("take", request.Take),
                ("status", request.Status),
                ("failed", request.Failed),
                ("keyword", request.Keyword)),
            null,
            cancellationToken);

    private static string WmsListQuery(BusinessConsoleWmsListRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("skip", request.Skip),
            ("take", request.Take),
            ("status", request.Status),
            ("keyword", request.Keyword));

    private sealed record BusinessConsoleWmsInboundOrderDownstreamListResponse(
        IReadOnlyCollection<BusinessConsoleWmsInboundOrderItem> Items,
        int Total);
}
