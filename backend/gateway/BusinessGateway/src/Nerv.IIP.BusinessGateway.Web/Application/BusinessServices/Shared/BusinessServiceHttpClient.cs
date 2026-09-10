using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public abstract class BusinessServiceHttpClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> RegisteredSharedLegacySemanticCodes = new(StringComparer.Ordinal)
    {
        "idempotency-conflict",
        "lifecycle-conflict",
    };

    protected virtual bool IsRegisteredLegacySemanticCode(string? code) =>
        code is not null && RegisteredSharedLegacySemanticCodes.Contains(code);

    /// <summary>
    /// 下游错误载体是否为裸 <c>{code,message}</c>（不套平台 ResponseData envelope）。
    /// 默认 <c>false</c>：只认 <c>success:false</c> envelope。FileStorage 直接 <c>WriteAsJsonAsync</c>
    /// 写出 <c>FileStorageError</c>，不选入的话非 400 响应的 code 与 message 会双双落空。
    /// 这里只描述**载体形状**，不承载任何下游领域规则。
    /// </summary>
    protected virtual bool AcceptsBareDownstreamErrorPayload => false;

    protected async Task<TResponse> SendAsync<TResponse>(
        string internalBearerToken,
        HttpMethod method,
        string requestUri,
        object? body,
        CancellationToken cancellationToken,
        JsonSerializerOptions? jsonOptions = null,
        Action<HttpRequestMessage>? configureRequest = null,
        bool failClosedOnFailureEnvelope = false)
    {
        using var response = await SendRequestAsync(
            internalBearerToken,
            method,
            requestUri,
            body,
            cancellationToken,
            jsonOptions,
            configureRequest);
        try
        {
            return await ReadResponseDataAsync<TResponse>(
                response,
                jsonOptions ?? JsonOptions,
                cancellationToken,
                failClosedOnFailureEnvelope);
        }
        catch (JsonException ex)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response",
                ex);
        }
        catch (InvalidOperationException ex)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response",
                ex);
        }
    }

    protected async Task SendNoContentAsync(
        string internalBearerToken,
        HttpMethod method,
        string requestUri,
        object? body,
        CancellationToken cancellationToken,
        JsonSerializerOptions? jsonOptions = null,
        Action<HttpRequestMessage>? configureRequest = null)
    {
        using var response = await SendRequestAsync(
            internalBearerToken,
            method,
            requestUri,
            body,
            cancellationToken,
            jsonOptions,
            configureRequest);
        if (response.StatusCode != HttpStatusCode.NoContent)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }
    }

    private async Task<HttpResponseMessage> SendRequestAsync(
        string internalBearerToken,
        HttpMethod method,
        string requestUri,
        object? body,
        CancellationToken cancellationToken,
        JsonSerializerOptions? jsonOptions,
        Action<HttpRequestMessage>? configureRequest)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalBearerToken);
        var idempotencyKey = BusinessGatewayIdempotencyKey.FromBody(body);
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        configureRequest?.Invoke(request);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: jsonOptions ?? JsonOptions);
        }

        var response = await SendRawAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var downstreamMessage = await ReadDownstreamEnvelopeMessageAsync(
                    response,
                    cancellationToken);
                throw BusinessServiceProxyException.FromDownstreamBusinessMessage(downstreamMessage);
            }

            var envelope = await ReadDownstreamErrorEnvelopeAsync(response, cancellationToken);
            throw BusinessServiceProxyException.FromDownstreamError(
                response.StatusCode,
                envelope.SemanticCode,
                envelope.Message,
                envelope.ErrorData,
                envelope.AllowMessageAsSemanticCode);
        }
    }

    /// <summary>
    /// 唯一一份下游传输故障映射：JSON 面（<see cref="SendAsync{TResponse}"/>）与字节代理面共用。
    /// 字节面需要 <see cref="HttpCompletionOption.ResponseHeadersRead"/> 才能流式转发，
    /// 因此这里暴露 completionOption；异常口径不允许在 capability client 里另抄一份。
    /// </summary>
    protected async Task<HttpResponseMessage> SendRawAsync(
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(request, completionOption, cancellationToken);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.ServiceUnavailable,
                "downstream-timeout",
                ex);
        }
        catch (Polly.Timeout.TimeoutRejectedException ex)
        {
            // 网关 resilience 管道超时（#1306 修法 3）：不再逃逸成 500/「未知错误」，
            // 以 504 + downstream-timeout 透传，前端映射为可行动提示（任务可能仍在处理）。
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.GatewayTimeout,
                "downstream-timeout",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.ServiceUnavailable,
                "downstream-unavailable",
                ex);
        }
    }

    private static async Task<TResponse> ReadResponseDataAsync<TResponse>(
        HttpResponseMessage response,
        JsonSerializerOptions jsonOptions,
        CancellationToken cancellationToken,
        bool failClosedOnFailureEnvelope)
    {
        var content = response.Content
            ?? throw new InvalidOperationException("Platform API returned an empty response.");
        var json = await content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("Platform API returned an empty response.");
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        // Business services use the platform response envelope. A 2xx response
        // with success=false is a business validation failure, not a parse error.
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("success", out var success) &&
            success.ValueKind == JsonValueKind.False)
        {
            if (failClosedOnFailureEnvelope)
            {
                throw new InvalidOperationException("Platform API returned a failure envelope for an authoritative read.");
            }
            throw BusinessServiceProxyException.FromDownstreamBusinessMessage(DownstreamEnvelopeMessage(root));
        }

        var payload = root.TryGetProperty("data", out var data)
            ? data
            : root;

        if (payload.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new InvalidOperationException("Platform API returned an empty response data payload.");
        }

        return payload.Deserialize<TResponse>(jsonOptions)
            ?? throw new InvalidOperationException("Platform API returned an empty response data payload.");
    }

    private static string? DownstreamEnvelopeMessage(JsonElement root) =>
        root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
            ? message.GetString()
            : null;

    private static async Task<string?> ReadDownstreamEnvelopeMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<DownstreamErrorEnvelope> ReadDownstreamErrorEnvelopeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            return DownstreamErrorEnvelope.Invalid;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return ParseDownstreamErrorEnvelope(document.RootElement, response.StatusCode);
        }
        catch (JsonException)
        {
            return DownstreamErrorEnvelope.Invalid;
        }
    }

    private DownstreamErrorEnvelope ParseDownstreamErrorEnvelope(
        JsonElement root,
        HttpStatusCode statusCode)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return DownstreamErrorEnvelope.Invalid;
        }

        if (!root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.False)
        {
            return AcceptsBareDownstreamErrorPayload
                ? ParseBareDownstreamErrorPayload(root)
                : DownstreamErrorEnvelope.Invalid;
        }

        var message = root.TryGetProperty("message", out var messageValue) &&
            messageValue.ValueKind == JsonValueKind.String
            ? messageValue.GetString()
            : null;
        if (!root.TryGetProperty("code", out var codeValue))
        {
            // 兼容 capability 已登记的既有 success=false envelope；Shared 默认不从 message 猜 code。
            return new(
                null,
                message,
                ReadDownstreamErrorData(root),
                AllowMessageAsSemanticCode: IsRegisteredLegacySemanticCode(message));
        }

        if (codeValue.ValueKind == JsonValueKind.String)
        {
            var code = codeValue.GetString();
            return IsMatchingTransportStatus(code, statusCode)
                ? new(
                    null,
                    message,
                    ReadDownstreamErrorData(root),
                    AllowMessageAsSemanticCode: IsRegisteredLegacySemanticCode(message))
                : new(code, message, ReadDownstreamErrorData(root), AllowMessageAsSemanticCode: false);
        }

        if (codeValue.ValueKind == JsonValueKind.Number &&
            codeValue.TryGetInt32(out var numericCode) &&
            numericCode == (int)statusCode)
        {
            return new(
                null,
                message,
                ReadDownstreamErrorData(root),
                AllowMessageAsSemanticCode: IsRegisteredLegacySemanticCode(message));
        }

        return DownstreamErrorEnvelope.Invalid;
    }

    private DownstreamErrorEnvelope ParseBareDownstreamErrorPayload(JsonElement root)
    {
        var code = root.TryGetProperty("code", out var codeValue) && codeValue.ValueKind == JsonValueKind.String
            ? codeValue.GetString()
            : null;
        var message = root.TryGetProperty("message", out var messageValue) && messageValue.ValueKind == JsonValueKind.String
            ? messageValue.GetString()
            : null;
        return code is null && message is null
            ? DownstreamErrorEnvelope.Invalid
            : new(code, message, [], AllowMessageAsSemanticCode: IsRegisteredLegacySemanticCode(message));
    }

    private static IReadOnlyCollection<JsonElement> ReadDownstreamErrorData(JsonElement root)
    {
        if (root.TryGetProperty("errorData", out var errorData) &&
            errorData.ValueKind == JsonValueKind.Array)
        {
            return BusinessServiceProxyException.ProjectSafeErrorData(
                errorData.EnumerateArray().ToArray());
        }

        if (root.TryGetProperty("data", out var data))
        {
            var candidates = data.ValueKind switch
            {
                JsonValueKind.Array => data.EnumerateArray().ToArray(),
                JsonValueKind.Object => [data],
                _ => [],
            };
            return BusinessServiceProxyException.ProjectSafeErrorData(candidates);
        }

        return [];
    }

    private static bool IsMatchingTransportStatus(string? code, HttpStatusCode statusCode) =>
        int.TryParse(
            code,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var numericCode) &&
        numericCode == (int)statusCode;

    private sealed record DownstreamErrorEnvelope(
        string? SemanticCode,
        string? Message,
        IReadOnlyCollection<JsonElement> ErrorData,
        bool AllowMessageAsSemanticCode)
    {
        public static DownstreamErrorEnvelope Invalid { get; } = new(null, null, [], false);
    }

    protected static string Query(params (string Name, object? Value)[] values)
    {
        var pairs = values
            .Where(x => x.Value is not null && !string.IsNullOrWhiteSpace(Convert.ToString(x.Value, System.Globalization.CultureInfo.InvariantCulture)))
            .Select(x => $"{Uri.EscapeDataString(x.Name)}={Uri.EscapeDataString(FormatValue(x.Value!))}");
        return string.Join('&', pairs);
    }

    protected static string RepeatedQuery(string name, IEnumerable<string>? values)
    {
        if (values is null)
        {
            return string.Empty;
        }

        var encodedName = Uri.EscapeDataString(name);
        return string.Join(
            '&',
            values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => $"{encodedName}={Uri.EscapeDataString(value.Trim())}"));
    }

    protected static string JoinQuery(params string[] parts) =>
        string.Join('&', parts.Where(part => !string.IsNullOrWhiteSpace(part)));

    protected static bool? TrueFlag(bool value) => value ? true : null;

    private static string FormatValue(object value) => value switch
    {
        bool boolValue => boolValue.ToString().ToLowerInvariant(),
        DateOnly date => date.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset dateTime => dateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
    };
}
