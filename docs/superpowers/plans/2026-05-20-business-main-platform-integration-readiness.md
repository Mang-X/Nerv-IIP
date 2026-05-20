# Business Main-Platform Integration Readiness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the minimum main-platform SDK and public contracts ready for Business Platform implementation without turning the SDK into a business runtime.

**Architecture:** Keep Business services as independent CleanDDD services that consume main-platform capabilities only through public APIs, public contracts, integration events and IAM authorization context. Strengthen `Sdk.Core`, `Sdk.Auth`, `Sdk.FileStorage`, add minimal `Sdk.Notification` and `Sdk.Observability`, and add business connector ingestion contracts for telemetry and WCS callbacks. Do not put ERP, MES, WMS, MRP, PDM/PLM, IIoT or CMMS domain rules inside main-platform SDKs.

**Tech Stack:** .NET 10, HttpClient, System.Text.Json, xUnit, FastEndpoints contract tests, existing IAM/FileStorage/AppHub/Ops/Notification boundaries.

---

## Why This Plan Exists

Business Platform slices can start with domain services, but later slices require stable main-platform touchpoints:

1. ProductEngineering and Quality need File Storage references and upload/download client support.
2. IndustrialTelemetry and WMS automation need Connector Host or external-client write authentication.
3. BusinessApproval, EngineeringChange, MRP, Maintenance and WMS failures need Notification intent support.
4. Cross-service acceptance needs correlation ID, trace context, organization/environment headers and idempotency key propagation.
5. Business services need IAM authorization context and permission checks without directly reading IAM tables.

This plan is a readiness gate before telemetry-heavy and full-chain work. MasterData can start before this plan is fully implemented, but Full-Chain Acceptance must not start until this plan passes.

## Boundaries

1. Do not add Business domain concepts to SDK modules.
2. Do not let SDK write final platform facts directly; all writes go through public APIs.
3. Do not let Connector Host bypass IAM authorization when writing telemetry, alarms or WCS callbacks.
4. Do not expose object storage keys, long-lived download URLs, refresh tokens or service database identifiers through SDK DTOs.
5. Do not make `PlatformGateway` or `Platform SDK` own Business Platform rules.

## File Structure Map

```text
backend/common/Sdk/Nerv.IIP.Sdk.Core/
  SdkCore.cs
  PlatformApiClient.cs
  PlatformRequestContext.cs

backend/common/Sdk/Nerv.IIP.Sdk.Auth/
  SdkAuth.cs
  PlatformTokenAuthentication.cs

backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/
  FileStorageSdk.cs
  FileStorageClient.cs

backend/common/Sdk/Nerv.IIP.Sdk.Notification/
  Nerv.IIP.Sdk.Notification.csproj
  NotificationClient.cs

backend/common/Sdk/Nerv.IIP.Sdk.Observability/
  Nerv.IIP.Sdk.Observability.csproj
  ObservabilityContext.cs

backend/common/Contracts/Nerv.IIP.Contracts.BusinessIntegration/
  Nerv.IIP.Contracts.BusinessIntegration.csproj
  BusinessTelemetryContracts.cs
  BusinessWcsContracts.cs
  BusinessNotificationContracts.cs

backend/tests/Nerv.IIP.Sdk.Tests/
  Nerv.IIP.Sdk.Tests.csproj
  PlatformApiClientTests.cs
  PlatformTokenAuthenticationTests.cs
  FileStorageClientTests.cs
  NotificationClientTests.cs
  ObservabilityContextTests.cs
  BusinessIntegrationContractJsonTests.cs

docs/architecture/platform-sdk-baseline.md
docs/architecture/business-platform-domain-architecture.md
docs/superpowers/specs/2026-05-20-business-platform-domain-design.md
README.md
scripts/verify-business-main-platform-integration-readiness.ps1
```

## Task 1: Strengthen SDK Core Request Context

**Files:**

- Modify: `backend/common/Sdk/Nerv.IIP.Sdk.Core/SdkCore.cs`
- Create: `backend/common/Sdk/Nerv.IIP.Sdk.Core/PlatformApiClient.cs`
- Create: `backend/common/Sdk/Nerv.IIP.Sdk.Core/PlatformRequestContext.cs`
- Create: `backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj`
- Create: `backend/tests/Nerv.IIP.Sdk.Tests/PlatformApiClientTests.cs`
- Create: `backend/tests/Nerv.IIP.Sdk.Tests/ObservabilityContextTests.cs`
- Modify: `backend/Nerv.IIP.sln`

