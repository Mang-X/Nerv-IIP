using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

/// <summary>
/// #3085 交接班附件门面。
///
/// 客户端层用 <see cref="StubHandler"/>——它看得见**发往 FileStorage 的线上形状**。端点层的
/// <c>RecordingBusinessFileStorageClient</c> 桩看不见这一层，所以「HEAD 被当 PATCH 发」「content 腿
/// 丢 org/env 头」这类今天就能 ship 的 bug 只有这一层能红（#3096 审核 C3）。
/// </summary>
public sealed class BusinessConsoleShiftHandoverAttachmentFacadeTests
{
    private const string UploadSessionsRoute =
        "/api/business-console/v1/files/shift-handover-attachments/upload-sessions";

    private const string TusRoute =
        "/api/business-console/v1/files/shift-handover-attachments/tus/ups-handover-1";

    private const string ContentRoute =
        "/api/business-console/v1/files/shift-handover-attachments/file-handover-1/content";

    // =====================================================================
    // 客户端层：发往 FileStorage 的线上形状
    // =====================================================================

    [Fact]
    public async Task Upload_session_fixes_the_purpose_and_owner_and_returns_a_gateway_owned_tus_url()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/files/v1/upload-sessions" => Json(UploadSession("tus", "/api/files/v1/tus/ups-handover-1")),
            var path => throw new InvalidOperationException($"Unexpected downstream call: {path}"),
        });
        var client = CreateClient(handler);

        var session = await client.CreateShiftHandoverAttachmentUploadSessionAsync(
            "internal-test-token",
            "user-admin",
            UploadRequest(),
            CancellationToken.None);

        using var forwarded = JsonDocument.Parse(handler.Bodies[0]!);
        Assert.Equal("shift-handover-photo", forwarded.RootElement.GetProperty("filePurpose").GetString());
        var owner = forwarded.RootElement.GetProperty("owner");
        Assert.Equal("business-mes", owner.GetProperty("ownerService").GetString());
        Assert.Equal("shift-handover-attachment", owner.GetProperty("ownerType").GetString());
        Assert.Equal("user-admin", owner.GetProperty("ownerId").GetString());
        Assert.Equal("Bearer internal-test-token", handler.Requests[0].Headers.Authorization!.ToString());

        Assert.Equal("tus", session.UploadProtocol);
        Assert.Equal(
            "/api/business-console/v1/files/shift-handover-attachments/tus/ups-handover-1",
            session.UploadUrl);
    }

    // 仓库默认 FileStorage:UploadProvider 就是 server-proxy，而 server-proxy 没有任何字节 endpoint。
    [Fact]
    public async Task Upload_session_fails_closed_when_file_storage_is_not_running_the_tus_protocol()
    {
        var client = CreateClient(new StubHandler(_ =>
            Json(UploadSession("server-proxy", "/api/files/v1/upload/ups-handover-1"))));

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() =>
            client.CreateShiftHandoverAttachmentUploadSessionAsync(
                "internal-test-token", "user-admin", UploadRequest(), CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Equal("filestorage-upload-protocol-unsupported", exception.Message);
    }

    // ADR 0023 决策 1.3 / ADR 0030：调用方不得取得 FileStorage 内部地址。姊妹网关同型用例：
    // GatewayConsoleFileStorageTests.File_storage_http_client_rejects_* 。
    [Theory]
    [InlineData("https://filestorage.internal/api/files/v1/tus/ups-handover-1")]
    [InlineData("//filestorage.internal/api/files/v1/tus/ups-handover-1")]
    [InlineData("/api/files/v2/tus/ups-handover-1")]
    public async Task Upload_session_refuses_a_transfer_url_that_is_not_a_proxyable_internal_path(string downstreamUrl)
    {
        var client = CreateClient(new StubHandler(_ => Json(UploadSession("tus", downstreamUrl))));

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() =>
            client.CreateShiftHandoverAttachmentUploadSessionAsync(
                "internal-test-token", "user-admin", UploadRequest(), CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Equal("filestorage-transfer-url-not-proxyable", exception.Message);
    }

    [Fact]
    public async Task Complete_declares_the_shift_handover_purpose_and_returns_the_attachment_record()
    {
        var handler = new StubHandler(_ => Json(FileMetadata("shift-handover-photo")));
        var client = CreateClient(handler);

        var attachment = await client.CompleteShiftHandoverAttachmentUploadAsync(
            "internal-test-token",
            "ups-handover-1",
            new BusinessConsoleCompleteShiftHandoverAttachmentUploadRequest("org-001", "env-dev", null, 2048),
            CancellationToken.None);

        Assert.Equal(
            "/api/files/v1/upload-sessions/ups-handover-1/complete",
            handler.Requests[0].RequestUri!.AbsolutePath);
        using var forwarded = JsonDocument.Parse(handler.Bodies[0]!);
        Assert.Equal("shift-handover-photo", forwarded.RootElement.GetProperty("filePurpose").GetString());
        Assert.Equal(
            new BusinessConsoleMesShiftHandoverAttachment("file-handover-1", "handover.png", "image/png", 2048),
            attachment);
    }

    // #3096 审核 C1：下游把「上传内容与声明的文件类型不匹配」这类可行动原因写在 message 里，
    // 塌成单一 code 会让调用方看不到该改什么。
    [Fact]
    public async Task Complete_preserves_the_downstream_reason_instead_of_flattening_it()
    {
        var client = CreateClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new
            {
                code = "file-storage-bad-request",
                message = "上传内容与声明的文件类型不匹配。",
            }),
        }));

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() =>
            client.CompleteShiftHandoverAttachmentUploadAsync(
                "internal-test-token",
                "ups-handover-1",
                new BusinessConsoleCompleteShiftHandoverAttachmentUploadRequest("org-001", "env-dev", null, 2048),
                CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("上传内容与声明的文件类型不匹配。", exception.Message);
    }

    // #3085 的下载口径：FileStorage 的 download-grant 不看用途，门面不看就等于把交接班读权限
    // 变成通用文件读权限。
    [Fact]
    public async Task Content_refuses_a_file_whose_purpose_is_not_a_shift_handover_photo()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/files/v1/files/file-sop-v2" => Json(FileMetadata("engineering-document", "file-sop-v2")),
            var path => throw new InvalidOperationException($"Unexpected downstream call: {path}"),
        });
        var client = CreateClient(handler);
        var httpContext = ResponseContext();

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() =>
            client.StreamShiftHandoverAttachmentContentAsync(
                "internal-test-token", "file-sop-v2", "org-001", "env-dev", httpContext.Response, CancellationToken.None));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("filestorage-file-not-shift-handover-attachment", exception.Message);
        // 用途不符时不得向 FileStorage 签发 download grant。
        Assert.Single(handler.Requests);
    }

    // #3096 审核 C3(a)：content 腿必须把 org/env 带到下游 content 端点，否则真实 FileStorage 必 400。
    [Fact]
    public async Task Content_signs_the_grant_server_side_and_forwards_the_transfer_headers_downstream()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/files/v1/files/file-handover-1" => Json(FileMetadata("shift-handover-photo")),
            "/api/files/v1/files/file-handover-1/download-grants" => Json(new DownloadGrantResponse(
                "file-handover-1",
                DateTimeOffset.Parse("2026-09-02T08:10:00Z"),
                new TransferInstructions(
                    "/api/files/v1/download-grants/grant-handover-1/content",
                    new Dictionary<string, string>
                    {
                        ["X-Organization-Id"] = "org-001",
                        ["X-Environment-Id"] = "env-dev",
                    }))),
            "/api/files/v1/download-grants/grant-handover-1/content" =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("photo"u8.ToArray()) },
            var path => throw new InvalidOperationException($"Unexpected downstream call: {path}"),
        });
        var client = CreateClient(handler);
        var httpContext = ResponseContext();

        await client.StreamShiftHandoverAttachmentContentAsync(
            "internal-test-token", "file-handover-1", "org-001", "env-dev", httpContext.Response, CancellationToken.None);

        // grant 请求体带的是调用方上下文
        using var grantBody = JsonDocument.Parse(handler.Bodies[1]!);
        Assert.Equal("org-001", grantBody.RootElement.GetProperty("organizationId").GetString());
        Assert.Equal("env-dev", grantBody.RootElement.GetProperty("environmentId").GetString());

        // content 请求必须带 FileStorage 要求的传输头
        var contentRequest = handler.Requests[2];
        Assert.Equal(HttpMethod.Get, contentRequest.Method);
        Assert.Equal("org-001", contentRequest.Headers.GetValues("X-Organization-Id").Single());
        Assert.Equal("env-dev", contentRequest.Headers.GetValues("X-Environment-Id").Single());

        httpContext.Response.Body.Position = 0;
        Assert.Equal("photo", new StreamReader(httpContext.Response.Body).ReadToEnd());
    }

    [Fact]
    public async Task Content_refuses_to_follow_a_grant_url_that_is_not_a_proxyable_internal_path()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/files/v1/files/file-handover-1" => Json(FileMetadata("shift-handover-photo")),
            "/api/files/v1/files/file-handover-1/download-grants" => Json(new DownloadGrantResponse(
                "file-handover-1",
                DateTimeOffset.Parse("2026-09-02T08:10:00Z"),
                new TransferInstructions(
                    "https://filestorage.internal/api/files/v1/download-grants/grant-handover-1/content",
                    new Dictionary<string, string>()))),
            var path => throw new InvalidOperationException($"Unexpected downstream call: {path}"),
        });
        var client = CreateClient(handler);
        var httpContext = ResponseContext();

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() =>
            client.StreamShiftHandoverAttachmentContentAsync(
                "internal-test-token", "file-handover-1", "org-001", "env-dev", httpContext.Response, CancellationToken.None));

        Assert.Equal("filestorage-transfer-url-not-proxyable", exception.Message);
        // 拒绝必须发生在跟随之前
        Assert.Equal(2, handler.Requests.Count);
    }

    // #3096 审核 C3(b)：HEAD 探测若被当 PATCH 发出去，断点续传偏移会被写坏。
    [Fact]
    public async Task Tus_head_probe_is_sent_as_http_head_without_a_body()
    {
        var handler = new StubHandler(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.NoContent);
            response.Headers.TryAddWithoutValidation("Tus-Resumable", "1.0.0");
            response.Headers.TryAddWithoutValidation("Upload-Offset", "512");
            response.Content = new ByteArrayContent([]);
            return response;
        });
        var client = CreateClient(handler);
        var httpContext = ResponseContext();

        await client.ProxyShiftHandoverAttachmentTusHeadAsync(
            "internal-test-token", "ups-handover-1", httpContext.Response, CancellationToken.None);

        var sent = handler.Requests[0];
        Assert.Equal(HttpMethod.Head, sent.Method);
        Assert.Equal("/api/files/v1/tus/ups-handover-1", sent.RequestUri!.AbsolutePath);
        Assert.Null(handler.Bodies[0]);
        Assert.Equal(StatusCodes.Status204NoContent, httpContext.Response.StatusCode);
        Assert.Equal("512", httpContext.Response.Headers["Upload-Offset"]);
    }

    [Fact]
    public async Task Tus_patch_proxy_forwards_the_resume_headers_and_the_chunk_bytes()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("/api/files/v1/tus/ups-handover-1", request.RequestUri!.AbsolutePath);
            Assert.Equal("1.0.0", request.Headers.GetValues("Tus-Resumable").Single());
            Assert.Equal("512", request.Headers.GetValues("Upload-Offset").Single());
            Assert.Equal("sha256 abc", request.Headers.GetValues("Upload-Checksum").Single());
            var response = new HttpResponseMessage(HttpStatusCode.NoContent);
            response.Headers.TryAddWithoutValidation("Upload-Offset", "1024");
            response.Content = new ByteArrayContent([]);
            return response;
        });
        var client = CreateClient(handler);
        var httpContext = ResponseContext();
        httpContext.Request.Method = "PATCH";
        httpContext.Request.ContentType = "application/offset+octet-stream";
        httpContext.Request.Body = new MemoryStream("chunk"u8.ToArray());
        httpContext.Request.Headers["Tus-Resumable"] = "1.0.0";
        httpContext.Request.Headers["Upload-Offset"] = "512";
        httpContext.Request.Headers["Upload-Checksum"] = "sha256 abc";

        await client.ProxyShiftHandoverAttachmentTusPatchAsync(
            "internal-test-token", "ups-handover-1", httpContext.Request, httpContext.Response, CancellationToken.None);

        Assert.Equal("chunk", handler.Bodies[0]);
        Assert.Equal(StatusCodes.Status204NoContent, httpContext.Response.StatusCode);
        Assert.Equal("1024", httpContext.Response.Headers["Upload-Offset"]);
    }

    // 真栈实测出来的输入：business-gateway 拿不到 FileStorage 地址时连接直接失败。
    // 不映射的话异常逃逸成 500「未知错误」，调用方无从判断是下游不可用。
    [Theory]
    [InlineData("transport")]
    [InlineData("timeout")]
    public async Task Byte_paths_report_downstream_unavailability_instead_of_leaking_a_transport_failure(string failure)
    {
        var client = CreateClient(new StubHandler(_ => failure == "transport"
            ? throw new HttpRequestException("connection refused")
            : throw new TaskCanceledException("timed out")));
        var httpContext = ResponseContext();

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() =>
            client.ProxyShiftHandoverAttachmentTusHeadAsync(
                "internal-test-token", "ups-handover-1", httpContext.Response, CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal(failure == "transport" ? "downstream-unavailable" : "downstream-timeout", exception.Message);
    }

    // =====================================================================
    // 端点层：权限口径与门面接线
    // =====================================================================

    [Fact]
    public async Task Upload_session_endpoint_binds_the_owner_to_the_authenticated_principal()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var files = new RecordingBusinessFileStorageClient();
        await using var lease = LeaseHost(auth, files);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.PostAsJsonAsync(UploadSessionsRoute, new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            fileName = "handover.jpg",
            contentType = "image/jpeg",
            expectedSizeBytes = 2048,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.MesHandoversManage, auth.LastRequirement!.PermissionCode);
        Assert.Equal("mes-shift-handover-attachment", auth.LastRequirement.ResourceType);
        Assert.Equal("user-admin", files.LastUploadOwnerId);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("tus", data.GetProperty("uploadProtocol").GetString());
        Assert.Equal(
            "/api/business-console/v1/files/shift-handover-attachments/tus/ups-handover-1",
            data.GetProperty("uploadUrl").GetString());
    }

    [Fact]
    public async Task Complete_endpoint_returns_the_attachment_record_the_handover_write_face_consumes()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var files = new RecordingBusinessFileStorageClient();
        await using var lease = LeaseHost(auth, files);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.PostAsJsonAsync(
            $"{UploadSessionsRoute}/ups-handover-1/complete",
            new { organizationId = "org-001", environmentId = "env-dev", sizeBytes = 2048 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.MesHandoversManage, auth.LastRequirement!.PermissionCode);
        Assert.Equal("ups-handover-1", files.LastCompletedUploadSessionId);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("file-handover-1", data.GetProperty("fileId").GetString());
        Assert.Equal("handover.jpg", data.GetProperty("fileName").GetString());
        Assert.Equal(2048, data.GetProperty("sizeBytes").GetInt64());
    }

    [Theory]
    [InlineData("HEAD")]
    [InlineData("PATCH")]
    public async Task Tus_endpoints_reach_the_matching_proxy_leg_under_the_handover_write_permission(string method)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var files = new RecordingBusinessFileStorageClient();
        await using var lease = LeaseHost(auth, files);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var request = new HttpRequestMessage(new HttpMethod(method), TusRoute);
        request.Headers.Add("X-Organization-Id", "org-001");
        request.Headers.Add("X-Environment-Id", "env-dev");
        if (method == "PATCH")
        {
            request.Content = new ByteArrayContent("chunk"u8.ToArray());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.MesHandoversManage, auth.LastRequirement!.PermissionCode);
        // HEAD 与 PATCH 必须落到各自那条腿上，不能共用一个按可空性分派的方法。
        Assert.Equal(method == "HEAD" ? "ups-handover-1" : null, files.LastTusHeadUploadSessionId);
        Assert.Equal(method == "PATCH" ? "ups-handover-1" : null, files.LastTusPatchUploadSessionId);
    }

    [Fact]
    public async Task Tus_offset_endpoint_rejects_a_principal_without_the_handover_write_permission()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.MesHandoversRead);
        var files = new RecordingBusinessFileStorageClient();
        await using var lease = LeaseHost(auth, files);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var request = new HttpRequestMessage(HttpMethod.Head, TusRoute);
        request.Headers.Add("X-Organization-Id", "org-001");
        request.Headers.Add("X-Environment-Id", "env-dev");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.MesHandoversManage, auth.LastRequirement!.PermissionCode);
        Assert.Null(files.LastTusHeadUploadSessionId);
    }

    // #3085 票面写死的失败输入：只持 handovers.read 的主体在旧口径下换不出下载地址。
    [Fact]
    public async Task A_principal_holding_only_handovers_read_can_reach_the_attachment_bytes()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.MesHandoversRead);
        var files = new RecordingBusinessFileStorageClient();
        await using var lease = LeaseHost(auth, files);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var request = new HttpRequestMessage(HttpMethod.Get, ContentRoute);
        request.Headers.Add("X-Organization-Id", "org-001");
        request.Headers.Add("X-Environment-Id", "env-dev");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("handover photo bytes", await response.Content.ReadAsStringAsync());
        Assert.Equal(BusinessGatewayPermissions.MesHandoversRead, auth.LastRequirement!.PermissionCode);
        Assert.Equal("mes-shift-handover-attachment", auth.LastRequirement.ResourceType);
        Assert.Equal("file-handover-1", files.LastAttachmentContentFileId);
        Assert.Equal("org-001", files.LastAttachmentContentOrganizationId);
        Assert.Equal("env-dev", files.LastAttachmentContentEnvironmentId);
    }

    // 反向：SOP 下载面不因为本票而对交接班读者开门。
    [Fact]
    public async Task A_principal_holding_only_handovers_read_still_cannot_use_the_sop_download_face()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.MesHandoversRead);
        var files = new RecordingBusinessFileStorageClient();
        await using var lease = LeaseHost(auth, files);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.PostAsJsonAsync(
            "/api/business-console/v1/files/file-sop-v2/download-grants",
            new { organizationId = "org-001", environmentId = "env-dev" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.EngineeringDocumentsRead, auth.LastRequirement!.PermissionCode);
    }

    /// <summary>
    /// #3096 审核 A1 的具体越权序列：主体 Q 持 <c>engineering.documents.read</c> 从 SOP 面开出 grant，
    /// 把 grantId 交给只持 <c>handovers.read</c> 的主体 P。本门面不得给 P 任何以 grantId 为入参的
    /// 兑换路由——否则 P 能取到 SOP 字节。修法是结构性的：交接班面根本不暴露 grant id。
    /// </summary>
    [Fact]
    public async Task No_shift_handover_route_redeems_a_download_grant_id_issued_by_another_face()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.MesHandoversRead);
        var files = new RecordingBusinessFileStorageClient();
        await using var lease = LeaseHost(auth, files);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        foreach (var route in new[]
        {
            "/api/business-console/v1/files/shift-handover-attachments/download-grants/grant-sop-v2/content",
            "/api/business-console/v1/files/shift-handover-attachments/grant-sop-v2/download-grants",
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, route);
            request.Headers.Add("X-Organization-Id", "org-001");
            request.Headers.Add("X-Environment-Id", "env-dev");

            var response = await client.SendAsync(request);

            Assert.True(
                response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                $"{route} 不得成为 grant id 的兑换面，实际 {(int)response.StatusCode}");
        }

        Assert.Null(files.LastAttachmentContentFileId);
    }

    // =====================================================================
    // 夹具
    // =====================================================================

    private static BusinessConsoleCreateShiftHandoverAttachmentUploadSessionRequest UploadRequest() =>
        new("org-001", "env-dev", "handover.png", "image/png", 2048);

    private static CreateUploadSessionResponse UploadSession(string provider, string uploadUrl) =>
        new(
            "ups-handover-1",
            "file-handover-1",
            provider,
            provider,
            DateTimeOffset.Parse("2026-09-02T08:15:00Z"),
            new TransferInstructions(uploadUrl, new Dictionary<string, string> { ["x-nerv-upload-mode"] = provider }));

    private static FileMetadataResponse FileMetadata(string purpose, string fileId = "file-handover-1") =>
        new(
            fileId,
            "org-001",
            "env-dev",
            new OwnerReference("business-mes", "shift-handover-attachment", "user-admin"),
            purpose,
            "handover.png",
            "image/png",
            2048,
            null,
            "available",
            DateTimeOffset.Parse("2026-09-02T08:00:00Z"),
            DateTimeOffset.Parse("2026-09-02T08:01:00Z"));

    private static DefaultHttpContext ResponseContext()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        return httpContext;
    }

    private static BusinessGatewayTestHostLease LeaseHost(
        FakeBusinessGatewayAuthorizationClient auth,
        IBusinessFileStorageClient files) =>
        BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessFileStorageClient>();
            services.AddSingleton(files);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(
                new TestInternalServiceTokenProvider("internal-test-token"));
        });

    private static HttpBusinessFileStorageClient CreateClient(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://file-storage.local") });

    private static HttpResponseMessage Json<T>(T payload) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string?> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken));
            Requests.Add(request);
            return responseFactory(request);
        }
    }
}
