using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;
using Nerv.IIP.Contracts.DemandPlanning;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;

public sealed class HttpPlanningSuggestionDownstreamBridge(
    HttpMesPlanningSuggestionDownstreamBridge mesBridge,
    HttpErpPlanningSuggestionDownstreamBridge erpBridge,
    HttpScheduledReceiptSuggestionDownstreamBridge scheduledReceiptBridge) : IPlanningSuggestionDownstreamBridge
{
    public Task<PlanningSuggestionDownstreamReference> CreateDownstreamAsync(
        PlanningSuggestion suggestion,
        PlanningSuggestionDownstreamRequest request,
        CancellationToken cancellationToken)
    {
        if (HttpScheduledReceiptSuggestionDownstreamBridge.CanHandle(suggestion))
        {
            return scheduledReceiptBridge.CreateDownstreamAsync(suggestion, request, cancellationToken);
        }

        if (HttpMesPlanningSuggestionDownstreamBridge.CanHandle(request, suggestion))
        {
            return mesBridge.CreateDownstreamAsync(suggestion, request, cancellationToken);
        }

        if (HttpErpPlanningSuggestionDownstreamBridge.CanHandle(request, suggestion))
        {
            return erpBridge.CreateDownstreamAsync(suggestion, request, cancellationToken);
        }

        throw new KnownException("计划建议下游创建方式不受支持，请检查下游服务和单据类型。");
    }
}

public sealed class HttpScheduledReceiptSuggestionDownstreamBridge(
    HttpClient erpClient,
    IHttpClientFactory clientFactory,
    IInternalServiceTokenProvider internalTokenProvider) : IPlanningSuggestionDownstreamBridge
{
    public static bool CanHandle(PlanningSuggestion suggestion) =>
        suggestion.SuggestionType is "reschedule-in" or "reschedule-out" or "cancel";

    public async Task<PlanningSuggestionDownstreamReference> CreateDownstreamAsync(
        PlanningSuggestion suggestion,
        PlanningSuggestionDownstreamRequest request,
        CancellationToken cancellationToken)
    {
        var source = suggestion.PeggingLinks.Single(x => x.PeggingType == "scheduled-receipt").DemandSourceReference;
        var erpPrefix = "erp:purchase-order:";
        var mesPrefix = "mes:work-order:";
        string service;
        string documentType;
        string documentId;
        string path;
        object body;
        HttpClient client;
        if (source.StartsWith(erpPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var reference = source[erpPrefix.Length..];
            var separator = reference.LastIndexOf(':');
            if (separator <= 0 || separator == reference.Length - 1)
                throw new KnownException("采购建议缺少采购单行引用。");
            var orderNo = reference[..separator];
            var lineNo = reference[(separator + 1)..];
            service = DemandPlanningDownstreamReferences.BusinessErp;
            documentType = "PurchaseOrderLine";
            documentId = reference;
            path = $"/api/business/v1/erp/purchase-orders/{Uri.EscapeDataString(orderNo)}/lines/{Uri.EscapeDataString(lineNo)}/{(suggestion.SuggestionType == "cancel" ? "cancel" : "reschedule")}";
            body = suggestion.SuggestionType == "cancel"
                ? new { suggestion.OrganizationId, suggestion.EnvironmentId, PurchaseOrderNo = orderNo, LineNo = lineNo, Reason = "计划建议取消在途收货" }
                : new { suggestion.OrganizationId, suggestion.EnvironmentId, PurchaseOrderNo = orderNo, LineNo = lineNo, PromisedDate = suggestion.RequiredDate };
            client = erpClient;
        }
        else if (source.StartsWith(mesPrefix, StringComparison.OrdinalIgnoreCase))
        {
            documentId = source[mesPrefix.Length..];
            service = DemandPlanningDownstreamReferences.BusinessMes;
            documentType = DemandPlanningDownstreamReferences.WorkOrder;
            path = $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(documentId)}/{(suggestion.SuggestionType == "cancel" ? "cancel" : "due-utc")}";
            body = suggestion.SuggestionType == "cancel"
                ? new { suggestion.OrganizationId, suggestion.EnvironmentId, WorkOrderId = documentId, Reason = "计划建议取消在途收货" }
                : new { suggestion.OrganizationId, suggestion.EnvironmentId, WorkOrderId = documentId, DueUtc = new DateTimeOffset(suggestion.RequiredDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) };
            client = clientFactory.CreateClient("planning-mes-command");
        }
        else
        {
            throw new KnownException("计划建议的在途来源不支持写回。");
        }

        if (request.DownstreamService != "ScheduledReceipt" || request.DownstreamDocumentType != "ScheduledReceipt")
            throw new KnownException("计划建议下游目标与在途来源不一致。");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalTokenProvider.BearerToken);
        if (service == DemandPlanningDownstreamReferences.BusinessMes && suggestion.SuggestionType == "cancel")
            httpRequest.Headers.TryAddWithoutValidation("X-Authenticated-Actor", "service:demand-planning");
        using var response = await client.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new KnownException($"{service} 下游写回失败，计划建议仍未接受。");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (payload.RootElement.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
            throw new KnownException($"{service} 下游写回失败，计划建议仍未接受。");
        return new PlanningSuggestionDownstreamReference(service, documentType, documentId);
    }
}