- [ ] **Step 1: Create SDK test project**

Run:

```powershell
dotnet new xunit -n Nerv.IIP.Sdk.Tests -o backend/tests/Nerv.IIP.Sdk.Tests --framework net10.0
dotnet add backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj reference backend/common/Sdk/Nerv.IIP.Sdk.Core/Nerv.IIP.Sdk.Core.csproj
dotnet sln backend/Nerv.IIP.sln add backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj
```

Expected: project is added to `backend/Nerv.IIP.sln`.

- [ ] **Step 2: Write failing request context tests**

Create tests that assert `PlatformApiClient` applies:

```text
X-Nerv-IIP-Sdk-Version
X-Organization-Id
X-Environment-Id
X-Correlation-Id
Idempotency-Key
traceparent
```

The test request context is:

```csharp
var context = new PlatformRequestContext(
    OrganizationId: "org-001",
    EnvironmentId: "env-dev",
    CorrelationId: "corr-001",
    IdempotencyKey: "idem-001",
    TraceParent: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00");
```

Expected: FAIL because `PlatformApiClient` and `PlatformRequestContext` do not exist yet.

- [ ] **Step 3: Implement request context and client helper**

Add:

```csharp
public sealed record PlatformRequestContext(
    string OrganizationId,
    string EnvironmentId,
    string? CorrelationId = null,
    string? IdempotencyKey = null,
    string? TraceParent = null);

public static class PlatformApiClient
{
    public static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        PlatformApiOptions options,
        PlatformRequestContext context);
}
```

Rules:

1. `OrganizationId` and `EnvironmentId` are required.
2. `CorrelationId` is generated when omitted.
3. `Idempotency-Key` is only added when a value is provided.
4. `traceparent` is only added when a value is provided.
5. `X-Nerv-IIP-Sdk-Version` always comes from `PlatformApiOptions.SdkVersion`.

- [ ] **Step 4: Run Core tests**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj --no-restore --filter "FullyQualifiedName~PlatformApiClientTests|FullyQualifiedName~ObservabilityContextTests"
```

Expected: PASS.

- [ ] **Step 5: Commit Core readiness**

Run:

```powershell
git add backend/Nerv.IIP.sln backend/common/Sdk/Nerv.IIP.Sdk.Core backend/tests/Nerv.IIP.Sdk.Tests
git commit -m "feat: add platform sdk request context"
```

## Task 2: Extend SDK Auth Beyond Connector Host Headers

**Files:**

- Modify: `backend/common/Sdk/Nerv.IIP.Sdk.Auth/SdkAuth.cs`
- Create: `backend/common/Sdk/Nerv.IIP.Sdk.Auth/PlatformTokenAuthentication.cs`
- Create: `backend/tests/Nerv.IIP.Sdk.Tests/PlatformTokenAuthenticationTests.cs`

- [ ] **Step 1: Write failing authentication tests**

Tests must cover:

```csharp
PlatformBearerToken.Apply(request, "access-token-001");
ExternalClientCredential.Apply(request, new ExternalClientCredential("client-001", "secret-001", "org-001", "env-dev"));
ConnectorHostAuthentication.Apply(request, new ConnectorHostCredential("host-001", "secret-001", "org-001", "env-dev"));
```

Expected headers:

| Method | Required headers |
| --- | --- |
| `PlatformBearerToken.Apply` | `Authorization: Bearer access-token-001` |
| `ExternalClientCredential.Apply` | `Authorization: ExternalClient client-001`, `X-External-Client-Id`, `X-External-Client-Secret`, organization, environment |
| `ConnectorHostAuthentication.Apply` | existing connector host headers remain unchanged |

Expected: FAIL because bearer/external-client helpers do not exist.

- [ ] **Step 2: Implement auth helpers**

Add immutable records and static helper methods. Validate blank token, client secret or connector secret with `PlatformApiResult<T>.Failure(...)`.

- [ ] **Step 3: Run auth tests**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj --no-restore --filter FullyQualifiedName~PlatformTokenAuthenticationTests
```

Expected: PASS.

- [ ] **Step 4: Commit auth readiness**

