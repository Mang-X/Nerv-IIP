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

    Task<DownloadGrantResponse> CreateDownloadGrantAsync(
        string fileId,
        CreateDownloadGrantRequest request,
        CancellationToken cancellationToken);

    Task ProxyTusHeadAsync(
        string uploadSessionId,
        HttpResponse response,
        CancellationToken cancellationToken);

    Task ProxyTusPatchAsync(
        string uploadSessionId,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken);

    Task ProxyDownloadGrantContentAsync(
        string downloadGrantId,
        HttpResponse response,
        CancellationToken cancellationToken);
}

public sealed class HttpGatewayFileStorageClient(
    HttpClient httpClient,
    IInternalServiceTokenProvider internalServiceToken) : IGatewayFileStorageClient
{
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

    public async Task<DownloadGrantResponse> CreateDownloadGrantAsync(
        string fileId,
        CreateDownloadGrantRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendForJsonAsync<DownloadGrantResponse>(
            () => JsonContent.Create(request),
            HttpMethod.Post,
            $"/api/files/v1/files/{Uri.EscapeDataString(fileId)}/download-grants",
            cancellationToken);

        return response with
        {
            Download = RewriteTransferInstructions(response.Download)
        };
    }

    public Task ProxyTusHeadAsync(
        string uploadSessionId,
        HttpResponse response,
        CancellationToken cancellationToken) =>
        ProxyRawAsync(
            HttpMethod.Head,
            $"/api/files/v1/tus/{Uri.EscapeDataString(uploadSessionId)}",
            null,
            response,
            cancellationToken);

    public Task ProxyTusPatchAsync(
        string uploadSessionId,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken) =>
        ProxyRawAsync(
            HttpMethod.Patch,
            $"/api/files/v1/tus/{Uri.EscapeDataString(uploadSessionId)}",
            request,
            response,
            cancellationToken);

    public Task ProxyDownloadGrantContentAsync(
        string downloadGrantId,
        HttpResponse response,
        CancellationToken cancellationToken) =>
        ProxyRawAsync(
            HttpMethod.Get,
            $"/api/files/v1/download-grants/{Uri.EscapeDataString(downloadGrantId)}/content",
            null,
            response,
            cancellationToken);

    private async Task ProxyRawAsync(
        HttpMethod method,
        string requestUri,
        HttpRequest? sourceRequest,
        HttpResponse targetResponse,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalServiceToken.BearerToken);
            CopyTusRequestHeaders(sourceRequest, request);
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
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
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
        if (Uri.TryCreate(instructions.Url, UriKind.Absolute, out _))
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

        if (url.StartsWith(ConsoleFileStorageTransferRoutes.DownstreamDownloadGrantPrefix, StringComparison.Ordinal))
        {
            return ConsoleFileStorageTransferRoutes.ConsoleDownloadGrantPrefix
                + url[ConsoleFileStorageTransferRoutes.DownstreamDownloadGrantPrefix.Length..];
        }

        return url;
    }

    private static void CopyTusRequestHeaders(HttpRequest? sourceRequest, HttpRequestMessage targetRequest)
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

    private static void CopyResponseHeaders(HttpResponseMessage sourceResponse, HttpResponse targetResponse)
    {
        foreach (var header in sourceResponse.Headers)
        {
            targetResponse.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in sourceResponse.Content.Headers)
        {
            targetResponse.Headers[header.Key] = header.Value.ToArray();
        }

        targetResponse.Headers.Remove("Content-Length");
    }

    private sealed record FileStorageErrorEnvelope(string? Message);
}
