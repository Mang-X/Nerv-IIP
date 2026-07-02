using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;
using Nerv.IIP.Contracts.DemandPlanning;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;

public sealed class HttpPlanningSuggestionDownstreamBridge(
    HttpMesPlanningSuggestionDownstreamBridge mesBridge,
    HttpErpPlanningSuggestionDownstreamBridge erpBridge) : IPlanningSuggestionDownstreamBridge
{
    public Task<PlanningSuggestionDownstreamReference> CreateDownstreamAsync(
        PlanningSuggestion suggestion,
        PlanningSuggestionDownstreamRequest request,
        CancellationToken cancellationToken)
    {
        if (HttpMesPlanningSuggestionDownstreamBridge.CanHandle(request, suggestion))
        {
            return mesBridge.CreateDownstreamAsync(suggestion, request, cancellationToken);
        }

        if (HttpErpPlanningSuggestionDownstreamBridge.CanHandle(request, suggestion))
        {
            return erpBridge.CreateDownstreamAsync(suggestion, request, cancellationToken);
        }

        throw new KnownException($"Planning suggestion downstream bridge is not supported for {request.DownstreamService}/{request.DownstreamDocumentType}.");
    }
}

public sealed class HttpMesPlanningSuggestionDownstreamBridge(
    HttpClient httpClient,
    IInternalServiceTokenProvider? internalTokenProvider = null) : IPlanningSuggestionDownstreamBridge
{
    public async Task<PlanningSuggestionDownstreamReference> CreateDownstreamAsync(
        PlanningSuggestion suggestion,
        PlanningSuggestionDownstreamRequest request,
        CancellationToken cancellationToken)
    {
        if (!CanHandle(request, suggestion))
        {
            throw new KnownException($"Planning suggestion downstream bridge is not supported for {request.DownstreamService}/{request.DownstreamDocumentType}.");
        }

        var productionVersion = suggestion.PeggingLinks
            .Select(x => x.ProductionVersionReference)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        var demandReference = suggestion.PeggingLinks
            .Select(x => x.DemandSourceReference)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
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
            request.IdempotencyKey);

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
            var diagnostic = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new KnownException(
                $"MES rejected planning suggestion downstream creation with HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {TrimDiagnostic(diagnostic)}");
        }

        var accepted = await response.Content.ReadFromJsonAsync<MesAcceptedResponse>(cancellationToken);
        if (accepted is null || string.IsNullOrWhiteSpace(accepted.ReferenceId))
        {
            throw new KnownException("MES did not return a work order reference for the accepted planning suggestion.");
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

    private static string TrimDiagnostic(string diagnostic)
    {
        diagnostic = string.IsNullOrWhiteSpace(diagnostic) ? "empty response body" : diagnostic.Trim();
        return diagnostic.Length <= 500 ? diagnostic : diagnostic[..500];
    }
}

public sealed class HttpErpPlanningSuggestionDownstreamBridge(
    HttpClient httpClient,
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
            throw new KnownException($"Planning suggestion downstream bridge is not supported for {request.DownstreamService}/{request.DownstreamDocumentType}.");
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
            var diagnostic = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new KnownException(
                $"ERP rejected planning suggestion downstream creation with HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {TrimDiagnostic(diagnostic)}");
        }

        var accepted = await ReadResponseDataAsync<ErpPurchaseRequisitionAcceptedResponse>(response, cancellationToken);
        var referenceId = !string.IsNullOrWhiteSpace(accepted.RequisitionNo)
            ? accepted.RequisitionNo
            : accepted.PurchaseRequisitionId;
        if (string.IsNullOrWhiteSpace(referenceId))
        {
            throw new KnownException("ERP did not return a purchase requisition reference for the accepted planning suggestion.");
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
            throw new KnownException("ERP returned an empty response for the accepted planning suggestion.");
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var payload = root.TryGetProperty("data", out var data) ? data : root;
        return payload.Deserialize<T>(JsonOptions)
            ?? throw new KnownException("ERP did not return a purchase requisition reference for the accepted planning suggestion.");
    }

    private static string TrimDiagnostic(string diagnostic)
    {
        diagnostic = string.IsNullOrWhiteSpace(diagnostic) ? "empty response body" : diagnostic.Trim();
        return diagnostic.Length <= 500 ? diagnostic : diagnostic[..500];
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
    string IdempotencyKey);

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
