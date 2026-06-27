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
            throw new KnownException("ProductEngineering work center usage check is unavailable.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new KnownException("ProductEngineering work center usage check timed out.", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new KnownException($"ProductEngineering work center usage check failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<ProductEngineeringWorkCenterUsageResponse>>(cancellationToken);
            if (envelope?.Data is null || !envelope.Success)
            {
                throw new KnownException($"ProductEngineering work center usage check failed: {envelope?.Message ?? "empty response"}");
            }

            return new MasterDataDownstreamReferenceUsage(envelope.Data.HasActiveReference, envelope.Data.References ?? []);
        }
    }

    private sealed record ProductEngineeringWorkCenterUsageResponse(bool HasActiveReference, IReadOnlyCollection<string>? References);

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