Run:

```powershell
git add backend/common/Sdk/Nerv.IIP.Sdk.Auth backend/tests/Nerv.IIP.Sdk.Tests
git commit -m "feat: extend platform sdk auth helpers"
```

## Task 3: Implement File Storage SDK Client Minimum

**Files:**

- Modify: `backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/FileStorageSdk.cs`
- Create: `backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/FileStorageClient.cs`
- Create: `backend/tests/Nerv.IIP.Sdk.Tests/FileStorageClientTests.cs`

- [ ] **Step 1: Write failing File Storage client tests**

Use a fake `HttpMessageHandler` and assert:

```csharp
await client.CreateUploadSessionAsync(new CreateUploadSessionRequest(
    "engineering-document",
    "pump.dwg",
    "application/acad",
    1024), context, cancellationToken);

await client.CompleteUploadAsync("upload-session-001", "file-001", context, cancellationToken);

await client.CreateDownloadGrantAsync("file-001", "engineering-preview", context, cancellationToken);
```

Expected routes:

| Method | Route |
| --- | --- |
| POST | `/api/files/v1/upload-sessions` |
| POST | `/api/files/v1/upload-sessions/{uploadSessionId}/complete` |
| POST | `/api/files/v1/files/{fileId}/download-grants` |

Expected: FAIL because `FileStorageClient` does not exist.

- [ ] **Step 2: Implement File Storage client contracts**

Use these request/response records:

```csharp
public sealed record CreateUploadSessionRequest(string Purpose, string FileName, string ContentType, long SizeBytes);
public sealed record UploadSessionResponse(string UploadSessionId, IReadOnlyCollection<UploadInstruction> Instructions);
public sealed record CompleteUploadRequest(string FileId);
public sealed record CreateDownloadGrantRequest(string Purpose);
```

Return existing `FileReference`, `UploadInstruction` and `DownloadGrant` records. Do not expose object storage key or long-lived URL.

- [ ] **Step 3: Run File Storage SDK tests**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj --no-restore --filter FullyQualifiedName~FileStorageClientTests
```

Expected: PASS.

- [ ] **Step 4: Commit File Storage readiness**

Run:

```powershell
git add backend/common/Sdk/Nerv.IIP.Sdk.FileStorage backend/tests/Nerv.IIP.Sdk.Tests
git commit -m "feat: add file storage sdk client"
```

## Task 4: Add Notification and Observability SDK Minimum

**Files:**

- Create: `backend/common/Sdk/Nerv.IIP.Sdk.Notification/Nerv.IIP.Sdk.Notification.csproj`
- Create: `backend/common/Sdk/Nerv.IIP.Sdk.Notification/NotificationClient.cs`
- Create: `backend/common/Sdk/Nerv.IIP.Sdk.Observability/Nerv.IIP.Sdk.Observability.csproj`
- Create: `backend/common/Sdk/Nerv.IIP.Sdk.Observability/ObservabilityContext.cs`
- Create: `backend/tests/Nerv.IIP.Sdk.Tests/NotificationClientTests.cs`
- Modify: `backend/Nerv.IIP.sln`

- [ ] **Step 1: Create SDK projects**

Run:

```powershell
dotnet new classlib -n Nerv.IIP.Sdk.Notification -o backend/common/Sdk/Nerv.IIP.Sdk.Notification --framework net10.0
dotnet new classlib -n Nerv.IIP.Sdk.Observability -o backend/common/Sdk/Nerv.IIP.Sdk.Observability --framework net10.0
dotnet add backend/common/Sdk/Nerv.IIP.Sdk.Notification/Nerv.IIP.Sdk.Notification.csproj reference backend/common/Sdk/Nerv.IIP.Sdk.Core/Nerv.IIP.Sdk.Core.csproj
dotnet add backend/common/Sdk/Nerv.IIP.Sdk.Notification/Nerv.IIP.Sdk.Notification.csproj reference backend/common/Sdk/Nerv.IIP.Sdk.Auth/Nerv.IIP.Sdk.Auth.csproj
dotnet add backend/common/Sdk/Nerv.IIP.Sdk.Observability/Nerv.IIP.Sdk.Observability.csproj reference backend/common/Sdk/Nerv.IIP.Sdk.Core/Nerv.IIP.Sdk.Core.csproj
dotnet add backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj reference backend/common/Sdk/Nerv.IIP.Sdk.Notification/Nerv.IIP.Sdk.Notification.csproj
dotnet add backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj reference backend/common/Sdk/Nerv.IIP.Sdk.Observability/Nerv.IIP.Sdk.Observability.csproj
dotnet sln backend/Nerv.IIP.sln add backend/common/Sdk/Nerv.IIP.Sdk.Notification/Nerv.IIP.Sdk.Notification.csproj
dotnet sln backend/Nerv.IIP.sln add backend/common/Sdk/Nerv.IIP.Sdk.Observability/Nerv.IIP.Sdk.Observability.csproj
```

- [ ] **Step 2: Write failing Notification client tests**

Assert `NotificationClient.SubmitIntentAsync(...)` posts to `/api/notifications/v1/intents` with request:

```csharp
public sealed record SubmitNotificationIntentRequest(
    string IntentType,
    string Severity,
    string ResourceType,
    string ResourceId,
    string Title,
    string Summary,
    IReadOnlyCollection<string> SuggestedRecipientRefs);
