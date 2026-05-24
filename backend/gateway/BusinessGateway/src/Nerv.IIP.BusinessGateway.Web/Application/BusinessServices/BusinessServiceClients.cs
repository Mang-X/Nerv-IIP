using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

public interface IBusinessMesClient
{
    Task<BusinessConsoleMesWorkOrderListResponse> ListWorkOrdersAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateRushWorkOrderResponse> CreateRushWorkOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateRushWorkOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesScheduleResult> RunScheduleAsync(
        string internalBearerToken,
        BusinessConsoleRunScheduleRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleRecordProductionReportResponse> RecordProductionReportAsync(
        string internalBearerToken,
        BusinessConsoleRecordProductionReportRequest request,
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
            request.Content = JsonContent.Create(body);
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
                ("includeDisabled", request.IncludeDisabled),
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
            request,
            cancellationToken);
}

public sealed class HttpBusinessQualityClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessQualityClient
{
    public Task<BusinessConsoleQualityListResponse> ListInspectionPlansAsync(
        string internalBearerToken,
        BusinessConsoleQualityListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualityListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/inspection-plans?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("status", request.Status),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleCreateInspectionRecordResponse> CreateInspectionRecordAsync(
        string internalBearerToken,
        BusinessConsoleCreateInspectionRecordRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateInspectionRecordResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/quality/inspection-records",
            request,
            cancellationToken);

    public Task<BusinessConsoleQualityListResponse> ListNcrsAsync(
        string internalBearerToken,
        BusinessConsoleQualityListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualityListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/ncrs?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("status", request.Status),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> SubmitNcrDispositionAsync(
        string internalBearerToken,
        string ncrId,
        BusinessConsoleNcrDispositionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/quality/ncrs/{Uri.EscapeDataString(ncrId)}/disposition",
            request,
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
            request,
            cancellationToken);
}

public sealed class HttpBusinessMesClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessMesClient
{
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

    public Task<BusinessConsoleCreateRushWorkOrderResponse> CreateRushWorkOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateRushWorkOrderRequest request,
        CancellationToken cancellationToken) =>
        CreateRushWorkOrderCoreAsync(internalBearerToken, request, cancellationToken);

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
