using System.Net.Http.Headers;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Application.FileStorage;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

/// <summary>
/// Console 文件面两个 SUT 共用的夹具。
///
/// #3314 第 2 轮审核 Q3：原来的 <c>GatewayConsoleFileStorageTests</c> 跨过 1000 行且类内混了
/// 两个 SUT（经 <see cref="WebApplicationFactory{T}"/> 的**端点面**，与直接 new 出来的
/// <c>HttpGatewayFileStorageClient</c> **客户端面**）。按 SUT 拆成
/// <c>GatewayConsoleFileStorageEndpointTests</c> 与 <c>GatewayFileStorageClientTests</c>，
/// 夹具落在本文件，避免两边各造一套近似品。
/// </summary>
internal static class ConsoleFileStorageTestFixtures
{
    internal static void AssertNoGrantLeak(HttpResponseMessage response, string body, string grantId)
    {
        var rendered = string.Join(
            "\n",
            response.Headers
                .Concat(response.Content.Headers)
                .Select(header => $"{header.Key}: {string.Join(",", header.Value)}"));

        foreach (var (surface, text) in new[] { ("响应头", rendered), ("响应体", body) })
        {
            Assert.False(
                text.Contains("/download-grants/", StringComparison.OrdinalIgnoreCase),
                $"{surface}泄漏了 FileStorage 的 download-grant 路径：{text}");
            Assert.False(
                text.Contains(grantId, StringComparison.OrdinalIgnoreCase),
                $"{surface}泄漏了 download grant id：{text}");
        }
    }

    internal static DownloadGrantResponse GrantFor(string url) =>
        new(
            "file-001",
            DateTimeOffset.Parse("2026-09-20T08:00:00Z"),
            new TransferInstructions(url, new Dictionary<string, string>
            {
                ["X-Organization-Id"] = "org-001",
                ["X-Environment-Id"] = "env-dev",
            }));

