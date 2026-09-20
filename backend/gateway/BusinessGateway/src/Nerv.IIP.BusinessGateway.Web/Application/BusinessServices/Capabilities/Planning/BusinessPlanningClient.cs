using System.Globalization;
using System.Text.Json;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;


public interface IBusinessPlanningClient
{
    Task<BusinessConsoleMpsBucketListResponse> ListMpsBucketsAsync(
        string internalBearerToken,
        BusinessConsoleMpsListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMpsBucketItem> CreateMpsBucketAsync(
        string internalBearerToken,
        BusinessConsoleCreateMpsBucketRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMpsBucketItem> UpdateMpsBucketAsync(
        string internalBearerToken,
        string mpsId,
        BusinessConsoleUpdateMpsBucketRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMpsBucketItem> ReviewMpsBucketAsync(
        string internalBearerToken,
        string mpsId,
        BusinessConsoleReviewMpsBucketRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMpsBucketItem> ReleaseMpsBucketAsync(
        string internalBearerToken,
        string mpsId,
        BusinessConsoleReleaseMpsBucketRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleDemandSourceListResponse> ListDemandSourcesAsync(
        string internalBearerToken,
        BusinessConsoleDemandSourceListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleDemandSourceResponse> CreateOrUpdateDemandSourceAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateDemandSourceRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> CancelDemandSourceAsync(
        string internalBearerToken,
        string demandSourceId,
        BusinessConsolePlanningDemandCancelRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleForecastInputListResponse> ListForecastInputsAsync(
        string internalBearerToken,
        BusinessConsoleForecastInputListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleForecastInputItem> CreateOrUpdateForecastInputAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateForecastInputRequest request,
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

    Task<BusinessConsolePlanningSuggestionRejectedResponse> RejectSuggestionAsync(
        string internalBearerToken,
        string suggestionId,
        string rejectedBy,
        BusinessConsoleRejectPlanningSuggestionRequest request,
        CancellationToken cancellationToken);
}
public sealed class HttpBusinessPlanningClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessPlanningClient
{
    public Task<BusinessConsoleMpsBucketListResponse> ListMpsBucketsAsync(
        string internalBearerToken,
        BusinessConsoleMpsListRequest request,
        CancellationToken cancellationToken) =>
        ListMpsBucketsCoreAsync(internalBearerToken, request, cancellationToken);

    private async Task<BusinessConsoleMpsBucketListResponse> ListMpsBucketsCoreAsync(
        string internalBearerToken,
        BusinessConsoleMpsListRequest request,
        CancellationToken cancellationToken)
    {
        var items = await SendAsync<IReadOnlyCollection<DownstreamMpsBucketItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/planning/mps?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("siteCode", request.SiteCode),
                ("fromDate", request.FromDate),
                ("toDate", request.ToDate),
                ("status", request.Status)),
            null,
            cancellationToken);
        return new BusinessConsoleMpsBucketListResponse(items.Select(ToBusinessConsoleMpsBucket).ToArray());
    }

    public async Task<BusinessConsoleMpsBucketItem> CreateMpsBucketAsync(
        string internalBearerToken,
        BusinessConsoleCreateMpsBucketRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamMpsBucketItem>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/planning/mps",
            request,
            cancellationToken);
        return ToBusinessConsoleMpsBucket(response);
    }

    public async Task<BusinessConsoleMpsBucketItem> UpdateMpsBucketAsync(
        string internalBearerToken,
        string mpsId,
        BusinessConsoleUpdateMpsBucketRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamMpsBucketItem>(
            internalBearerToken,
            HttpMethod.Put,
            $"/api/business/v1/planning/mps/{Uri.EscapeDataString(mpsId)}",
            request,
            cancellationToken);
        return ToBusinessConsoleMpsBucket(response);
    }

    public async Task<BusinessConsoleMpsBucketItem> ReviewMpsBucketAsync(
        string internalBearerToken,
        string mpsId,
        BusinessConsoleReviewMpsBucketRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamMpsBucketItem>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/planning/mps/{Uri.EscapeDataString(mpsId)}/review?" + PlanningContextQuery(request.OrganizationId, request.EnvironmentId),
            request,
            cancellationToken);
        return ToBusinessConsoleMpsBucket(response);
    }

    public async Task<BusinessConsoleMpsBucketItem> ReleaseMpsBucketAsync(
        string internalBearerToken,
        string mpsId,
        BusinessConsoleReleaseMpsBucketRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamMpsBucketItem>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/planning/mps/{Uri.EscapeDataString(mpsId)}/release?" + PlanningContextQuery(request.OrganizationId, request.EnvironmentId),
            request,
            cancellationToken);
        return ToBusinessConsoleMpsBucket(response);
    }

    public Task<BusinessConsoleDemandSourceListResponse> ListDemandSourcesAsync(
        string internalBearerToken,
        BusinessConsoleDemandSourceListRequest request,
        CancellationToken cancellationToken) =>
        ListDemandSourcesCoreAsync(internalBearerToken, request, cancellationToken);

    private async Task<BusinessConsoleDemandSourceListResponse> ListDemandSourcesCoreAsync(
        string internalBearerToken,
        BusinessConsoleDemandSourceListRequest request,
        CancellationToken cancellationToken)
    {
        var items = await SendAsync<IReadOnlyCollection<BusinessConsoleDemandSourceResponse>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/planning/demands?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("keyword", request.Keyword),
                ("skip", request.Skip),
                ("take", request.Take)),
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
            string.Empty,
            string.Empty,
            0,
            "active",
            request.SkuCode,
            request.UomCode,
            request.SiteCode,
            request.Quantity,
            request.DueDate);
    }

    public async Task<BusinessConsoleAcceptedResponse> CancelDemandSourceAsync(
        string internalBearerToken,
        string demandSourceId,
        BusinessConsolePlanningDemandCancelRequest request,
        CancellationToken cancellationToken)
    {
        await SendAsync<string>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/planning/demands/{Uri.EscapeDataString(demandSourceId)}/cancel?" + PlanningContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);
        return new BusinessConsoleAcceptedResponse(true);
    }

    public Task<BusinessConsoleForecastInputListResponse> ListForecastInputsAsync(
        string internalBearerToken,
        BusinessConsoleForecastInputListRequest request,
        CancellationToken cancellationToken) =>
        ListForecastInputsCoreAsync(internalBearerToken, request, cancellationToken);

    private async Task<BusinessConsoleForecastInputListResponse> ListForecastInputsCoreAsync(
        string internalBearerToken,
        BusinessConsoleForecastInputListRequest request,
        CancellationToken cancellationToken)
    {
        var items = await SendAsync<IReadOnlyCollection<BusinessConsoleForecastInputItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/planning/forecasts?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("siteCode", request.SiteCode),
                ("fromDate", request.FromDate),
                ("toDate", request.ToDate)),
            null,
            cancellationToken);
        return new BusinessConsoleForecastInputListResponse(items);
    }

    public async Task<BusinessConsoleForecastInputItem> CreateOrUpdateForecastInputAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateForecastInputRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateOrUpdateForecastInputResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/planning/forecasts",
            request,
            cancellationToken);
        return new BusinessConsoleForecastInputItem(
            response.ForecastInputId,
            response.ForecastReference,
            request.SkuCode,
            request.UomCode,
            request.SiteCode,
            request.PeriodStartDate,
            request.PeriodEndDate,
            request.Quantity,
            request.BackwardConsumptionDays,
            request.ForwardConsumptionDays);
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
        return new BusinessConsoleRunMrpResponse(
            response.RunId,
            MrpRunStatusName(response.Status));
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
            x.InventorySnapshotSource,
            x.HasInputDegradation,
            x.InputDegradationSources ?? [],
            x.InputSources ?? [],
            x.InputCoverageStart,
            x.InputCoverageEnd,
            x.FailureReason)).ToArray());
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
            x.ReasonCode,
            x.NetRequirementExplanation is null
                ? null
                : new BusinessConsoleNetRequirementExplanation(
                    x.NetRequirementExplanation.GrossDemandQuantity,
                    x.NetRequirementExplanation.OnHandQuantity,
                    x.NetRequirementExplanation.ReservedQuantity,
                    x.NetRequirementExplanation.AvailableToNetQuantity,
                    x.NetRequirementExplanation.ScheduledReceiptQuantity,
                    x.NetRequirementExplanation.SafetyStockQuantity,
                    x.NetRequirementExplanation.NetRequirementQuantity,
                    x.NetRequirementExplanation.PlannedQuantity,
                    x.NetRequirementExplanation.ScrapRate,
                    x.NetRequirementExplanation.YieldRate,
                    x.NetRequirementExplanation.PrimarySourceType,
                    x.NetRequirementExplanation.Formula,
                    x.NetRequirementExplanation.UomConversions ?? [],
                    x.NetRequirementExplanation.DegradationSources ?? []),
            x.AcceptedDownstreamService,
            x.AcceptedDownstreamDocumentType,
            x.AcceptedDownstreamDocumentId)).ToArray());
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
        return await SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/planning/suggestions/{Uri.EscapeDataString(suggestionId)}/accept",
            request,
            cancellationToken);
    }

    public Task<BusinessConsolePlanningSuggestionRejectedResponse> RejectSuggestionAsync(
        string internalBearerToken,
        string suggestionId,
        string rejectedBy,
        BusinessConsoleRejectPlanningSuggestionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsolePlanningSuggestionRejectedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/planning/suggestions/{Uri.EscapeDataString(suggestionId)}/reject",
            new DownstreamRejectPlanningSuggestionRequest(suggestionId, rejectedBy, request.Reason),
            cancellationToken);

    private sealed record DownstreamRejectPlanningSuggestionRequest(
        string SuggestionId,
        string RejectedBy,
        string Reason);

    private static string PlanningContextQuery(string organizationId, string environmentId) =>
        Query(("organizationId", organizationId), ("environmentId", environmentId));

    private static BusinessConsoleMpsBucketItem ToBusinessConsoleMpsBucket(DownstreamMpsBucketItem item) =>
        new(
            item.MpsId,
            item.SkuCode,
            item.UomCode,
            item.SiteCode,
            item.BucketDate,
            item.Quantity,
            MpsStatusName(item.Status),
            item.ReviewedBy,
            item.ReviewedAtUtc,
            item.ReleasedBy,
            item.ReleasedAtUtc);

    private static string MpsStatusName(JsonElement status) => status.ValueKind switch
    {
        JsonValueKind.Number => status.GetInt32() switch
        {
            0 => "Draft",
            1 => "Reviewed",
            2 => "Released",
            var value => value.ToString(CultureInfo.InvariantCulture),
        },
        JsonValueKind.String => status.GetString() ?? string.Empty,
        _ => status.ToString(),
    };

    private static string MrpRunStatusName(int status) =>
        status switch
        {
            0 => "Created",
            1 => "Running",
            2 => "Completed",
            3 => "Failed",
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

    private sealed record DownstreamCreateOrUpdateForecastInputResponse(
        string ForecastInputId,
        string ForecastReference);

    private sealed record DownstreamMpsBucketItem(
        string MpsId,
        string SkuCode,
        string UomCode,
        string SiteCode,
        DateOnly BucketDate,
        decimal Quantity,
        JsonElement Status,
        string? ReviewedBy,
        DateTimeOffset? ReviewedAtUtc,
        string? ReleasedBy,
        DateTimeOffset? ReleasedAtUtc);

    private sealed record DownstreamRunMrpResponse(
        string RunId,
        int Status);

    private sealed record DownstreamMrpRunItem(
        string RunId,
        DateOnly HorizonStart,
        DateOnly HorizonEnd,
        int Status,
        int DemandCount,
        int AvailabilityCount,
        int SuggestionCount,
        string ProductionEngineeringSnapshotSource,
        string InventorySnapshotSource,
        bool HasInputDegradation,
        IReadOnlyCollection<string>? InputDegradationSources,
        IReadOnlyCollection<string>? InputSources,
        DateOnly? InputCoverageStart,
        DateOnly? InputCoverageEnd,
        string? FailureReason);

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
        string ReasonCode,
        string? AcceptedDownstreamService,
        string? AcceptedDownstreamDocumentType,
        string? AcceptedDownstreamDocumentId,
        DownstreamNetRequirementExplanation? NetRequirementExplanation);

    private sealed record DownstreamNetRequirementExplanation(
        decimal GrossDemandQuantity,
        decimal OnHandQuantity,
        decimal ReservedQuantity,
        decimal AvailableToNetQuantity,
        decimal ScheduledReceiptQuantity,
        decimal SafetyStockQuantity,
        decimal NetRequirementQuantity,
        decimal PlannedQuantity,
        decimal ScrapRate,
        decimal YieldRate,
        string PrimarySourceType,
        string Formula,
        IReadOnlyCollection<string>? UomConversions,
        IReadOnlyCollection<string>? DegradationSources);
}
