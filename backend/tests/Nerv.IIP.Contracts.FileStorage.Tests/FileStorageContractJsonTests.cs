using System.Text.Json;
using Nerv.IIP.Contracts.FileStorage;

namespace Nerv.IIP.Contracts.FileStorage.Tests;

public sealed class FileStorageContractJsonTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Create_upload_session_request_round_trips_with_nested_owner_and_web_json_names()
    {
        var source = new CreateUploadSessionRequest(
            "org-001",
            "env-dev",
            new OwnerReference("AppHub", "App", "app-001"),
            "attachment",
            "report.pdf",
            "application/pdf",
            12345,
            "sha256:abc");

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var result = JsonSerializer.Deserialize<CreateUploadSessionRequest>(json, JsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("org-001", root.GetProperty("organizationId").GetString());
        Assert.Equal("env-dev", root.GetProperty("environmentId").GetString());
        Assert.True(root.TryGetProperty("owner", out var owner));
        Assert.Equal("AppHub", owner.GetProperty("ownerService").GetString());
        Assert.Equal("App", owner.GetProperty("ownerType").GetString());
        Assert.Equal("app-001", owner.GetProperty("ownerId").GetString());
        Assert.Equal(12345, root.GetProperty("expectedSizeBytes").GetInt64());
        AssertDoesNotExposeObjectKey(json);
        Assert.NotNull(result);
        Assert.Equal("attachment", result.FilePurpose);
        Assert.Equal("app-001", result.Owner.OwnerId);
    }

    [Fact]
    public void Complete_upload_session_request_round_trips_with_optional_checksum_and_size()
    {
        var source = new CompleteUploadSessionRequest(
            "org-001",
            "env-dev",
            "attachment",
            Checksum: null,
            SizeBytes: null);

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var result = JsonSerializer.Deserialize<CompleteUploadSessionRequest>(json, JsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("org-001", root.GetProperty("organizationId").GetString());
        Assert.Equal("env-dev", root.GetProperty("environmentId").GetString());
        Assert.Equal("attachment", root.GetProperty("filePurpose").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("checksum").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("sizeBytes").ValueKind);
        AssertDoesNotExposeObjectKey(json);
        Assert.NotNull(result);
        Assert.Null(result.Checksum);
        Assert.Null(result.SizeBytes);
    }

    [Fact]
    public void Create_download_grant_request_round_trips_with_context_fields()
    {
        var source = new CreateDownloadGrantRequest("org-001", "env-dev");

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var result = JsonSerializer.Deserialize<CreateDownloadGrantRequest>(json, JsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("org-001", root.GetProperty("organizationId").GetString());
        Assert.Equal("env-dev", root.GetProperty("environmentId").GetString());
        AssertDoesNotExposeObjectKey(json);
        Assert.NotNull(result);
        Assert.Equal("env-dev", result.EnvironmentId);
    }

    [Fact]
    public void Create_upload_session_response_round_trips_with_web_json_names()
    {
        var source = new CreateUploadSessionResponse(
            "upload-session-001",
            "file-001",
            "server-proxy",
            "local",
            DateTimeOffset.Parse("2026-05-21T00:05:00Z"),
            new TransferInstructions(
                "/api/files/v1/upload-sessions/upload-session-001/content",
                new Dictionary<string, string> { ["x-nerv-upload-mode"] = "server-proxy" }));

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var result = JsonSerializer.Deserialize<CreateUploadSessionResponse>(json, JsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("upload-session-001", root.GetProperty("uploadSessionId").GetString());
        Assert.Equal("server-proxy", root.GetProperty("uploadMode").GetString());
        Assert.True(root.TryGetProperty("upload", out var upload));
        Assert.Equal("/api/files/v1/upload-sessions/upload-session-001/content", upload.GetProperty("url").GetString());
        AssertDoesNotExposeObjectKey(json);
        Assert.NotNull(result);
        Assert.Equal("file-001", result.FileId);
        Assert.Equal("server-proxy", result.UploadMode);
    }

    [Fact]
    public void File_metadata_response_round_trips_with_web_json_names()
    {
        var source = new FileMetadataResponse(
            "file-001",
            "org-001",
            "env-dev",
            new OwnerReference("AppHub", "App", "app-001"),
            "attachment",
            "report.pdf",
            "application/pdf",
            12345,
            "sha256:abc",
            "pending",
            "completed",
            DateTimeOffset.Parse("2026-05-21T00:00:00Z"),
            DateTimeOffset.Parse("2026-05-21T00:01:00Z"));

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var result = JsonSerializer.Deserialize<FileMetadataResponse>(json, JsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("file-001", root.GetProperty("fileId").GetString());
        Assert.Equal("AppHub", root.GetProperty("owner").GetProperty("ownerService").GetString());
        Assert.Equal("report.pdf", root.GetProperty("fileName").GetString());
        AssertDoesNotExposeObjectKey(json);
        Assert.NotNull(result);
        Assert.Equal("completed", result.Status);
        Assert.Equal("app-001", result.Owner.OwnerId);
    }

    [Fact]
    public void Download_grant_response_round_trips_with_web_json_names()
    {
        var source = new DownloadGrantResponse(
            "file-001",
            DateTimeOffset.Parse("2026-05-21T00:05:00Z"),
            new TransferInstructions(
                "/api/files/v1/files/file-001/download",
                new Dictionary<string, string> { ["x-nerv-download-grant"] = "grant-001" }));

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var result = JsonSerializer.Deserialize<DownloadGrantResponse>(json, JsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("file-001", root.GetProperty("fileId").GetString());
        Assert.True(root.TryGetProperty("download", out var download));
        Assert.Equal("/api/files/v1/files/file-001/download", download.GetProperty("url").GetString());
        AssertDoesNotExposeObjectKey(json);
        Assert.NotNull(result);
        Assert.Equal("file-001", result.FileId);
        Assert.Equal("grant-001", result.Download.Headers["x-nerv-download-grant"]);
    }

    private static void AssertDoesNotExposeObjectKey(string json)
    {
        Assert.DoesNotContain("objectKey", json, StringComparison.Ordinal);
        Assert.DoesNotContain("object_key", json, StringComparison.Ordinal);
    }
}
