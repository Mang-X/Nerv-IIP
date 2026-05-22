using System.Text.Json;

namespace Nerv.IIP.Sdk.Core;

public sealed record PlatformApiOptions(Uri BaseAddress, string SdkVersion = "1.0");

public sealed record PlatformRequestContext(
    string OrganizationId,
    string EnvironmentId,
    string CorrelationId,
    string? IdempotencyKey = null,
    string? TraceParent = null);

public sealed record PlatformApiError(string Code, string Message);

public sealed record PlatformApiResult<T>(T? Value, PlatformApiError? Error)
{
    public bool Succeeded => Error is null;

    public static PlatformApiResult<T> Success(T value) => new(value, null);
    public static PlatformApiResult<T> Failure(string code, string message) => new(default, new PlatformApiError(code, message));
}

public static class PlatformApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static HttpRequestMessage CreateRequest(HttpMethod method, string path, PlatformRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.OrganizationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.EnvironmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.CorrelationId);

        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Organization-Id", context.OrganizationId);
        request.Headers.Add("X-Environment-Id", context.EnvironmentId);
        request.Headers.Add("X-Correlation-Id", context.CorrelationId);

        if (!string.IsNullOrWhiteSpace(context.IdempotencyKey))
        {
            request.Headers.Add("X-Idempotency-Key", context.IdempotencyKey);
        }

        if (!string.IsNullOrWhiteSpace(context.TraceParent))
        {
            request.Headers.Add("traceparent", context.TraceParent);
        }

        return request;
    }

    public static async Task<T> ReadResponseDataAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        var content = response.Content
            ?? throw new InvalidOperationException("Platform API returned an empty response.");
        var json = await content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("Platform API returned an empty response.");
        }

        using var document = JsonDocument.Parse(json);
        var payload = document.RootElement.TryGetProperty("data", out var data)
            ? data
            : document.RootElement;

        if (payload.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new InvalidOperationException("Platform API returned an empty response data payload.");
        }

        return payload.Deserialize<T>(JsonOptions)
            ?? throw new InvalidOperationException("Platform API returned an empty response data payload.");
    }
}
