using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.PlatformGateway.Web.Application.FileStorage;

public interface IGatewayFileStorageClient
{
    Task<CreateUploadSessionResponse> CreateUploadSessionAsync(
        CreateUploadSessionRequest request,
        CancellationToken cancellationToken);

    Task<FileMetadataResponse> CompleteUploadSessionAsync(
        string uploadSessionId,
        CompleteUploadSessionRequest request,
        CancellationToken cancellationToken);

    Task<FileMetadataResponse> GetFileMetadataAsync(
        string fileId,
        CancellationToken cancellationToken);

    Task<FileListResponse> ListFilesAsync(
        ListFilesRequest request,
        CancellationToken cancellationToken);

    Task<FileStorageUsageResponse> GetUsageAsync(
        FileStorageUsageRequest request,
        CancellationToken cancellationToken);

    Task ProxyTusHeadAsync(
        string uploadSessionId,
        string organizationId,
        string environmentId,
        HttpResponse response,
        CancellationToken cancellationToken);

    Task ProxyTusPatchAsync(
        string uploadSessionId,
        string organizationId,
        string environmentId,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken);

    /// <summary>
    /// 平台控制台取文件字节的**唯一**入口：在服务端签发 download grant、校验其 URL 是可代理的
    /// 内部路径、随即就地兑换。grant id 不出本进程（#3314）。
    /// </summary>
    Task StreamFileContentAsync(
        string fileId,
        string organizationId,
        string environmentId,
        HttpResponse response,
        CancellationToken cancellationToken);
}

