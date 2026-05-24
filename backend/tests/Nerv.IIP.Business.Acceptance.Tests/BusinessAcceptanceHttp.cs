using System.Net.Http.Json;
using System.Text.Json;

namespace Nerv.IIP.Business.Acceptance.Tests;

public static class BusinessAcceptanceHttp
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<TResponse> PostJsonForDataAsync<TRequest, TResponse>(
        HttpClient client,
        string route,
        TRequest request,
        BusinessAcceptanceCorrelation correlation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(correlation);

        using var message = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        ApplyCorrelationHeaders(message, correlation);

        using var response = await client.SendAsync(message, cancellationToken);
        return await ReadDataEnvelopeAsync<TResponse>(response, cancellationToken);
    }

    public static async Task<TResponse> GetJsonForDataAsync<TResponse>(
        HttpClient client,
        string route,
        BusinessAcceptanceCorrelation correlation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(correlation);

        using var message = new HttpRequestMessage(HttpMethod.Get, route);
        ApplyCorrelationHeaders(message, correlation);

        using var response = await client.SendAsync(message, cancellationToken);
        return await ReadDataEnvelopeAsync<TResponse>(response, cancellationToken);
    }

    public static async Task<TResponse> ReadDataEnvelopeAsync<TResponse>(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<BusinessAcceptanceResponseEnvelope<TResponse>>(
            JsonOptions,
            cancellationToken);

        if (envelope is null)
        {
            throw new InvalidOperationException("The acceptance response body was empty or not a valid JSON envelope.");
        }

        if (!envelope.Success)
        {
            throw new InvalidOperationException($"The acceptance response envelope failed with code {envelope.Code}: {envelope.Message}");
        }

        return envelope.Data ?? throw new InvalidOperationException("The acceptance response envelope did not contain data.");
    }

    private static void ApplyCorrelationHeaders(HttpRequestMessage message, BusinessAcceptanceCorrelation correlation)
    {
        // Request headers make correlation explicit even when a reused client has fixture defaults.
        message.Headers.Remove("X-Correlation-Id");
        message.Headers.Add("X-Correlation-Id", correlation.CorrelationId);
        message.Headers.Remove("X-Organization-Id");
        message.Headers.Add("X-Organization-Id", correlation.OrganizationId);
        message.Headers.Remove("X-Environment-Id");
        message.Headers.Add("X-Environment-Id", correlation.EnvironmentId);
    }
}

public sealed record BusinessAcceptanceResponseEnvelope<T>(
    T? Data,
    bool Success,
    string Message,
    int Code);
