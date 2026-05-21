# FileStorage MVP Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete FileStorage MVP after the metadata/schema baseline by adding stable public contracts, PostgreSQL-backed API behavior, and tus as the MVP binary-transfer path.

**Architecture:** FileStorage keeps `server-proxy` metadata stub as the first slice and now moves toward durable PostgreSQL facts plus a stable SDK boundary. tus is the MVP complete upload/download transfer capability; MinIO/S3 multipart is explicitly post-MVP deployment integration. Public contracts never expose `objectKey` or `object_key`.

**Tech Stack:** .NET 10, FastEndpoints, EF Core PostgreSQL, xUnit, Platform SDK, tus protocol/provider abstraction, Nerv.IIP.Testing schema convention helpers.

---

## File Structure

Create:

1. `backend/common/Contracts/Nerv.IIP.Contracts.FileStorage/Nerv.IIP.Contracts.FileStorage.csproj` - public FileStorage DTO package.
2. `backend/common/Contracts/Nerv.IIP.Contracts.FileStorage/FileStorageContracts.cs` - request/response DTOs shared by SDK and Web boundary tests.
3. `backend/tests/Nerv.IIP.Contracts.FileStorage.Tests/Nerv.IIP.Contracts.FileStorage.Tests.csproj` - JSON contract test project.
4. `backend/tests/Nerv.IIP.Contracts.FileStorage.Tests/FileStorageContractJsonTests.cs` - verifies web JSON names and `objectKey` non-exposure.
5. `backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/FileStorageClient.cs` - `IFileStorageClient` and `HttpFileStorageClient`.
6. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/PostgreSqlFileStorageService.cs` - PostgreSQL-backed implementation of the existing API behavior.
7. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/UploadProviders/FileStorageUploadProvider.cs` - provider abstraction used by server-proxy and tus.
8. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/UploadProviders/TusUploadProvider.cs` - MVP tus provider shape.
9. `backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/FileStoragePostgreSqlServiceTests.cs` - persistence behavior tests using EF in-memory SQLite or provider-light test DbContext if available.
10. `backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/FileStorageTusProviderTests.cs` - tus instruction and completion behavior tests.

Modify:

1. `backend/Nerv.IIP.sln` - add FileStorage contracts and tests.
2. `backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/Nerv.IIP.Sdk.FileStorage.csproj` - reference `Contracts.FileStorage`.
3. `backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/FileStorageSdk.cs` - keep backward compatible aliases or move skeleton records behind contracts.
4. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/InMemoryFileStorageService.cs` - consume contracts or stay aligned with contract DTO names.
5. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Endpoints/Files/FileStorageEndpoints.cs` - use contract DTOs.
6. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Program.cs` - choose in-memory or PostgreSQL service by `Persistence:Provider`; register upload provider.
7. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/Records/*.cs` - add factory methods/constructors needed by the PostgreSQL service.
8. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/EntityConfigurations/*.cs` - update model only if service implementation reveals a missing column or relationship.
9. `docs/architecture/file-storage-baseline.md` - update only after code evidence exists.
10. `docs/architecture/platform-sdk-baseline.md` - update SDK status after contracts/client land.

## Task 1: Contracts And SDK Boundary

**Files:**
- Create: `backend/common/Contracts/Nerv.IIP.Contracts.FileStorage/Nerv.IIP.Contracts.FileStorage.csproj`
- Create: `backend/common/Contracts/Nerv.IIP.Contracts.FileStorage/FileStorageContracts.cs`
- Create: `backend/tests/Nerv.IIP.Contracts.FileStorage.Tests/Nerv.IIP.Contracts.FileStorage.Tests.csproj`
- Create: `backend/tests/Nerv.IIP.Contracts.FileStorage.Tests/FileStorageContractJsonTests.cs`
- Modify: `backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/Nerv.IIP.Sdk.FileStorage.csproj`
- Modify: `backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/FileStorageSdk.cs`
- Create: `backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/FileStorageClient.cs`
- Modify: `backend/Nerv.IIP.sln`

- [ ] **Step 1: Write failing contract JSON tests**

Add `FileStorageContractJsonTests` with one round-trip for `CreateUploadSessionResponse`, `FileMetadataResponse`, and `DownloadGrantResponse`. Assert web JSON contains `uploadSessionId`, `uploadMode`, `download`, `fileId`, and does not contain `objectKey` or `object_key`.

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.FileStorage.Tests/Nerv.IIP.Contracts.FileStorage.Tests.csproj --no-restore
```

Expected: FAIL because the contracts project does not exist yet.

- [ ] **Step 2: Add public contracts**

Create DTOs:

```csharp
namespace Nerv.IIP.Contracts.FileStorage;

public sealed record OwnerReference(string OwnerService, string OwnerType, string OwnerId);
public sealed record TransferInstructions(string Url, IReadOnlyDictionary<string, string> Headers);

public sealed record CreateUploadSessionRequest(
    string OrganizationId,
    string EnvironmentId,
    OwnerReference Owner,
    string FilePurpose,
    string FileName,
    string ContentType,
    long ExpectedSizeBytes,
    string? Checksum);

public sealed record CompleteUploadSessionRequest(
    string OrganizationId,
    string EnvironmentId,
    string FilePurpose,
    string? Checksum = null,
    long? SizeBytes = null);

public sealed record CreateDownloadGrantRequest(string OrganizationId, string EnvironmentId);

public sealed record CreateUploadSessionResponse(
    string UploadSessionId,
    string FileId,
    string UploadMode,
    string Provider,
    DateTimeOffset ExpiresAtUtc,
    TransferInstructions Upload);

public sealed record FileMetadataResponse(
    string FileId,
    string OrganizationId,
    string EnvironmentId,
    OwnerReference Owner,
    string FilePurpose,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Checksum,
    string ScanStatus,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc);

public sealed record DownloadGrantResponse(
    string FileId,
    DateTimeOffset ExpiresAtUtc,
    TransferInstructions Download);
```

- [ ] **Step 3: Add SDK client**

Add:

```csharp
public interface IFileStorageClient
{
    Task<CreateUploadSessionResponse> CreateUploadSessionAsync(CreateUploadSessionRequest request, CancellationToken cancellationToken = default);
    Task<FileMetadataResponse> CompleteUploadSessionAsync(string uploadSessionId, CompleteUploadSessionRequest request, CancellationToken cancellationToken = default);
    Task<FileMetadataResponse> GetFileMetadataAsync(string fileId, CancellationToken cancellationToken = default);
    Task<DownloadGrantResponse> CreateDownloadGrantAsync(string fileId, CreateDownloadGrantRequest request, CancellationToken cancellationToken = default);
}
```

`HttpFileStorageClient` must call the four existing `/api/files/v1/**` endpoints and escape route ids with `Uri.EscapeDataString`.

- [ ] **Step 4: Verify contracts and SDK build**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.FileStorage.Tests/Nerv.IIP.Contracts.FileStorage.Tests.csproj --no-restore
dotnet build backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/Nerv.IIP.Sdk.FileStorage.csproj --no-restore
```

Expected: PASS.

## Task 2: Use Contracts At The FileStorage Web Boundary

**Files:**
- Modify: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/InMemoryFileStorageService.cs`
- Modify: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Endpoints/Files/FileStorageEndpoints.cs`
- Modify: `backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/FileStorageSkeletonTests.cs`
- Modify: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Nerv.IIP.FileStorage.Web.csproj`

- [ ] **Step 1: Add failing Web contract alignment test**

Update the API test to deserialize responses with `Nerv.IIP.Contracts.FileStorage` DTOs. Expected before implementation: compile failure because Web does not reference contracts.

- [ ] **Step 2: Replace local public DTOs with contracts**

Use contract request/response types in FastEndpoints and service interface. Keep `FileStorageResult<T>` internal to Web. Keep internal Domain `OwnerReference` and `FileMetadata` mapping private; public responses use contract `OwnerReference`.

- [ ] **Step 3: Re-run FileStorage tests**

Run:

```powershell
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore
```

Expected: PASS with the existing 4 tests.

## Task 3: PostgreSQL-Backed API Service

**Files:**
- Modify: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/Records/*.cs`
- Create: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/PostgreSqlFileStorageService.cs`
- Modify: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Program.cs`
- Create: `backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/FileStoragePostgreSqlServiceTests.cs`

- [ ] **Step 1: Write failing persistence behavior tests**

Add tests that use a real EF service provider with `ApplicationDbContext` and assert:

```text
CreateUploadSession persists upload_sessions.
CompleteUploadSession marks upload session completed and inserts stored_files.
GetFileMetadata reads stored_files.
CreateDownloadGrant inserts download_grants.
object_key is not present in public response JSON.
```

Expected before implementation: FAIL because there is no PostgreSQL-backed service.

- [ ] **Step 2: Add record factories**

Add explicit public static factory methods to `StoredFileRecord`, `UploadSessionRecord`, and `DownloadGrantRecord`; keep setters private for EF materialization.

- [ ] **Step 3: Implement `PostgreSqlFileStorageService`**

Use `ApplicationDbContext` with async EF calls and cancellation tokens. Keep behavior equivalent to the current in-memory service: validation, expiry checks, context mismatch checks, internal object key generation, server-proxy placeholder URLs.

- [ ] **Step 4: Register by provider**

In `Program.cs`, register:

```text
Persistence:Provider=PostgreSQL -> PostgreSqlFileStorageService
default/InMemory -> InMemoryFileStorageService
```

Do not require a real PostgreSQL connection in default tests.

- [ ] **Step 5: Verify**

Run:

```powershell
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

Expected: PASS.

## Task 4: Tus MVP Provider Shape

**Files:**
- Create: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/UploadProviders/FileStorageUploadProvider.cs`
- Create: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/UploadProviders/TusUploadProvider.cs`
- Modify: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/InMemoryFileStorageService.cs`
- Modify: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/PostgreSqlFileStorageService.cs`
- Create: `backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/FileStorageTusProviderTests.cs`

- [ ] **Step 1: Write failing tus provider tests**

Assert that selecting the tus provider returns:

```text
uploadMode = tus
provider = tus
upload.url = /api/files/v1/tus/{uploadSessionId}
headers include x-nerv-upload-mode = tus
```

Expected before implementation: FAIL because only server-proxy exists.

- [ ] **Step 2: Add provider abstraction**

Define:

```csharp
public interface IFileStorageUploadProvider
{
    string Provider { get; }
    string UploadMode { get; }
    TransferInstructions CreateUploadInstructions(string uploadSessionId, string fileId);
}
```

- [ ] **Step 3: Implement tus provider**

Add `TusUploadProvider` that only creates platform-owned tus instructions. Do not add MinIO/S3 multipart. Do not expose object key.

- [ ] **Step 4: Wire selection**

Keep default provider `server-proxy` until configuration says `FileStorage:UploadProvider=tus`. Preserve existing tests by default.

- [ ] **Step 5: Verify**

Run:

```powershell
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

Expected: PASS.

## Task 5: Docs And Final Verification

**Files:**
- Modify: `README.md`
- Modify: `docs/architecture/file-storage-baseline.md`
- Modify: `docs/architecture/platform-sdk-baseline.md`
- Modify: `docs/architecture/api-contract-and-codegen.md`
- Modify: `docs/architecture/implementation-readiness.md`

- [ ] **Step 1: Update docs from actual diff**

Document only completed behavior:

```text
Contracts/SDK landed.
PostgreSQL-backed API service landed if Task 3 completed.
tus landed if Task 4 completed.
MinIO/S3 multipart remains post-MVP.
```

- [ ] **Step 2: Run final verification**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.FileStorage.Tests/Nerv.IIP.Contracts.FileStorage.Tests.csproj --no-restore
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

Expected: all PASS.

## Self-Review

Spec coverage:

1. Public contracts and SDK boundary are covered in Task 1.
2. Web boundary alignment is covered in Task 2.
3. PostgreSQL-backed API behavior is covered in Task 3.
4. tus as the MVP complete transfer path is covered in Task 4.
5. MinIO/S3 multipart is excluded from all implementation tasks and documented as post-MVP.
6. Documentation and verification are covered in Task 5.

Placeholder scan: no TODO/TBD placeholders remain; every task has concrete files, behavior, and commands.

Type consistency: DTO names match the existing API semantics and keep `objectKey/object_key` out of public contracts.
