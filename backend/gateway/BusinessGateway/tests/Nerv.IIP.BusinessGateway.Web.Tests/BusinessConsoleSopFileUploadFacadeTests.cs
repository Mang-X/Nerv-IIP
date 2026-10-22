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

public sealed class BusinessConsoleSopFileUploadFacadeTests
{
    private const string Sessions = "/api/business-console/v1/files/sop-documents/upload-sessions";
    private const string Tus = "/api/business-console/v1/files/sop-documents/tus/ups-sop-1";
    private const string Register = "/api/business-console/v1/engineering/documents";
    private static BusinessConsoleRegisterEngineeringDocumentRequest Registration() =>
        new("org-001", "env-dev", null, "A", "file-sop-1", "work-instruction.txt", "text/plain", "sop");

    [Fact]
    public async Task Tus_contract_describes_actual_success_and_raw_binary_patch()
    {
        using var doc = JsonDocument.Parse(await BusinessGatewayTestHost.GetOpenApiDocumentAsync());
        var tus = doc.RootElement.GetProperty("paths").GetProperty("/api/business-console/v1/files/sop-documents/tus/{uploadSessionId}");
        Assert.True(tus.GetProperty("head").GetProperty("responses").TryGetProperty("200", out _));
        Assert.False(tus.GetProperty("head").GetProperty("responses").TryGetProperty("204", out _));
        var body = tus.GetProperty("patch").GetProperty("requestBody");
        Assert.True(body.GetProperty("required").GetBoolean());
        Assert.Equal("binary", body.GetProperty("content").GetProperty("application/offset+octet-stream").GetProperty("schema").GetProperty("format").GetString());
    }

