# FileStorage MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first FileStorage MVP using a server-proxy metadata path, with PostgreSQL migration and schema convention coverage in the same phase.

**Architecture:** FileStorage owns generic file facts, upload sessions and download grants. The first provider is a server-proxy metadata stub so API, persistence and SDK work can proceed without MinIO deployment. tus comes after the core facts are stable and is the MVP complete binary-transfer path; MinIO/S3 multipart is post-MVP deployment integration.

**Tech Stack:** .NET 10, FastEndpoints, xUnit, EF Core, PostgreSQL, Nerv.IIP.Testing schema convention helpers.

---

## File Structure

Modify:

1. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Domain/FileStorageBoundaries.cs` - replace skeleton records with MVP domain facts and simple policy helpers.
2. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Program.cs` - register the in-memory MVP store first, later register persistence.
3. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Endpoints/Boundaries/FileStorageBoundaryEndpoints.cs` - keep or update boundary diagnostic endpoint.
4. `backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/FileStorageSkeletonTests.cs` - convert skeleton coverage into behavior-focused API tests.
5. `docs/architecture/file-storage-baseline.md` - update after implementation evidence exists.

Create in the API slice:

1. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Endpoints/Files/FileUploadSessionEndpoints.cs`
2. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Endpoints/Files/FileMetadataEndpoints.cs`
3. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Services/InMemoryFileStorageStore.cs`

Create in the persistence slice:

1. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/EntityConfigurations/StoredFileEntityTypeConfiguration.cs`
3. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/EntityConfigurations/UploadSessionEntityTypeConfiguration.cs`
4. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/EntityConfigurations/DownloadGrantEntityTypeConfiguration.cs`
5. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/Migrations/*`
6. Schema convention tests under `backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests`.

## Task 1: Server-Proxy Metadata API

**Files:**
- Modify: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Domain/FileStorageBoundaries.cs`
- Modify: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Program.cs`
- Modify: `backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/FileStorageSkeletonTests.cs`
- Create: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Endpoints/Files/FileUploadSessionEndpoints.cs`
- Create: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Endpoints/Files/FileMetadataEndpoints.cs`
- Create: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Services/InMemoryFileStorageStore.cs`

- [x] **Step 1: Write failing API tests**

Add tests that:

```text
POST /api/files/v1/upload-sessions creates a server-proxy session.
POST /api/files/v1/upload-sessions/{uploadSessionId}/complete completes the session.
GET /api/files/v1/files/{fileId} returns metadata.
POST /api/files/v1/files/{fileId}/download-grants returns a short-lived grant.
Metadata and grant JSON do not contain objectKey or object_key.
```

Run:

```powershell
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore
```

Expected before implementation: FAIL with missing endpoints or non-success status.

- [x] **Step 2: Implement the minimum in-memory store and endpoints**

Implement only server-proxy metadata behavior:

```text
uploadMode = server-proxy
provider = server-proxy
uploadUrl = /api/files/v1/upload-sessions/{uploadSessionId}/content
downloadUrl = /api/files/v1/download-grants/{downloadGrantId}/content
```

The internal object key can be deterministic:

```text
{organizationId}/{fileId}
```

Do not expose that object key in public responses.

- [x] **Step 3: Re-run FileStorage tests**

Run:

```powershell
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore
```

Expected: PASS.

## Task 2: PostgreSQL Migration And Schema Convention

**Files:**
- Modify: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/Nerv.IIP.FileStorage.Infrastructure.csproj`
- Modify: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Program.cs`
- Modify: `backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj`
- Create: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/EntityConfigurations/*.cs`
- Create: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/Migrations/*`
- Create: FileStorage schema convention tests.

- [x] **Step 1: Add DbContext and entity configurations**

Use schema `filestorage` and configure migrations history under the same schema.

Tables:

```text
stored_files
upload_sessions
download_grants
```

All business tables and business columns need comments. JSON/text compatibility comments are required for any JSON/text payload fields.

- [x] **Step 2: Generate initial migration**

Run the repo-local EF tool pattern used by AppHub/Ops/IAM, setting provider to PostgreSQL.

- [x] **Step 3: Add schema convention tests**

Reuse `Nerv.IIP.Testing` helpers. Cover:

```text
table comments
column comments
string ID length conventions
migrations history schema
object_key remains persistence-only
```

- [x] **Step 4: Run FileStorage persistence tests**

Run:

```powershell
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore
```

Expected: PASS.

## Task 3: Documentation And Verification

**Files:**
- Modify: `docs/architecture/file-storage-baseline.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `docs/superpowers/plans/2026-05-21-next-stage-stabilization-and-readiness.md`

- [x] **Step 1: Update docs from actual diff**

Document that first FileStorage MVP uses server-proxy metadata stub first, tus as the complete MVP transfer path, and MinIO/S3 multipart only as post-MVP deployment integration.

- [x] **Step 2: Run verification**

Run:

```powershell
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

Expected: both pass.