```

The client must include organization, environment, correlation and idempotency headers from `PlatformRequestContext`.

- [ ] **Step 3: Implement Notification and Observability SDKs**

`ObservabilityContext` provides:

```csharp
public static PlatformRequestContext CreateRequestContext(
    string organizationId,
    string environmentId,
    string? idempotencyKey = null);
```

It reads `Activity.Current?.Id` into `TraceParent` and generates a correlation ID when the caller does not supply one.

- [ ] **Step 4: Run tests**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj --no-restore --filter "FullyQualifiedName~NotificationClientTests|FullyQualifiedName~ObservabilityContextTests"
```

Expected: PASS.

- [ ] **Step 5: Commit Notification and Observability readiness**

Run:

```powershell
git add backend/Nerv.IIP.sln backend/common/Sdk/Nerv.IIP.Sdk.Notification backend/common/Sdk/Nerv.IIP.Sdk.Observability backend/tests/Nerv.IIP.Sdk.Tests
git commit -m "feat: add notification and observability sdk minimum"
```

## Task 5: Add Business Integration Contracts for Connector Scenarios

**Files:**

- Create: `backend/common/Contracts/Nerv.IIP.Contracts.BusinessIntegration/Nerv.IIP.Contracts.BusinessIntegration.csproj`
- Create: `backend/common/Contracts/Nerv.IIP.Contracts.BusinessIntegration/BusinessTelemetryContracts.cs`
- Create: `backend/common/Contracts/Nerv.IIP.Contracts.BusinessIntegration/BusinessWcsContracts.cs`
- Create: `backend/common/Contracts/Nerv.IIP.Contracts.BusinessIntegration/BusinessNotificationContracts.cs`
- Create: `backend/tests/Nerv.IIP.Sdk.Tests/BusinessIntegrationContractJsonTests.cs`
- Modify: `backend/Nerv.IIP.sln`

- [ ] **Step 1: Create contracts project**

Run:

```powershell
dotnet new classlib -n Nerv.IIP.Contracts.BusinessIntegration -o backend/common/Contracts/Nerv.IIP.Contracts.BusinessIntegration --framework net10.0
dotnet add backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.BusinessIntegration/Nerv.IIP.Contracts.BusinessIntegration.csproj
dotnet sln backend/Nerv.IIP.sln add backend/common/Contracts/Nerv.IIP.Contracts.BusinessIntegration/Nerv.IIP.Contracts.BusinessIntegration.csproj
```

- [ ] **Step 2: Add telemetry contracts**

Create:

```csharp
public sealed record CreateTelemetryTagRequest(string DeviceAssetId, string TagKey, string ValueType, string Unit, string SamplingPolicy);
public sealed record RecordTelemetrySampleRequest(string TagId, string Value, DateTimeOffset OccurredAtUtc, string SourceSequence);
public sealed record RaiseAlarmRequest(string DeviceAssetId, string AlarmCode, string Severity, DateTimeOffset OccurredAtUtc, string ExternalAlarmId);
```

These contracts are used by IndustrialTelemetry public APIs. They do not contain PLC/DCS control commands.

- [ ] **Step 3: Add WCS callback contracts**

Create:

