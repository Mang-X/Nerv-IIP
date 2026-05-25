using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

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

    public static async Task<string> PostJsonForLiveHttpAsync<TRequest>(
        HttpClient client,
        string route,
        TRequest request,
        BusinessAcceptanceCorrelation correlation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(correlation);

        using var message = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        ApplyCorrelationHeaders(message, correlation);

        using var response = await client.SendAsync(message, cancellationToken);
        return await ReadSuccessfulBodyAsync(response, cancellationToken);
    }

    public static async Task<string> GetJsonForLiveHttpAsync(
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
        return await ReadSuccessfulBodyAsync(response, cancellationToken);
    }

    public static string RequiredJsonValue(string json, string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        var node = JsonNode.Parse(json)
            ?? throw new InvalidOperationException("The live HTTP response was not valid JSON.");

        var value = FindValue(node, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"The live HTTP response did not contain '{propertyName}': {json}");
        }

        return value;
    }

    private static async Task<string> ReadSuccessfulBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Live HTTP acceptance call failed with {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}",
                null,
                response.StatusCode);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException("Live HTTP acceptance call succeeded but returned an empty response body.");
        }

        return body;
    }

    private static string? FindValue(JsonNode node, string propertyName)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var pair in jsonObject)
            {
                if (string.Equals(pair.Key, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return ExtractString(pair.Value);
                }

                if (pair.Value is not null)
                {
                    var nested = FindValue(pair.Value, propertyName);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }
            }
        }

        if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                if (item is null)
                {
                    continue;
                }

                var nested = FindValue(item, propertyName);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string? ExtractString(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            return value.ToString();
        }

        return node["value"]?.ToString()
            ?? node["id"]?.ToString()
            ?? node.ToJsonString(JsonOptions);
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
