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
        bool failClosedOnFailureEnvelope = false,
        bool acceptKnownExceptionCodeZero = false,
        bool mapDownstreamServerErrorToBadGateway = false)
    {
        using var response = await SendRequestAsync(
            internalBearerToken,
            method,
            requestUri,
            body,
            cancellationToken,
            jsonOptions,
            configureRequest,
            mapDownstreamServerErrorToBadGateway);
        try
        {
            return await ReadResponseDataAsync<TResponse>(
                response,
                jsonOptions ?? JsonOptions,
                cancellationToken,
                failClosedOnFailureEnvelope,
                acceptKnownExceptionCodeZero);
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

    protected async Task<byte[]> SendBytesAsync(
        string internalBearerToken,
        string requestUri,
        CancellationToken cancellationToken)
    {
        using var response = await SendRequestAsync(
            internalBearerToken, HttpMethod.Get, requestUri, null, cancellationToken, null, null);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
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
        Action<HttpRequestMessage>? configureRequest,
        bool mapDownstreamServerErrorToBadGateway = false)
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
            if (mapDownstreamServerErrorToBadGateway && (int)response.StatusCode >= 500)
            {
                throw BusinessServiceProxyException.FromOpaqueDownstreamFailure(HttpStatusCode.BadGateway);
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
        catch (Polly.CircuitBreaker.BrokenCircuitException ex)
        {
            // 网关 resilience 管道熔断打开（#3272）：这是**预期内的保护动作**，不是未知故障。
            // 未映射时它逃逸出 UseKnownExceptionHandler 的已知异常面，呈现为 500/「未知错误」，
            // 于是用户看到「系统坏了」而不是「稍后再试」，运维看到 500 而不是熔断信号。
            // 与相邻两条同一处理形态（FromSafeDownstreamMessage + 稳定 kebab 码）。
            //
            // 与 downstream-unavailable 分开一条码，是因为**这一格的事实不同**：熔断打开时请求
            // 根本没有发往下游，本次写入必然未发生；而 HttpRequestException 是已发出后连接失败，
            // 结果不确定。两者若共用一条码，前端就说不出「本次请求未发出」这句可行动的话。
            //
            // 本方法是 JSON 面与字节面共用的唯一一份传输故障映射（#3096 从 SendRequestAsync
            // 提取），因此 #3385 在 SendRequestAsync 内联块上新增的这一格在此处承接后，
            // 同时覆盖 streaming-safe 档的字节面。
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.ServiceUnavailable,
                "downstream-circuit-open",
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
        bool failClosedOnFailureEnvelope,
        bool acceptKnownExceptionCodeZero)
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
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("success", out var success) &&
            success.ValueKind == JsonValueKind.False)
        {
            var statusCode = ReadFailureEnvelopeStatus(root, acceptKnownExceptionCodeZero);
            if (failClosedOnFailureEnvelope || statusCode is not (400 or 403 or 404 or 409 or 422))
            {
                throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                    HttpStatusCode.BadGateway,
                    "downstream-invalid-response");
            }

            throw BusinessServiceProxyException.FromDownstreamBusinessMessage(
                (HttpStatusCode)statusCode, DownstreamEnvelopeMessage(root));
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

    private static int ReadFailureEnvelopeStatus(JsonElement root, bool acceptKnownExceptionCodeZero)
    {
        if (!root.TryGetProperty("code", out var code))
        {
            return -1;
        }

        return code.ValueKind switch
        {
            JsonValueKind.Number when code.TryGetInt32(out var number) =>
                acceptKnownExceptionCodeZero && number == 0 ? (int)HttpStatusCode.BadRequest : number,
            JsonValueKind.String when int.TryParse(code.GetString(), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var number) => number,
            _ => -1,
        };
    }

    private static string? DownstreamEnvelopeMessage(JsonElement root) =>
        root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
            ? message.GetString()
            : null;

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

        if (!root.TryGetProperty("success", out var success))
        {
            return AcceptsBareDownstreamErrorPayload
                ? ParseBareDownstreamErrorPayload(root)
                : DownstreamErrorEnvelope.Invalid;
        }

        if (success.ValueKind != JsonValueKind.False)
        {
            return DownstreamErrorEnvelope.Invalid;
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
            // KnownException 使用数值 0 表示没有声明领域错误码。
            (numericCode == (int)statusCode || (statusCode == HttpStatusCode.BadRequest && numericCode == 0)))
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
