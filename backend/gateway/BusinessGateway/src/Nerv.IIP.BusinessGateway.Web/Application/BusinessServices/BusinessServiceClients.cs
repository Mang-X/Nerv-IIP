using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using Nerv.IIP.Sdk.Core;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessMasterDataClient
{
    Task<BusinessConsoleResourceListResponse> ListResourcesAsync(
        string internalBearerToken,
        BusinessConsoleListResourcesRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> CreateSkuAsync(
        string internalBearerToken,
        BusinessConsoleCreateSkuRequest request,
        CancellationToken cancellationToken);
}

public interface IBusinessInventoryClient
{
    Task<BusinessConsoleInventoryAvailabilityResponse> GetAvailabilityAsync(
        string internalBearerToken,
        BusinessConsoleInventoryAvailabilityRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsolePostStockMovementResponse> PostMovementAsync(
        string internalBearerToken,
        BusinessConsolePostStockMovementRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateStockCountTaskResponse> CreateCountTaskAsync(
        string internalBearerToken,
        BusinessConsoleCreateStockCountTaskRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleConfirmStockCountAdjustmentResponse> ConfirmCountAdjustmentAsync(
        string internalBearerToken,
        string countTaskId,
        BusinessConsoleConfirmStockCountAdjustmentRequest request,
        CancellationToken cancellationToken);
}

public interface IBusinessQualityClient
{
    Task<BusinessConsoleQualityListResponse> ListInspectionPlansAsync(
        string internalBearerToken,
        BusinessConsoleQualityListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateInspectionRecordResponse> CreateInspectionRecordAsync(
        string internalBearerToken,
        BusinessConsoleCreateInspectionRecordRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityListResponse> ListNcrsAsync(
        string internalBearerToken,
        BusinessConsoleQualityListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> SubmitNcrDispositionAsync(
        string internalBearerToken,
        string ncrId,
        BusinessConsoleNcrDispositionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> CloseNcrAsync(
        string internalBearerToken,
        string ncrId,
        BusinessConsoleNcrCloseRequest request,
        CancellationToken cancellationToken);
}

public interface IBusinessProductEngineeringClient
{
    Task<BusinessConsoleEngineeringBomListResponse> ListEngineeringBomsAsync(
        string internalBearerToken,
        BusinessConsoleListEngineeringBomsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleRoutingListResponse> ListRoutingsAsync(
        string internalBearerToken,
        BusinessConsoleListRoutingsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleProductionVersionListResponse> ListProductionVersionsAsync(
        string internalBearerToken,
        BusinessConsoleListProductionVersionsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResolveProductionVersionResponse> ResolveProductionVersionAsync(
        string internalBearerToken,
        BusinessConsoleResolveProductionVersionRequest request,
        CancellationToken cancellationToken);
}

public interface IBusinessPlanningClient
{
    Task<BusinessConsoleDemandSourceListResponse> ListDemandSourcesAsync(
        string internalBearerToken,
        BusinessConsolePlanningContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleDemandSourceResponse> CreateOrUpdateDemandSourceAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateDemandSourceRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleRunMrpResponse> RunMrpAsync(
        string internalBearerToken,
        BusinessConsoleRunMrpRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMrpRunListResponse> ListMrpRunsAsync(
        string internalBearerToken,
        BusinessConsolePlanningContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMrpPeggingListResponse> ListMrpPeggingAsync(
        string internalBearerToken,
        string runId,
        CancellationToken cancellationToken);

    Task<BusinessConsolePlanningSuggestionListResponse> ListSuggestionsAsync(
        string internalBearerToken,
        BusinessConsolePlanningSuggestionListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> AcceptSuggestionAsync(
        string internalBearerToken,
        string suggestionId,
        BusinessConsoleAcceptPlanningSuggestionRequest request,
        CancellationToken cancellationToken);
}

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
        BusinessConsoleMesListRequest request,
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
        BusinessConsoleMesListRequest request,
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

    Task<BusinessConsoleCreateRushWorkOrderResponse> CreateRushWorkOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateRushWorkOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesMaterialReadinessResponse> GetMaterialReadinessAsync(
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
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> ConfirmLineSideMaterialReceiptAsync(
        string internalBearerToken,
        string requestId,
        BusinessConsoleMesConfirmLineSideReceiptRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesDispatchTaskListResponse> ListDispatchTasksAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> AssignDispatchTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesAssignDispatchTaskRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesOperationTaskListResponse> ListOperationTasksAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
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
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesScheduleResult> RunScheduleAsync(
        string internalBearerToken,
        BusinessConsoleRunScheduleRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleRecordProductionReportResponse> RecordProductionReportAsync(
        string internalBearerToken,
        BusinessConsoleRecordProductionReportRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> RecordDefectAsync(
        string internalBearerToken,
        BusinessConsoleMesRecordDefectRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesRelatedQualityItemListResponse> ListRelatedQualityItemsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesReceiptRequestListResponse> ListFinishedGoodsReceiptRequestsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesCreateReceiptResponse> CreateFinishedGoodsReceiptRequestAsync(
        string internalBearerToken,
        BusinessConsoleMesCreateReceiptRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesDowntimeEventListResponse> ListDowntimeEventsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> RecordDowntimeEventAsync(
        string internalBearerToken,
        BusinessConsoleMesRecordDowntimeEventRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> ConfirmDowntimeRecoveryAsync(
        string internalBearerToken,
        string downtimeEventId,
        BusinessConsoleMesRecoverDowntimeEventRequest request,
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

public sealed class BusinessServiceProxyException : Exception
{
    public const string DownstreamRequestFailedMessage = "downstream-request-failed";

    public BusinessServiceProxyException(
        HttpStatusCode statusCode,
        string message,
        Exception? innerException = null)
        : base(DownstreamRequestFailedMessage, innerException)
    {
        _ = message;
        StatusCode = statusCode;
    }

    private BusinessServiceProxyException(
        HttpStatusCode statusCode,
        string safeMessage,
        Exception? innerException,
        bool messageIsSafe)
        : base(messageIsSafe ? safeMessage : DownstreamRequestFailedMessage, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }

    public static BusinessServiceProxyException FromSafeDownstreamMessage(
        HttpStatusCode statusCode,
        string? downstreamMessage,
        Exception? innerException = null) =>
        new(
            statusCode,
            IsSafeDownstreamMessage(downstreamMessage)
                ? downstreamMessage!
                : DownstreamRequestFailedMessage,
            innerException,
            messageIsSafe: true);

    private static bool IsSafeDownstreamMessage(string? downstreamMessage)
    {
        if (string.IsNullOrWhiteSpace(downstreamMessage) || downstreamMessage.Length > 128)
        {
            return false;
        }

        var first = downstreamMessage[0];
        if (!IsLowerAsciiLetter(first) && !char.IsAsciiDigit(first))
        {
            return false;
        }

        return downstreamMessage.All(static value =>
            IsLowerAsciiLetter(value) ||
            char.IsAsciiDigit(value) ||
            value is '-' or '_' or '.');
    }

    private static bool IsLowerAsciiLetter(char value) => value is >= 'a' and <= 'z';
}

public abstract class BusinessServiceHttpClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected async Task<TResponse> SendAsync<TResponse>(
        string internalBearerToken,
        HttpMethod method,
        string requestUri,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalBearerToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                response.StatusCode,
                await ReadDownstreamEnvelopeMessageAsync(response, cancellationToken));
        }

        try
        {
            return await PlatformApiClient.ReadResponseDataAsync<TResponse>(response, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response",
                ex);
        }
        catch (InvalidOperationException ex)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response",
                ex);
        }
    }

    private static async Task<string?> ReadDownstreamEnvelopeMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    protected static string Query(params (string Name, object? Value)[] values)
    {
        var pairs = values
            .Where(x => x.Value is not null && !string.IsNullOrWhiteSpace(Convert.ToString(x.Value, System.Globalization.CultureInfo.InvariantCulture)))
            .Select(x => $"{Uri.EscapeDataString(x.Name)}={Uri.EscapeDataString(FormatValue(x.Value!))}");
        return string.Join('&', pairs);
    }

    protected static bool? TrueFlag(bool value) => value ? true : null;

    private static string FormatValue(object value) => value switch
    {
        bool boolValue => boolValue.ToString().ToLowerInvariant(),
        DateOnly date => date.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset dateTime => dateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
    };
}

public sealed class HttpBusinessMasterDataClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessMasterDataClient
{
    public async Task<BusinessConsoleResourceListResponse> ListResourcesAsync(
        string internalBearerToken,
        BusinessConsoleListResourcesRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<BusinessConsoleResourceListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/master-data/resources?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("resourceType", request.ResourceType),
                ("includeDisabled", TrueFlag(request.IncludeDisabled)),
                ("take", request.Take)),
            null,
            cancellationToken);
        return response.Total > 0 ? response : response with { Total = response.Resources.Count };
    }

    public Task<BusinessConsoleResourceItem> CreateSkuAsync(
        string internalBearerToken,
        BusinessConsoleCreateSkuRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleResourceItem>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/master-data/skus",
            request,
            cancellationToken);
}

public sealed class HttpBusinessInventoryClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessInventoryClient
{
    public Task<BusinessConsoleInventoryAvailabilityResponse> GetAvailabilityAsync(
        string internalBearerToken,
        BusinessConsoleInventoryAvailabilityRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleInventoryAvailabilityResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/inventory/v1/availability?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("uomCode", request.UomCode),
                ("siteCode", request.SiteCode),
                ("locationCode", request.LocationCode),
                ("lotNo", request.LotNo),
                ("serialNo", request.SerialNo),
                ("qualityStatus", request.QualityStatus),
                ("ownerType", request.OwnerType),
                ("ownerId", request.OwnerId)),
            null,
            cancellationToken);

    public Task<BusinessConsolePostStockMovementResponse> PostMovementAsync(
        string internalBearerToken,
        BusinessConsolePostStockMovementRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsolePostStockMovementResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/inventory/v1/movements",
            request,
            cancellationToken);

    public Task<BusinessConsoleCreateStockCountTaskResponse> CreateCountTaskAsync(
        string internalBearerToken,
        BusinessConsoleCreateStockCountTaskRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateStockCountTaskResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/inventory/v1/count-tasks",
            request,
            cancellationToken);

    public Task<BusinessConsoleConfirmStockCountAdjustmentResponse> ConfirmCountAdjustmentAsync(
        string internalBearerToken,
        string countTaskId,
        BusinessConsoleConfirmStockCountAdjustmentRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleConfirmStockCountAdjustmentResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/inventory/v1/count-tasks/{Uri.EscapeDataString(countTaskId)}/adjustments",
            new DownstreamConfirmStockCountAdjustmentRequest(
                countTaskId,
                request.CountedQuantity,
                request.IdempotencyKey),
            cancellationToken);

    private sealed record DownstreamConfirmStockCountAdjustmentRequest(
        string CountTaskId,
        decimal CountedQuantity,
        string IdempotencyKey);
}

public sealed class HttpBusinessQualityClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessQualityClient
{
    public async Task<BusinessConsoleQualityListResponse> ListInspectionPlansAsync(
        string internalBearerToken,
        BusinessConsoleQualityListRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamInspectionPlanListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/inspection-plans?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("status", request.Status),
                ("take", request.Take)),
            null,
            cancellationToken);
        return new BusinessConsoleQualityListResponse(
            response.Items.Select(ToQualityItem).ToArray());
    }

    public Task<BusinessConsoleCreateInspectionRecordResponse> CreateInspectionRecordAsync(
        string internalBearerToken,
        BusinessConsoleCreateInspectionRecordRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateInspectionRecordResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/quality/inspection-records",
            ToDownstreamRequest(request),
            cancellationToken);

    public async Task<BusinessConsoleQualityListResponse> ListNcrsAsync(
        string internalBearerToken,
        BusinessConsoleQualityListRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamNcrListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/ncrs?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("status", request.Status),
                ("take", request.Take)),
            null,
            cancellationToken);
        return new BusinessConsoleQualityListResponse(
            response.Items.Select(ToQualityItem).ToArray());
    }

    public Task<BusinessConsoleAcceptedResponse> SubmitNcrDispositionAsync(
        string internalBearerToken,
        string ncrId,
        BusinessConsoleNcrDispositionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/quality/ncrs/{Uri.EscapeDataString(ncrId)}/disposition",
            new DownstreamSubmitNcrDispositionRequest(
                ncrId,
                request.DispositionType,
                request.DispositionApprovalChainId,
                request.AttachmentFileIds),
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> CloseNcrAsync(
        string internalBearerToken,
        string ncrId,
        BusinessConsoleNcrCloseRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/quality/ncrs/{Uri.EscapeDataString(ncrId)}/close",
            new DownstreamCloseNcrRequest(
                ncrId,
                request.ReworkWorkOrderId,
                request.ScrapMovementId,
                request.ReturnDocumentId),
            cancellationToken);

    private static BusinessConsoleQualityItem ToQualityItem(DownstreamInspectionPlanItem item) =>
        new(
            item.InspectionPlanId,
            item.PlanCode,
            item.Status,
            item.Category,
            item.SkuCode,
            item.PartnerId,
            item.WorkCenterId,
            item.DeviceAssetId,
            item.DocumentType,
            null,
            null,
            null,
            null,
            null,
            null);

    private static BusinessConsoleQualityItem ToQualityItem(DownstreamNcrItem item) =>
        new(
            item.NcrId,
            item.NcrCode,
            item.Status,
            null,
            item.SkuCode,
            null,
            null,
            null,
            null,
            item.SourceType,
            item.SourceDocumentId,
            item.DefectQuantity,
            item.DefectReason,
            item.BatchNo,
            item.SerialNo);

    private sealed record DownstreamInspectionPlanListResponse(
        IReadOnlyCollection<DownstreamInspectionPlanItem> Items);

    private static DownstreamCreateInspectionRecordRequest ToDownstreamRequest(
        BusinessConsoleCreateInspectionRecordRequest request) =>
        new(
            request.OrganizationId,
            request.EnvironmentId,
            request.InspectionPlanId,
            request.SourceType,
            request.SourceService,
            request.SourceDocumentId,
            request.SkuCode,
            request.InspectedQuantity,
            request.BatchNo,
            request.SerialNo,
            request.ResultLines?.Select(ToDownstreamLine).ToArray(),
            request.DispositionReason,
            request.DispositionAttachmentFileIds);

    private static DownstreamInspectionResultLine ToDownstreamLine(
        BusinessConsoleInspectionCharacteristicResult line) =>
        new(
            line.CharacteristicCode,
            line.ObservedValue,
            line.UnitCode,
            line.Result,
            line.DefectReason,
            line.DefectQuantity,
            line.AttachmentFileIds ?? []);

    private sealed record DownstreamCreateInspectionRecordRequest(
        string OrganizationId,
        string EnvironmentId,
        string? InspectionPlanId,
        string SourceType,
        string SourceService,
        string SourceDocumentId,
        string SkuCode,
        decimal InspectedQuantity,
        string? BatchNo,
        string? SerialNo,
        IReadOnlyCollection<DownstreamInspectionResultLine>? ResultLines,
        string? DispositionReason,
        IReadOnlyCollection<string>? DispositionAttachmentFileIds);

    private sealed record DownstreamInspectionResultLine(
        string CharacteristicCode,
        string ObservedValue,
        string? UnitCode,
        string Result,
        string? DefectReason,
        decimal? DefectQuantity,
        IReadOnlyCollection<string> AttachmentFileIds);

    private sealed record DownstreamInspectionPlanItem(
        string InspectionPlanId,
        string PlanCode,
        string Category,
        string? SkuCode,
        string? PartnerId,
        string? WorkCenterId,
        string? DeviceAssetId,
        string? DocumentType,
        int Version,
        string Status);

    private sealed record DownstreamNcrListResponse(
        IReadOnlyCollection<DownstreamNcrItem> Items);

    private sealed record DownstreamNcrItem(
        string NcrId,
        string NcrCode,
        string SourceType,
        string SourceDocumentId,
        string SkuCode,
        decimal DefectQuantity,
        string DefectReason,
        string? BatchNo,
        string? SerialNo,
        string Status);

    private sealed record DownstreamSubmitNcrDispositionRequest(
        string NcrId,
        string DispositionType,
        string? DispositionApprovalChainId,
        IReadOnlyCollection<string>? AttachmentFileIds);

    private sealed record DownstreamCloseNcrRequest(
        string NcrId,
        string? ReworkWorkOrderId,
        string? ScrapMovementId,
        string? ReturnDocumentId);
}

public sealed class HttpBusinessProductEngineeringClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessProductEngineeringClient
{
    public Task<BusinessConsoleEngineeringBomListResponse> ListEngineeringBomsAsync(
        string internalBearerToken,
        BusinessConsoleListEngineeringBomsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringBomListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/engineering-boms?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("parentItemCode", request.ParentItemCode),
                ("status", request.Status)),
            null,
            cancellationToken);

    public Task<BusinessConsoleRoutingListResponse> ListRoutingsAsync(
        string internalBearerToken,
        BusinessConsoleListRoutingsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleRoutingListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/routings?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("status", request.Status)),
            null,
            cancellationToken);

    public Task<BusinessConsoleProductionVersionListResponse> ListProductionVersionsAsync(
        string internalBearerToken,
        BusinessConsoleListProductionVersionsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleProductionVersionListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/production-versions?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("status", request.Status)),
            null,
            cancellationToken);

    public Task<BusinessConsoleResolveProductionVersionResponse> ResolveProductionVersionAsync(
        string internalBearerToken,
        BusinessConsoleResolveProductionVersionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleResolveProductionVersionResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/production-versions/resolve?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("effectiveDate", request.EffectiveDate),
                ("lotSize", request.LotSize)),
            null,
            cancellationToken);
}

public sealed class HttpBusinessPlanningClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessPlanningClient
{
    public Task<BusinessConsoleDemandSourceListResponse> ListDemandSourcesAsync(
        string internalBearerToken,
        BusinessConsolePlanningContextRequest request,
        CancellationToken cancellationToken) =>
        ListDemandSourcesCoreAsync(internalBearerToken, request, cancellationToken);

    private async Task<BusinessConsoleDemandSourceListResponse> ListDemandSourcesCoreAsync(
        string internalBearerToken,
        BusinessConsolePlanningContextRequest request,
        CancellationToken cancellationToken)
    {
        var items = await SendAsync<IReadOnlyCollection<BusinessConsoleDemandSourceResponse>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/planning/demands?" + PlanningContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);
        return new BusinessConsoleDemandSourceListResponse(items);
    }

    public async Task<BusinessConsoleDemandSourceResponse> CreateOrUpdateDemandSourceAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateDemandSourceRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateOrUpdateDemandSourceResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/planning/demands",
            request,
            cancellationToken);
        return new BusinessConsoleDemandSourceResponse(
            response.DemandSourceId,
            request.SourceReference ?? response.DemandSourceId,
            request.DemandType,
            request.SkuCode,
            request.UomCode,
            request.SiteCode,
            request.Quantity,
            request.DueDate);
    }

    public async Task<BusinessConsoleRunMrpResponse> RunMrpAsync(
        string internalBearerToken,
        BusinessConsoleRunMrpRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamRunMrpResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/planning/mrp-runs",
            request,
            cancellationToken);
        return new BusinessConsoleRunMrpResponse(response.RunId, response.SuggestionCount);
    }

    public Task<BusinessConsoleMrpRunListResponse> ListMrpRunsAsync(
        string internalBearerToken,
        BusinessConsolePlanningContextRequest request,
        CancellationToken cancellationToken) =>
        ListMrpRunsCoreAsync(internalBearerToken, request, cancellationToken);

    private async Task<BusinessConsoleMrpRunListResponse> ListMrpRunsCoreAsync(
        string internalBearerToken,
        BusinessConsolePlanningContextRequest request,
        CancellationToken cancellationToken)
    {
        var items = await SendAsync<IReadOnlyCollection<DownstreamMrpRunItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/planning/mrp-runs?" + PlanningContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);
        return new BusinessConsoleMrpRunListResponse(items.Select(x => new BusinessConsoleMrpRunItem(
            x.RunId,
            x.HorizonStart,
            x.HorizonEnd,
            MrpRunStatusName(x.Status),
            x.DemandCount,
            x.AvailabilityCount,
            x.SuggestionCount,
            x.ProductionEngineeringSnapshotSource,
            x.InventorySnapshotSource)).ToArray());
    }

    public Task<BusinessConsoleMrpPeggingListResponse> ListMrpPeggingAsync(
        string internalBearerToken,
        string runId,
        CancellationToken cancellationToken) =>
        ListMrpPeggingCoreAsync(internalBearerToken, runId, cancellationToken);

    private async Task<BusinessConsoleMrpPeggingListResponse> ListMrpPeggingCoreAsync(
        string internalBearerToken,
        string runId,
        CancellationToken cancellationToken)
    {
        var items = await SendAsync<IReadOnlyCollection<BusinessConsoleMrpPeggingItem>>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/planning/mrp-runs/{Uri.EscapeDataString(runId)}/pegging",
            null,
            cancellationToken);
        return new BusinessConsoleMrpPeggingListResponse(items);
    }

    public Task<BusinessConsolePlanningSuggestionListResponse> ListSuggestionsAsync(
        string internalBearerToken,
        BusinessConsolePlanningSuggestionListRequest request,
        CancellationToken cancellationToken) =>
        ListSuggestionsCoreAsync(internalBearerToken, request, cancellationToken);

    private async Task<BusinessConsolePlanningSuggestionListResponse> ListSuggestionsCoreAsync(
        string internalBearerToken,
        BusinessConsolePlanningSuggestionListRequest request,
        CancellationToken cancellationToken)
    {
        var items = await SendAsync<IReadOnlyCollection<DownstreamPlanningSuggestionItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/planning/suggestions?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("status", request.Status)),
            null,
            cancellationToken);
        return new BusinessConsolePlanningSuggestionListResponse(items.Select(x => new BusinessConsolePlanningSuggestionItem(
            x.SuggestionId,
            x.MrpRunId,
            x.SuggestionType,
            x.SkuCode,
            x.UomCode,
            x.SiteCode,
            x.Quantity,
            x.RequiredDate,
            PlanningSuggestionStatusName(x.Status),
            x.ReasonCode)).ToArray());
    }

    public Task<BusinessConsoleAcceptedResponse> AcceptSuggestionAsync(
        string internalBearerToken,
        string suggestionId,
        BusinessConsoleAcceptPlanningSuggestionRequest request,
        CancellationToken cancellationToken) =>
        AcceptSuggestionCoreAsync(internalBearerToken, suggestionId, request, cancellationToken);

    private async Task<BusinessConsoleAcceptedResponse> AcceptSuggestionCoreAsync(
        string internalBearerToken,
        string suggestionId,
        BusinessConsoleAcceptPlanningSuggestionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await SendAsync<string>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/planning/suggestions/{Uri.EscapeDataString(suggestionId)}/accept",
            request,
            cancellationToken);
        return new BusinessConsoleAcceptedResponse(
            string.Equals(result, "accepted", StringComparison.OrdinalIgnoreCase));
    }

    private static string PlanningContextQuery(string organizationId, string environmentId) =>
        Query(("organizationId", organizationId), ("environmentId", environmentId));

    private static string MrpRunStatusName(int status) =>
        status switch
        {
            0 => "Created",
            1 => "Running",
            2 => "Completed",
            _ => status.ToString(CultureInfo.InvariantCulture),
        };

    private static string PlanningSuggestionStatusName(int status) =>
        status switch
        {
            0 => "Open",
            1 => "Accepted",
            2 => "Rejected",
            3 => "Closed",
            _ => status.ToString(CultureInfo.InvariantCulture),
        };

    private sealed record DownstreamCreateOrUpdateDemandSourceResponse(string DemandSourceId);

    private sealed record DownstreamRunMrpResponse(string RunId, int SuggestionCount);

    private sealed record DownstreamMrpRunItem(
        string RunId,
        DateOnly HorizonStart,
        DateOnly HorizonEnd,
        int Status,
        int DemandCount,
        int AvailabilityCount,
        int SuggestionCount,
        string ProductionEngineeringSnapshotSource,
        string InventorySnapshotSource);

    private sealed record DownstreamPlanningSuggestionItem(
        string SuggestionId,
        string MrpRunId,
        string SuggestionType,
        string SkuCode,
        string UomCode,
        string SiteCode,
        decimal Quantity,
        DateOnly RequiredDate,
        int Status,
        string ReasonCode);
}

public sealed class HttpBusinessMesClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessMesClient
{
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
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesProductionPlanListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/production-plans?" + ListQuery(request),
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
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/production-plans/{Uri.EscapeDataString(productionPlanId)}/work-orders",
            request,
            cancellationToken);

    public Task<BusinessConsoleMesWorkOrderListResponse> ListWorkOrdersAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesWorkOrderListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/work-orders?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("status", request.Status),
                ("take", request.Take)),
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
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}/release",
            request,
            cancellationToken);

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

    public Task<BusinessConsoleAcceptedResponse> CreateMaterialIssueRequestAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesCreateMaterialIssueRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}/material-issue-requests",
            request,
            cancellationToken);

    public Task<BusinessConsoleMesMaterialIssueRequestListResponse> ListMaterialIssueRequestsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesMaterialIssueRequestListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/material-issue-requests?" + ListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> ConfirmLineSideMaterialReceiptAsync(
        string internalBearerToken,
        string requestId,
        BusinessConsoleMesConfirmLineSideReceiptRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/material-issue-requests/{Uri.EscapeDataString(requestId)}/line-side-receipts",
            request,
            cancellationToken);

    public Task<BusinessConsoleMesDispatchTaskListResponse> ListDispatchTasksAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesDispatchTaskListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/dispatch-tasks?" + ListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> AssignDispatchTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesAssignDispatchTaskRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/dispatch-tasks/{Uri.EscapeDataString(operationTaskId)}/assign",
            request,
            cancellationToken);

    public Task<BusinessConsoleMesOperationTaskListResponse> ListOperationTasksAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesOperationTaskListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/operation-tasks?" + ListQuery(request),
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
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesProductionReportListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/production-reports?" + ListQuery(request),
            null,
            cancellationToken);

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

    public Task<BusinessConsoleRecordProductionReportResponse> RecordProductionReportAsync(
        string internalBearerToken,
        BusinessConsoleRecordProductionReportRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleRecordProductionReportResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/mes/production-reports",
            request,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> RecordDefectAsync(
        string internalBearerToken,
        BusinessConsoleMesRecordDefectRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/mes/defects",
            request,
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
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesReceiptRequestListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/finished-goods-receipt-requests?" + ListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesCreateReceiptResponse> CreateFinishedGoodsReceiptRequestAsync(
        string internalBearerToken,
        BusinessConsoleMesCreateReceiptRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesCreateReceiptResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/mes/finished-goods-receipt-requests",
            request,
            cancellationToken);

    public Task<BusinessConsoleMesDowntimeEventListResponse> ListDowntimeEventsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesDowntimeEventListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/downtime-events?" + ListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> RecordDowntimeEventAsync(
        string internalBearerToken,
        BusinessConsoleMesRecordDowntimeEventRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/mes/downtime-events",
            request,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> ConfirmDowntimeRecoveryAsync(
        string internalBearerToken,
        string downtimeEventId,
        BusinessConsoleMesRecoverDowntimeEventRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/downtime-events/{Uri.EscapeDataString(downtimeEventId)}/recover",
            request,
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
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/mes/shift-handovers",
            request,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> AcceptShiftHandoverAsync(
        string internalBearerToken,
        string handoverId,
        BusinessConsoleMesAcceptShiftHandoverRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/shift-handovers/{Uri.EscapeDataString(handoverId)}/accept",
            request,
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

    private Task<BusinessConsoleMesOperationTaskActionResponse> OperationTaskActionAsync(
        string internalBearerToken,
        string operationTaskId,
        string action,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesOperationTaskActionResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/operation-tasks/{Uri.EscapeDataString(operationTaskId)}/{action}",
            request,
            cancellationToken);

    private static string ContextQuery(string organizationId, string environmentId) =>
        Query(("organizationId", organizationId), ("environmentId", environmentId));

    private static string ListQuery(BusinessConsoleMesListRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("status", request.Status),
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

    private static string FormatTrigger(JsonElement trigger) => trigger.ValueKind switch
    {
        JsonValueKind.String => trigger.GetString() ?? string.Empty,
        JsonValueKind.Number => trigger.GetRawText(),
        _ => trigger.ToString(),
    };
}
