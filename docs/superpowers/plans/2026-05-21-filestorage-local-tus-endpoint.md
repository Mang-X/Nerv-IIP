# FileStorage Local Tus Endpoint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a minimal FileStorage-owned tus upload endpoint and download content endpoint without introducing MinIO/S3 multipart.

**Architecture:** FileStorage keeps upload-session creation and completion as the metadata authority. When `FileStorage:UploadProvider=tus` is selected, clients can use `HEAD` to read the current upload offset and `PATCH` to append bytes into a local filesystem store. Download grants serve completed bytes back through FileStorage using the same local store.

**Tech Stack:** .NET 10, FastEndpoints, xUnit, ASP.NET Core `WebApplicationFactory`, local filesystem storage.

---

## File Structure

Create:

1. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/Tus/LocalTusFileStore.cs` - local temp/completed byte storage and offset operations.
2. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Endpoints/Files/TusFileEndpoints.cs` - `HEAD` and `PATCH` upload endpoints plus download content endpoint.

Modify:

1. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/InMemoryFileStorageService.cs` - expose upload-session/file lookup for tus endpoint and register completed local content.
2. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/PostgreSqlFileStorageService.cs` - keep compile compatibility; local tus endpoint can depend on `IFileStorageService` plus a small tus-aware interface.
3. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Program.cs` - register local tus store singleton.
4. `backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/FileStorageTusProviderTests.cs` - add endpoint workflow tests.
5. `docs/architecture/file-storage-baseline.md` and `docs/architecture/implementation-readiness.md` - document minimal tus endpoint behavior after code is verified.

## Task 1: Minimal Tus Upload Endpoint

**Files:**
- Create: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/Tus/LocalTusFileStore.cs`
- Create: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Endpoints/Files/TusFileEndpoints.cs`
- Modify: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/InMemoryFileStorageService.cs`
- Modify: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Program.cs`
- Test: `backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/FileStorageTusProviderTests.cs`

- [ ] **Step 1: Write failing endpoint workflow test**

Add a test that creates a tus upload session, calls `HEAD /api/files/v1/tus/{uploadSessionId}` and expects `Upload-Offset: 0`, calls `PATCH` with `Tus-Resumable: 1.0.0`, `Upload-Offset: 0`, `Content-Type: application/offset+octet-stream`, and expects `Upload-Offset` to advance by the byte count.

- [ ] **Step 2: Verify red**

Run:

```powershell
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore --filter TusUploadEndpoint_HeadAndPatch_TracksOffset
```

Expected: FAIL with 404 or missing endpoint.

- [ ] **Step 3: Implement local tus store and endpoints**

Implement `LocalTusFileStore` with:

```csharp
public sealed class LocalTusFileStore(IConfiguration configuration)
{
    public long GetOffset(string uploadSessionId);
    public Task<long> AppendAsync(string uploadSessionId, long expectedOffset, Stream content, CancellationToken cancellationToken);
    public FileStream OpenRead(string uploadSessionId);
}
```

Implement endpoints:

```text
HEAD  /api/files/v1/tus/{uploadSessionId}
PATCH /api/files/v1/tus/{uploadSessionId}
```

Rules:

1. `HEAD` returns `Tus-Resumable: 1.0.0`, `Upload-Offset`, and `Cache-Control: no-store`.
2. `PATCH` requires matching `Upload-Offset`; mismatch returns `409 Conflict` with current `Upload-Offset`.
3. `PATCH` appends bytes and returns `204 NoContent` with new `Upload-Offset`.
4. Endpoint is local store only; no MinIO/S3 multipart.

- [ ] **Step 4: Verify green**

Run the focused test above, then:

```powershell
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore
```

Expected: PASS.

## Task 2: Download Content Endpoint

**Files:**
- Modify: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/InMemoryFileStorageService.cs`
- Modify: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Endpoints/Files/TusFileEndpoints.cs`
- Test: `backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/FileStorageTusProviderTests.cs`

- [ ] **Step 1: Write failing download workflow test**

Add a test that uploads bytes through tus, completes the upload session, creates a download grant, calls `GET /api/files/v1/download-grants/{grantId}/content`, and expects the original bytes.

- [ ] **Step 2: Verify red**

Run:

```powershell
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore --filter TusUploadEndpoint_CompleteAndDownload_ReturnsUploadedBytes
```

Expected: FAIL with 404 or missing content endpoint.

- [ ] **Step 3: Track grant-to-session mapping for local content**

Keep in-memory mappings inside the in-memory service for MVP:

```csharp
fileId -> uploadSessionId
downloadGrantId -> fileId
```

Add a narrow internal interface used by endpoints:

```csharp
public interface ILocalFileContentIndex
{
    bool TryGetUploadSessionIdForDownloadGrant(string downloadGrantId, out string uploadSessionId);
}
```

- [ ] **Step 4: Implement download endpoint**

Implement:

```text
GET /api/files/v1/download-grants/{downloadGrantId}/content
```

It resolves the grant to the uploaded local file and streams `application/octet-stream`. Unknown grants return 404.

- [ ] **Step 5: Verify green**

Run the focused test, then full FileStorage Web tests.

## Task 3: Docs And Verification

**Files:**
- Modify: `docs/architecture/file-storage-baseline.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `README.md` if the status paragraph would otherwise be stale.

- [ ] **Step 1: Update docs from actual diff**

Document that MVP now supports local tus `HEAD/PATCH` upload offset tracking and platform download content for the in-memory/local profile. Keep PostgreSQL metadata and local byte store limitations explicit.

- [ ] **Step 2: Final verification**

Run:

```powershell
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

Expected: PASS.

## Self-Review

Spec coverage: The plan covers upload resume offset, append upload, download content, documentation, and verification.

Scope check: This stays within local filesystem tus MVP and does not implement MinIO/S3 multipart or a full tus creation protocol.

Placeholder scan: No placeholders remain.
