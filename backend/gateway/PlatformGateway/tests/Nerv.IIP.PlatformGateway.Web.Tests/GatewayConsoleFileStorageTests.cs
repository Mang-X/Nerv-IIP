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
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayConsoleFileStorageTests
{
    [Fact]
    public async Task Create_upload_session_forwards_payload_and_requires_upload_permission()
    {
        var files = new FakeGatewayFileStorageClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(files, auth);
        using var request = AuthorizedRequest(HttpMethod.Post, "/api/console/v1/files/upload-sessions");
        request.Content = JsonContent.Create(CreateUploadSessionRequest());

        var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await ReadResponseDataAsync<CreateUploadSessionResponse>(response);
        Assert.Equal("upload-session-001", body.UploadSessionId);
        Assert.Equal("/api/console/v1/files/tus/upload-session-001", body.Upload.Url);
        Assert.Equal("example.csv", files.LastCreateRequest!.FileName);
        Assert.Equal(GatewayPermissions.FilesUpload, auth.LastRequirement!.PermissionCode);
    }

    [Fact]
    public async Task Complete_upload_session_forwards_session_id_and_requires_upload_permission()
    {
        var files = new FakeGatewayFileStorageClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(files, auth);
        using var request = AuthorizedRequest(HttpMethod.Post, "/api/console/v1/files/upload-sessions/upload-session-001/complete");
        request.Content = JsonContent.Create(new CompleteUploadSessionRequest("org-001", "env-dev", "notification-attachment"));

        var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await ReadResponseDataAsync<FileMetadataResponse>(response);
        Assert.Equal("file-001", body.FileId);
        Assert.Equal("upload-session-001", files.LastCompleteUploadSessionId);
        Assert.Equal(GatewayPermissions.FilesUpload, auth.LastRequirement!.PermissionCode);
    }

    [Fact]
    public async Task Get_file_metadata_forwards_file_id_and_requires_read_permission()
    {
        var files = new FakeGatewayFileStorageClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(files, auth);
        using var request = AuthorizedRequest(HttpMethod.Get, "/api/console/v1/files/file-001");

        var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await ReadResponseDataAsync<FileMetadataResponse>(response);
        Assert.Equal("file-001", body.FileId);
        Assert.Equal("file-001", files.LastMetadataFileId);
        Assert.Equal(GatewayPermissions.FilesRead, auth.LastRequirement!.PermissionCode);
    }

    [Fact]
    public async Task Create_download_grant_forwards_file_id_and_requires_download_grant_permission()
    {
        var files = new FakeGatewayFileStorageClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(files, auth);
        using var request = AuthorizedRequest(HttpMethod.Post, "/api/console/v1/files/file-001/download-grants");
        request.Content = JsonContent.Create(new CreateDownloadGrantRequest("org-001", "env-dev"));

        var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await ReadResponseDataAsync<DownloadGrantResponse>(response);
        Assert.Equal("file-001", body.FileId);
        Assert.Equal("/api/console/v1/files/download-grants/download-grant-001/content", body.Download.Url);
        Assert.Equal("file-001", files.LastDownloadGrantFileId);
        Assert.Equal(GatewayPermissions.FilesDownloadGrantsCreate, auth.LastRequirement!.PermissionCode);
    }

    [Fact]
    public async Task Patch_tus_upload_proxies_bytes_and_requires_upload_permission()
    {
        var files = new FakeGatewayFileStorageClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(files, auth);
        using var request = AuthorizedRequest(HttpMethod.Patch, "/api/console/v1/files/tus/upload-session-001");
        request.Content = new ByteArrayContent([1, 2, 3]);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");
        request.Headers.Add("Tus-Resumable", "1.0.0");
        request.Headers.Add("Upload-Offset", "0");

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("3", response.Headers.GetValues("Upload-Offset").Single());
        Assert.Equal("upload-session-001", files.LastTusPatchUploadSessionId);
        Assert.Equal(GatewayPermissions.FilesUpload, auth.LastRequirement!.PermissionCode);
    }

    [Fact]
    public async Task Download_grant_content_proxies_stream_and_requires_read_permission()
    {
        var files = new FakeGatewayFileStorageClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(files, auth);
        using var request = AuthorizedRequest(HttpMethod.Get, "/api/console/v1/files/download-grants/download-grant-001/content");

        var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.Equal("hello", await response.Content.ReadAsStringAsync());
        Assert.Equal("download-grant-001", files.LastDownloadContentGrantId);
        Assert.Equal(GatewayPermissions.FilesRead, auth.LastRequirement!.PermissionCode);
    }

    [Fact]
    public async Task File_storage_downstream_validation_error_preserves_status_and_message()
    {
        var files = new FakeGatewayFileStorageClient
        {
            ExceptionToThrow = new GatewayAuthException(HttpStatusCode.BadRequest, "upload session request is invalid")
        };
        await using var factory = CreateFactory(files);
        using var request = AuthorizedRequest(HttpMethod.Post, "/api/console/v1/files/upload-sessions");
        request.Content = JsonContent.Create(CreateUploadSessionRequest());

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<object>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.Success);
        Assert.Equal(400, envelope.Code);
        Assert.Equal("upload session request is invalid", envelope.Message);
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

    private static WebApplicationFactory<Program> CreateFactory(
        FakeGatewayFileStorageClient files,
        FakeGatewayAuthorizationClient? auth = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGatewayFileStorageClient>();
                services.AddSingleton<IGatewayFileStorageClient>(files);
                services.RemoveAll<IGatewayAuthorizationClient>();
                services.AddSingleton<IGatewayAuthorizationClient>(auth ?? FakeGatewayAuthorizationClient.Allowed());
            }));
    }

    private static HttpRequestMessage AuthorizedRequest(HttpMethod method, string requestUri)
    {
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new("Bearer", GatewayTestTokens.ValidAccessToken());
        return request;
    }

    private static CreateUploadSessionRequest CreateUploadSessionRequest() =>
        new(
            "org-001",
            "env-dev",
            new OwnerReference("notification", "message", "msg-001"),
            "notification-attachment",
            "example.csv",
            "text/csv",
            42,
            null);

    private sealed class FakeGatewayFileStorageClient : IGatewayFileStorageClient
    {
        public CreateUploadSessionRequest? LastCreateRequest { get; private set; }
        public string? LastCompleteUploadSessionId { get; private set; }
        public string? LastMetadataFileId { get; private set; }
        public string? LastDownloadGrantFileId { get; private set; }
        public string? LastTusHeadUploadSessionId { get; private set; }
        public string? LastTusPatchUploadSessionId { get; private set; }
        public string? LastDownloadContentGrantId { get; private set; }
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
            return Task.FromResult(FileMetadata());
        }

        public Task<FileMetadataResponse> GetFileMetadataAsync(string fileId, CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastMetadataFileId = fileId;
            return Task.FromResult(FileMetadata());
        }

        public Task<DownloadGrantResponse> CreateDownloadGrantAsync(
            string fileId,
            CreateDownloadGrantRequest request,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastDownloadGrantFileId = fileId;
            return Task.FromResult(new DownloadGrantResponse(
                "file-001",
                DateTimeOffset.UtcNow.AddMinutes(5),
                new TransferInstructions("/api/console/v1/files/download-grants/download-grant-001/content", new Dictionary<string, string>())));
        }

        public Task ProxyTusHeadAsync(
            string uploadSessionId,
            HttpResponse response,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastTusHeadUploadSessionId = uploadSessionId;
            response.Headers["Tus-Resumable"] = "1.0.0";
            response.Headers["Upload-Offset"] = "0";
            response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        }

        public Task ProxyTusPatchAsync(
            string uploadSessionId,
            HttpRequest request,
            HttpResponse response,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastTusPatchUploadSessionId = uploadSessionId;
            response.Headers["Tus-Resumable"] = "1.0.0";
            response.Headers["Upload-Offset"] = "3";
            response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        }

        public async Task ProxyDownloadGrantContentAsync(
            string downloadGrantId,
            HttpResponse response,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastDownloadContentGrantId = downloadGrantId;
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

    private static FileMetadataResponse FileMetadata() =>
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
            "pending",
            "completed",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow);

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization));
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri RequestUri,
        AuthenticationHeaderValue? Authorization);

    private sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);

    private static async Task<T> ReadResponseDataAsync<T>(HttpResponseMessage response)
    {
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>();
        Assert.NotNull(envelope);
        Assert.True(envelope.Success, envelope.Message);
        Assert.NotNull(envelope.Data);
        return envelope.Data;
    }
}