public sealed class HttpGatewayFileStorageClient(
    HttpClient httpClient,
    IInternalServiceTokenProvider internalServiceToken) : IGatewayFileStorageClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> HopByHopResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Content-Length",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade",
    };

    public async Task<CreateUploadSessionResponse> CreateUploadSessionAsync(
        CreateUploadSessionRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendForJsonAsync<CreateUploadSessionResponse>(
            () => JsonContent.Create(request),
            HttpMethod.Post,
            "/api/files/v1/upload-sessions",
            cancellationToken);

        return response with
        {
            Upload = RewriteTransferInstructions(response.Upload)
        };
    }

    public Task<FileMetadataResponse> CompleteUploadSessionAsync(
        string uploadSessionId,
        CompleteUploadSessionRequest request,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<FileMetadataResponse>(
            () => JsonContent.Create(request),
            HttpMethod.Post,
            $"/api/files/v1/upload-sessions/{Uri.EscapeDataString(uploadSessionId)}/complete",
            cancellationToken);

    public Task<FileMetadataResponse> GetFileMetadataAsync(
        string fileId,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<FileMetadataResponse>(
            () => null,
            HttpMethod.Get,
            $"/api/files/v1/files/{Uri.EscapeDataString(fileId)}",
            cancellationToken);

    public Task<FileListResponse> ListFilesAsync(
        ListFilesRequest request,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<FileListResponse>(
            () => null,
            HttpMethod.Get,
            "/api/files/v1/files" + BuildListQuery(request),
            cancellationToken);

    public Task<FileStorageUsageResponse> GetUsageAsync(
        FileStorageUsageRequest request,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<FileStorageUsageResponse>(
            () => null,
            HttpMethod.Get,
            "/api/files/v1/usage" + BuildUsageQuery(request),
            cancellationToken);

    public Task ProxyTusHeadAsync(
        string uploadSessionId,
        string organizationId,
        string environmentId,
        HttpResponse response,
        CancellationToken cancellationToken) =>
        ProxyRawAsync(
            HttpMethod.Head,
            FileStorageDownstreamAddress.Tus(uploadSessionId),
            null,
            response,
            cancellationToken,
            new Dictionary<string, string>
            {
                ["X-Organization-Id"] = organizationId,
                ["X-Environment-Id"] = environmentId
            });

    public Task ProxyTusPatchAsync(
        string uploadSessionId,
        string organizationId,
        string environmentId,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken) =>
        ProxyRawAsync(
            HttpMethod.Patch,
            FileStorageDownstreamAddress.Tus(uploadSessionId),
            request,
            response,
            cancellationToken,
            new Dictionary<string, string>
            {
                ["X-Organization-Id"] = organizationId,
                ["X-Environment-Id"] = environmentId
            });

    public async Task StreamFileContentAsync(
        string fileId,
        string organizationId,
        string environmentId,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var grant = await SendForJsonAsync<DownloadGrantResponse>(
            () => JsonContent.Create(new CreateDownloadGrantRequest(organizationId, environmentId)),
            HttpMethod.Post,
            $"/api/files/v1/files/{Uri.EscapeDataString(fileId)}/download-grants",
            cancellationToken);

        // 取字节地址只能由**刚刚签发的那个 grant** 产出（见 FileStorageDownstreamAddress）：
        // 本方法没有、也不可能有一个接受调用方标识或 URL 字符串的分支。下游 URL 的形状校验
        // 与失败关闭都在该类型的工厂里。
        var address = FileStorageDownstreamAddress.FromSignedGrant(grant);

        // 无条件转发**下游签发时给出的**传输头，不做「为空就用网关自己拼的租户头」这类回落：
        // 该回落生产不可达（真实 producer `PostgreSqlFileStorageService` 恒返回三个头），而一旦
        // 真的走到，它会把下游签发的凭据头静默换成网关另拼的一套——正是本 PR 要消灭的口径漂移。
        // 与 BusinessGateway 字节面同一动作保持一致（`BusinessFileTransferClient` 也是无条件转发）。
        await ProxyRawAsync(
            HttpMethod.Get,
            address,
            null,
            response,
            cancellationToken,
            grant.Download.Headers);
    }

    /// <summary>
    /// 代理一跳。**目标地址的类型是 <see cref="FileStorageDownstreamAddress"/> 而不是
    /// <see cref="string"/>**——这是 #3314 第 2 轮审核 E1 的结构性替代：没有任何入口能把
    /// 调用方给的 grant 标识（无论走 path 还是 query）变成一次下游兑换调用。
    /// </summary>
    private async Task ProxyRawAsync(
        HttpMethod method,
        FileStorageDownstreamAddress address,
        HttpRequest? sourceRequest,
        HttpResponse targetResponse,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        try
        {
            using var request = new HttpRequestMessage(method, address.Path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalServiceToken.BearerToken);
            CopyTransferRequestHeaders(sourceRequest, request);
            CopyHeaders(headers, request);
            if (sourceRequest is not null && method == HttpMethod.Patch)
            {
                request.Content = new StreamContent(sourceRequest.Body);
                if (!string.IsNullOrWhiteSpace(sourceRequest.ContentType))
                {
                    request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(sourceRequest.ContentType);
                }
            }

            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            targetResponse.StatusCode = (int)response.StatusCode;
            CopyResponseHeaders(response, targetResponse);

            if (method != HttpMethod.Head && response.Content is not null)
            {
                await response.Content.CopyToAsync(targetResponse.Body, cancellationToken);
            }
        }
        catch (HttpRequestException)
        {
            throw GatewayAuthException.Unavailable("filestorage-unavailable");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw GatewayAuthException.Unavailable("filestorage-unavailable");
        }
    }

    private async Task<T> SendForJsonAsync<T>(
        Func<HttpContent?> contentFactory,
        HttpMethod method,
        string requestUri,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(contentFactory, method, requestUri, cancellationToken);
        try
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(payload))
            {
                throw GatewayAuthException.BadGateway("filestorage-empty-response");
            }

            return JsonSerializer.Deserialize<T>(payload, JsonOptions)
                ?? throw GatewayAuthException.BadGateway("filestorage-empty-response");
        }
        catch (JsonException)
        {
            throw GatewayAuthException.BadGateway("filestorage-invalid-response");
        }
        catch (NotSupportedException)
        {
            throw GatewayAuthException.BadGateway("filestorage-invalid-response");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpContent?> contentFactory,
        HttpMethod method,
        string requestUri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, requestUri);
            request.Content = contentFactory();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalServiceToken.BearerToken);

            var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var statusCode = response.StatusCode;
            var message = await ReadDownstreamErrorMessageAsync(response, cancellationToken);
            response.Dispose();
            throw ToGatewayException(statusCode, message);
        }
        catch (GatewayAuthException)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode statusCode)
        {
            throw ToGatewayException(statusCode, null);
        }
        catch (HttpRequestException)
        {
            throw GatewayAuthException.Unavailable("filestorage-unavailable");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw GatewayAuthException.Unavailable("filestorage-unavailable");
        }
    }

    private static async Task<string?> ReadDownstreamErrorMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<FileStorageErrorEnvelope>(cancellationToken);
            return error?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static GatewayAuthException ToGatewayException(HttpStatusCode statusCode, string? message)
    {
        var reason = string.IsNullOrWhiteSpace(message)
            ? $"filestorage-unexpected-status-{(int)statusCode}"
            : message;

        return statusCode switch
        {
            HttpStatusCode.BadRequest => new GatewayAuthException(statusCode, reason),
            HttpStatusCode.Unauthorized => GatewayAuthException.Unauthorized("filestorage-unauthorized"),
            HttpStatusCode.Forbidden => new GatewayAuthException(statusCode, "filestorage-forbidden"),
            HttpStatusCode.NotFound => new GatewayAuthException(statusCode, reason),
            HttpStatusCode.Conflict => new GatewayAuthException(statusCode, reason),
            _ when (int)statusCode >= 500 => GatewayAuthException.Unavailable("filestorage-unavailable"),
            _ => GatewayAuthException.BadGateway(reason)
        };
    }

    private static TransferInstructions RewriteTransferInstructions(TransferInstructions instructions)
    {
        if (IsExternallyAddressedTransferUrl(instructions.Url))
        {
            throw GatewayAuthException.BadGateway("filestorage-transfer-url-not-proxyable");
        }

        return instructions with
        {
            Url = RewriteTransferUrl(instructions.Url)
        };
    }

    private static string RewriteTransferUrl(string url)
    {
        if (url.StartsWith(ConsoleFileStorageTransferRoutes.DownstreamTusPrefix, StringComparison.Ordinal))
        {
            return ConsoleFileStorageTransferRoutes.ConsoleTusPrefix
                + url[ConsoleFileStorageTransferRoutes.DownstreamTusPrefix.Length..];
        }

        return url;
    }

    private static bool IsExternallyAddressedTransferUrl(string url)
    {
        var trimmed = url.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return true;
        }

        var firstSeparator = trimmed.IndexOfAny(['/', '?', '#']);
        var firstColon = trimmed.IndexOf(':');
        return firstColon > 0 && (firstSeparator < 0 || firstColon < firstSeparator);
    }

    private static string BuildListQuery(ListFilesRequest request)
    {
        var values = new List<string>();
        Add(values, "organizationId", request.OrganizationId);
        Add(values, "environmentId", request.EnvironmentId);
        Add(values, "filePurpose", request.FilePurpose);
        Add(values, "uploaderId", request.UploaderId);
        Add(values, "ownerId", request.OwnerId);
        Add(values, "createdFromUtc", request.CreatedFromUtc?.ToString("O"));
        Add(values, "createdToUtc", request.CreatedToUtc?.ToString("O"));
        Add(values, "status", request.Status);
        Add(values, "skip", request.Skip?.ToString());
        Add(values, "take", request.Take?.ToString());

        return values.Count == 0 ? string.Empty : "?" + string.Join("&", values);
    }

    private static string BuildUsageQuery(FileStorageUsageRequest request)
    {
        var values = new List<string>();
        Add(values, "organizationId", request.OrganizationId);
        Add(values, "environmentId", request.EnvironmentId);
        Add(values, "filePurpose", request.FilePurpose);
        return values.Count == 0 ? string.Empty : "?" + string.Join("&", values);
    }

    private static void Add(List<string> values, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        values.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
    }

    private static void CopyTransferRequestHeaders(HttpRequest? sourceRequest, HttpRequestMessage targetRequest)
    {
        if (sourceRequest is null)
        {
            return;
        }

        CopyHeader(sourceRequest, targetRequest, "Tus-Resumable");
        CopyHeader(sourceRequest, targetRequest, "Upload-Offset");
        CopyHeader(sourceRequest, targetRequest, "Upload-Checksum");
    }

    private static void CopyHeader(HttpRequest sourceRequest, HttpRequestMessage targetRequest, string name)
    {
        if (sourceRequest.Headers.TryGetValue(name, out var values))
        {
            targetRequest.Headers.TryAddWithoutValidation(name, values.ToArray());
        }
    }

    private static void CopyHeaders(IReadOnlyDictionary<string, string>? headers, HttpRequestMessage targetRequest)
    {
        if (headers is null)
        {
            return;
        }

        foreach (var header in headers)
        {
            targetRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    private static void CopyResponseHeaders(HttpResponseMessage sourceResponse, HttpResponse targetResponse)
    {
        var connectionHeaderValues = sourceResponse.Headers.Connection
            .SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var header in sourceResponse.Headers)
        {
            if (ShouldSkipResponseHeader(header.Key, connectionHeaderValues))
            {
                continue;
            }

            targetResponse.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in sourceResponse.Content.Headers)
        {
            if (ShouldSkipResponseHeader(header.Key, connectionHeaderValues))
            {
                continue;
            }

            targetResponse.Headers[header.Key] = header.Value.ToArray();
        }
    }

    private static bool ShouldSkipResponseHeader(string headerName, HashSet<string> connectionHeaderValues)
    {
        return HopByHopResponseHeaders.Contains(headerName)
            || connectionHeaderValues.Contains(headerName);
    }

    private sealed record FileStorageErrorEnvelope(string? Message);
}
