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

        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("X-Organization-Id", context.OrganizationId);
        request.Headers.TryAddWithoutValidation("X-Environment-Id", context.EnvironmentId);
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", context.CorrelationId);

        if (!string.IsNullOrWhiteSpace(context.IdempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("X-Idempotency-Key", context.IdempotencyKey);
        }

        if (!string.IsNullOrWhiteSpace(context.TraceParent))
        {
            request.Headers.TryAddWithoutValidation("traceparent", context.TraceParent);
        }

        return request;
    }

    public static async Task<T> ReadResponseDataAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
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
