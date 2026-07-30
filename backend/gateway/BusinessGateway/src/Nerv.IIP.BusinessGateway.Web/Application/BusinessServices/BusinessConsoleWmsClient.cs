namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessWmsClient
{
    Task<BusinessConsoleWmsWorkScopeCatalog> GetReceiptWorkScopesAsync(
        string internalBearerToken,
        BusinessWmsWorkScopeCatalogRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsWorkScopeCatalog> GetShipmentWorkScopesAsync(
        string internalBearerToken,
        BusinessWmsWorkScopeCatalogRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsWorkScopeCatalog> GetCountWorkScopesAsync(
        string internalBearerToken,
        BusinessWmsWorkScopeCatalogRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateWmsInboundOrderResponse> CreateInboundOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateWmsInboundOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsInboundOrderListResponse> ListInboundOrdersAsync(
        string internalBearerToken,
        BusinessWmsScopedListRequest request,
        string? inboundOrderId,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsAssignmentResult> AssignInboundOrderAsync(
        string internalBearerToken,
        string inboundOrderId,
        BusinessWmsAssignInboundOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateWmsWarehouseTaskResponse> CreatePutawayTaskAsync(
        string internalBearerToken,
        string inboundOrderId,
        BusinessConsoleCreateWmsPutawayTaskRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsWarehouseTaskListResponse> ListPutawayTasksAsync(
        string internalBearerToken,
        BusinessWmsWarehouseTaskListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsAssignmentResult> AssignPutawayTaskAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsAssignPutawayTaskRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsWarehouseTaskActionResult> StartPutawayTaskAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsStartWarehouseTaskActionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsWarehouseTaskActionResult> RecordPutawayTaskProgressAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsRecordWarehouseTaskProgressActionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsWarehouseTaskActionResult> ReportPutawayTaskExceptionAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsReportWarehouseTaskExceptionActionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsWarehouseTaskActionResult> CompletePutawayTaskAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsCompleteWarehouseTaskActionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCompleteWmsMovementResponse> CompleteInboundOrderAsync(
        string internalBearerToken,
        string inboundOrderId,
        BusinessWmsCompleteInboundOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateWmsOutboundOrderResponse> CreateOutboundOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateWmsOutboundOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsOutboundOrderListResponse> ListOutboundOrdersAsync(
        string internalBearerToken,
        BusinessWmsScopedListRequest request,
        string? outboundOrderId,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsAssignmentResult> AssignOutboundOrderAsync(
        string internalBearerToken,
        string outboundOrderId,
        BusinessWmsAssignOutboundOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateWmsWarehouseTaskResponse> CreatePickingTaskAsync(
        string internalBearerToken,
        string outboundOrderId,
        BusinessConsoleCreateWmsPickingTaskRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsWarehouseTaskListResponse> ListPickingTasksAsync(
        string internalBearerToken,
        BusinessWmsWarehouseTaskListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsAssignmentResult> AssignPickingTaskAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsAssignPickingTaskRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsWarehouseTaskActionResult> StartPickingTaskAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsStartWarehouseTaskActionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsWarehouseTaskActionResult> RecordPickingTaskProgressAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsRecordWarehouseTaskProgressActionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsWarehouseTaskActionResult> ReportPickingTaskExceptionAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsReportWarehouseTaskExceptionActionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsWarehouseTaskActionResult> CompletePickingTaskAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsCompleteWarehouseTaskActionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCompleteWmsMovementResponse> CompleteOutboundOrderAsync(
        string internalBearerToken,
        string outboundOrderId,
        BusinessWmsCompleteOutboundOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCompleteWmsMovementResponse> RetryOutboundInventoryPostingAsync(
        string internalBearerToken,
        string outboundOrderId,
        BusinessConsoleRetryWmsOutboundInventoryPostingRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateWmsCountExecutionResponse> CreateCountExecutionAsync(
        string internalBearerToken,
        BusinessConsoleCreateWmsCountExecutionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsCountExecutionListResponse> ListCountExecutionsAsync(
        string internalBearerToken,
        BusinessWmsCountExecutionListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsAssignmentResult> AssignCountExecutionAsync(
        string internalBearerToken,
        string countExecutionId,
        BusinessWmsAssignCountExecutionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCompleteWmsMovementResponse> CompleteCountExecutionAsync(
        string internalBearerToken,
        string countExecutionId,
        BusinessWmsCompleteCountExecutionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleDispatchWmsWcsTaskResponse> DispatchWcsTaskAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsDispatchWcsTaskRequest request,
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

    Task<BusinessConsoleWmsReceivingQualityGateListResponse> ListReceivingQualityGatesAsync(
        string internalBearerToken,
        BusinessWmsReceivingQualityGateListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWmsSupplierReturnListResponse> ListSupplierReturnRequestsAsync(
        string internalBearerToken,
        BusinessConsoleWmsListRequest request,
        CancellationToken cancellationToken);
}

public sealed class HttpBusinessWmsClient(HttpClient httpClient) : BusinessServiceHttpClient(httpClient), IBusinessWmsClient
{
    public Task<BusinessConsoleWmsWorkScopeCatalog> GetReceiptWorkScopesAsync(
        string internalBearerToken,
        BusinessWmsWorkScopeCatalogRequest request,
        CancellationToken cancellationToken) =>
        GetWorkScopesAsync(internalBearerToken, "receipts", request, cancellationToken);

    public Task<BusinessConsoleWmsWorkScopeCatalog> GetShipmentWorkScopesAsync(
        string internalBearerToken,
        BusinessWmsWorkScopeCatalogRequest request,
        CancellationToken cancellationToken) =>
        GetWorkScopesAsync(internalBearerToken, "shipments", request, cancellationToken);

    public Task<BusinessConsoleWmsWorkScopeCatalog> GetCountWorkScopesAsync(
        string internalBearerToken,
        BusinessWmsWorkScopeCatalogRequest request,
        CancellationToken cancellationToken) =>
        GetWorkScopesAsync(internalBearerToken, "counts", request, cancellationToken);

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
        BusinessWmsScopedListRequest request,
        string? inboundOrderId,
        CancellationToken cancellationToken)
    {
        var page = await SendAsync<BusinessConsoleWmsInboundOrderDownstreamListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/wms/inbound-orders?" + WmsListQuery(
                request,
                ("inboundOrderId", inboundOrderId)),
            null,
            cancellationToken);
        return new BusinessConsoleWmsInboundOrderListResponse(page.Items, page.Total, null, "unsupported");
    }

    public Task<BusinessConsoleWmsAssignmentResult> AssignInboundOrderAsync(
        string internalBearerToken,
        string inboundOrderId,
        BusinessWmsAssignInboundOrderRequest request,
        CancellationToken cancellationToken) =>
        AssignAsync(
            internalBearerToken,
            $"inbound-orders/{Uri.EscapeDataString(inboundOrderId)}",
            request,
            cancellationToken);

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

    public Task<BusinessConsoleWmsWarehouseTaskListResponse> ListPutawayTasksAsync(
        string internalBearerToken,
        BusinessWmsWarehouseTaskListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleWmsWarehouseTaskListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/wms/putaway-tasks?" + WmsWarehouseTaskListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleWmsAssignmentResult> AssignPutawayTaskAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsAssignPutawayTaskRequest request,
        CancellationToken cancellationToken) =>
        AssignAsync(
            internalBearerToken,
            $"putaway-tasks/{Uri.EscapeDataString(warehouseTaskId)}",
            request,
            cancellationToken);

    public Task<BusinessConsoleWmsWarehouseTaskActionResult> StartPutawayTaskAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsStartWarehouseTaskActionRequest request,
        CancellationToken cancellationToken) =>
        WarehouseTaskActionAsync(
            internalBearerToken,
            "putaway-tasks",
            warehouseTaskId,
            "start",
            request,
            cancellationToken);

    public Task<BusinessConsoleWmsWarehouseTaskActionResult> RecordPutawayTaskProgressAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsRecordWarehouseTaskProgressActionRequest request,
        CancellationToken cancellationToken) =>
        WarehouseTaskActionAsync(
            internalBearerToken,
            "putaway-tasks",
            warehouseTaskId,
            "progress",
            request,
            cancellationToken);

    public Task<BusinessConsoleWmsWarehouseTaskActionResult> ReportPutawayTaskExceptionAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsReportWarehouseTaskExceptionActionRequest request,
        CancellationToken cancellationToken) =>
        WarehouseTaskActionAsync(
            internalBearerToken,
            "putaway-tasks",
            warehouseTaskId,
            "exception",
            request,
            cancellationToken);

    public Task<BusinessConsoleWmsWarehouseTaskActionResult> CompletePutawayTaskAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsCompleteWarehouseTaskActionRequest request,
        CancellationToken cancellationToken) =>
        WarehouseTaskActionAsync(
            internalBearerToken,
            "putaway-tasks",
            warehouseTaskId,
            "complete",
            request,
            cancellationToken);

    public Task<BusinessConsoleCompleteWmsMovementResponse> CompleteInboundOrderAsync(
        string internalBearerToken,
        string inboundOrderId,
        BusinessWmsCompleteInboundOrderRequest request,
        CancellationToken cancellationToken) =>
        CompleteMovementAsync(
            internalBearerToken,
            $"/api/business/v1/wms/inbound-orders/{Uri.EscapeDataString(inboundOrderId)}/complete",
            request,
            "wms.inbound-order.complete",
            "inbound-order",
            inboundOrderId,
            request.IdempotencyKey,
            $"/api/business-console/v1/wms/inbound-orders?organizationId={Uri.EscapeDataString(request.OrganizationId)}&environmentId={Uri.EscapeDataString(request.EnvironmentId)}&inboundOrderId={Uri.EscapeDataString(inboundOrderId)}",
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
        BusinessWmsScopedListRequest request,
        string? outboundOrderId,
        CancellationToken cancellationToken)
    {
        return await SendAsync<BusinessConsoleWmsOutboundOrderListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/wms/outbound-orders?" + WmsListQuery(
                request,
                ("outboundOrderId", outboundOrderId)),
            null,
            cancellationToken);
    }

    public Task<BusinessConsoleWmsAssignmentResult> AssignOutboundOrderAsync(
        string internalBearerToken,
        string outboundOrderId,
        BusinessWmsAssignOutboundOrderRequest request,
        CancellationToken cancellationToken) =>
        AssignAsync(
            internalBearerToken,
            $"outbound-orders/{Uri.EscapeDataString(outboundOrderId)}",
            request,
            cancellationToken);

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

    public Task<BusinessConsoleWmsWarehouseTaskListResponse> ListPickingTasksAsync(
        string internalBearerToken,
        BusinessWmsWarehouseTaskListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleWmsWarehouseTaskListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/wms/picking-tasks?" + WmsWarehouseTaskListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleWmsAssignmentResult> AssignPickingTaskAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsAssignPickingTaskRequest request,
        CancellationToken cancellationToken) =>
        AssignAsync(
            internalBearerToken,
            $"picking-tasks/{Uri.EscapeDataString(warehouseTaskId)}",
            request,
            cancellationToken);

    public Task<BusinessConsoleWmsWarehouseTaskActionResult> StartPickingTaskAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsStartWarehouseTaskActionRequest request,
        CancellationToken cancellationToken) =>
        WarehouseTaskActionAsync(
            internalBearerToken,
            "picking-tasks",
            warehouseTaskId,
            "start",
            request,
            cancellationToken);

    public Task<BusinessConsoleWmsWarehouseTaskActionResult> RecordPickingTaskProgressAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsRecordWarehouseTaskProgressActionRequest request,
        CancellationToken cancellationToken) =>
        WarehouseTaskActionAsync(
            internalBearerToken,
            "picking-tasks",
            warehouseTaskId,
            "progress",
            request,
            cancellationToken);

    public Task<BusinessConsoleWmsWarehouseTaskActionResult> ReportPickingTaskExceptionAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsReportWarehouseTaskExceptionActionRequest request,
        CancellationToken cancellationToken) =>
        WarehouseTaskActionAsync(
            internalBearerToken,
            "picking-tasks",
            warehouseTaskId,
            "exception",
            request,
            cancellationToken);

    public Task<BusinessConsoleWmsWarehouseTaskActionResult> CompletePickingTaskAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsCompleteWarehouseTaskActionRequest request,
        CancellationToken cancellationToken) =>
        WarehouseTaskActionAsync(
            internalBearerToken,
            "picking-tasks",
            warehouseTaskId,
            "complete",
            request,
            cancellationToken);

    public Task<BusinessConsoleCompleteWmsMovementResponse> CompleteOutboundOrderAsync(
        string internalBearerToken,
        string outboundOrderId,
        BusinessWmsCompleteOutboundOrderRequest request,
        CancellationToken cancellationToken) =>
        CompleteMovementAsync(
            internalBearerToken,
            $"/api/business/v1/wms/outbound-orders/{Uri.EscapeDataString(outboundOrderId)}/complete",
            request,
            "wms.outbound-order.complete",
            "outbound-order",
            outboundOrderId,
            request.IdempotencyKey,
            $"/api/business-console/v1/wms/outbound-orders?organizationId={Uri.EscapeDataString(request.OrganizationId)}&environmentId={Uri.EscapeDataString(request.EnvironmentId)}&outboundOrderId={Uri.EscapeDataString(outboundOrderId)}",
            cancellationToken);

    public Task<BusinessConsoleCompleteWmsMovementResponse> RetryOutboundInventoryPostingAsync(
        string internalBearerToken,
        string outboundOrderId,
        BusinessConsoleRetryWmsOutboundInventoryPostingRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCompleteWmsMovementResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/wms/outbound-orders/{Uri.EscapeDataString(outboundOrderId)}/inventory-posting/retry",
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

    public Task<BusinessConsoleWmsCountExecutionListResponse> ListCountExecutionsAsync(
        string internalBearerToken,
        BusinessWmsCountExecutionListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleWmsCountExecutionListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/wms/count-executions?" + WmsCountExecutionListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleWmsAssignmentResult> AssignCountExecutionAsync(
        string internalBearerToken,
        string countExecutionId,
        BusinessWmsAssignCountExecutionRequest request,
        CancellationToken cancellationToken) =>
        AssignAsync(
            internalBearerToken,
            $"count-executions/{Uri.EscapeDataString(countExecutionId)}",
            request,
            cancellationToken);

    public Task<BusinessConsoleCompleteWmsMovementResponse> CompleteCountExecutionAsync(
        string internalBearerToken,
        string countExecutionId,
        BusinessWmsCompleteCountExecutionRequest request,
        CancellationToken cancellationToken) =>
        CompleteMovementAsync(
            internalBearerToken,
            $"/api/business/v1/wms/count-executions/{Uri.EscapeDataString(countExecutionId)}/complete",
            request,
            "wms.count-execution.complete",
            "count-execution",
            countExecutionId,
            request.IdempotencyKey,
            $"/api/business-console/v1/wms/count-executions?organizationId={Uri.EscapeDataString(request.OrganizationId)}&environmentId={Uri.EscapeDataString(request.EnvironmentId)}&countExecutionId={Uri.EscapeDataString(countExecutionId)}",
            cancellationToken);

    private async Task<BusinessConsoleCompleteWmsMovementResponse> CompleteMovementAsync(
        string internalBearerToken,
        string requestUri,
        object request,
        string operationType,
        string resourceType,
        string resourceId,
        string idempotencyKey,
        string readbackPath,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<BusinessConsoleCompleteWmsMovementResponse>(
            internalBearerToken,
            HttpMethod.Post,
            requestUri,
            request,
            cancellationToken);
        return response with
        {
            OperationReceipt = BusinessConsoleOperationReceipts.Accepted(
                operationType,
                "wms",
                resourceType,
                resourceId,
                readbackPath,
                idempotencyKey),
        };
    }

    public Task<BusinessConsoleDispatchWmsWcsTaskResponse> DispatchWcsTaskAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsDispatchWcsTaskRequest request,
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

    public Task<BusinessConsoleWmsReceivingQualityGateListResponse> ListReceivingQualityGatesAsync(
        string internalBearerToken,
        BusinessWmsReceivingQualityGateListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleWmsReceivingQualityGateListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/wms/receiving-quality-gates?" + AppendAuthorizedSites(
                Query(
                    ("organizationId", request.OrganizationId),
                    ("environmentId", request.EnvironmentId),
                    ("actorPrincipalId", request.ActorPrincipalId),
                    ("scopeKind", request.ScopeKind),
                    ("scopeId", request.ScopeId),
                    ("skip", request.Skip),
                    ("take", request.Take),
                    ("gateStatus", request.GateStatus),
                    ("keyword", request.Keyword),
                    ("includeNotRequired", TrueFlag(request.IncludeNotRequired)),
                    ("inboundOrderNo", request.InboundOrderNo)),
                request.AuthorizedSiteCodes),
            null,
            cancellationToken);

    public Task<BusinessConsoleWmsSupplierReturnListResponse> ListSupplierReturnRequestsAsync(
        string internalBearerToken,
        BusinessConsoleWmsListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleWmsSupplierReturnListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/wms/supplier-return-requests?" + WmsListQuery(request),
            null,
            cancellationToken);

    private Task<BusinessConsoleWmsWorkScopeCatalog> GetWorkScopesAsync(
        string internalBearerToken,
        string catalog,
        BusinessWmsWorkScopeCatalogRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleWmsWorkScopeCatalog>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/wms/work-scopes/{Uri.EscapeDataString(catalog)}?" +
            AppendAuthorizedSites(
                Query(
                    ("organizationId", request.OrganizationId),
                    ("environmentId", request.EnvironmentId),
                    ("actorPrincipalId", request.ActorPrincipalId)),
                request.AuthorizedSiteCodes),
            null,
            cancellationToken);

    private Task<BusinessConsoleWmsAssignmentResult> AssignAsync(
        string internalBearerToken,
        string resourcePath,
        object request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleWmsAssignmentResult>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/wms/{resourcePath}/assignment",
            request,
            cancellationToken);

    private Task<BusinessConsoleWmsWarehouseTaskActionResult> WarehouseTaskActionAsync(
        string internalBearerToken,
        string taskCollection,
        string warehouseTaskId,
        string action,
        object request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleWmsWarehouseTaskActionResult>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/wms/{taskCollection}/{Uri.EscapeDataString(warehouseTaskId)}/{action}",
            request,
            cancellationToken);

    private static string WmsListQuery(BusinessConsoleWmsListRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("skip", request.Skip),
            ("take", request.Take),
            ("status", request.Status),
            ("keyword", request.Keyword));

    private static string WmsListQuery(
        BusinessWmsScopedListRequest request,
        (string Name, object? Value) exactId) =>
        AppendAuthorizedSites(
            Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("actorPrincipalId", request.ActorPrincipalId),
                ("scopeKind", request.ScopeKind),
                ("scopeId", request.ScopeId),
                ("locationCode", request.LocationCode),
                ("lotNo", request.LotNo),
                ("siteCode", request.SiteCode),
                ("skip", request.Skip),
                ("take", request.Take),
                ("status", request.Status),
                ("keyword", request.Keyword),
                exactId),
            request.AuthorizedSiteCodes);

    private static string WmsWarehouseTaskListQuery(BusinessWmsWarehouseTaskListRequest request) =>
        AppendAuthorizedSites(
            Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("actorPrincipalId", request.ActorPrincipalId),
                ("scopeKind", request.ScopeKind),
                ("scopeId", request.ScopeId),
                ("locationCode", request.LocationCode),
                ("lotNo", request.LotNo),
                ("siteCode", request.SiteCode),
                ("skip", request.Skip),
                ("take", request.Take),
                ("status", request.Status),
                ("keyword", request.Keyword)),
            request.AuthorizedSiteCodes);

    private static string WmsCountExecutionListQuery(BusinessWmsCountExecutionListRequest request) =>
        AppendAuthorizedSites(
            Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("actorPrincipalId", request.ActorPrincipalId),
                ("scopeKind", request.ScopeKind),
                ("scopeId", request.ScopeId),
                ("locationCode", request.LocationCode),
                ("siteCode", request.SiteCode),
                ("skip", request.Skip),
                ("take", request.Take),
                ("status", request.Status),
                ("keyword", request.Keyword),
                ("countExecutionId", request.CountExecutionId)),
            request.AuthorizedSiteCodes);

    private static string AppendAuthorizedSites(
        string query,
        IReadOnlyCollection<string> authorizedSiteCodes)
    {
        var sites = authorizedSiteCodes
            .Where(siteCode => !string.IsNullOrWhiteSpace(siteCode))
            .Select(siteCode =>
                $"authorizedSiteCodes={Uri.EscapeDataString(siteCode.Trim())}");
        return string.Join('&', [query, .. sites]);
    }

    private sealed record BusinessConsoleWmsInboundOrderDownstreamListResponse(
        IReadOnlyCollection<BusinessConsoleWmsInboundOrderItem> Items,
        int Total);
}