```csharp
public sealed record DispatchWcsTaskRequest(string WarehouseTaskId, string AdapterType, string PayloadJson);
public sealed record CompleteWcsTaskRequest(string ExternalTaskId, string ResultCode, DateTimeOffset OccurredAtUtc, string? DiagnosticMessage);
public sealed record FailWcsTaskRequest(string ExternalTaskId, string FailureCode, string DiagnosticMessage, DateTimeOffset OccurredAtUtc);
```

These contracts are used by WMS public APIs and connector callbacks. They do not model WCS internal scheduling.

- [ ] **Step 4: Add JSON compatibility tests**

Serialize every contract with `JsonSerializerDefaults.Web`. Assert JSON contains camelCase names such as `deviceAssetId`, `sourceSequence`, `externalTaskId`, `failureCode`.

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj --no-restore --filter FullyQualifiedName~BusinessIntegrationContractJsonTests
```

Expected: PASS.

- [ ] **Step 5: Commit contracts**

Run:

```powershell
git add backend/Nerv.IIP.sln backend/common/Contracts/Nerv.IIP.Contracts.BusinessIntegration backend/tests/Nerv.IIP.Sdk.Tests
git commit -m "feat: add business integration connector contracts"
```

## Task 6: Update Documentation and Verification

**Files:**

- Modify: `docs/architecture/platform-sdk-baseline.md`
- Modify: `docs/architecture/business-platform-domain-architecture.md`
- Modify: `docs/superpowers/specs/2026-05-20-business-platform-domain-design.md`
- Create: `scripts/verify-business-main-platform-integration-readiness.ps1`
- Modify: `README.md`

- [ ] **Step 1: Update SDK baseline documentation**

Document current implementation status for:

1. `Sdk.Core` request context and header injection.
2. `Sdk.Auth` bearer, external-client and connector-host helpers.
3. `Sdk.FileStorage` upload/download client.
4. `Sdk.Notification` notification intent client.
5. `Sdk.Observability` correlation and trace context helper.
6. `Contracts.BusinessIntegration` telemetry and WCS callback contracts.

- [ ] **Step 2: Update Business Platform docs**

Add one sentence to the Business Platform architecture and spec handoff: Business slices consume main-platform capabilities through this readiness plan and must not reference main-platform service Domain or Infrastructure projects.

- [ ] **Step 3: Add verification script**

The script runs:

```powershell
dotnet test backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj --no-restore
dotnet build backend/common/Sdk/Nerv.IIP.Sdk.Core/Nerv.IIP.Sdk.Core.csproj --no-restore
dotnet build backend/common/Sdk/Nerv.IIP.Sdk.Auth/Nerv.IIP.Sdk.Auth.csproj --no-restore
dotnet build backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/Nerv.IIP.Sdk.FileStorage.csproj --no-restore
dotnet build backend/common/Sdk/Nerv.IIP.Sdk.Notification/Nerv.IIP.Sdk.Notification.csproj --no-restore
dotnet build backend/common/Sdk/Nerv.IIP.Sdk.Observability/Nerv.IIP.Sdk.Observability.csproj --no-restore
dotnet build backend/common/Contracts/Nerv.IIP.Contracts.BusinessIntegration/Nerv.IIP.Contracts.BusinessIntegration.csproj --no-restore
```

- [ ] **Step 4: Run final verification**

Run:

```powershell
scripts/verify-business-main-platform-integration-readiness.ps1
git diff --check
```

Expected: both commands exit `0`.

- [ ] **Step 5: Commit docs and verification**

Run:

```powershell
git add docs/architecture/platform-sdk-baseline.md docs/architecture/business-platform-domain-architecture.md docs/superpowers/specs/2026-05-20-business-platform-domain-design.md scripts/verify-business-main-platform-integration-readiness.ps1 README.md
git commit -m "docs: record business platform integration readiness"
```

## Self-Review Checklist

1. No SDK module references `backend/services/*` or `backend/gateway/*` Web, Domain or Infrastructure projects.
2. No Business domain type appears in SDK module names or DTO names except neutral integration contracts for telemetry and WCS callbacks.
3. File Storage SDK returns file IDs, references and short-lived grants only.
4. Notification SDK submits intents only; it does not implement delivery providers.
5. Connector Host and external-client scenarios still require IAM-authenticated public APIs.
6. Full-Chain Acceptance lists this readiness script as a prerequisite.
