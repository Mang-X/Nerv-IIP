using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.Contracts.EquipmentRuntime;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessMesClient
{
    Task<BusinessConsoleMesReadinessArea> GetFoundationReadinessAreaAsync(
        string internalBearerToken,
        string areaCode,
        BusinessConsoleMesFoundationReadinessRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesOverviewResponse> GetOverviewAsync(
        string internalBearerToken,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesProductionPlanListResponse> ListProductionPlansAsync(
        string internalBearerToken,
        BusinessConsoleMesProductionPlanListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesFoundationReadinessResponse> GetProductionPlanReadinessAsync(
        string internalBearerToken,
        string productionPlanId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> ConvertPlanToWorkOrderAsync(
        string internalBearerToken,
        string productionPlanId,
        BusinessConsoleMesConvertPlanToWorkOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesWorkOrderListResponse> ListWorkOrdersAsync(
        string internalBearerToken,
        BusinessMesWorkOrderListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesWorkOrderDetailResponse> GetWorkOrderDetailAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> ReleaseWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesReleaseWorkOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> HoldWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesWorkOrderReasonRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> CancelWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesWorkOrderReasonRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> CloseWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesCloseWorkOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> RecordEngineeringChangeDecisionAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesEngineeringChangeDecisionRequest request,
        string actor,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> ForceReleaseQualityHoldAsync(
        string internalBearerToken,
        string sourceDocumentId,
        BusinessConsoleMesForceReleaseQualityHoldRequest request,
        string actor,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> ForceReleaseQualityHoldAsync(
        string internalBearerToken,
        string sourceDocumentId,
        BusinessConsoleMesForceReleaseQualityHoldRequest request,
        string actor,
        string correlationId,
        CancellationToken cancellationToken) =>
        ForceReleaseQualityHoldAsync(internalBearerToken, sourceDocumentId, request, actor, cancellationToken);

    Task<BusinessConsoleMesQualityHoldTimelineResponse> GetQualityHoldTimelineAsync(
        string internalBearerToken,
        string sourceDocumentId,
        BusinessConsoleMesQualityHoldTimelineRequest request,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    Task<BusinessConsoleMesReverseProductionReportResponse> ReverseProductionReportAsync(
        string internalBearerToken,
        string reportNo,
        BusinessConsoleMesReverseProductionReportRequest request,
        string actor,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesCreateReceiptResponse> RetryFinishedGoodsReceiptInventoryPostingAsync(
        string internalBearerToken,
        string requestNo,
        BusinessConsoleMesRetryFinishedGoodsReceiptInventoryPostingRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateRushWorkOrderResponse> CreateRushWorkOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateRushWorkOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesMaterialReadinessResponse> GetMaterialReadinessAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesReceivableProducedLotListResponse> ListReceivableProducedLotsAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> CreateMaterialIssueRequestAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesCreateMaterialIssueRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesMaterialIssueRequestListResponse> ListMaterialIssueRequestsAsync(
        string internalBearerToken,
        BusinessConsoleMesMaterialIssueRequestListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesMaterialIssueRequestRow> GetMaterialIssueRequestAsync(
        string internalBearerToken,
        string requestId,
        BusinessConsoleMesMaterialIssueRequestDetailRequest request,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    Task<BusinessConsoleAcceptedResponse> ConfirmLineSideMaterialReceiptAsync(
        string internalBearerToken,
        string requestId,
        BusinessConsoleMesConfirmLineSideReceiptRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> ReturnLineSideMaterialAsync(
        string internalBearerToken,
        string requestId,
        BusinessConsoleMesReturnLineSideMaterialRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesDispatchTaskListResponse> ListDispatchTasksAsync(
        string internalBearerToken,
        BusinessConsoleMesDispatchTaskListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> AssignDispatchTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesAssignDispatchTaskForwardRequest request,
        string actor,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesOperationTaskListResponse> ListOperationTasksAsync(
        string internalBearerToken,
        BusinessMesOperationTaskListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesOperationTaskListResponse> ListReportableOperationTasksAsync(
        string internalBearerToken,
        BusinessMesOperationTaskListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesOperationTaskActionResponse> StartOperationTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesOperationTaskActionResponse> PauseOperationTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesOperationTaskActionResponse> ResumeOperationTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesOperationTaskActionResponse> CompleteOperationTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesWipSummaryResponse> GetWipSummaryAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesProductionReportListResponse> ListProductionReportsAsync(
        string internalBearerToken,
        BusinessConsoleMesListWithoutStatusRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesProductionReportDetailResponse> GetProductionReportAsync(
        string internalBearerToken,
        string reportNo,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesTelemetryCandidateListResponse> ListTelemetryCandidatesAsync(string internalBearerToken, BusinessConsoleMesTelemetryCandidateListRequest request, CancellationToken cancellationToken);
    Task<BusinessConsoleMesTelemetryCandidateRow> GetTelemetryCandidateAsync(string internalBearerToken, string candidateId, string organizationId, string environmentId, CancellationToken cancellationToken);
    Task<BusinessConsoleRecordProductionReportResponse> PromoteTelemetryCandidateAsync(string internalBearerToken, string candidateId, BusinessConsoleMesTelemetryCandidatePromoteRequest request, string actor, CancellationToken cancellationToken);
    Task<BusinessConsoleAcceptedResponse> DismissTelemetryCandidateAsync(string internalBearerToken, string candidateId, BusinessConsoleMesTelemetryCandidateDismissRequest request, string actor, CancellationToken cancellationToken);

    Task<BusinessConsoleMesScheduleResult> RunScheduleAsync(
        string internalBearerToken,
        BusinessConsoleRunScheduleRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleRecordProductionReportResponse> RecordProductionReportAsync(
        string internalBearerToken,
        BusinessConsoleRecordProductionReportRequest request,
        string actor,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> RecordDefectAsync(
        string internalBearerToken,
        BusinessMesRecordDefectRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesRelatedQualityItemListResponse> ListRelatedQualityItemsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesReceiptRequestListResponse> ListFinishedGoodsReceiptRequestsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken,
        string? exactRequestNo = null);

    Task<BusinessConsoleMesCreateReceiptResponse> CreateFinishedGoodsReceiptRequestAsync(
        string internalBearerToken,
        BusinessConsoleMesCreateReceiptRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesDowntimeEventListResponse> ListDowntimeEventsAsync(
        string internalBearerToken,
        BusinessConsoleMesDowntimeEventListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> RecordDowntimeEventAsync(
        string internalBearerToken,
        BusinessConsoleMesRecordDowntimeEventRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> RecordDowntimeEventAsync(
        string internalBearerToken,
        BusinessMesRecordDowntimeEventRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> ConfirmDowntimeRecoveryAsync(
        string internalBearerToken,
        string downtimeEventId,
        BusinessConsoleMesRecoverDowntimeEventRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesScheduleResultListResponse> ListScheduleResultsAsync(
        string internalBearerToken,
        BusinessConsoleMesScheduleResultListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesShiftHandoverListResponse> ListShiftHandoversAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> CreateShiftHandoverAsync(
        string internalBearerToken,
        BusinessConsoleMesCreateShiftHandoverRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> AcceptShiftHandoverAsync(
        string internalBearerToken,
        string handoverId,
        BusinessConsoleMesAcceptShiftHandoverRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesTraceabilityResponse> GetWorkOrderTraceabilityAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesTraceabilityResponse> GetBatchTraceabilityAsync(
        string internalBearerToken,
        string batchOrSerial,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesTraceabilityResponse> GetMaterialLotTraceabilityAsync(
        string internalBearerToken,
        string materialLotId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesCapacityImpactListResponse> ListCapacityImpactsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken);
}


public sealed class HttpBusinessMesClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessMesClient
{
    // MES middleware 以仅含 message 的 HTTP 422 失败 envelope 返回此既有协议。
    // capability 登记必须保持精确；readiness code 与任意大写消息都不是本路径的错误码。
    private const string RoutingSnapshotMissingLegacyCode = "ROUTING_SNAPSHOT_MISSING";

    // #1341: downstreamService / downstreamDocumentType 是全仓共用的一张词表（PascalCase），
    // 由 DemandPlanningDownstreamReferences（BusinessErp/PurchaseRequisition/BusinessMes/WorkOrder）
    // 定下口径，网关既有读面（BusinessConsoleMesEndpoints 的 "BusinessMes"/"ProductionPlan"）与前端
    // 精确等值判断（PlanningWorkbench 的 service === 'BusinessMes' && type === 'WorkOrder'）都按它匹配。
    // MES 受理回执没有理由另起一套编码，因此这里沿用同一词表，新单据类型按同样构词法扩展。
    private const string MesDownstreamService = "BusinessMes";
    private const string MesMaterialIssueRequestDocumentType = "MaterialIssueRequest";
    private const string MesWorkOrderDocumentType = "WorkOrder";
    private const string MesQualityHoldDocumentType = "QualityHold";
    private const string MesDispatchTaskDocumentType = "DispatchTask";
    private const string MesTelemetryCandidateDocumentType = "TelemetryProductionReportCandidate";
    private const string MesDefectDocumentType = "Defect";
    private const string MesDowntimeEventDocumentType = "DowntimeEvent";
    private const string MesShiftHandoverDocumentType = "ShiftHandover";

    protected override bool IsRegisteredLegacySemanticCode(string? code) =>
        base.IsRegisteredLegacySemanticCode(code) ||
        string.Equals(code, RoutingSnapshotMissingLegacyCode, StringComparison.Ordinal);

    /// <summary>MES accepted-receipt body as returned by the service endpoints.</summary>
    private sealed record MesServiceAcceptedResponse(string? Status, string? ReferenceId, DateTimeOffset? AcceptedAtUtc);

    private static BusinessConsoleAcceptedResponse ToAcceptedResponse(
        MesServiceAcceptedResponse? accepted,
        string downstreamDocumentType) =>
        new(
            // MES only answers 2xx when it accepted the intent, so a parsed body is the acceptance.
            accepted is not null,
            MesDownstreamService,
            downstreamDocumentType,
            string.IsNullOrWhiteSpace(accepted?.ReferenceId) ? null : accepted.ReferenceId);

    /// <summary>
    /// #1341: MES 写面统一回 <c>{ status, referenceId, acceptedAtUtc }</c>；直接反序列化成控制台契约
    /// 会让 <c>accepted</c> 恒 false 并丢掉下游单号，因此所有受理型写面都必须走显式映射。
    /// </summary>
    private async Task<BusinessConsoleAcceptedResponse> SendAcceptedAsync(
        string internalBearerToken,
        string requestUri,
        object? body,
        string downstreamDocumentType,
        CancellationToken cancellationToken,
        Action<HttpRequestMessage>? configureRequest = null)
    {
        var accepted = await SendAsync<MesServiceAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            requestUri,
            body,
            cancellationToken,
            configureRequest: configureRequest);
        return ToAcceptedResponse(accepted, downstreamDocumentType);
    }

    public Task<BusinessConsoleMesReadinessArea> GetFoundationReadinessAreaAsync(
        string internalBearerToken,
        string areaCode,
        BusinessConsoleMesFoundationReadinessRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesReadinessArea>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/mes/foundation-readiness/{Uri.EscapeDataString(areaCode)}?" + FoundationQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesOverviewResponse> GetOverviewAsync(
        string internalBearerToken,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesOverviewResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/overview?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesProductionPlanListResponse> ListProductionPlansAsync(
        string internalBearerToken,
        BusinessConsoleMesProductionPlanListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesProductionPlanListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/production-plans?" + ProductionPlanListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesFoundationReadinessResponse> GetProductionPlanReadinessAsync(
        string internalBearerToken,
        string productionPlanId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesFoundationReadinessResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/mes/production-plans/{Uri.EscapeDataString(productionPlanId)}/readiness?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> ConvertPlanToWorkOrderAsync(
        string internalBearerToken,
        string productionPlanId,
        BusinessConsoleMesConvertPlanToWorkOrderRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/production-plans/{Uri.EscapeDataString(productionPlanId)}/work-orders",
            request,
            MesWorkOrderDocumentType,
            cancellationToken);

    public Task<BusinessConsoleMesWorkOrderListResponse> ListWorkOrdersAsync(
        string internalBearerToken,
        BusinessMesWorkOrderListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesWorkOrderListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/work-orders?" + WorkOrderListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesWorkOrderDetailResponse> GetWorkOrderDetailAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesWorkOrderDetailResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> ReleaseWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesReleaseWorkOrderRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}/release",
            request,
            MesWorkOrderDocumentType,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> HoldWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesWorkOrderReasonRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}/hold",
            request,
            MesWorkOrderDocumentType,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> CancelWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesWorkOrderReasonRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}/cancel",
            request,
            MesWorkOrderDocumentType,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> CloseWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesCloseWorkOrderRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}/close",
            request,
            MesWorkOrderDocumentType,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> RecordEngineeringChangeDecisionAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesEngineeringChangeDecisionRequest request,
        string actor,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}/engineering-change-decisions",
            new MesEngineeringChangeDecisionRequest(
                request.OrganizationId,
                request.EnvironmentId,
                workOrderId,
                request.ChangeNumber,
                request.Decision,
                actor,
                request.Reason),
            MesWorkOrderDocumentType,
            cancellationToken);

    private sealed record MesEngineeringChangeDecisionRequest(
        string OrganizationId,
        string EnvironmentId,
        string WorkOrderId,
        string ChangeNumber,
        string Decision,
        string DecidedBy,
        string Reason);

    public Task<BusinessConsoleAcceptedResponse> ForceReleaseQualityHoldAsync(
        string internalBearerToken,
        string sourceDocumentId,
        BusinessConsoleMesForceReleaseQualityHoldRequest request,
        string actor,
        CancellationToken cancellationToken) =>
        ForceReleaseQualityHoldAsync(internalBearerToken, sourceDocumentId, request, actor, Guid.CreateVersion7().ToString("N"), cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> ForceReleaseQualityHoldAsync(
        string internalBearerToken,
        string sourceDocumentId,
        BusinessConsoleMesForceReleaseQualityHoldRequest request,
        string actor,
        string correlationId,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/quality-holds/{Uri.EscapeDataString(sourceDocumentId)}/force-release",
            new DownstreamForceReleaseQualityHoldRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.Reason,
                request.SourceService,
                request.ReleasedAtUtc),
            MesQualityHoldDocumentType,
            cancellationToken,
            configureRequest: message =>
            {
                message.Headers.TryAddWithoutValidation("X-Authenticated-Actor", actor);
                message.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
                message.Headers.TryAddWithoutValidation("X-Idempotency-Key", request.IdempotencyKey);
            });

    public Task<BusinessConsoleMesQualityHoldTimelineResponse> GetQualityHoldTimelineAsync(
        string internalBearerToken,
        string sourceDocumentId,
        BusinessConsoleMesQualityHoldTimelineRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesQualityHoldTimelineResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/mes/quality-holds/{Uri.EscapeDataString(sourceDocumentId)}/timeline?organizationId={Uri.EscapeDataString(request.OrganizationId)}&environmentId={Uri.EscapeDataString(request.EnvironmentId)}&sourceService={Uri.EscapeDataString(request.SourceService)}",
            null,
            cancellationToken);

    public Task<BusinessConsoleMesReverseProductionReportResponse> ReverseProductionReportAsync(
        string internalBearerToken,
        string reportNo,
        BusinessConsoleMesReverseProductionReportRequest request,
        string actor,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesReverseProductionReportResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/production-reports/{Uri.EscapeDataString(reportNo)}/reverse",
            new DownstreamReverseProductionReportRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.Reason,
                actor,
                request.ReversedAtUtc,
                request.IdempotencyKey),
            cancellationToken);

    public async Task<BusinessConsoleMesCreateReceiptResponse> RetryFinishedGoodsReceiptInventoryPostingAsync(
        string internalBearerToken,
        string requestNo,
        BusinessConsoleMesRetryFinishedGoodsReceiptInventoryPostingRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateFinishedGoodsReceiptRequestResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/finished-goods-receipt-requests/{Uri.EscapeDataString(requestNo)}/inventory-posting/retry",
            request,
            cancellationToken);

        if (response.FinishedGoodsReceiptRequestId is null ||
            response.FinishedGoodsReceiptRequestId.Id == Guid.Empty ||
            string.IsNullOrWhiteSpace(response.RequestNo))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }

        return new BusinessConsoleMesCreateReceiptResponse(
            response.FinishedGoodsReceiptRequestId.Id.ToString(),
            response.RequestNo);
    }

    public Task<BusinessConsoleCreateRushWorkOrderResponse> CreateRushWorkOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateRushWorkOrderRequest request,
        CancellationToken cancellationToken) =>
        CreateRushWorkOrderCoreAsync(internalBearerToken, request, cancellationToken);

    public Task<BusinessConsoleMesMaterialReadinessResponse> GetMaterialReadinessAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesMaterialReadinessResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}/material-readiness?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesReceivableProducedLotListResponse> ListReceivableProducedLotsAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesReceivableProducedLotListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}/produced-lots?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    // MES answers with { status, referenceId, acceptedAtUtc }; deserializing that straight into the
    // console contract left `accepted` false and dropped the allocated 领料单号. Map it explicitly.
    public Task<BusinessConsoleAcceptedResponse> CreateMaterialIssueRequestAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesCreateMaterialIssueRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}/material-issue-requests",
            request,
            MesMaterialIssueRequestDocumentType,
            cancellationToken);

    public Task<BusinessConsoleMesMaterialIssueRequestListResponse> ListMaterialIssueRequestsAsync(
        string internalBearerToken,
        BusinessConsoleMesMaterialIssueRequestListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesMaterialIssueRequestListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/material-issue-requests?" + MaterialIssueRequestListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesMaterialIssueRequestRow> GetMaterialIssueRequestAsync(
        string internalBearerToken,
        string requestId,
        BusinessConsoleMesMaterialIssueRequestDetailRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesMaterialIssueRequestRow>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/mes/material-issue-requests/{Uri.EscapeDataString(requestId)}?" +
            ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> ConfirmLineSideMaterialReceiptAsync(
        string internalBearerToken,
        string requestId,
        BusinessConsoleMesConfirmLineSideReceiptRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/material-issue-requests/{Uri.EscapeDataString(requestId)}/line-side-receipts",
            request,
            MesMaterialIssueRequestDocumentType,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> ReturnLineSideMaterialAsync(
        string internalBearerToken,
        string requestId,
        BusinessConsoleMesReturnLineSideMaterialRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/material-issue-requests/{Uri.EscapeDataString(requestId)}/line-side-returns",
            request,
            MesMaterialIssueRequestDocumentType,
            cancellationToken);

    public Task<BusinessConsoleMesDispatchTaskListResponse> ListDispatchTasksAsync(
        string internalBearerToken,
        BusinessConsoleMesDispatchTaskListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesDispatchTaskListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/dispatch-tasks?" + DispatchTaskListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> AssignDispatchTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesAssignDispatchTaskForwardRequest request,
        string actor,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/dispatch-tasks/{Uri.EscapeDataString(operationTaskId)}/assign",
            request,
            MesDispatchTaskDocumentType,
            cancellationToken,
            configureRequest: message =>
                message.Headers.TryAddWithoutValidation("X-Authenticated-Actor", actor));

    public Task<BusinessConsoleMesOperationTaskListResponse> ListOperationTasksAsync(
        string internalBearerToken,
        BusinessMesOperationTaskListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesOperationTaskListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/operation-tasks?" + OperationTaskListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesOperationTaskListResponse> ListReportableOperationTasksAsync(
        string internalBearerToken,
        BusinessMesOperationTaskListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesOperationTaskListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/reportable-operation-tasks?" + OperationTaskListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesOperationTaskActionResponse> StartOperationTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        OperationTaskActionAsync(internalBearerToken, operationTaskId, "start", request, cancellationToken);

    public Task<BusinessConsoleMesOperationTaskActionResponse> PauseOperationTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        OperationTaskActionAsync(internalBearerToken, operationTaskId, "pause", request, cancellationToken);

    public Task<BusinessConsoleMesOperationTaskActionResponse> ResumeOperationTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        OperationTaskActionAsync(internalBearerToken, operationTaskId, "resume", request, cancellationToken);

    public Task<BusinessConsoleMesOperationTaskActionResponse> CompleteOperationTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        OperationTaskActionAsync(internalBearerToken, operationTaskId, "complete", request, cancellationToken);

    public Task<BusinessConsoleMesWipSummaryResponse> GetWipSummaryAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesWipSummaryResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/wip?" + ListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesProductionReportListResponse> ListProductionReportsAsync(
        string internalBearerToken,
        BusinessConsoleMesListWithoutStatusRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesProductionReportListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/production-reports?" + ListQueryWithoutStatus(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesProductionReportDetailResponse> GetProductionReportAsync(
        string internalBearerToken,
        string reportNo,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesProductionReportDetailResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/mes/production-reports/{Uri.EscapeDataString(reportNo)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesTelemetryCandidateListResponse> ListTelemetryCandidatesAsync(string internalBearerToken, BusinessConsoleMesTelemetryCandidateListRequest request, CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesTelemetryCandidateListResponse>(internalBearerToken, HttpMethod.Get,
            "/api/business/v1/mes/telemetry-production-report-candidates?" + Query(
                ("organizationId", request.OrganizationId), ("environmentId", request.EnvironmentId), ("status", request.Status),
                ("workCenterId", request.WorkCenterId), ("deviceAssetId", request.DeviceAssetId),
                ("fromUtc", request.FromUtc), ("toUtc", request.ToUtc), ("skip", request.Skip), ("take", request.Take)), null, cancellationToken);

    public Task<BusinessConsoleMesTelemetryCandidateRow> GetTelemetryCandidateAsync(string internalBearerToken, string candidateId, string organizationId, string environmentId, CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesTelemetryCandidateRow>(internalBearerToken, HttpMethod.Get,
            $"/api/business/v1/mes/telemetry-production-report-candidates/{Uri.EscapeDataString(candidateId)}?organizationId={Uri.EscapeDataString(organizationId)}&environmentId={Uri.EscapeDataString(environmentId)}", null, cancellationToken);

    public Task<BusinessConsoleRecordProductionReportResponse> PromoteTelemetryCandidateAsync(string internalBearerToken, string candidateId, BusinessConsoleMesTelemetryCandidatePromoteRequest request, string actor, CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleRecordProductionReportResponse>(internalBearerToken, HttpMethod.Post,
            $"/api/business/v1/mes/telemetry-production-report-candidates/{Uri.EscapeDataString(candidateId)}/promote",
            new { request.OrganizationId, request.EnvironmentId, CandidateId = candidateId, request.WorkOrderId, request.OperationTaskId, Actor = actor }, cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> DismissTelemetryCandidateAsync(string internalBearerToken, string candidateId, BusinessConsoleMesTelemetryCandidateDismissRequest request, string actor, CancellationToken cancellationToken) =>
        SendAcceptedAsync(internalBearerToken,
            $"/api/business/v1/mes/telemetry-production-report-candidates/{Uri.EscapeDataString(candidateId)}/dismiss",
            new { request.OrganizationId, request.EnvironmentId, CandidateId = candidateId, request.Reason, Actor = actor },
            MesTelemetryCandidateDocumentType, cancellationToken);

    public async Task<BusinessConsoleMesScheduleResult> RunScheduleAsync(
        string internalBearerToken,
        BusinessConsoleRunScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await SendAsync<DownstreamMesScheduleResult>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/mes/schedules/run",
            request,
            cancellationToken);
        return result.ToBusinessConsoleResult();
    }

    public async Task<BusinessConsoleRecordProductionReportResponse> RecordProductionReportAsync(
        string internalBearerToken,
        BusinessConsoleRecordProductionReportRequest request,
        string actor,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamRecordProductionReportResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/mes/production-reports",
            new DownstreamRecordProductionReportRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.WorkOrderId,
                request.OperationTaskId,
                request.GoodQuantity,
                request.ScrapQuantity,
                request.CompletesOperation,
                request.ReportedAtUtc,
                request.IdempotencyKey,
                request.ConsumedMaterialLots,
                request.ReworkQuantity,
                request.ScrapReasonCode,
                request.DefectRecordNo,
                request.ProducedLotNo,
                request.SerialNo,
                actor),
            cancellationToken);

        if (response.ProductionReportId is null ||
            response.ProductionReportId.Id == Guid.Empty ||
            string.IsNullOrWhiteSpace(response.ReportNo))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }

        return new BusinessConsoleRecordProductionReportResponse(
            response.ProductionReportId.Id.ToString(),
            response.ReportNo,
            string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? null
                : BusinessConsoleOperationReceipts.Accepted(
                    "mes.production-report.record",
                    "mes",
                    "production-report",
                    response.ProductionReportId.Id.ToString(),
                    $"/api/business-console/v1/mes/production-reports/{Uri.EscapeDataString(response.ReportNo)}?organizationId={Uri.EscapeDataString(request.OrganizationId)}&environmentId={Uri.EscapeDataString(request.EnvironmentId)}",
                    request.IdempotencyKey));
    }

    public Task<BusinessConsoleAcceptedResponse> RecordDefectAsync(
        string internalBearerToken,
        BusinessMesRecordDefectRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            "/api/business/v1/mes/defects",
            new
            {
                request.OrganizationId,
                request.EnvironmentId,
                request.WorkOrderId,
                request.OperationTaskId,
                request.DefectCode,
                request.Quantity,
                request.RecordedAtUtc,
                request.IdempotencyKey,
            },
            MesDefectDocumentType,
            cancellationToken);

    public Task<BusinessConsoleMesRelatedQualityItemListResponse> ListRelatedQualityItemsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesRelatedQualityItemListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/related-quality-items?" + ListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesReceiptRequestListResponse> ListFinishedGoodsReceiptRequestsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken,
        string? exactRequestNo = null) =>
        SendAsync<BusinessConsoleMesReceiptRequestListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/finished-goods-receipt-requests?" + ReceiptListQuery(request, exactRequestNo),
            null,
            cancellationToken);

    public async Task<BusinessConsoleMesCreateReceiptResponse> CreateFinishedGoodsReceiptRequestAsync(
        string internalBearerToken,
        BusinessConsoleMesCreateReceiptRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateFinishedGoodsReceiptRequestResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/mes/finished-goods-receipt-requests",
            request,
            cancellationToken);

        if (response.FinishedGoodsReceiptRequestId is null ||
            response.FinishedGoodsReceiptRequestId.Id == Guid.Empty ||
            string.IsNullOrWhiteSpace(response.RequestNo))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }

        return new BusinessConsoleMesCreateReceiptResponse(
            response.FinishedGoodsReceiptRequestId.Id.ToString(),
            response.RequestNo);
    }

    public Task<BusinessConsoleMesDowntimeEventListResponse> ListDowntimeEventsAsync(
        string internalBearerToken,
        BusinessConsoleMesDowntimeEventListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesDowntimeEventListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/downtime-events?" + DowntimeEventListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> RecordDowntimeEventAsync(
        string internalBearerToken,
        BusinessConsoleMesRecordDowntimeEventRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            "/api/business/v1/mes/downtime-events",
            request,
            MesDowntimeEventDocumentType,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> RecordDowntimeEventAsync(
        string internalBearerToken,
        BusinessMesRecordDowntimeEventRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            "/api/business/v1/mes/downtime-events",
            new
            {
                request.OrganizationId,
                request.EnvironmentId,
                request.WorkOrderId,
                request.OperationTaskId,
                request.WorkCenterId,
                request.DeviceAssetId,
                request.ReasonCode,
                request.StartedAtUtc,
                request.IdempotencyKey,
                request.ToUtc,
            },
            MesDowntimeEventDocumentType,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> ConfirmDowntimeRecoveryAsync(
        string internalBearerToken,
        string downtimeEventId,
        BusinessConsoleMesRecoverDowntimeEventRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/downtime-events/{Uri.EscapeDataString(downtimeEventId)}/recover",
            request,
            MesDowntimeEventDocumentType,
            cancellationToken);

    public Task<BusinessConsoleMesScheduleResultListResponse> ListScheduleResultsAsync(
        string internalBearerToken,
        BusinessConsoleMesScheduleResultListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesScheduleResultListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/schedules?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("trigger", request.Trigger),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesShiftHandoverListResponse> ListShiftHandoversAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesShiftHandoverListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/shift-handovers?" + ListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> CreateShiftHandoverAsync(
        string internalBearerToken,
        BusinessConsoleMesCreateShiftHandoverRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            "/api/business/v1/mes/shift-handovers",
            request,
            MesShiftHandoverDocumentType,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> AcceptShiftHandoverAsync(
        string internalBearerToken,
        string handoverId,
        BusinessConsoleMesAcceptShiftHandoverRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/shift-handovers/{Uri.EscapeDataString(handoverId)}/accept",
            request,
            MesShiftHandoverDocumentType,
            cancellationToken);

    public Task<BusinessConsoleMesTraceabilityResponse> GetWorkOrderTraceabilityAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesTraceabilityResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/mes/traceability/work-orders/{Uri.EscapeDataString(workOrderId)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesTraceabilityResponse> GetBatchTraceabilityAsync(
        string internalBearerToken,
        string batchOrSerial,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesTraceabilityResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/mes/traceability/batches/{Uri.EscapeDataString(batchOrSerial)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesTraceabilityResponse> GetMaterialLotTraceabilityAsync(
        string internalBearerToken,
        string materialLotId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesTraceabilityResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/mes/traceability/material-lots/{Uri.EscapeDataString(materialLotId)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesCapacityImpactListResponse> ListCapacityImpactsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesCapacityImpactListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/capacity-impacts?" + ListQuery(request),
            null,
            cancellationToken);

    private async Task<BusinessConsoleCreateRushWorkOrderResponse> CreateRushWorkOrderCoreAsync(
        string internalBearerToken,
        BusinessConsoleCreateRushWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateRushWorkOrderResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/mes/work-orders/rush",
            request,
            cancellationToken);
        return new BusinessConsoleCreateRushWorkOrderResponse(
            response.WorkOrderId,
            response.Schedule.ToBusinessConsoleResult(),
            response.AffectedWorkOrderIds);
    }

    private async Task<BusinessConsoleMesOperationTaskActionResponse> OperationTaskActionAsync(
        string internalBearerToken,
        string operationTaskId,
        string action,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<BusinessConsoleMesOperationTaskActionResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/operation-tasks/{Uri.EscapeDataString(operationTaskId)}/{action}",
            request,
            cancellationToken);
        var expectedStatus = action switch
        {
            "start" or "resume" => "InProgress",
            "pause" => "Paused",
            "complete" => "Completed",
            _ => null,
        };
        if (!string.Equals(response.OperationTaskId, operationTaskId, StringComparison.Ordinal)
            || expectedStatus is null
            || !string.Equals(response.Status, expectedStatus, StringComparison.Ordinal)
            || response.ChangedAtUtc == default)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }

        return response with
        {
            OperationReceipt = string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? null
                : BusinessConsoleOperationReceipts.Confirmed(
                    $"mes.operation-task.{action}",
                    "mes",
                    "operation-task",
                    response.OperationTaskId,
                    response.ChangedAtUtc,
                    response.Status,
                    request.IdempotencyKey),
        };
    }

    private static string ContextQuery(string organizationId, string environmentId) =>
        Query(("organizationId", organizationId), ("environmentId", environmentId));

    private static string ListQuery(BusinessConsoleMesListRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("status", request.Status),
            ("keyword", request.Keyword),
            ("workCenterId", request.WorkCenterId),
            ("shiftId", request.ShiftId),
            ("deviceAssetId", request.DeviceAssetId),
            ("workOrderId", request.WorkOrderId),
            ("skip", request.Skip),
            ("take", request.Take));

    private static string DowntimeEventListQuery(BusinessConsoleMesDowntimeEventListRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("status", request.Status),
            ("keyword", request.Keyword),
            ("workCenterId", request.WorkCenterId),
            ("shiftId", request.ShiftId),
            ("deviceAssetId", request.DeviceAssetId),
            ("workOrderId", request.WorkOrderId),
            ("reasonCode", request.ReasonCode),
            ("skip", request.Skip),
            ("take", request.Take));

    private static string MaterialIssueRequestListQuery(BusinessConsoleMesMaterialIssueRequestListRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("status", request.Status),
            ("keyword", request.Keyword),
            ("workCenterId", request.WorkCenterId),
            ("shiftId", request.ShiftId),
            ("deviceAssetId", request.DeviceAssetId),
            ("workOrderId", request.WorkOrderId),
            ("skip", request.Skip),
            ("take", request.Take),
            ("operationTaskId", request.OperationTaskId));

    private static string DispatchTaskListQuery(BusinessConsoleMesDispatchTaskListRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("status", request.Status),
            ("keyword", request.Keyword),
            ("workCenterId", request.WorkCenterId),
            ("shiftId", request.ShiftId),
            ("deviceAssetId", request.DeviceAssetId),
            ("assignedUserId", request.AssignedUserId),
            ("skip", request.Skip),
            ("take", request.Take));

    private static string ReceiptListQuery(BusinessConsoleMesListRequest request, string? exactRequestNo) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("status", request.Status),
            ("keyword", request.Keyword),
            ("workCenterId", request.WorkCenterId),
            ("shiftId", request.ShiftId),
            ("deviceAssetId", request.DeviceAssetId),
            ("workOrderId", request.WorkOrderId),
            ("requestNo", exactRequestNo),
            ("skip", request.Skip),
            ("take", request.Take));

    private static string OperationTaskListQuery(BusinessMesOperationTaskListRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("status", request.Status),
            ("keyword", request.Keyword),
            ("workCenterId", request.WorkCenterId),
            ("shiftId", request.ShiftId),
            ("deviceAssetId", request.DeviceAssetId),
            ("workOrderId", request.WorkOrderId),
            ("assignedUserIds", request.AssignedUserIds),
            ("teamIds", request.TeamIds),
            ("workCenterIds", request.WorkCenterIds),
            ("operationTaskId", request.OperationTaskId),
            ("skip", request.Skip),
            ("take", request.Take));

    private static string WorkOrderListQuery(BusinessMesWorkOrderListRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("status", request.Status),
            ("keyword", request.Keyword),
            ("workCenterId", request.WorkCenterId),
            ("assignedUserIds", request.AssignedUserIds),
            ("teamIds", request.TeamIds),
            ("workCenterIds", request.WorkCenterIds),
            ("shiftId", request.ShiftId),
            ("deviceAssetId", request.DeviceAssetId),
            ("deviceAssetIds", request.DeviceAssetIds),
            ("statuses", request.Statuses),
            ("workOrderId", request.WorkOrderId),
            ("skip", request.Skip),
            ("take", request.Take));

    private static string ListQueryWithoutStatus(BusinessConsoleMesListWithoutStatusRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("keyword", request.Keyword),
            ("workCenterId", request.WorkCenterId),
            ("shiftId", request.ShiftId),
            ("deviceAssetId", request.DeviceAssetId),
            ("skip", request.Skip),
            ("take", request.Take));

    private static string ProductionPlanListQuery(BusinessConsoleMesProductionPlanListRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("status", request.Status),
            ("keyword", request.Keyword),
            ("workCenterId", request.WorkCenterId),
            ("shiftId", request.ShiftId),
            ("deviceAssetId", request.DeviceAssetId),
            ("source", request.Source),
            ("readinessStatus", request.ReadinessStatus),
            ("skip", request.Skip),
            ("take", request.Take));

    private static string FoundationQuery(BusinessConsoleMesFoundationReadinessRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("siteCode", request.SiteCode),
            ("lineCode", request.LineCode),
            ("workCenterCode", request.WorkCenterCode),
            ("skuId", request.SkuId),
            ("productionVersionId", request.ProductionVersionId),
            ("plannedStartUtc", request.PlannedStartUtc),
            ("plannedEndUtc", request.PlannedEndUtc));

    private sealed record DownstreamCreateRushWorkOrderResponse(
        string WorkOrderId,
        DownstreamMesScheduleResult Schedule,
        IReadOnlyCollection<string> AffectedWorkOrderIds);

    private sealed record DownstreamMesScheduleResult(
        int ScheduleVersion,
        JsonElement Trigger,
        DateTimeOffset ScheduledAtUtc,
        IReadOnlyCollection<BusinessConsoleScheduledOperation> Assignments,
        IReadOnlyCollection<string> AffectedWorkOrderIds)
    {
        public BusinessConsoleMesScheduleResult ToBusinessConsoleResult() =>
            new(
                ScheduleVersion,
                FormatTrigger(Trigger),
                ScheduledAtUtc,
                Assignments,
                AffectedWorkOrderIds);
    }

    private sealed record DownstreamRecordProductionReportResponse(
        DownstreamProductionReportId? ProductionReportId,
        string? ReportNo);

    private sealed record DownstreamProductionReportId(Guid Id);

    private sealed record DownstreamCreateFinishedGoodsReceiptRequestResponse(
        DownstreamFinishedGoodsReceiptRequestId? FinishedGoodsReceiptRequestId,
        string? RequestNo);

    private sealed record DownstreamFinishedGoodsReceiptRequestId(Guid Id);

    // Downstream force-release body carries the actor injected by the gateway from the
    // authenticated principal; the request DTO no longer exposes a caller-supplied actor.
    private sealed record DownstreamForceReleaseQualityHoldRequest(
        string OrganizationId,
        string EnvironmentId,
        string Reason,
        string? SourceService,
        DateTimeOffset? ReleasedAtUtc);

    // 报工下游载荷携带 Gateway 从已认证 principal 注入的操作人；公开请求 DTO 不暴露调用方自带的身份字段。
    private sealed record DownstreamRecordProductionReportRequest(
        string OrganizationId,
        string EnvironmentId,
        string WorkOrderId,
        string OperationTaskId,
        decimal GoodQuantity,
        decimal ScrapQuantity,
        bool CompletesOperation,
        DateTimeOffset ReportedAtUtc,
        string IdempotencyKey,
        IReadOnlyCollection<BusinessConsoleConsumedMaterialLotInput>? ConsumedMaterialLots,
        decimal ReworkQuantity,
        string? ScrapReasonCode,
        string? DefectRecordNo,
        string? ProducedLotNo,
        string? SerialNo,
        string ReportedBy);

    private sealed record DownstreamReverseProductionReportRequest(
        string OrganizationId,
        string EnvironmentId,
        string Reason,
        string ActorRef,
        DateTimeOffset? ReversedAtUtc,
        string? IdempotencyKey);

    private static string FormatTrigger(JsonElement trigger) => trigger.ValueKind switch
    {
        JsonValueKind.String => trigger.GetString() ?? string.Empty,
        JsonValueKind.Number => trigger.GetRawText(),
        _ => trigger.ToString(),
    };
}
