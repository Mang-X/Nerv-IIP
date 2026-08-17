using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;

public sealed record MasterDataDownstreamReferenceUsage(bool HasActiveReference, IReadOnlyCollection<string> References);

public interface IMasterDataDownstreamReferenceChecker
{
    Task<MasterDataDownstreamReferenceUsage> GetWorkCenterUsageAsync(
        string organizationId,
        string environmentId,
        string workCenterCode,
        CancellationToken cancellationToken);
}

public sealed class NullMasterDataDownstreamReferenceChecker : IMasterDataDownstreamReferenceChecker
{
    public static readonly NullMasterDataDownstreamReferenceChecker Instance = new();

    private NullMasterDataDownstreamReferenceChecker()
    {
    }

    public Task<MasterDataDownstreamReferenceUsage> GetWorkCenterUsageAsync(
        string organizationId,
        string environmentId,
        string workCenterCode,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new MasterDataDownstreamReferenceUsage(false, []));
    }
}

public sealed class HttpProductEngineeringReferenceUsageChecker(
    HttpClient httpClient,
    IInternalServiceTokenProvider internalTokenProvider) : IMasterDataDownstreamReferenceChecker
{
    public async Task<MasterDataDownstreamReferenceUsage> GetWorkCenterUsageAsync(
        string organizationId,
        string environmentId,
        string workCenterCode,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/business/v1/engineering/internal/master-data/work-centers/{Uri.EscapeDataString(workCenterCode)}/usage?organizationId={Uri.EscapeDataString(organizationId)}&environmentId={Uri.EscapeDataString(environmentId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalTokenProvider.BearerToken);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new KnownException("暂时无法检查 ProductEngineering 中的工作中心使用情况，请稍后重试。", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new KnownException("检查 ProductEngineering 中的工作中心使用情况超时，请稍后重试。", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw DownstreamFailure(new HttpRequestException(
                    $"ProductEngineering work center usage check failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                    null,
                    response.StatusCode));
            }

            var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<ProductEngineeringWorkCenterUsageResponse>>(cancellationToken);
            if (envelope?.Data is null || !envelope.Success)
            {
                throw DownstreamFailure(new InvalidOperationException(
                    $"ProductEngineering work center usage response was invalid. Code={envelope?.Code}; Message={envelope?.Message ?? "empty response"}"));
            }

            return new MasterDataDownstreamReferenceUsage(envelope.Data.HasActiveReference, envelope.Data.References ?? []);
        }
    }

    private static KnownException DownstreamFailure(Exception diagnostic) =>
        new KnownException(
            "无法确认 ProductEngineering 工作中心使用情况，已取消停用操作。请联系管理员。",
            diagnostic);

    private sealed record ProductEngineeringWorkCenterUsageResponse(bool HasActiveReference, IReadOnlyCollection<string>? References);

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
