# Observability Enhancement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the missing SDK observability context helper and the server-side OpenTelemetry traces/metrics baseline without touching Notification or Ops business work.

**Architecture:** `Nerv.IIP.Sdk.Observability` is a thin external SDK module that depends only on `Sdk.Core` and exposes request-context helpers. `Nerv.IIP.Observability` remains the server shared library and will keep existing Serilog/correlation behavior while adding OpenTelemetry resource, traces, metrics, ASP.NET Core, HttpClient, and runtime instrumentation.

**Tech Stack:** .NET 10, xUnit, Microsoft.Extensions.DependencyInjection, Serilog, OpenTelemetry .NET SDK, central package management.

---

### Task 1: SDK Observability MVP

**Files:**
- Modify: `backend/common/Sdk/Nerv.IIP.Sdk.Core/SdkCore.cs`
- Create: `backend/common/Sdk/Nerv.IIP.Sdk.Observability/Nerv.IIP.Sdk.Observability.csproj`
- Create: `backend/common/Sdk/Nerv.IIP.Sdk.Observability/ObservabilityContext.cs`
- Create: `backend/tests/Nerv.IIP.Sdk.Observability.Tests/Nerv.IIP.Sdk.Observability.Tests.csproj`
- Create: `backend/tests/Nerv.IIP.Sdk.Observability.Tests/ObservabilityContextTests.cs`

- [ ] **Step 1: Write the failing SDK tests**

Create `backend/tests/Nerv.IIP.Sdk.Observability.Tests/ObservabilityContextTests.cs` with tests for generated correlation id, preserved explicit correlation/idempotency key, and `Activity.Current.Id` capture as `TraceParent`.

- [ ] **Step 2: Run SDK tests and confirm they fail**

Run: `dotnet test backend/tests/Nerv.IIP.Sdk.Observability.Tests/Nerv.IIP.Sdk.Observability.Tests.csproj`

Expected before implementation: compile failure because the project/types do not exist yet, or test failure because behavior is missing.

- [ ] **Step 3: Implement minimal SDK API**

Add `PlatformRequestContext` to `Sdk.Core`:

```csharp
public sealed record PlatformRequestContext(
    string OrganizationId,
    string EnvironmentId,
    string CorrelationId,
    string? IdempotencyKey = null,
    string? TraceParent = null);
```

Create `ObservabilityContext.CreateRequestContext(...)`:

```csharp
public static PlatformRequestContext CreateRequestContext(
    string organizationId,
    string environmentId,
    string? correlationId = null,
    string? idempotencyKey = null)
```

Rules: validate organization/environment as non-empty, generate correlation id with `Guid.NewGuid().ToString("n")` when omitted, use `Activity.Current?.Id` for trace parent, and do not depend on server `Nerv.IIP.Observability`.

- [ ] **Step 4: Run SDK tests and confirm they pass**

Run: `dotnet test backend/tests/Nerv.IIP.Sdk.Observability.Tests/Nerv.IIP.Sdk.Observability.Tests.csproj`

Expected: all SDK Observability tests pass.

### Task 2: Server OpenTelemetry Traces and Metrics Baseline

**Files:**
- Modify: `backend/Directory.Packages.props`
- Modify: `backend/common/Observability/Nerv.IIP.Observability/Nerv.IIP.Observability.csproj`
- Modify: `backend/common/Observability/Nerv.IIP.Observability/NervIipObservability.cs`
- Create: `backend/tests/Nerv.IIP.Observability.Tests/Nerv.IIP.Observability.Tests.csproj`
- Create: `backend/tests/Nerv.IIP.Observability.Tests/NervIipObservabilityRegistrationTests.cs`

- [ ] **Step 1: Write the failing server observability tests**

Create tests that call `new ServiceCollection().AddNervIipObservability(configuration, "unit-test-service")`, build the provider, and assert:

- `NervIipObservabilityOptions.ServiceName` equals `unit-test-service`.
- OpenTelemetry hosted services are registered when observability is enabled.
- OpenTelemetry can be disabled through `OpenTelemetry:Enabled=false` while keeping existing options/logging registration.

- [ ] **Step 2: Run server tests and confirm they fail**

Run: `dotnet test backend/tests/Nerv.IIP.Observability.Tests/Nerv.IIP.Observability.Tests.csproj`

Expected before implementation: compile failure because the test project does not exist, or assertion failure because OpenTelemetry services are absent.

- [ ] **Step 3: Add OpenTelemetry dependencies and registration**

Add central package versions for:

```xml
<PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.15.3" />
<PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.15.3" />
<PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.15.2" />
<PackageVersion Include="OpenTelemetry.Instrumentation.Http" Version="1.15.1" />
<PackageVersion Include="OpenTelemetry.Instrumentation.Runtime" Version="1.15.1" />
```

Reference the packages from `Nerv.IIP.Observability.csproj`.

In `AddNervIipObservability`, keep Serilog behavior and add OpenTelemetry registration when `OpenTelemetry:Enabled` is not `false`:

- resource service name
- tracing: ASP.NET Core, HttpClient, OTLP exporter when endpoint configured
- metrics: ASP.NET Core, HttpClient, runtime, OTLP exporter when endpoint configured
- endpoint from `OTEL_EXPORTER_OTLP_ENDPOINT`, `OpenTelemetry:Endpoint`, or `Logging:OpenTelemetry:Endpoint`
- protocol from `OpenTelemetry:Protocol` or `Logging:OpenTelemetry:Protocol`, preserving current 4318/http auto-detection

- [ ] **Step 4: Run server tests and confirm they pass**

Run: `dotnet test backend/tests/Nerv.IIP.Observability.Tests/Nerv.IIP.Observability.Tests.csproj`

Expected: all server Observability tests pass.

### Task 3: Solution Integration and Verification

**Files:**
- Modify: `backend/Nerv.IIP.sln`

- [ ] **Step 1: Add new projects to the solution**

Run:

```powershell
dotnet sln backend/Nerv.IIP.sln add backend/common/Sdk/Nerv.IIP.Sdk.Observability/Nerv.IIP.Sdk.Observability.csproj --solution-folder common/Sdk
dotnet sln backend/Nerv.IIP.sln add backend/tests/Nerv.IIP.Sdk.Observability.Tests/Nerv.IIP.Sdk.Observability.Tests.csproj --solution-folder tests
dotnet sln backend/Nerv.IIP.sln add backend/tests/Nerv.IIP.Observability.Tests/Nerv.IIP.Observability.Tests.csproj --solution-folder tests
```

- [ ] **Step 2: Run focused verification**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Sdk.Observability.Tests/Nerv.IIP.Sdk.Observability.Tests.csproj
dotnet test backend/tests/Nerv.IIP.Observability.Tests/Nerv.IIP.Observability.Tests.csproj
dotnet build backend/Nerv.IIP.sln --no-restore
```

Expected: all commands exit 0.

- [ ] **Step 3: Review merge conflict surface**

Run: `git diff --name-status`

Expected touched files remain within SDK Observability, server Observability, `Sdk.Core`, central packages, new tests, solution, and this plan.
