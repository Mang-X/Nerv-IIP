using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Application.FileStorage;
using Nerv.IIP.PlatformGateway.Web.Endpoints.Files;
using Nerv.IIP.ServiceAuth;
using static Nerv.IIP.PlatformGateway.Web.Tests.ConsoleFileStorageTestFixtures;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

/// <summary>
/// SUT：<see cref="HttpGatewayFileStorageClient"/> 本身——**发往 FileStorage 的线上形状**
/// （URL、内部令牌、传输头、下游错误映射、下游 URL 的失败关闭）。
/// 端点层的替身看不见这一层，所以这些用例不能合并到端点测试里。
/// </summary>
public sealed class GatewayFileStorageClientTests
{
    [Fact]
    public async Task File_storage_http_client_lists_files_with_filters_and_internal_token()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new FileListResponse(
                1,
                [FileMetadata()]))
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://files.local") };
        var files = new HttpGatewayFileStorageClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));

        var response = await files.ListFilesAsync(
            new ListFilesRequest(
                "org-001",
                "env-dev",
                "notification-attachment",
                "user-001",
                null,
                DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                DateTimeOffset.Parse("2026-06-08T00:00:00Z"),
                "available",
                10,
                20),
            CancellationToken.None);

        Assert.Equal(1, response.Total);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "/api/files/v1/files?organizationId=org-001&environmentId=env-dev&filePurpose=notification-attachment&uploaderId=user-001&createdFromUtc=2026-06-01T00%3A00%3A00.0000000%2B00%3A00&createdToUtc=2026-06-08T00%3A00%3A00.0000000%2B00%3A00&status=available&skip=10&take=20",
            request.RequestUri.PathAndQuery);
        Assert.Equal("Bearer", request.Authorization!.Scheme);
        Assert.Equal("internal-test-token", request.Authorization.Parameter);
    }

    [Fact]
    public async Task File_storage_http_client_tus_head_forwards_organization_and_environment_headers()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://files.local") };
        var files = new HttpGatewayFileStorageClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));
        var httpContext = new DefaultHttpContext();

        await files.ProxyTusHeadAsync("upload-session-001", "org-001", "env-dev", httpContext.Response, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Head, request.Method);
        Assert.Equal("/api/files/v1/tus/upload-session-001", request.RequestUri!.AbsolutePath);
        Assert.Equal("org-001", request.Headers["X-Organization-Id"]);
        Assert.Equal("env-dev", request.Headers["X-Environment-Id"]);
        Assert.Equal("Bearer internal-test-token", request.Authorization!.ToString());
    }

    [Fact]
    public async Task File_storage_http_client_tus_patch_forwards_organization_and_environment_headers()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://files.local") };
        var files = new HttpGatewayFileStorageClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "PATCH";
        httpContext.Request.Body = new MemoryStream([1, 2, 3]);

        await files.ProxyTusPatchAsync("upload-session-001", "org-001", "env-dev", httpContext.Request, httpContext.Response, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal("/api/files/v1/tus/upload-session-001", request.RequestUri!.AbsolutePath);
        Assert.Equal("org-001", request.Headers["X-Organization-Id"]);
        Assert.Equal("env-dev", request.Headers["X-Environment-Id"]);
        Assert.Equal("Bearer internal-test-token", request.Authorization!.ToString());
    }

    [Fact]
    public async Task File_storage_http_client_uses_internal_token_and_rewrites_transfer_urls()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new CreateUploadSessionResponse(
                "upload-session-001",
                "file-001",
                "tus",
                "tus",
                DateTimeOffset.UtcNow.AddMinutes(15),
                new TransferInstructions("/api/files/v1/tus/upload-session-001", new Dictionary<string, string>
                {
                    ["Tus-Resumable"] = "1.0.0"
                })))
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://files.local")
        };
        var files = new HttpGatewayFileStorageClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));

        var response = await files.CreateUploadSessionAsync(CreateUploadSessionRequest(), CancellationToken.None);

        Assert.Equal("/api/console/v1/files/tus/upload-session-001", response.Upload.Url);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/files/v1/upload-sessions", request.RequestUri.PathAndQuery);
        Assert.Equal("Bearer", request.Authorization!.Scheme);
        Assert.Equal("internal-test-token", request.Authorization.Parameter);
    }

    [Fact]
    public async Task File_storage_http_client_rejects_absolute_transfer_urls()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new CreateUploadSessionResponse(
                "upload-session-001",
                "file-001",
                "tus",
                "tus",
                DateTimeOffset.UtcNow.AddMinutes(15),
                new TransferInstructions("https://files.example.test/direct/upload-session-001", new Dictionary<string, string>())))
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://files.local") };
        var files = new HttpGatewayFileStorageClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));

        var exception = await Assert.ThrowsAsync<GatewayAuthException>(
            () => files.CreateUploadSessionAsync(CreateUploadSessionRequest(), CancellationToken.None));

        Assert.Equal("filestorage-transfer-url-not-proxyable", exception.Reason);
    }

    [Fact]
    public async Task File_storage_http_client_rejects_network_path_transfer_urls()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new CreateUploadSessionResponse(
                "upload-session-001",
                "file-001",
                "tus",
                "tus",
                DateTimeOffset.UtcNow.AddMinutes(15),
                new TransferInstructions("//files.example.test/direct/upload-session-001", new Dictionary<string, string>())))
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://files.local") };
        var files = new HttpGatewayFileStorageClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));

        var exception = await Assert.ThrowsAsync<GatewayAuthException>(
            () => files.CreateUploadSessionAsync(CreateUploadSessionRequest(), CancellationToken.None));

        Assert.Equal("filestorage-transfer-url-not-proxyable", exception.Reason);
    }

    [Theory]
    [InlineData("", "filestorage-empty-response")]
    [InlineData("{ not-valid-json", "filestorage-invalid-response")]
    public async Task File_storage_http_client_maps_invalid_json_responses(string payload, string expectedReason)
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload)
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://files.local") };
        var files = new HttpGatewayFileStorageClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));

        var exception = await Assert.ThrowsAsync<GatewayAuthException>(
            () => files.CreateUploadSessionAsync(CreateUploadSessionRequest(), CancellationToken.None));

        Assert.Equal(expectedReason, exception.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "filestorage-unexpected-status-400")]
    [InlineData(HttpStatusCode.Unauthorized, "filestorage-unauthorized")]
    [InlineData(HttpStatusCode.Forbidden, "filestorage-forbidden")]
    [InlineData(HttpStatusCode.NotFound, "filestorage-unexpected-status-404")]
    [InlineData(HttpStatusCode.Conflict, "filestorage-unexpected-status-409")]
    [InlineData(HttpStatusCode.InternalServerError, "filestorage-unavailable")]
    [InlineData(HttpStatusCode.BadGateway, "filestorage-unavailable")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "filestorage-unavailable")]
    public async Task File_storage_http_client_maps_downstream_status_codes(
        HttpStatusCode statusCode,
        string expectedReason)
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent("downstream error")
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://files.local") };
        var files = new HttpGatewayFileStorageClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));

        var exception = await Assert.ThrowsAsync<GatewayAuthException>(
            () => files.CreateUploadSessionAsync(CreateUploadSessionRequest(), CancellationToken.None));

        Assert.Equal(expectedReason, exception.Reason);
    }

    [Fact]
    public async Task File_storage_raw_proxy_filters_hop_by_hop_response_headers()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/download-grants", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(GrantFor("/api/files/v1/download-grants/download-grant-001/content"))
                };
            }

            var downstream = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("hello")
            };
            downstream.Headers.TransferEncodingChunked = true;
            downstream.Headers.Connection.Add("X-Private-Hop");
            downstream.Headers.Add("Keep-Alive", "timeout=5");
            downstream.Headers.Add("X-Private-Hop", "secret");
            downstream.Headers.Add("X-Trace-Id", "trace-001");
            return downstream;
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://files.local") };
        var files = new HttpGatewayFileStorageClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));
        var context = new DefaultHttpContext();
        await using var body = new MemoryStream();
        context.Response.Body = body;

        await files.StreamFileContentAsync("file-001", "org-001", "env-dev", context.Response, CancellationToken.None);

        var byteRequest = handler.Requests[1];
        Assert.Equal("/api/files/v1/download-grants/download-grant-001/content", byteRequest.RequestUri.AbsolutePath);
        Assert.Equal("org-001", byteRequest.Headers["X-Organization-Id"]);
        Assert.Equal("env-dev", byteRequest.Headers["X-Environment-Id"]);
        Assert.False(context.Response.Headers.ContainsKey("Transfer-Encoding"));
        Assert.False(context.Response.Headers.ContainsKey("Connection"));
        Assert.False(context.Response.Headers.ContainsKey("Keep-Alive"));
        Assert.False(context.Response.Headers.ContainsKey("X-Private-Hop"));
        Assert.False(context.Response.Headers.ContainsKey("Content-Length"));
        Assert.Equal("trace-001", context.Response.Headers["X-Trace-Id"].Single());
        body.Position = 0;
        using var reader = new StreamReader(body);
        Assert.Equal("hello", await reader.ReadToEndAsync());
    }

    /// <summary>
    /// #3314 路线 A 的结构不变量：本 client 取字节前必须自己签发 grant，且**不把 grant id 交出去**。
    ///
    /// 会失败的具体输入：把「先签发」那一跳去掉、直接拿调用方给的标识拼 content URL——那样
    /// <c>handler.Requests</c> 里就不会出现 <c>POST /api/files/v1/files/{fileId}/download-grants</c>，
    /// 第一条断言红。
    /// </summary>
    [Fact]
    public async Task File_content_signs_its_own_grant_server_side_and_never_takes_a_grant_id_from_the_caller()
    {
        var handler = new RecordingHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/download-grants", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(GrantFor("/api/files/v1/download-grants/grant-server-signed/content"))
                }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("bytes") });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://files.local") };
        var files = new HttpGatewayFileStorageClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));
        var context = new DefaultHttpContext();
        await using var body = new MemoryStream();
        context.Response.Body = body;

        await files.StreamFileContentAsync("file-001", "org-001", "env-dev", context.Response, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("/api/files/v1/files/file-001/download-grants", handler.Requests[0].RequestUri.AbsolutePath);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.Equal("/api/files/v1/download-grants/grant-server-signed/content", handler.Requests[1].RequestUri.AbsolutePath);
    }

    /// <summary>
    /// 失败关闭：FileStorage 若回一个不可代理的下游地址（绝对 URL / 前缀不符），本 client 不得跟随。
    /// </summary>
    [Theory]
    [InlineData("https://filestorage.internal/api/files/v1/download-grants/g/content")]
    [InlineData("//filestorage.internal/api/files/v1/download-grants/g/content")]
    [InlineData("/api/files/v1/tus/g")]
    public async Task File_content_refuses_a_grant_url_that_is_not_a_proxyable_internal_download_path(string url)
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(GrantFor(url))
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://files.local") };
        var files = new HttpGatewayFileStorageClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));
        var context = new DefaultHttpContext();
        await using var body = new MemoryStream();
        context.Response.Body = body;

        var exception = await Assert.ThrowsAsync<GatewayAuthException>(
            () => files.StreamFileContentAsync("file-001", "org-001", "env-dev", context.Response, CancellationToken.None));

        Assert.Equal("filestorage-transfer-url-not-proxyable", exception.Reason);
        // 拒绝必须发生在跟随之前
        Assert.Single(handler.Requests);
    }
}
