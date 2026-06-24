using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;

public sealed class HttpMesPlanningSuggestionDownstreamBridge(
    HttpClient httpClient,
    IInternalServiceTokenProvider? internalTokenProvider = null) : IPlanningSuggestionDownstreamBridge
{
    public async Task<PlanningSuggestionDownstreamReference> CreateDownstreamAsync(
        PlanningSuggestion suggestion,
        PlanningSuggestionDownstreamRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsMesWorkOrder(request, suggestion))
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
            "DemandPlanning",
            "PlanningSuggestion",
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
        response.EnsureSuccessStatusCode();
        var accepted = await response.Content.ReadFromJsonAsync<MesAcceptedResponse>(cancellationToken);
        if (accepted is null || string.IsNullOrWhiteSpace(accepted.ReferenceId))
        {
            throw new KnownException("MES did not return a work order reference for the accepted planning suggestion.");
        }

        return new PlanningSuggestionDownstreamReference("BusinessMes", "WorkOrder", accepted.ReferenceId);
    }

    private static bool IsMesWorkOrder(PlanningSuggestionDownstreamRequest request, PlanningSuggestion suggestion)
    {
        return string.Equals(suggestion.SuggestionType, "planned-work-order", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(request.DownstreamService, "BusinessMes", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(request.DownstreamDocumentType, "WorkOrder", StringComparison.OrdinalIgnoreCase);
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