public sealed class HttpMesPlanningSuggestionDownstreamBridge(
    HttpClient httpClient,
    ILogger<HttpMesPlanningSuggestionDownstreamBridge> logger,
    IInternalServiceTokenProvider? internalTokenProvider = null) : IPlanningSuggestionDownstreamBridge
{
    public async Task<PlanningSuggestionDownstreamReference> CreateDownstreamAsync(
        PlanningSuggestion suggestion,
        PlanningSuggestionDownstreamRequest request,
        CancellationToken cancellationToken)
    {
        if (!CanHandle(request, suggestion))
        {
            throw new KnownException("计划建议下游创建方式不受支持，请检查下游服务和单据类型。");
        }

        var productionVersion = suggestion.PeggingLinks
            .Select(x => x.ProductionVersionReference)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        // 合批建议 peg 到多个需求源：完整携带 demand 类型引用，MES 才能为每张订单持久化可追溯关联键；
        // 无 demand 类型 pegging 时回退旧行为（任意非空引用），避免历史数据丢链。
        var demandReferences = suggestion.GetDemandSourceReferences();
        var demandReference = suggestion.GetPrimaryDemandSourceReference();
        var dueUtc = new DateTimeOffset(suggestion.RequiredDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var body = new MesConvertPlanToWorkOrderRequest(
            suggestion.OrganizationId,
            suggestion.EnvironmentId,
            suggestion.Id.ToString(),
            request.DownstreamDocumentId,
            suggestion.SkuCode,
            productionVersion,
            suggestion.Quantity,
            suggestion.UomCode,
            dueUtc,
            null,
            DateTimeOffset.UtcNow,
            DemandPlanningSourceReferences.DemandPlanning,
            DemandPlanningSourceReferences.PlanningSuggestion,
            suggestion.Id.ToString(),
            demandReference,
            request.IdempotencyKey,
            demandReferences,
            request.AssemblyParentSuggestionIds);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/business/v1/mes/production-plans/{Uri.EscapeDataString(suggestion.Id.ToString())}/work-orders")
        {
            Content = JsonContent.Create(body)
        };
        if (!string.IsNullOrWhiteSpace(internalTokenProvider?.BearerToken))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalTokenProvider.BearerToken);
        }

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var diagnostic = await PlanningSuggestionDownstreamDiagnostics.ReadResponseBodyAsync(response, cancellationToken);
            logger.LogWarning(
                "DemandPlanning MES downstream returned HTTP {StatusCode} {ReasonPhrase}; response body: {ResponseBody}",
                (int)response.StatusCode,
                response.ReasonPhrase,
                diagnostic);
            throw new KnownException("MES 下游创建工单失败，请稍后重试。");
        }

        var accepted = await response.Content.ReadFromJsonAsync<MesAcceptedResponse>(cancellationToken);
        if (accepted is null || string.IsNullOrWhiteSpace(accepted.ReferenceId))
        {
            throw new KnownException("MES 未返回工单引用，无法完成计划建议。");
        }

        return new PlanningSuggestionDownstreamReference(
            DemandPlanningDownstreamReferences.BusinessMes,
            DemandPlanningDownstreamReferences.WorkOrder,
            accepted.ReferenceId);
    }

    public static bool CanHandle(PlanningSuggestionDownstreamRequest request, PlanningSuggestion suggestion)
    {
        return string.Equals(suggestion.SuggestionType, DemandPlanningSuggestionTypes.PlannedWorkOrder, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(request.DownstreamService, DemandPlanningDownstreamReferences.BusinessMes, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(request.DownstreamDocumentType, DemandPlanningDownstreamReferences.WorkOrder, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class HttpErpPlanningSuggestionDownstreamBridge(
    HttpClient httpClient,
    ILogger<HttpErpPlanningSuggestionDownstreamBridge> logger,
    IInternalServiceTokenProvider? internalTokenProvider = null) : IPlanningSuggestionDownstreamBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PlanningSuggestionDownstreamReference> CreateDownstreamAsync(
        PlanningSuggestion suggestion,
        PlanningSuggestionDownstreamRequest request,
        CancellationToken cancellationToken)
    {
        if (!CanHandle(request, suggestion))
        {
            throw new KnownException("计划建议下游创建方式不受支持，请检查下游服务和单据类型。");
        }

        var body = new ErpCreatePurchaseRequisitionFromSuggestionRequest(
            suggestion.OrganizationId,
            suggestion.EnvironmentId,
            null,
            suggestion.Id.ToString(),
            suggestion.SkuCode,
            suggestion.UomCode,
            suggestion.SiteCode,
            suggestion.Quantity,
            suggestion.RequiredDate,
            request.IdempotencyKey);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/business/v1/erp/purchase-requisitions/from-suggestion")
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        if (!string.IsNullOrWhiteSpace(internalTokenProvider?.BearerToken))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalTokenProvider.BearerToken);
        }

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var diagnostic = await PlanningSuggestionDownstreamDiagnostics.ReadResponseBodyAsync(response, cancellationToken);
            logger.LogWarning(
                "DemandPlanning ERP downstream returned HTTP {StatusCode} {ReasonPhrase}; response body: {ResponseBody}",
                (int)response.StatusCode,
                response.ReasonPhrase,
                diagnostic);
            throw new KnownException("ERP 下游创建采购申请失败，请稍后重试。");
        }

        var accepted = await ReadResponseDataAsync<ErpPurchaseRequisitionAcceptedResponse>(response, cancellationToken);
        var referenceId = !string.IsNullOrWhiteSpace(accepted.RequisitionNo)
            ? accepted.RequisitionNo
            : accepted.PurchaseRequisitionId;
        if (string.IsNullOrWhiteSpace(referenceId))
        {
            throw new KnownException("ERP 未返回采购申请引用，无法完成计划建议。");
        }

        return new PlanningSuggestionDownstreamReference(
            DemandPlanningDownstreamReferences.BusinessErp,
            DemandPlanningDownstreamReferences.PurchaseRequisition,
            referenceId);
    }

    public static bool CanHandle(PlanningSuggestionDownstreamRequest request, PlanningSuggestion suggestion)
    {
        return string.Equals(suggestion.SuggestionType, DemandPlanningSuggestionTypes.PlannedPurchase, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(request.DownstreamService, DemandPlanningDownstreamReferences.BusinessErp, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(request.DownstreamDocumentType, DemandPlanningDownstreamReferences.PurchaseRequisition, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<T> ReadResponseDataAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new KnownException("ERP 返回空响应，无法完成计划建议。");
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var payload = root.TryGetProperty("data", out var data) ? data : root;
        return payload.Deserialize<T>(JsonOptions)
            ?? throw new KnownException("ERP 未返回采购申请引用，无法完成计划建议。");
    }
}

internal sealed record MesConvertPlanToWorkOrderRequest(
    string OrganizationId,
    string EnvironmentId,
    string ProductionPlanId,
    string? WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    decimal PlannedQuantity,
    string UomCode,
    DateTimeOffset DueUtc,
    string? WorkCenterId,
    DateTimeOffset RequestedAtUtc,
    string SourceSystem,
    string SourceDocumentType,
    string SourceDocumentId,
    string? SourceDemandReference,
    string IdempotencyKey,
    IReadOnlyCollection<string>? SourceDemandReferences = null,
    IReadOnlyCollection<string>? AssemblyParentSuggestionIds = null);

internal sealed record MesAcceptedResponse(string Status, string ReferenceId, DateTimeOffset AcceptedAtUtc);

internal sealed record ErpCreatePurchaseRequisitionFromSuggestionRequest(
    string OrganizationId,
    string EnvironmentId,
    string? RequisitionNo,
    string SuggestionId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal Quantity,
    DateOnly RequiredDate,
    string IdempotencyKey);

internal sealed record ErpPurchaseRequisitionAcceptedResponse(
    string PurchaseRequisitionId,
    string? RequisitionNo = null);

internal static class PlanningSuggestionDownstreamDiagnostics
{
    private const int MaximumResponseBodyLength = 512;

    public static async Task<string> ReadResponseBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return body.Length <= MaximumResponseBodyLength
                ? body
                : body[..MaximumResponseBodyLength] + "…";
        }
        catch (HttpRequestException exception)
        {
            return $"<响应体读取失败：{exception.GetType().Name}>";
        }
        catch (IOException exception)
        {
            return $"<响应体读取失败：{exception.GetType().Name}>";
        }
    }
}
