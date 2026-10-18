using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessConsoleQualityEvidenceUploadFacadeTests
{
    private const string NcrId = "b871553a-d3e0-45d8-960b-df0e9c2eac88";
    private const string Sessions = "/api/business-console/v1/files/quality-evidence/upload-sessions";
    private const string Tus = "/api/business-console/v1/files/quality-evidence/tus/ups-quality-1";
    private const string Detail = "/api/business-console/v1/quality/ncrs/" + NcrId;

    [Fact]
    public async Task Minimal_quality_permissions_upload_fixed_ncr_evidence_and_read_public_attachments()
    {
        var handler = new Downstream();
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(
            BusinessGatewayPermissions.QualityNcrRead, BusinessGatewayPermissions.QualityNcrManage);
        await using var lease = Lease(auth, handler);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        var creation = await client.PostAsJsonAsync(Sessions, CreateRequest());
        Assert.Equal(HttpStatusCode.OK, creation.StatusCode);
        var payload = await creation.Content.ReadAsStringAsync();
        Assert.DoesNotContain("/api/files/v1", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("objectKey", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("downloadGrant", payload, StringComparison.OrdinalIgnoreCase);
        using var session = JsonDocument.Parse(payload);
        Assert.Equal(Tus, session.RootElement.GetProperty("data").GetProperty("uploadUrl").GetString());
        using var ownerBody = JsonDocument.Parse(handler.CreatedSession!);
        Assert.Equal("quality-evidence", ownerBody.RootElement.GetProperty("filePurpose").GetString());
        var owner = ownerBody.RootElement.GetProperty("owner");
        Assert.Equal("business-quality", owner.GetProperty("ownerService").GetString());
        Assert.Equal("nonconformance-report", owner.GetProperty("ownerType").GetString());
        Assert.Equal(NcrId, owner.GetProperty("ownerId").GetString());
        Assert.Equal(1, handler.NcrReads);

        using var headRequest = Scoped(HttpMethod.Head, Tus);
        var head = await client.SendAsync(headRequest);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal("0", head.Headers.GetValues("Upload-Offset").Single());
        using var patchRequest = Scoped(HttpMethod.Patch, Tus);
        patchRequest.Headers.Add("Upload-Offset", "0");
        patchRequest.Content = new ByteArrayContent(Downstream.Content);
        patchRequest.Content.Headers.ContentType = new("application/offset+octet-stream");
        var patch = await client.SendAsync(patchRequest);
        Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);
        Assert.Equal(Downstream.Content, handler.Bytes);
        var completion = await client.PostAsJsonAsync(Sessions + "/ups-quality-1/complete", CompleteRequest());
        Assert.Equal(HttpStatusCode.OK, completion.StatusCode);
        using var completed = JsonDocument.Parse(await completion.Content.ReadAsStringAsync());
        Assert.Equal("file-quality-1", completed.RootElement.GetProperty("data").GetProperty("fileId").GetString());
        Assert.Equal("quality-evidence", handler.CompletedPurpose);

        var submitted = await client.PostAsJsonAsync(Detail + "/disposition", new
        {
            organizationId = "org-001", environmentId = "env-dev", ncrId = NcrId,
            dispositionType = "sort-and-screen", attachmentFileIds = new[] { "file-quality-1" },
        });
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var detail = await client.GetAsync(Detail + "?organizationId=org-001&environmentId=env-dev");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var readback = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        var data = readback.RootElement.GetProperty("data");
        Assert.Equal(NcrId, data.GetProperty("id").GetString());
        Assert.Equal("disposition-in-progress", data.GetProperty("status").GetString());
        Assert.Equal("file-quality-1", Assert.Single(data.GetProperty("attachmentFileIds").EnumerateArray()).GetString());
    }

    [Theory]
    [InlineData("POST", Sessions)]
    [InlineData("POST", Sessions + "/ups-quality-1/complete")]
    [InlineData("HEAD", Tus)]
    [InlineData("PATCH", Tus)]
    public async Task Read_only_quality_user_cannot_upload(string method, string path)
    {
        var handler = new Downstream();
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.QualityNcrRead), handler);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var request = Scoped(new HttpMethod(method), path);
        if (method == "POST") request.Content = JsonContent.Create(CreateRequest());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("other-org", "env-dev")]
    [InlineData("org-001", "other-env")]
    public async Task All_upload_operations_reject_context_outside_authenticated_scope(string organization, string environment)
    {
        var handler = new Downstream();
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.Allowed(), handler);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        foreach (var path in new[] { Sessions, Sessions + "/ups-quality-1/complete" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(path,
                new { organizationId = organization, environmentId = environment, ncrId = NcrId,
                    fileName = "evidence.png", contentType = "image/png", expectedSizeBytes = 8 })).StatusCode);
        foreach (var method in new[] { HttpMethod.Head, HttpMethod.Patch })
        {
            using var request = Scoped(method, Tus, organization, environment);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
        }
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Missing_or_cross_tenant_ncr_prevents_session_creation_and_completion()
    {
        var handler = new Downstream { NcrExists = false };
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.Allowed(), handler);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(Sessions, CreateRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(Sessions + "/ups-quality-1/complete", CompleteRequest())).StatusCode);
        Assert.Equal(0, handler.FileCalls);
    }

    [Theory]
    [InlineData("HEAD")]
    [InlineData("PATCH")]
    public async Task Tus_rejects_a_session_from_another_file_surface(string method)
    {
        var handler = new Downstream { SessionPurpose = "engineering-document" };
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.Allowed(), handler);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var request = Scoped(new HttpMethod(method), Tus);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(request)).StatusCode);
        Assert.Empty(handler.Bytes);
        Assert.Equal(1, handler.FileCalls);
    }

    [Theory]
    [InlineData("purpose")]
    [InlineData("owner")]
    [InlineData("organization")]
    [InlineData("environment")]
    [InlineData("unavailable")]
    public async Task Complete_does_not_return_a_file_reference_for_wrong_ncr_or_scope(string failure)
    {
        var handler = new Downstream { CompletionFailure = failure };
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.Allowed(), handler);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(
            Sessions + "/ups-quality-1/complete", CompleteRequest())).StatusCode);
    }

    [Fact]
    public async Task Public_tus_contract_preserves_head_status_and_binary_patch()
    {
        using var doc = JsonDocument.Parse(await BusinessGatewayTestHost.GetOpenApiDocumentAsync());
        var tus = doc.RootElement.GetProperty("paths").GetProperty(
            "/api/business-console/v1/files/quality-evidence/tus/{uploadSessionId}");
        Assert.True(tus.GetProperty("head").GetProperty("responses").TryGetProperty("200", out _));
        Assert.Equal("binary", tus.GetProperty("patch").GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/offset+octet-stream").GetProperty("schema").GetProperty("format").GetString());
    }

    private static object CreateRequest() => new
    {
        organizationId = "org-001", environmentId = "env-dev", ncrId = NcrId,
        fileName = "evidence.png", contentType = "image/png", expectedSizeBytes = Downstream.Content.Length,
        filePurpose = "engineering-document", owner = new { ownerService = "forged", ownerType = "forged", ownerId = "forged" },
    };
    private static object CompleteRequest() => new
    { organizationId = "org-001", environmentId = "env-dev", ncrId = NcrId, sizeBytes = Downstream.Content.Length };

    private static HttpRequestMessage Scoped(HttpMethod method, string path, string org = "org-001", string env = "env-dev")
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Organization-Id", org);
        request.Headers.Add("X-Environment-Id", env);
        request.Headers.Add("Tus-Resumable", "1.0.0");
        if (method == HttpMethod.Patch)
        {
            request.Content = new ByteArrayContent(Downstream.Content);
            request.Content.Headers.ContentType = new("application/offset+octet-stream");
        }
        return request;
    }

    private static BusinessGatewayTestHostLease Lease(FakeBusinessGatewayAuthorizationClient auth, Downstream handler) =>
        BusinessGatewayTestHost.Lease(auth, services =>
        {
            var http = new HttpClient(handler) { BaseAddress = new Uri("http://downstream.test") };
            services.RemoveAll<IBusinessFileStorageClient>();
            services.AddSingleton<IBusinessFileStorageClient>(new HttpBusinessFileStorageClient(http));
            services.RemoveAll<IBusinessFileTransferClient>();
            services.AddSingleton<IBusinessFileTransferClient>(new HttpBusinessFileTransferClient(http));
            services.RemoveAll<IBusinessQualityClient>();
            services.AddSingleton<IBusinessQualityClient>(new HttpBusinessQualityClient(http));
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });

    // 实际 Gateway HTTP/JSON/字节代理 + 下游 HTTP fixture；不是 FileStorage provider 或真实栈证据。
    private sealed class Downstream : HttpMessageHandler
    {
        public static readonly byte[] Content = "evidence"u8.ToArray();
        public int Calls { get; private set; }
        public int FileCalls { get; private set; }
        public int NcrReads { get; private set; }
        public string? CreatedSession { get; private set; }
        public string? CompletedPurpose { get; private set; }
        public byte[] Bytes { get; private set; } = [];
        public bool NcrExists { get; init; } = true;
        public string SessionPurpose { get; init; } = "quality-evidence";
        public string? CompletionFailure { get; init; }
        private string[] attachments = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal("Bearer internal-test-token", request.Headers.Authorization!.ToString());
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/api/business/v1/quality/ncrs/", StringComparison.Ordinal))
            {
                if (request.Method == HttpMethod.Get)
                {
                    NcrReads++;
                    Assert.Contains("organizationId=org-001", request.RequestUri.Query, StringComparison.Ordinal);
                    Assert.Contains("environmentId=env-dev", request.RequestUri.Query, StringComparison.Ordinal);
                    if (!NcrExists) return new HttpResponseMessage(HttpStatusCode.NotFound);
                    return Envelope(new { ncrId = NcrId, ncrCode = "NCR-001", sourceType = "mes-defect",
                        sourceDocumentId = "DEFECT-001", skuCode = "SKU-001", defectQuantity = 1,
                        defectReason = "scratch", status = attachments.Length > 0 ? "disposition-in-progress" : "open",
                        reworkWorkOrderCreationStatus = "not-requested", attachmentFileIds = attachments });
                }
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal("sort-and-screen", body.RootElement.GetProperty("dispositionType").GetString());
                attachments = body.RootElement.GetProperty("attachmentFileIds").EnumerateArray().Select(x => x.GetString()!).ToArray();
                return Envelope(new { accepted = true });
            }
            FileCalls++;
            if (path == "/api/files/v1/upload-sessions")
            {
                CreatedSession = await request.Content!.ReadAsStringAsync(ct);
                return Json(new CreateUploadSessionResponse("ups-quality-1", "file-quality-1", "tus", "tus",
                    DateTimeOffset.Parse("2026-10-18T12:00:00Z"), new TransferInstructions(
                        "/api/files/v1/tus/ups-quality-1", new Dictionary<string, string>())));
            }
            if (path == "/api/files/v1/tus/ups-quality-1")
            {
                Assert.Equal("quality-evidence", request.Headers.GetValues("X-File-Purpose").Single());
                Assert.Equal("org-001", request.Headers.GetValues("X-Organization-Id").Single());
                Assert.Equal("env-dev", request.Headers.GetValues("X-Environment-Id").Single());
                if (SessionPurpose != "quality-evidence") return new HttpResponseMessage(HttpStatusCode.NotFound);
                if (request.Method == HttpMethod.Patch) Bytes = await request.Content!.ReadAsByteArrayAsync(ct);
                var response = new HttpResponseMessage(request.Method == HttpMethod.Head ? HttpStatusCode.OK : HttpStatusCode.NoContent);
                response.Headers.Add("Upload-Offset", Bytes.Length.ToString());
                return response;
            }
            if (path.EndsWith("/complete", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                CompletedPurpose = body.RootElement.GetProperty("filePurpose").GetString();
                var file = new FileMetadataResponse("file-quality-1", "org-001", "env-dev",
                    new OwnerReference("business-quality", "nonconformance-report", NcrId), "quality-evidence",
                    "evidence.png", "image/png", Content.Length, null, "available",
                    DateTimeOffset.Parse("2026-10-18T00:00:00Z"), DateTimeOffset.Parse("2026-10-18T00:00:00Z"));
                file = CompletionFailure switch
                {
                    "purpose" => file with { FilePurpose = "engineering-document" },
                    "owner" => file with { Owner = file.Owner with { OwnerId = "other-ncr" } },
                    "organization" => file with { OrganizationId = "other-org" },
                    "environment" => file with { EnvironmentId = "other-env" },
                    "unavailable" => file with { Status = "uploading" },
                    _ => file,
                };
                return Json(file);
            }
            throw new InvalidOperationException($"Unexpected request: {request.Method} {path}");
        }
        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
        private static HttpResponseMessage Envelope<T>(T value) => Json(new { data = value, success = true, code = 0, message = "" });
    }
}