    [Fact]
    public async Task Authenticated_upload_uses_fixed_owner_and_gateway_tus_then_registers_the_completed_file()
    {
        var downstream = new FileStorageHandler();
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.Allowed(), downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        var session = await client.PostAsJsonAsync(Sessions, new
        {
            organizationId = "org-001", environmentId = "env-dev", fileName = "work-instruction.txt",
            contentType = "text/plain", expectedSizeBytes = FileStorageHandler.Content.Length,
            ownerId = "caller-forged", filePurpose = "shift-handover-photo",
        });
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        using var response = JsonDocument.Parse(await session.Content.ReadAsStringAsync());
        Assert.Equal(Tus, response.RootElement.GetProperty("data").GetProperty("uploadUrl").GetString());
        using var creation = JsonDocument.Parse(downstream.CreatedSession!);
        Assert.Equal("engineering-document", creation.RootElement.GetProperty("filePurpose").GetString());
        var owner = creation.RootElement.GetProperty("owner");
        Assert.Equal("business-product-engineering", owner.GetProperty("ownerService").GetString());
        Assert.Equal("engineering-document", owner.GetProperty("ownerType").GetString());
        Assert.Equal("user-admin", owner.GetProperty("ownerId").GetString());

        using var headRequest = Scoped(HttpMethod.Head, Tus);
        var head = await client.SendAsync(headRequest);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal("0", head.Headers.GetValues("Upload-Offset").Single());
        using var patchRequest = Scoped(HttpMethod.Patch, Tus);
        patchRequest.Headers.Add("Upload-Offset", "0");
        patchRequest.Content = new ByteArrayContent(FileStorageHandler.Content);
        patchRequest.Content.Headers.ContentType = new("application/offset+octet-stream");
        var patch = await client.SendAsync(patchRequest);
        Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);
        Assert.Equal(FileStorageHandler.Content, downstream.Bytes);
        var complete = await client.PostAsJsonAsync(Sessions + "/ups-sop-1/complete",
            new { organizationId = "org-001", environmentId = "env-dev", sizeBytes = FileStorageHandler.Content.Length });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        using var completion = JsonDocument.Parse(downstream.Completion!);
        Assert.Equal("engineering-document", completion.RootElement.GetProperty("filePurpose").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Register, Registration())).StatusCode);
        using var contentRequest = Scoped(HttpMethod.Get,
            "/api/business-console/v1/files/sop-documents/file-sop-1/content");
        var content = await client.SendAsync(contentRequest);
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal(FileStorageHandler.Content, await content.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Tus_head_preserves_downstream_not_found_instead_of_reporting_success()
    {
        var downstream = new FileStorageHandler { HeadStatus = HttpStatusCode.NotFound };
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.Allowed(), downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var request = Scoped(HttpMethod.Head, Tus);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(request)).StatusCode);
        Assert.Equal(1, downstream.Calls);
    }

    [Theory]
    [InlineData("POST", Sessions)]
    [InlineData("POST", Sessions + "/ups-sop-1/complete")]
    [InlineData("HEAD", Tus)]
    [InlineData("PATCH", Tus)]
    [InlineData("POST", Register)]
    public async Task Read_permission_cannot_upload_or_register(string method, string path)
    {
        var downstream = new FileStorageHandler();
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.EngineeringDocumentsRead);
        await using var lease = Lease(auth, downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var request = Scoped(new HttpMethod(method), path);
        request.Content = path == Register ? JsonContent.Create(Registration()) : JsonContent.Create(new
        {
            organizationId = "org-001", environmentId = "env-dev", fileName = "work-instruction.txt",
            contentType = "text/plain", expectedSizeBytes = 100,
        });
        if (method == "PATCH")
        {
            request.Content = new ByteArrayContent(FileStorageHandler.Content);
            request.Content.Headers.ContentType = new("application/offset+octet-stream");
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
        Assert.Equal(BusinessGatewayPermissions.EngineeringDocumentsManage, auth.LastRequirement!.PermissionCode);
        Assert.Equal(0, downstream.Calls);
    }

    [Theory]
    [InlineData("shift-handover-photo", "org-001", "env-dev", "available", HttpStatusCode.NotFound)]
    [InlineData("engineering-document", "other-org", "env-dev", "available", HttpStatusCode.NotFound)]
    [InlineData("engineering-document", "org-001", "other-env", "available", HttpStatusCode.NotFound)]
    [InlineData("engineering-document", "org-001", "env-dev", "archived", HttpStatusCode.Conflict)]
    public async Task Registration_rejects_wrong_purpose_scope_or_unavailable_file(
        string purpose, string organization, string environment, string status, HttpStatusCode expected)
    {
        var downstream = new FileStorageHandler
        {
            Purpose = purpose, Organization = organization, Environment = environment, Status = status,
        };
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.Allowed(), downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        Assert.Equal(expected, (await client.PostAsJsonAsync(Register, Registration())).StatusCode);
        Assert.Equal(1, downstream.Calls);
        Assert.Equal(0, downstream.Engineering.WriteCallCount);
    }

    [Fact]
    public async Task Registration_before_upload_completion_does_not_forward_to_engineering()
    {
        var downstream = new FileStorageHandler { FileExists = false };
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.Allowed(), downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(Register, Registration())).StatusCode);
        Assert.Equal(0, downstream.Engineering.WriteCallCount);
    }

    [Theory]
    [InlineData("other-org", "env-dev")]
    [InlineData("org-001", "other-env")]
    public async Task Upload_and_completion_reject_context_outside_the_authenticated_scope(string organization, string environment)
    {
        var downstream = new FileStorageHandler();
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.Allowed(), downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        foreach (var path in new[] { Sessions, Sessions + "/ups-sop-1/complete" })
        {
            var response = await client.PostAsJsonAsync(path, new
            {
                organizationId = organization, environmentId = environment, fileName = "work-instruction.txt",
                contentType = "text/plain", expectedSizeBytes = 100,
            });
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        using var request = new HttpRequestMessage(HttpMethod.Patch, Tus);
        request.Content = new ByteArrayContent(FileStorageHandler.Content);
        request.Content.Headers.ContentType = new("application/offset+octet-stream");
        request.Headers.Add("X-Organization-Id", organization);
        request.Headers.Add("X-Environment-Id", environment);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
        Assert.Equal(0, downstream.Calls);
    }

    private static HttpRequestMessage Scoped(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Organization-Id", "org-001");
        request.Headers.Add("X-Environment-Id", "env-dev");
        request.Headers.Add("Tus-Resumable", "1.0.0");
        return request;
    }

    private static BusinessGatewayTestHostLease Lease(FakeBusinessGatewayAuthorizationClient auth, FileStorageHandler handler) =>
        BusinessGatewayTestHost.Lease(auth, services =>
        {
            var http = new HttpClient(handler) { BaseAddress = new Uri("http://file-storage.local") };
            services.RemoveAll<IBusinessFileStorageClient>();
            services.AddSingleton<IBusinessFileStorageClient>(new HttpBusinessFileStorageClient(http));
            services.RemoveAll<IBusinessFileTransferClient>();
            services.AddSingleton<IBusinessFileTransferClient>(new HttpBusinessFileTransferClient(http));
            services.RemoveAll<IBusinessProductEngineeringClient>();
            services.AddSingleton<IBusinessProductEngineeringClient>(handler.Engineering);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });

    // HTTP fixture: verifies the Gateway's actual JSON/byte clients; it is not a real FileStorage/provider proof.
    private sealed class FileStorageHandler : HttpMessageHandler
    {
        public static readonly byte[] Content = "SOP: tighten bolts to 12 Nm.\n"u8.ToArray();
        public RecordingProductEngineeringClient Engineering { get; } = new();
        public int Calls { get; private set; }
        public string? CreatedSession { get; private set; }
        public string? Completion { get; private set; }
        public byte[] Bytes { get; private set; } = [];
        public string Purpose { get; init; } = "engineering-document";
        public string Organization { get; init; } = "org-001";
        public string Environment { get; init; } = "env-dev";
        public string Status { get; init; } = "available";
        public bool FileExists { get; init; } = true;
        public HttpStatusCode HeadStatus { get; init; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal("Bearer internal-test-token", request.Headers.Authorization!.ToString());
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/files/v1/upload-sessions")
            {
                CreatedSession = await request.Content!.ReadAsStringAsync(ct);
                return Json(new CreateUploadSessionResponse("ups-sop-1", "file-sop-1", "tus", "tus",
                    DateTimeOffset.UtcNow.AddHours(1), new TransferInstructions("/api/files/v1/tus/ups-sop-1", new Dictionary<string, string>())));
            }
            if (path == "/api/files/v1/tus/ups-sop-1")
            {
                Assert.Equal("1.0.0", request.Headers.GetValues("Tus-Resumable").Single());
                Assert.Equal("engineering-document", request.Headers.GetValues("X-File-Purpose").Single());
                Assert.Equal("org-001", request.Headers.GetValues("X-Organization-Id").Single());
                Assert.Equal("env-dev", request.Headers.GetValues("X-Environment-Id").Single());
                if (request.Method == HttpMethod.Patch)
                {
                    Assert.Equal("0", request.Headers.GetValues("Upload-Offset").Single());
                    Assert.Equal("application/offset+octet-stream", request.Content!.Headers.ContentType!.MediaType);
                    Bytes = await request.Content.ReadAsByteArrayAsync(ct);
                }
                else
                {
                    Assert.Equal(HttpMethod.Head, request.Method);
                    if (HeadStatus != HttpStatusCode.OK) return new HttpResponseMessage(HeadStatus);
                }
                var response = new HttpResponseMessage(request.Method == HttpMethod.Head ? HttpStatusCode.OK : HttpStatusCode.NoContent);
                response.Headers.Add("Upload-Offset", Bytes.Length.ToString());
                return response;
            }
            if (path.EndsWith("/complete", StringComparison.Ordinal))
            {
                Completion = await request.Content!.ReadAsStringAsync(ct);
                return Json(Metadata());
            }
            if (path == "/api/files/v1/files/file-sop-1")
                return FileExists ? Json(Metadata()) : new HttpResponseMessage(HttpStatusCode.NotFound);
            if (path.EndsWith("/download-grants", StringComparison.Ordinal))
                return Json(new DownloadGrantResponse("file-sop-1", DateTimeOffset.UtcNow.AddMinutes(1),
                    new TransferInstructions("/api/files/v1/download-grants/grant-sop-1/content", new Dictionary<string, string>())));
            if (path == "/api/files/v1/download-grants/grant-sop-1/content")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Bytes) };
            throw new InvalidOperationException($"Unexpected downstream request: {request.Method} {path}");
        }

        private FileMetadataResponse Metadata() => new("file-sop-1", Organization, Environment,
            new OwnerReference("business-product-engineering", "engineering-document", "user-admin"),
            Purpose, "work-instruction.txt", "text/plain", Bytes.Length, null, Status, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        private static HttpResponseMessage Json<T>(T data) => new(HttpStatusCode.OK) { Content = JsonContent.Create(data) };
    }
}
