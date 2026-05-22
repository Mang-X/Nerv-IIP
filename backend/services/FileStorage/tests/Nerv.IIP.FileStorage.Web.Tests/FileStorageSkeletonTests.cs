using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed class FileStorageSkeletonTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task File_storage_api_endpoints_require_internal_service_authorization()
    {
        var client = factory.CreateClient();

        using var patchRequest = new HttpRequestMessage(HttpMethod.Patch, "/api/files/v1/tus/ups-missing")
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("hello"))
        };
        patchRequest.Headers.Add("Tus-Resumable", "1.0.0");
        patchRequest.Headers.Add("Upload-Offset", "0");
        patchRequest.Content.Headers.ContentType = new("application/offset+octet-stream");

        var responses = new[]
        {
            await client.PostAsJsonAsync("/api/files/v1/upload-sessions", CreateUploadSessionRequest()),
            await client.PostAsJsonAsync("/api/files/v1/upload-sessions/ups-missing/complete", new CompleteUploadSessionRequest("org-001", "prod", "application-package", null, 0)),
            await client.GetAsync("/api/files/v1/files/file-missing"),
            await client.PostAsJsonAsync("/api/files/v1/files/file-missing/download-grants", new CreateDownloadGrantRequest("org-001", "prod")),
            await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/api/files/v1/tus/ups-missing")),
            await client.SendAsync(patchRequest),
            await client.GetAsync("/api/files/v1/download-grants/grant-missing/content")
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
    }

    [Fact]
    public async Task File_storage_health_endpoint_remains_anonymous()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Service_exposes_health_and_file_storage_boundaries()
    {
        var client = factory.CreateClient();

        var health = await client.GetStringAsync("/health");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            InternalServiceAuthentication.DefaultDevelopmentBearerToken);
        var boundaries = await client.GetFromJsonAsync<FileStorageBoundaries>("/internal/file-storage/v1/boundaries");

        Assert.Equal("Healthy", health);
        Assert.Contains("FileMetadata", boundaries!.DomainFacts);
        Assert.Contains("UploadProvider", boundaries.ProviderBoundaries);
        Assert.Contains("scanStatus", boundaries.DomainFacts);
    }

    [Fact]
    public async Task UploadSessionWorkflow_MetadataFirstServerProxy_CompletesFileAndIssuesDownloadGrant()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            InternalServiceAuthentication.DefaultDevelopmentBearerToken);
        var createRequest = new CreateUploadSessionRequest(
            "org-001",
            "prod",
            new OwnerReference("AppHub", "ApplicationPackage", "app-42"),
            "application-package",
            "demo.zip",
            "application/zip",
            4096,
            "sha256:test");

        var createdResponse = await client.PostAsJsonAsync("/api/files/v1/upload-sessions", createRequest);

        createdResponse.EnsureSuccessStatusCode();
        var created = await createdResponse.Content.ReadFromJsonAsync<CreateUploadSessionResponse>();
        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created.UploadSessionId));
        Assert.False(string.IsNullOrWhiteSpace(created.FileId));
        Assert.Equal("server-proxy", created.UploadMode);
        Assert.Equal("server-proxy", created.Provider);
        Assert.StartsWith("/api/files/v1/upload-sessions/", created.Upload.Url, StringComparison.Ordinal);
        await AssertObjectKeyIsNotExposedAsync(createdResponse);

        var completeResponse = await client.PostAsJsonAsync(
            $"/api/files/v1/upload-sessions/{created.UploadSessionId}/complete",
            new CompleteUploadSessionRequest("org-001", "prod", "application-package", "sha256:test", 4096));

        completeResponse.EnsureSuccessStatusCode();
        var completed = await completeResponse.Content.ReadFromJsonAsync<FileMetadataResponse>();
        Assert.NotNull(completed);
        Assert.Equal(created.FileId, completed.FileId);
        Assert.Equal("org-001", completed.OrganizationId);
        Assert.Equal("prod", completed.EnvironmentId);
        Assert.Equal("AppHub", completed.Owner.OwnerService);
        Assert.Equal("ApplicationPackage", completed.Owner.OwnerType);
        Assert.Equal("app-42", completed.Owner.OwnerId);
        Assert.Equal("application-package", completed.FilePurpose);
        Assert.Equal("demo.zip", completed.FileName);
        Assert.Equal("application/zip", completed.ContentType);
        Assert.Equal(4096, completed.SizeBytes);
        Assert.Equal("sha256:test", completed.Checksum);
        Assert.Equal("pending", completed.ScanStatus);
        Assert.Equal("available", completed.Status);
        await AssertObjectKeyIsNotExposedAsync(completeResponse);
        await AssertFlatOwnerFieldsAreNotExposedAsync(completeResponse);

        var metadataResponse = await client.GetAsync($"/api/files/v1/files/{created.FileId}");

        metadataResponse.EnsureSuccessStatusCode();
        var metadata = await metadataResponse.Content.ReadFromJsonAsync<FileMetadataResponse>();
        Assert.NotNull(metadata);
        Assert.Equal(completed, metadata);
        await AssertObjectKeyIsNotExposedAsync(metadataResponse);
        await AssertFlatOwnerFieldsAreNotExposedAsync(metadataResponse);

        var grantResponse = await client.PostAsJsonAsync(
            $"/api/files/v1/files/{created.FileId}/download-grants",
            new CreateDownloadGrantRequest("org-001", "prod"));

        grantResponse.EnsureSuccessStatusCode();
        var grant = await grantResponse.Content.ReadFromJsonAsync<DownloadGrantResponse>();
        Assert.NotNull(grant);
        Assert.Equal(created.FileId, grant.FileId);
        Assert.Matches("^/api/files/v1/download-grants/[^/]+/content$", grant.Download.Url);
        await AssertObjectKeyIsNotExposedAsync(grantResponse);
    }

    [Fact]
    public async Task CreateUploadSession_UnsupportedPurpose_ReturnsBadRequest()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            InternalServiceAuthentication.DefaultDevelopmentBearerToken);
        var request = new CreateUploadSessionRequest(
            "org-001",
            "prod",
            new OwnerReference("AppHub", "ApplicationPackage", "app-42"),
            "not-supported",
            "demo.zip",
            "application/zip",
            4096,
            null);

        var response = await client.PostAsJsonAsync("/api/files/v1/upload-sessions", request);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static CreateUploadSessionRequest CreateUploadSessionRequest()
    {
        return new CreateUploadSessionRequest(
            "org-001",
            "prod",
            new OwnerReference("AppHub", "ApplicationPackage", "app-42"),
            "application-package",
            "demo.zip",
            "application/zip",
            4096,
            "sha256:test");
    }

    private static async Task AssertObjectKeyIsNotExposedAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("objectKey", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("object_key", body, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(body);
        Assert.False(JsonContainsAnyProperty(document.RootElement, ["objectKey", "object_key"]));
    }

    private static async Task AssertFlatOwnerFieldsAreNotExposedAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.TryGetProperty("owner", out _));
        Assert.False(document.RootElement.TryGetProperty("ownerService", out _));
        Assert.False(document.RootElement.TryGetProperty("ownerType", out _));
        Assert.False(document.RootElement.TryGetProperty("ownerId", out _));
    }

    private static bool JsonContainsAnyProperty(JsonElement element, IReadOnlyList<string> propertyNames)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().Any(property =>
                propertyNames.Any(propertyName =>
                    string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                || JsonContainsAnyProperty(property.Value, propertyNames)),
            JsonValueKind.Array => element.EnumerateArray().Any(item => JsonContainsAnyProperty(item, propertyNames)),
            _ => false
        };
    }

    private sealed record FileStorageBoundaries(IReadOnlyList<string> DomainFacts, IReadOnlyList<string> ProviderBoundaries);
}
