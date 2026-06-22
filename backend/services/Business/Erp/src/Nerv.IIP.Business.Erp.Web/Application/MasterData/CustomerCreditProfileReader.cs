using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Erp.Web.Application.MasterData;

public sealed record CustomerCreditProfile(string CustomerCode, decimal CreditLimit, string CurrencyCode);

public interface ICustomerCreditProfileReader
{
    Task<CustomerCreditProfile?> GetAsync(string organizationId, string environmentId, string customerCode, CancellationToken cancellationToken);
}

public sealed class HttpCustomerCreditProfileReader(
    HttpClient httpClient,
    IInternalServiceTokenProvider internalTokenProvider) : ICustomerCreditProfileReader
{
    public async Task<CustomerCreditProfile?> GetAsync(string organizationId, string environmentId, string customerCode, CancellationToken cancellationToken)
    {
        var path = $"/api/business/v1/master-data/partners/{Uri.EscapeDataString(customerCode)}/credit?organizationId={Uri.EscapeDataString(organizationId)}&environmentId={Uri.EscapeDataString(environmentId)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalTokenProvider.BearerToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<BusinessPartnerCreditProfile>>(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new KnownException(envelope?.Message ?? $"MasterData credit profile lookup failed for customer '{customerCode}'.");
        }

        if (envelope is null || !envelope.Success || envelope.Data is null)
        {
            throw new KnownException(envelope?.Message ?? $"MasterData did not return a credit profile for customer '{customerCode}'.");
        }

        return new CustomerCreditProfile(envelope.Data.CustomerCode, envelope.Data.CreditLimit, envelope.Data.CurrencyCode);
    }

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
