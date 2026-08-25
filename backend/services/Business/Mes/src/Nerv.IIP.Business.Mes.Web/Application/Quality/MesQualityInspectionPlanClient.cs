using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Contracts.ProductEngineering;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Mes.Web.Application.Quality;

public sealed class MesQualityHttpClient(HttpClient httpClient)
{
    public HttpClient HttpClient { get; } = httpClient;
}

public interface IMesQualityInspectionPlanReader
{
    Task<bool> HasActiveOperationPlanAsync(
        string organizationId,
        string environmentId,
        string skuCode,
        string? workCenterId,
        CancellationToken cancellationToken);
}

public sealed class MesQualityInspectionPlanClient(
    MesQualityHttpClient qualityClient,
    IInternalServiceTokenProvider? internalTokenProvider = null) : IMesQualityInspectionPlanReader
{
    public async Task<bool> HasActiveOperationPlanAsync(
        string organizationId,
        string environmentId,
        string skuCode,
        string? workCenterId,
        CancellationToken cancellationToken)
    {
        var query = new List<string>
        {
            $"organizationId={Uri.EscapeDataString(organizationId)}",
            $"environmentId={Uri.EscapeDataString(environmentId)}",
            "category=operation",
            $"skuCode={Uri.EscapeDataString(skuCode)}",
            $"status={Uri.EscapeDataString(ProductionEngineeringContractStatuses.Active)}",
            "skip=0",
            "take=1",
        };
        if (!string.IsNullOrWhiteSpace(workCenterId))
        {
            query.Add($"workCenterId={Uri.EscapeDataString(workCenterId)}");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/business/v1/quality/inspection-plans?" + string.Join('&', query));
        if (!string.IsNullOrWhiteSpace(internalTokenProvider?.BearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalTokenProvider.BearerToken);
        }

        HttpResponseMessage response;
        try
        {
            response = await qualityClient.HttpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new KnownException($"QUALITY_PLAN_SOURCE_UNAVAILABLE: Quality 检验方案来源服务暂不可用。{exception.Message}");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new KnownException($"QUALITY_PLAN_SOURCE_UNAVAILABLE: Quality 检验方案来源服务请求超时。{exception.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new KnownException(
                    $"QUALITY_PLAN_SOURCE_UNAVAILABLE: Quality 检验方案来源服务返回 {(int)response.StatusCode} {response.ReasonPhrase}。结果无法可靠确定。");
            }

            var envelope = await response.Content
                .ReadFromJsonAsync<MesQualityResponseDataEnvelope<MesQualityInspectionPlanListResponse>>(cancellationToken);
            if (envelope is null || !envelope.Success)
            {
                throw new KnownException("QUALITY_PLAN_SOURCE_UNAVAILABLE: Quality 检验方案来源服务返回空响应或失败响应。结果无法可靠确定。");
            }

            var data = envelope?.Data;
            return data is not null &&
                data.Items.Any(x =>
                    string.Equals(x.OrganizationId, organizationId, StringComparison.Ordinal) &&
                    string.Equals(x.EnvironmentId, environmentId, StringComparison.Ordinal) &&
                    string.Equals(x.Status, ProductionEngineeringContractStatuses.Active, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.SkuCode, skuCode, StringComparison.Ordinal) &&
                    string.Equals(x.Category, "operation", StringComparison.Ordinal) &&
                    string.Equals(x.WorkCenterId, workCenterId, StringComparison.Ordinal));
        }
    }
}

public sealed record MesQualityInspectionPlanListResponse(
    IReadOnlyCollection<MesQualityInspectionPlanItem> Items,
    int Total);

public sealed record MesQualityInspectionPlanItem(
    string OrganizationId,
    string EnvironmentId,
    string Status,
    string PlanCode,
    string? SkuCode,
    string? Category,
    string? WorkCenterId);

internal sealed record MesQualityResponseDataEnvelope<T>(
    T? Data,
    bool Success,
    string Message,
    int Code);