    internal static WebApplicationFactory<Program> CreateFactory(
        FakeGatewayFileStorageClient files,
        FakeGatewayAuthorizationClient? auth = null)
    {
        return PlatformGatewayTestHost.CreateFactory()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGatewayFileStorageClient>();
                services.AddSingleton<IGatewayFileStorageClient>(files);
                services.RemoveAll<IGatewayAuthorizationClient>();
                services.AddSingleton<IGatewayAuthorizationClient>(auth ?? FakeGatewayAuthorizationClient.Allowed());
            }));
    }

    internal static HttpRequestMessage AuthorizedRequest(HttpMethod method, string requestUri)
    {
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new("Bearer", GatewayTestTokens.ValidAccessToken());
        return request;
    }

    internal static void AddTenantHeaders(HttpRequestMessage request)
    {
        request.Headers.Add("X-Organization-Id", "org-001");
        request.Headers.Add("X-Environment-Id", "env-dev");
    }

    internal static CreateUploadSessionRequest CreateUploadSessionRequest() =>
        new(
            "org-001",
            "env-dev",
            new OwnerReference("notification", "message", "msg-001"),
            "notification-attachment",
            "example.csv",
            "text/csv",
            42,
            null);

    internal sealed class FakeGatewayFileStorageClient : IGatewayFileStorageClient
    {
        public CreateUploadSessionRequest? LastCreateRequest { get; private set; }
        public string? LastCompleteUploadSessionId { get; private set; }
        public CompleteUploadSessionRequest? LastCompleteRequest { get; private set; }
        public string? LastMetadataFileId { get; private set; }
        public ListFilesRequest? LastListRequest { get; private set; }
        public FileStorageUsageRequest? LastUsageRequest { get; private set; }
        public string? LastTusHeadUploadSessionId { get; private set; }
        public string? LastTusHeadOrganizationId { get; private set; }
        public string? LastTusHeadEnvironmentId { get; private set; }
        public string? LastTusPatchUploadSessionId { get; private set; }
        public string? LastTusPatchOrganizationId { get; private set; }
        public string? LastTusPatchEnvironmentId { get; private set; }
        public string? LastDownloadContentFileId { get; private set; }
        public string? LastDownloadContentOrganizationId { get; private set; }
        public string? LastDownloadContentEnvironmentId { get; private set; }
        public Exception? ExceptionToThrow { get; init; }

        public Task<CreateUploadSessionResponse> CreateUploadSessionAsync(
            CreateUploadSessionRequest request,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastCreateRequest = request;
            return Task.FromResult(new CreateUploadSessionResponse(
                "upload-session-001",
                "file-001",
                "tus",
                "tus",
                DateTimeOffset.UtcNow.AddMinutes(15),
                new TransferInstructions("/api/console/v1/files/tus/upload-session-001", new Dictionary<string, string>())));
        }

        public Task<FileMetadataResponse> CompleteUploadSessionAsync(
            string uploadSessionId,
            CompleteUploadSessionRequest request,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastCompleteUploadSessionId = uploadSessionId;
            LastCompleteRequest = request;
            return Task.FromResult(FileMetadata());
        }

        public Task<FileMetadataResponse> GetFileMetadataAsync(string fileId, CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastMetadataFileId = fileId;
            return Task.FromResult(FileMetadata());
        }

        public Task<FileListResponse> ListFilesAsync(ListFilesRequest request, CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastListRequest = request;
            return Task.FromResult(new FileListResponse(1, [FileMetadata()]));
        }

        public Task<FileStorageUsageResponse> GetUsageAsync(FileStorageUsageRequest request, CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastUsageRequest = request;
            return Task.FromResult(new FileStorageUsageResponse(
                request.OrganizationId,
                request.EnvironmentId,
                request.FilePurpose,
                42,
                4096));
        }

        public Task ProxyTusHeadAsync(
            string uploadSessionId,
            string organizationId,
            string environmentId,
            HttpResponse response,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastTusHeadUploadSessionId = uploadSessionId;
            LastTusHeadOrganizationId = organizationId;
            LastTusHeadEnvironmentId = environmentId;
            response.Headers["Tus-Resumable"] = "1.0.0";
            response.Headers["Upload-Offset"] = "0";
            response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        }

        public Task ProxyTusPatchAsync(
            string uploadSessionId,
            string organizationId,
            string environmentId,
            HttpRequest request,
            HttpResponse response,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastTusPatchUploadSessionId = uploadSessionId;
            LastTusPatchOrganizationId = organizationId;
            LastTusPatchEnvironmentId = environmentId;
            response.Headers["Tus-Resumable"] = "1.0.0";
            response.Headers["Upload-Offset"] = "3";
            response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        }

        public async Task StreamFileContentAsync(
            string fileId,
            string organizationId,
            string environmentId,
            HttpResponse response,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastDownloadContentFileId = fileId;
            LastDownloadContentOrganizationId = organizationId;
            LastDownloadContentEnvironmentId = environmentId;
            response.ContentType = "text/plain";
            await response.WriteAsync("hello", cancellationToken);
        }

        private void ThrowIfConfigured()
        {
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }
        }
    }

    internal static FileMetadataResponse FileMetadata() =>
        new(
            "file-001",
            "org-001",
            "env-dev",
            new OwnerReference("notification", "message", "msg-001"),
            "notification-attachment",
            "example.csv",
            "text/csv",
            42,
            null,
            "completed",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow);

    internal sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization,
                request.Headers.ToDictionary(
                    header => header.Key,
                    header => string.Join(",", header.Value),
                    StringComparer.OrdinalIgnoreCase)));
            return Task.FromResult(responseFactory(request));
        }
    }

    internal sealed record RecordedRequest(
        HttpMethod Method,
        Uri RequestUri,
        AuthenticationHeaderValue? Authorization,
        IReadOnlyDictionary<string, string> Headers);

    internal sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;

    internal sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);

    internal static async Task<T> ReadResponseDataAsync<T>(HttpResponseMessage response)
    {
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>();
        Assert.NotNull(envelope);
        Assert.True(envelope.Success, envelope.Message);
        Assert.NotNull(envelope.Data);
        return envelope.Data;
    }
}
