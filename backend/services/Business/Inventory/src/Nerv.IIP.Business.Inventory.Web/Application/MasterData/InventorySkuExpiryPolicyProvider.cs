using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Inventory.Web.Application.MasterData;

public sealed record InventorySkuExpiryPolicy(int? ShelfLifeDays, int? NearExpiryThresholdDays);

public interface IInventorySkuExpiryPolicyProvider
{
    Task<InventorySkuExpiryPolicy?> GetAsync(
        string organizationId,
        string environmentId,
        string skuCode,
        CancellationToken cancellationToken);
}

public sealed class HttpInventorySkuExpiryPolicyProvider(
    HttpClient httpClient,
    IInternalServiceTokenProvider internalTokenProvider) : IInventorySkuExpiryPolicyProvider
{
    public async Task<InventorySkuExpiryPolicy?> GetAsync(
        string organizationId,
        string environmentId,
        string skuCode,
        CancellationToken cancellationToken)
    {
        var uri = "/api/business/v1/master-data/resources/sku/"
            + Uri.EscapeDataString(skuCode)
            + "?organizationId="
            + Uri.EscapeDataString(organizationId)
            + "&environmentId="
            + Uri.EscapeDataString(environmentId);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalTokenProvider.BearerToken);
        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if ((int)response.StatusCode >= 500)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<MasterDataSkuDetail>>(cancellationToken);
        return envelope?.Data is null
            ? null
            : new InventorySkuExpiryPolicy(envelope.Data.ShelfLifeDays, envelope.Data.NearExpiryThresholdDays);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
        catch (HttpRequestException)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
    }

    private sealed record MasterDataSkuDetail(int? ShelfLifeDays, int? NearExpiryThresholdDays);

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
