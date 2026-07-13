# Parallel Full-Stack Test Sessions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add governed, ephemeral Aspire full-stack sessions that allow two or three worktrees to run concurrently with dynamic ports, isolated writable storage, exact ownership cleanup, retained diagnostics, and no minimum-memory admission rule.

**Architecture:** Keep Aspire AppHost as the only topology source. A thin root command dispatches to a PowerShell coordinator; two focused libraries own durable session state and runtime ownership, while ephemeral AppHost mode switches writable storage, target/public ports and ownership labels without changing persistent Development defaults. Automated scenarios always collect diagnostics and clean up in `finally`; a detached lease guardian and stale-session GC recover interrupted runs without touching unrelated resources.

**Tech Stack:** PowerShell 7, .NET 10, installed Aspire CLI 13.4.x, repository-locked Aspire AppHost packages 13.4.0, Docker, Vite, Playwright, JSON session manifests, existing `ScriptAutomation.ps1` helpers.

---

## File Structure

Create:

1. `scripts/lib/FullStackSessionState.ps1` - session IDs, state-root discovery, manifests, atomic writes, cross-process locking, state transitions, leases, and count-based admission.
2. `scripts/lib/FullStackSessionRuntime.ps1` - Aspire JSON parsing, endpoint discovery, exact Docker/process ownership checks, diagnostic collection, and idempotent runtime cleanup.
3. `scripts/fullstack-session.ps1` - governed command coordinator for `run`, `start`, `url`, `status`, `logs`, `stop`, `list`, and `gc`.
4. `scripts/fullstack-guardian.ps1` - bounded lease monitor that invokes cleanup for one exact session.
5. `scripts/tests/fullstack-session-state.Tests.ps1` - fast state, locking, admission, transition, and lease contract tests.
6. `scripts/tests/fullstack-session-runtime.Tests.ps1` - fast Aspire JSON, endpoint, redaction, and ownership contract tests using fixtures only.
7. `scripts/tests/fixtures/fullstack/aspire-start.json` - observed installed Aspire CLI 13.4.x detached-start JSON fixture with synthetic paths and identifiers.
8. `scripts/tests/fixtures/fullstack/aspire-describe.json` - observed installed Aspire CLI 13.4.x resource JSON fixture with synthetic dynamic URLs.
9. `scripts/tests/fixtures/fullstack/docker-resources.json` - synthetic Docker inspect fixture containing owned, mismatched, and unlabeled resources.
10. `scripts/verify-parallel-fullstack-isolation.ps1` - real two- or three-session Docker/Aspire acceptance entrypoint.

Modify:

1. `nerv.ps1` - thin `fullstack` command dispatch and help text.
2. `scripts/tests/dev-entrypoint.Tests.ps1` - root command/help routing checks.
3. `scripts/aspire-control.ps1` - remove generic prefix-based orphan deletion from ordinary development stop.
4. `scripts/lib/ScriptAutomation.ps1` - add a detached managed-process helper whose stdio is redirected directly to files.
5. `scripts/tests/check-script-governance.Tests.ps1` - detached helper survival and log tests.
6. `infra/aspire/Nerv.IIP.AppHost/Program.cs` - validated ephemeral session mode, dynamic endpoint definitions, session-specific stateful volume names, and container runtime ownership labels.
7. `frontend/apps/console/vite.config.ts`, `frontend/apps/business-console/vite.config.ts`, and `frontend/apps/screen/vite.config.ts` - honor Aspire-injected `PORT` with existing fixed ports as fallbacks.
8. `frontend/apps/console/playwright.config.ts` and `frontend/apps/business-console/playwright.config.ts` - attach to manifest-derived `PLAYWRIGHT_BASE_URL` without starting a second Vite server.
9. `README.md` - persistent development versus ephemeral full-stack usage.
10. `infra/aspire/README.md` - AppHost session-mode environment contract.
11. `docs/architecture/deployment-baseline.md` - isolated full-stack topology and temporary storage ownership.
12. `docs/architecture/script-automation-governance.md` - manifest, lease, exact-ownership, detached-process, and cleanup rules.
13. `docs/architecture/implementation-readiness.md` - delivered command surface and verification status after implementation.
14. `AGENTS.md` - require agent-owned full-stack checks to use `fullstack run` and prohibit leaving interactive sessions alive.

No backend endpoint, Gateway contract, OpenAPI snapshot, generated client, database migration, frontend application behavior/UI, or second Compose topology changes are part of this plan. Frontend changes are limited to development/test port configuration.

---

### Task 1: Add The Session State Contract

**Files:**
- Create: `scripts/lib/FullStackSessionState.ps1`
- Create: `scripts/tests/fullstack-session-state.Tests.ps1`

- [ ] **Step 1: Write failing tests for identity, manifests, transitions, admission, and leases**

Create `scripts/tests/fullstack-session-state.Tests.ps1` as a self-contained PowerShell test script. Use a temporary state root and remove it in `finally`:

```powershell
$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
. (Join-Path $repoRoot 'scripts/lib/FullStackSessionState.ps1')

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-fullstack-state-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null

try {
    $sessionId = New-NervFullStackSessionId -WorktreeRoot $repoRoot
    Assert-True ($sessionId -match '^nerv-[a-f0-9]{4}-[a-f0-9]{6}$') "Invalid session ID: $sessionId"

    $manifest = New-NervFullStackManifest `
        -SessionId $sessionId `
        -WorktreeRoot $repoRoot `
        -AppHostProject (Join-Path $repoRoot 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj') `
        -ArtifactPath (Join-Path $repoRoot "artifacts/fullstack/$sessionId") `
        -StateRoot $testRoot `
        -LeaseMinutes 90

    Assert-True ($manifest.schemaVersion -eq 1) 'Manifest schema must be 1.'
    Assert-True ($manifest.state -eq 'Creating') 'New manifests must be Creating.'
    Assert-True (-not ($manifest | ConvertTo-Json -Depth 20).Contains('connectionString')) 'Manifest must not contain connection strings.'

    Write-NervFullStackManifest -Manifest $manifest -StateRoot $testRoot
    $reloaded = Read-NervFullStackManifest -SessionId $sessionId -StateRoot $testRoot
    Assert-True ($reloaded.sessionId -eq $sessionId) 'Atomic manifest round-trip failed.'

    Move-NervFullStackSessionState -Manifest $reloaded -State Running
    Assert-True ($reloaded.state -eq 'Running') 'Creating -> Running must be allowed.'
    $invalidFailed = $false
    try { Move-NervFullStackSessionState -Manifest $reloaded -State Creating } catch { $invalidFailed = $true }
    Assert-True $invalidFailed 'Running -> Creating must be rejected.'

    Write-NervFullStackManifest -Manifest $reloaded -StateRoot $testRoot
    $admission = Test-NervFullStackAdmission -StateRoot $testRoot -MaximumSessions 1 -ExcludeSessionId 'none'
    Assert-True (-not $admission.Allowed) 'A second active session must be denied at the configured ceiling.'

    $reloaded.leaseExpiresAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('O')
    Assert-True (Test-NervFullStackSessionStale -Manifest $reloaded -Now ([DateTimeOffset]::UtcNow)) 'Expired lease must be stale.'

    $script:lockCount = 0
    Invoke-WithNervFullStackSessionLock -StateRoot $testRoot -ScriptBlock { $script:lockCount++ }
    Assert-True ($script:lockCount -eq 1) 'Session lock must execute its body once.'
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Full-stack session state tests passed.'
```

- [ ] **Step 2: Run the state tests and verify the missing API failure**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/fullstack-session-state.Tests.ps1
```

Expected: FAIL because `scripts/lib/FullStackSessionState.ps1` does not exist.

- [ ] **Step 3: Implement the state library**

Create `scripts/lib/FullStackSessionState.ps1` with these public functions and exact invariants:

```powershell
Set-StrictMode -Version Latest

$script:NervFullStackStates = @('Creating', 'Running', 'Collecting', 'Failed', 'Stopping', 'Stopped', 'CleanupFailed')
$script:NervFullStackTransitions = @{
    Creating = @('Running', 'Failed', 'Stopping')
    Running = @('Collecting', 'Failed', 'Stopping')
    Collecting = @('Failed', 'Stopping')
    Failed = @('Stopping')
    Stopping = @('Stopped', 'CleanupFailed')
    CleanupFailed = @('Stopping')
    Stopped = @()
}

function Get-NervFullStackStateRoot {
    if (-not [string]::IsNullOrWhiteSpace($env:NERV_IIP_FULLSTACK_STATE_ROOT)) {
        return [System.IO.Path]::GetFullPath($env:NERV_IIP_FULLSTACK_STATE_ROOT)
    }
    if ($IsWindows) { return (Join-Path $env:LOCALAPPDATA 'Nerv-IIP') }
    $base = if ($env:XDG_STATE_HOME) { $env:XDG_STATE_HOME } else { Join-Path $HOME '.local/state' }
    return (Join-Path $base 'nerv-iip')
}

function New-NervFullStackSessionId([string] $WorktreeRoot) {
    $bytes = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes([IO.Path]::GetFullPath($WorktreeRoot).ToLowerInvariant()))
    $worktreeHash = [Convert]::ToHexString($bytes).ToLowerInvariant().Substring(0, 4)
    $random = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(3)).ToLowerInvariant()
    return "nerv-$worktreeHash-$random"
}

function Get-NervFullStackManifestPath([string] $SessionId, [string] $StateRoot = (Get-NervFullStackStateRoot)) {
    if ($SessionId -notmatch '^nerv-[a-f0-9]{4}-[a-f0-9]{6}$') { throw "Invalid full-stack session ID '$SessionId'." }
    return (Join-Path $StateRoot "fullstack-sessions/$SessionId.json")
}

function New-NervFullStackManifest {
    param([string] $SessionId, [string] $WorktreeRoot, [string] $AppHostProject, [string] $ArtifactPath,
          [string] $StateRoot = (Get-NervFullStackStateRoot), [int] $LeaseMinutes = 90)
    $now = [DateTimeOffset]::UtcNow
    return [ordered]@{
        schemaVersion = 1; sessionId = $SessionId; state = 'Creating'; mode = 'ephemeral'
        createdAtUtc = $now.ToString('O'); updatedAtUtc = $now.ToString('O')
        leaseExpiresAtUtc = $now.AddMinutes($LeaseMinutes).ToString('O')
        worktreeRoot = [IO.Path]::GetFullPath($WorktreeRoot)
        appHostProject = [IO.Path]::GetFullPath($AppHostProject)
        coordinator = [ordered]@{ pid = $PID; processStartTimeUtc = (Get-Process -Id $PID).StartTime.ToUniversalTime().ToString('O') }
        guardian = $null
        aspire = [ordered]@{ appHostId = $null; dcpId = $null }
        runtime = [ordered]@{ processIds = @(); containers = @(); containerIds = @(); networkIds = @(); volumeNames = @() }
        endpoints = [ordered]@{}
        artifactPath = [IO.Path]::GetFullPath($ArtifactPath)
        transitions = @([ordered]@{ state = 'Creating'; atUtc = $now.ToString('O') })
        cleanup = [ordered]@{ completedAtUtc = $null; remaining = @(); errors = @() }
        failure = $null
    }
}
```

Implement `Invoke-WithNervFullStackSessionLock` with a `FileStream` opened using `FileShare.None`, 100 ms retry intervals, and a 30-second deadline. Implement `Write-NervFullStackManifest` by writing UTF-8 JSON to `<manifest>.tmp-<guid>` and calling `[IO.File]::Move(temp, target, $true)` while the caller holds the lock. Implement `Read-NervFullStackManifest`, `Get-NervFullStackManifests`, `Move-NervFullStackSessionState`, `Renew-NervFullStackLease`, `Test-NervProcessIdentity`, `Test-NervFullStackSessionStale`, and `Test-NervFullStackAdmission` against the fields above. Active states are every state except `Stopped`; `CleanupFailed` consumes a slot until recovered. Admission is based only on `NERV_IIP_FULLSTACK_MAX_SESSIONS` or the supplied `-MaximumSessions`, defaulting to 3; do not inspect physical memory. Reject a second active session with the same normalized `worktreeRoot`, because Aspire lifecycle commands identify an isolated AppHost by its exact project path and ordinary stop must remain unambiguous.

- [ ] **Step 4: Run the state tests**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/fullstack-session-state.Tests.ps1
```

Expected: PASS with `Full-stack session state tests passed.`

- [ ] **Step 5: Commit the state contract**

```powershell
git add scripts/lib/FullStackSessionState.ps1 scripts/tests/fullstack-session-state.Tests.ps1
git commit -m "test: define full-stack session state contract"
```

---

### Task 2: Replace Unsafe Prefix Cleanup With Exact Ownership

**Files:**
- Create: `scripts/lib/FullStackSessionRuntime.ps1`
- Create: `scripts/tests/fullstack-session-runtime.Tests.ps1`
- Create: `scripts/tests/fixtures/fullstack/docker-resources.json`
- Modify: `scripts/aspire-control.ps1`

- [ ] **Step 1: Write failing ownership tests**

The fixture must contain three synthetic objects: one with label `com.nerv-iip.session=nerv-abcd-123456`, one with a different session label, and one without the label. The test must assert that only the exact label and an ID already recorded in the manifest authorize deletion:

```powershell
$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
. (Join-Path $repoRoot 'scripts/lib/FullStackSessionRuntime.ps1')

$fixture = Get-Content (Join-Path $PSScriptRoot 'fixtures/fullstack/docker-resources.json') -Raw | ConvertFrom-Json
$recorded = @('owned-container-id')
$owned = @($fixture | Where-Object {
    Test-NervDockerResourceOwnership -InspectObject $_ -SessionId 'nerv-abcd-123456' -RecordedIds $recorded
})
if ($owned.Count -ne 1 -or $owned[0].Id -ne 'owned-container-id') {
    throw "Expected only the exact recorded and labeled resource, got $($owned.Id -join ', ')."
}

$recordedNames = @('nerv-iip-postgres-18-nerv-abcd-123456')
if (-not (Test-NervDockerRecordedNameOwnership -Name $recordedNames[0] -SessionId 'nerv-abcd-123456' -RecordedNames $recordedNames)) {
    throw 'The exact manifest-recorded session volume must pass ownership validation.'
}
foreach ($name in @('postgres-random', 'nerv-iip-postgres-18-nerv-dead-654321')) {
    if (Test-NervDockerRecordedNameOwnership -Name $name -SessionId 'nerv-abcd-123456' -RecordedNames $recordedNames) {
        throw "Generic name '$name' must never prove ownership."
    }
}

Write-Host 'Full-stack runtime ownership tests passed.'
```

- [ ] **Step 2: Run the ownership tests and verify failure**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/fullstack-session-runtime.Tests.ps1
```

Expected: FAIL because the runtime ownership functions do not exist.

- [ ] **Step 3: Implement exact ownership primitives**

Create `scripts/lib/FullStackSessionRuntime.ps1` and dot-source both `ScriptAutomation.ps1` and `FullStackSessionState.ps1`. Define:

```powershell
function Test-NervDockerResourceOwnership {
    param([object] $InspectObject, [string] $SessionId, [string[]] $RecordedIds)
    $id = "$($InspectObject.Id)"
    if ($RecordedIds -notcontains $id) { return $false }
    $labels = if ($InspectObject.Config -and $InspectObject.Config.Labels) { $InspectObject.Config.Labels } else { $InspectObject.Labels }
    if ($null -eq $labels) { return $false }
    return "$($labels.'com.nerv-iip.session')" -ceq $SessionId
}

function Test-NervDockerRecordedNameOwnership {
    param([string] $Name, [string] $SessionId, [string[]] $RecordedNames)
    if ($SessionId -notmatch '^nerv-[a-f0-9]{4}-[a-f0-9]{6}$') { return $false }
    return ($RecordedNames -ccontains $Name) -and
        $Name.EndsWith("-$SessionId", [StringComparison]::Ordinal)
}
```

Add `Get-NervSessionDockerResources`, `Remove-NervSessionContainers`, `Remove-NervSessionNetworks`, and `Remove-NervSessionVolumes`. Every list and inspect operation must go through `Invoke-NativeCommandOutput`; every remove operation must go through `Invoke-NativeCommandWithTimeout`. Require both recorded identity and exact session label for containers. For named volumes, require `Test-NervDockerRecordedNameOwnership` plus the exact label when Docker exposes it. For networks, require an exact recorded ID plus the label when exposed. Return unresolved objects instead of deleting when proof is incomplete; a suffix or label without a manifest match is never sufficient.

- [ ] **Step 4: Remove generic orphan deletion from ordinary Aspire stop**

Delete `Stop-OrphanedAspireDevContainers` and its call from `scripts/aspire-control.ps1`. Keep `Stop-ProjectProcessesForCurrentRepo`, but after fallback process cleanup list Aspire development containers and emit a warning without deleting them when exact ownership cannot be proven. Keep `-All` only for explicit human invocation; do not add it to any automated path.

- [ ] **Step 5: Run runtime and ordinary-stop contract tests**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/fullstack-session-runtime.Tests.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/dev-entrypoint.Tests.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/check-script-governance.ps1
```

Expected: all three commands exit 0; runtime output ends with `Full-stack runtime ownership tests passed.` and governance ends with `Script governance check passed.`

- [ ] **Step 6: Commit ownership-safe cleanup**

```powershell
git add scripts/lib/FullStackSessionRuntime.ps1 scripts/tests/fullstack-session-runtime.Tests.ps1 scripts/tests/fixtures/fullstack/docker-resources.json scripts/aspire-control.ps1
git commit -m "fix: require exact Aspire session ownership for cleanup"
```

---

### Task 3: Add AppHost Ephemeral Session Mode

**Files:**
- Modify: `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- Modify: `frontend/apps/console/vite.config.ts`
- Modify: `frontend/apps/business-console/vite.config.ts`
- Modify: `frontend/apps/screen/vite.config.ts`
- Modify: `frontend/apps/console/playwright.config.ts`
- Modify: `frontend/apps/business-console/playwright.config.ts`
- Modify: `scripts/tests/fullstack-session-runtime.Tests.ps1`

- [ ] **Step 1: Record the current fixed-port failure with two short-path worktrees**

Create two detached worktrees under `(Get-NervFullStackStateRoot)/endpoint-probe/<runId>/a` and `/b`, run each worktree's `scripts/setup-worktree.ps1` so pnpm uses its global content-addressable store and hard-links dependencies, then start the first AppHost with `aspire start --isolated`. Start the second AppHost with the same command and current code. Always stop both exact AppHost paths and remove only the two validated probe worktrees in `finally`.

Expected before implementation: the second topology cannot keep all three Vite resources up because `console`, `business-console`, and `screen` still bind 5105, 5125, and 5128. Save the redacted failure resource names in the task notes; do not commit machine paths or logs.

- [ ] **Step 2: Extend the runtime test with the AppHost environment contract**

Add assertions for `Get-NervFullStackEnvironment` in the runtime library. The expected values are:

```powershell
$environment = Get-NervFullStackEnvironment -SessionId 'nerv-abcd-123456'
if ($environment.NERV_IIP_EPHEMERAL -ne 'true') { throw 'Ephemeral flag missing.' }
if ($environment.NERV_IIP_SESSION_ID -ne 'nerv-abcd-123456') { throw 'Session ID missing.' }
foreach ($expected in @(
    'nerv-iip-postgres-18-nerv-abcd-123456',
    'nerv-iip-redis-nerv-abcd-123456',
    'nerv-iip-minio-nerv-abcd-123456',
    'nerv-iip-victoria-logs-nerv-abcd-123456'
)) {
    if ($environment.Values -notcontains $expected) { throw "Missing ephemeral volume '$expected'." }
}
```

Run the test and expect failure because the environment function is absent.

- [ ] **Step 3: Add validated AppHost storage, ownership, and endpoint modes**

At the top of `Program.cs`, after `CreateBuilder`, add validated configuration and derived volume names:

```csharp
using System.Text.RegularExpressions;

var fullStackSessionId = Environment.GetEnvironmentVariable("NERV_IIP_SESSION_ID");
var fullStackEphemeral = string.Equals(
    Environment.GetEnvironmentVariable("NERV_IIP_EPHEMERAL"),
    "true",
    StringComparison.OrdinalIgnoreCase);

if (fullStackEphemeral &&
    (string.IsNullOrWhiteSpace(fullStackSessionId) ||
     !Regex.IsMatch(fullStackSessionId, "^nerv-[a-f0-9]{4}-[a-f0-9]{6}$", RegexOptions.CultureInvariant)))
{
    throw new InvalidOperationException("NERV_IIP_EPHEMERAL=true requires a validated NERV_IIP_SESSION_ID.");
}

string SessionVolume(string persistentName) =>
    fullStackEphemeral ? $"{persistentName}-{fullStackSessionId}" : persistentName;
```

Replace only the four named writable volumes:

```csharp
.WithDataVolume(SessionVolume("nerv-iip-postgres-18"));
.WithDataVolume(SessionVolume("nerv-iip-redis"));
.WithVolume(SessionVolume("nerv-iip-minio"), "/data");
.WithVolume(SessionVolume("nerv-iip-victoria-logs"), "/victoria-logs-data");
```

Add a local generic helper using the repository-locked Aspire AppHost 13.4.0 API:

```csharp
Aspire.Hosting.ApplicationModel.IResourceBuilder<T> WithFullStackOwnership<T>(
    Aspire.Hosting.ApplicationModel.IResourceBuilder<T> resource)
    where T : Aspire.Hosting.ApplicationModel.ContainerResource
{
    return fullStackEphemeral
        ? resource.WithContainerRuntimeArgs("--label", $"com.nerv-iip.session={fullStackSessionId}")
        : resource;
}
```

Wrap PostgreSQL, Redis, MinIO, VictoriaLogs, and conditionally created RabbitMQ/OTel container builders with `WithFullStackOwnership`. Persistent development mode must retain the existing volume names and receive no session label.

For every .NET project resource, preserve the current `.WithHttpEndpoint(port: <fixed>, name: "http")` call in persistent mode, but use `.WithHttpEndpoint(name: "http")` in ephemeral mode so Aspire allocates both target and public ports. For stateful containers, keep the internal `targetPort` but omit public `port` in ephemeral mode. For each Vite app, preserve the current fixed, non-proxied endpoint in persistent mode; in ephemeral mode use `WithEndpoint(targetPort: null, port: null, scheme: "http", name: "http", env: "PORT", isProxied: false)` so DCP allocates and injects a process binding port. Do not rely on `--isolated` to rewrite an explicitly fixed non-proxied target.

- [ ] **Step 4: Make Vite and Playwright consume allocated endpoints**

In each Vite config, replace the literal server port with an environment-aware fallback and retain existing proxies:

```typescript
const port = Number(process.env.PORT ?? '<existing-port>')

server: {
  port,
  strictPort: true,
  proxy: { /* keep the current proxy map byte-for-byte */ },
}
```

Use `5105`, `5125`, and `5128` respectively for `<existing-port>`. The Vite proxies already use AppHost-provided Gateway environment variables, so browser requests remain same-origin and no permissive dynamic CORS policy is added.

In both Playwright configs, use the manifest-provided URL when present and do not launch another Vite server:

```typescript
const externalBaseURL = process.env.PLAYWRIGHT_BASE_URL
const baseURL = externalBaseURL ?? `http://127.0.0.1:${port}`

export default defineConfig({
  use: { baseURL },
  webServer: externalBaseURL
    ? undefined
    : { command: `vp dev --host 127.0.0.1 --port ${port}`, url: baseURL, reuseExistingServer: !process.env.CI, timeout: 120_000 },
})
```

- [ ] **Step 5: Implement the matching PowerShell environment generator**

Add `Get-NervFullStackEnvironment` to `FullStackSessionRuntime.ps1`. Validate the same regex and return a hashtable containing `NERV_IIP_EPHEMERAL`, `NERV_IIP_SESSION_ID`, `ASPNETCORE_ENVIRONMENT`, `DOTNET_ENVIRONMENT`, and the four derived volume names for manifest recording. The AppHost reads the first two; the remaining names are coordinator-owned manifest facts.

- [ ] **Step 6: Run fast checks and build both modes**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/fullstack-session-runtime.Tests.ps1
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
pnpm -C frontend --filter @nerv-iip/console typecheck
pnpm -C frontend --filter @nerv-iip/business-console typecheck
pnpm -C frontend --filter @nerv-iip/screen typecheck
```

Expected: runtime tests pass, AppHost build has no warnings/errors, and all three affected frontend typechecks pass.

- [ ] **Step 7: Prove two-instance dynamic endpoints before Task 4**

Repeat the two-worktree probe from Step 1 with the new ephemeral environment. Use `aspire describe --format Json` for each exact AppHost and assert that `gateway`, `business-gateway`, `console`, `business-console`, and `screen` each have distinct target and public ports across the two instances. HTTP-check all five URLs and verify the three Vite processes report the injected `PORT` rather than their persistent fallback.

Expected: both topologies remain up simultaneously and all ten endpoint URLs are reachable and pairwise distinct by resource. If any target remains fixed or either topology reports a `Finished` project, stop here and do not begin Task 4.

- [ ] **Step 8: Commit ephemeral AppHost mode**

```powershell
git add infra/aspire/Nerv.IIP.AppHost/Program.cs frontend/apps/console/vite.config.ts frontend/apps/business-console/vite.config.ts frontend/apps/screen/vite.config.ts frontend/apps/console/playwright.config.ts frontend/apps/business-console/playwright.config.ts scripts/lib/FullStackSessionRuntime.ps1 scripts/tests/fullstack-session-runtime.Tests.ps1
git commit -m "feat: isolate Aspire full-stack ports and storage"
```

---

### Task 4: Add Start, Endpoint Discovery, And Interactive Commands

**Files:**
- Create: `scripts/fullstack-session.ps1`
- Create: `scripts/tests/fixtures/fullstack/aspire-start.json`
- Create: `scripts/tests/fixtures/fullstack/aspire-describe.json`
- Modify: `scripts/lib/FullStackSessionRuntime.ps1`
- Modify: `scripts/tests/fullstack-session-runtime.Tests.ps1`
- Modify: `nerv.ps1`
- Modify: `scripts/tests/dev-entrypoint.Tests.ps1`

- [ ] **Step 1: Capture synthetic installed-Aspire-CLI 13.4.x fixtures and write failing parsers**

Run `aspire start --help`, `aspire describe --help`, and use one disposable manual session only long enough to capture the JSON property names. Replace paths, PIDs, IDs, URLs, and tokens with synthetic values before committing fixtures. The fixtures must contain no user profile paths or secrets.

Add tests that call:

```powershell
$start = Read-NervAspireJson -Text (Get-Content $startFixture -Raw)
$describe = Read-NervAspireJson -Text (Get-Content $describeFixture -Raw)
$identity = Get-NervAspireStartIdentity -StartObject $start
$endpoint = Get-NervAspireResourceEndpoint -DescribeObject $describe -ResourceName 'business-console' -EndpointName 'http'
if ([string]::IsNullOrWhiteSpace($identity.AppHostId)) { throw 'AppHost ID was not parsed.' }
if ($endpoint -ne 'http://127.0.0.1:43125') { throw "Unexpected endpoint '$endpoint'." }
```

`Read-NervAspireJson` must tolerate bounded human text before or after one JSON object, but must reject zero or multiple valid payloads and redact retained raw text through `Protect-ScriptAutomationText`.

- [ ] **Step 2: Implement Aspire JSON and endpoint helpers**

Add the parser functions above plus `Wait-NervAspireResource`, `Get-NervAspireResourceSnapshot`, and `Save-NervFullStackEndpoints`. Use only `Invoke-AspireOutput` with `--format Json --apphost <exact path> --non-interactive --nologo`; never parse table output or scan ports. Save `gateway`, `business-gateway`, `console`, `business-console`, and `screen` URLs to `manifest.endpoints`.

- [ ] **Step 3: Write failing root CLI routing tests**

Extend `dev-entrypoint.Tests.ps1` to require this help line:

```text
.\nerv.ps1 fullstack run -Scenario smoke
```

Invoke `fullstack help` and assert it lists `run`, `start`, `url`, `status`, `logs`, `stop`, `list`, and `gc`. Run the test and expect failure before changing `nerv.ps1`.

- [ ] **Step 4: Implement thin root dispatch**

Add `Scenario` and `SessionId` to the root parameter block so PowerShell can bind the documented named arguments:

```powershell
[ValidateSet('smoke')]
[string] $Scenario,

[string] $SessionId,
```

Add no full-stack lifecycle logic to `nerv.ps1`. Dispatch positional arguments plus only explicitly bound common options:

```powershell
'fullstack' {
    $fullStackScript = Join-Path $repoRoot 'scripts/fullstack-session.ps1'
    $fullStackArguments = [System.Collections.Generic.List[string]]::new()
    foreach ($argument in $RemainingArguments) { $fullStackArguments.Add($argument) }
    if ($PSBoundParameters.ContainsKey('Scenario')) { $fullStackArguments.Add('-Scenario'); $fullStackArguments.Add($Scenario) }
    if ($PSBoundParameters.ContainsKey('SessionId')) { $fullStackArguments.Add('-SessionId'); $fullStackArguments.Add($SessionId) }
    if ($PSBoundParameters.ContainsKey('NoBuild')) { $fullStackArguments.Add('-NoBuild') }
    if ($PSBoundParameters.ContainsKey('Tail')) { $fullStackArguments.Add('-Tail'); $fullStackArguments.Add("$Tail") }
    if ($PSBoundParameters.ContainsKey('Follow')) { $fullStackArguments.Add('-Follow') }
    & $fullStackScript @fullStackArguments
    exit $LASTEXITCODE
}
```

The tests must cover named forwarding for `fullstack run -Scenario smoke -NoBuild`, `fullstack stop -SessionId nerv-abcd-123456`, and positional forwarding for `fullstack url business-console`.

- [ ] **Step 5: Implement the governed coordinator shell**

Create `scripts/fullstack-session.ps1` with a Script-Governance `verify` header and parameters:

```powershell
param(
    [Parameter(Position = 0)]
    [ValidateSet('run', 'start', 'url', 'status', 'logs', 'stop', 'list', 'gc', 'help')]
    [string] $Action = 'help',
    [Parameter(Position = 1)] [string] $Target,
    [ValidateSet('smoke')] [string] $Scenario = 'smoke',
    [string] $SessionId,
    [switch] $NoBuild,
    [int] $Tail = 120,
    [switch] $Follow
)
```

Dot-source `ScriptAutomation.ps1`, `FullStackSessionState.ps1`, and `FullStackSessionRuntime.ps1`. Implement `start` under `Invoke-WithNervFullStackSessionLock`: reconcile stale manifests, enforce the active-session ceiling and one-active-session-per-worktree rule, create the manifest/artifact directory, call `Invoke-AspireOutput` with `start --isolated --format Json`, record exact startup identity, discover Docker resources with the exact session label into `manifest.runtime.containers` entries shaped as `{ resourceName, id, name }`, wait for all five public resources (`gateway`, `business-gateway`, `console`, `business-console`, and `screen`), discover endpoints, set `Running`, persist, and print session ID plus URLs. Resolve an omitted `-SessionId` for `url/status/logs/stop` only when exactly one non-stopped session belongs to the current worktree; otherwise require an explicit ID.

Implement `url` as a manifest read, lease renewal, and one URL written to stdout. Implement `status` and `list` as redacted summaries. Implement `logs` through Aspire CLI and the exact manifest AppHost path. Defer automated `run`, guardian, and GC behavior to Tasks 5 and 6, but make their dispatch branches fail with a clear non-zero `not available until lifecycle task is installed` message during this intermediate commit.

- [ ] **Step 6: Run fast CLI and parser tests**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/dev-entrypoint.Tests.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/fullstack-session-runtime.Tests.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/check-script-governance.ps1
```

Expected: all exit 0. No Docker or AppHost is started by these fast tests.

- [ ] **Step 7: Commit command and discovery surface**

```powershell
git add nerv.ps1 scripts/fullstack-session.ps1 scripts/lib/FullStackSessionRuntime.ps1 scripts/tests/dev-entrypoint.Tests.ps1 scripts/tests/fullstack-session-runtime.Tests.ps1 scripts/tests/fixtures/fullstack/aspire-start.json scripts/tests/fixtures/fullstack/aspire-describe.json
git commit -m "feat: add isolated full-stack session commands"
```

---

### Task 5: Add Lease Guardian, Stale GC, And Idempotent Stop

**Files:**
- Create: `scripts/fullstack-guardian.ps1`
- Modify: `scripts/fullstack-session.ps1`
- Modify: `scripts/lib/ScriptAutomation.ps1`
- Modify: `scripts/lib/FullStackSessionState.ps1`
- Modify: `scripts/lib/FullStackSessionRuntime.ps1`
- Modify: `scripts/tests/check-script-governance.Tests.ps1`
- Modify: `scripts/tests/fullstack-session-state.Tests.ps1`
- Modify: `scripts/tests/fullstack-session-runtime.Tests.ps1`

- [ ] **Step 1: Add failing stale-session and repeated-stop tests**

Extend state tests with manifests for expired lease, missing coordinator, reused PID with a different start time, and a stopped session. Extend runtime tests with injected command scriptblocks so that two calls to `Stop-NervFullStackSession` produce zero remaining resources and do not attempt broad Docker removal. Assert a live session is never selected by `Get-NervStaleFullStackSessions`.

Add a governance-helper test for `Start-DetachedManagedProcess`: launch a short fixture process whose stdout and stderr are redirected directly to separate files, let the launching PowerShell process exit, and assert from a second PowerShell process that the child survives long enough to write its completion marker. Also cover arguments containing spaces and quotes. The helper must return only the process identity needed by the manifest and must not retain a `Process` object or parent-side stream-copy task.

- [ ] **Step 2: Implement bounded stop and GC**

In `FullStackSessionRuntime.ps1`, implement `Stop-NervFullStackSession` in this order: transition to `Stopping`; invoke `aspire stop --apphost <exact manifest path>` with 120-second timeout; stop only recorded process identities; inspect and remove exact labeled/recorded containers, networks, and volumes; verify no owned runtime remains; transition to `Stopped` or `CleanupFailed`; persist cleanup errors and remaining identities. Missing manifests must throw. Already stopped manifests with no owned resources return success.

In `fullstack-session.ps1`, implement `stop` and `gc`. `gc` must hold the cross-process lock while choosing stale sessions, release it while performing bounded cleanup, and re-acquire it for each final manifest update. It must never call `aspire stop --all`, `docker system prune`, or delete by resource-name prefix.

- [ ] **Step 3: Implement the guardian process**

Create `scripts/fullstack-guardian.ps1` with Script-Governance category `verify`. Accept `-SessionId`, `-Mode Automated|Interactive`, optional `-CoordinatorPid`, optional `-CoordinatorStartTimeUtc`, and `-IntervalSeconds`. Every interval, read the exact manifest and exit for `Stopped`. In `Automated` mode, invoke the governed full-stack script with `stop -SessionId <id>` when the run coordinator identity disappears or the lease expires. In `Interactive` mode, ignore the short-lived `start` command PID and stop only when the renewable lease expires.

Add `Start-DetachedManagedProcess` to `ScriptAutomation.ps1`. It is the only governed wrapper allowed to use PowerShell's built-in `Start-Process`; pass an argument list without command-string evaluation, use `-WindowStyle Hidden` on Windows, redirect stdout and stderr directly to caller-supplied files, return PID plus start time, and retain no parent-side pipes or copy tasks. Preserve the existing `Start-ManagedBackgroundProcess` behavior for callers that need live output capture.

Launch the guardian through `Start-DetachedManagedProcess` using the current `pwsh` executable and dedicated artifact log files. For `run`, record the run script as coordinator and launch the guardian in `Automated` mode. For interactive `start`, launch in `Interactive` mode and replace `manifest.coordinator` with the guardian PID/start time before returning, so the completed `start` command is not mistaken for a crash. Record the same identity under `manifest.guardian`. Default interval is `NERV_IIP_FULLSTACK_GUARDIAN_INTERVAL_SECONDS` or 60 seconds. `status`, `url`, and `logs` renew the lease; default lease is `NERV_IIP_FULLSTACK_LEASE_MINUTES` or 90 minutes.

- [ ] **Step 4: Run fast lifecycle tests**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/fullstack-session-state.Tests.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/fullstack-session-runtime.Tests.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/check-script-governance.ps1
```

Expected: all exit 0 and no live process/container is started by fixtures.

- [ ] **Step 5: Commit lifecycle recovery**

```powershell
git add scripts/fullstack-guardian.ps1 scripts/fullstack-session.ps1 scripts/lib/ScriptAutomation.ps1 scripts/lib/FullStackSessionState.ps1 scripts/lib/FullStackSessionRuntime.ps1 scripts/tests/check-script-governance.Tests.ps1 scripts/tests/fullstack-session-state.Tests.ps1 scripts/tests/fullstack-session-runtime.Tests.ps1
git commit -m "feat: recover abandoned full-stack sessions"
```

---

### Task 6: Add Managed Smoke Run And Diagnostic Retention

**Files:**
- Modify: `scripts/fullstack-session.ps1`
- Modify: `scripts/lib/FullStackSessionRuntime.ps1`
- Modify: `scripts/tests/fullstack-session-runtime.Tests.ps1`

- [ ] **Step 1: Write failing scenario and redaction tests**

Add fixture-driven tests for `Invoke-NervFullStackSmokeScenario` using injected HTTP and Aspire snapshot callbacks. Prove that all five URLs come from `manifest.endpoints`, a resource state of `Finished` fails, and returned child environment values are exactly:

```powershell
@{
    NERV_IIP_GATEWAY_URL = $manifest.endpoints.gateway
    NERV_IIP_BUSINESS_GATEWAY_URL = $manifest.endpoints.'business-gateway'
    PLAYWRIGHT_BASE_URL = $manifest.endpoints.'business-console'
}
```

Add a redaction test containing `password=secret`, `Authorization: Bearer token`, and a PostgreSQL connection string; assert none survives in `summary.json` or diagnostic text.

- [ ] **Step 2: Implement bounded diagnostic collection**

Add `Collect-NervFullStackDiagnostics`. Before runtime shutdown, write bounded Aspire JSON logs for relevant resources under `artifacts/fullstack/<sessionId>/aspire-logs/`, preserve existing `traces/`, `screenshots/`, and `test-results/`, and write redacted `summary.json`. Collection timeout defaults to `NERV_IIP_FULLSTACK_COLLECT_TIMEOUT_SECONDS` or 120 seconds. Collection failures append a redacted error and never skip cleanup.

- [ ] **Step 3: Implement the governed smoke scenario**

`Invoke-NervFullStackSmokeScenario` must wait for and HTTP-check `gateway`, `business-gateway`, `console`, `business-console`, and `screen`; inspect the complete Aspire JSON snapshot; fail when any project resource is `Finished`; and return a scenario exit code. It must not accept arbitrary commands. The scenario registry is a literal switch with only `smoke` in this implementation.

- [ ] **Step 4: Implement `fullstack run` with original-error preservation**

Implement this control shape in `fullstack-session.ps1`:

```powershell
$scenarioFailure = $null
$cleanupFailure = $null
try {
    $manifest = Start-NervFullStackSession -NoBuild:$NoBuild -CoordinatorPid $PID
    Invoke-NervFullStackScenario -Name $Scenario -Manifest $manifest
}
catch {
    $scenarioFailure = $_
    if ($manifest) { Set-NervFullStackFailure -Manifest $manifest -Category 'ScenarioOrStartup' -ErrorRecord $_ }
}
finally {
    if ($manifest) {
        if ($manifest.state -eq 'Running') { Move-NervFullStackSessionState -Manifest $manifest -State Collecting }
        try { Collect-NervFullStackDiagnostics -Manifest $manifest }
        catch { Add-NervFullStackCleanupError -Manifest $manifest -ErrorRecord $_ }
        try { Stop-NervFullStackSession -Manifest $manifest }
        catch { $cleanupFailure = $_ }
    }
}
if ($cleanupFailure) { throw $cleanupFailure }
if ($scenarioFailure) { throw $scenarioFailure }
```

Do not leave the guardian alive after a completed automated run. Verify the guardian and coordinator-owned runtime are gone while preserving artifacts and stopped manifest history.

- [ ] **Step 5: Run fast scenario tests**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/fullstack-session-runtime.Tests.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/check-script-governance.ps1
```

Expected: both exit 0; tests prove `finally` cleanup through injected callbacks without Docker.

- [ ] **Step 6: Commit managed run behavior**

```powershell
git add scripts/fullstack-session.ps1 scripts/lib/FullStackSessionRuntime.ps1 scripts/tests/fullstack-session-runtime.Tests.ps1
git commit -m "feat: run full-stack smoke checks with guaranteed cleanup"
```

---

### Task 7: Add Real Parallel Isolation Acceptance

**Files:**
- Create: `scripts/verify-parallel-fullstack-isolation.ps1`
- Modify: `scripts/tests/fullstack-session-runtime.Tests.ps1`

- [ ] **Step 1: Add a failing static acceptance-script contract**

Extend runtime tests to parse the new script AST and assert it declares Script-Governance, accepts `-Sessions` with range 2..3, roots disposable worktrees below `Get-NervFullStackStateRoot` rather than the repository, invokes each worktree's `scripts/setup-worktree.ps1`, uses `Invoke-PwshScript`/managed helpers, and contains a `finally` cleanup path for every launched session. Run the test before creating the script and expect failure.

- [ ] **Step 2: Implement the real verification entrypoint**

Create `scripts/verify-parallel-fullstack-isolation.ps1` with:

```powershell
param(
    [ValidateRange(2, 3)] [int] $Sessions = 2,
    [switch] $NoBuild,
    [switch] $InjectFailure
)
```

The script must create disposable linked worktrees under the short machine-state path `(Get-NervFullStackStateRoot)/fullstack-worktrees/<runId>/` from the current commit using `Invoke-NativeCommandWithTimeout -Command 'git' -Arguments @('worktree', 'add', '--detach', <path>, 'HEAD')`. Never nest these worktrees under the repository or its `artifacts/` directory. Validate every resolved worktree path remains below that exact run directory before cleanup.

After each `git worktree add`, invoke that worktree's `scripts/setup-worktree.ps1` before starting Aspire. This reuses pnpm's global content-addressable store and hard-links packages into each worktree; do not copy the primary worktree's `node_modules`. Because the current setup script reports dependency-install failures as warnings, explicitly require `<worktree>/frontend/node_modules` to exist afterward and fail the acceptance setup if it does not. Keep backend restore opt-in because AppHost startup performs the required build unless `-NoBuild` was explicitly requested and valid outputs already exist. Launch `fullstack start` once per prepared worktree through governed background helpers so all stacks remain live during assertions, then collect each session by exact `manifest.worktreeRoot`.

Assert distinct endpoint URLs and distinct PostgreSQL, Redis, MinIO, and VictoriaLogs volume names. Select each PostgreSQL container from `manifest.runtime.containers` by `resourceName = 'postgres'`. Through `Invoke-NativeCommandOutput -Command 'docker'`, run `exec --user postgres <firstContainerId> psql -d postgres -v ON_ERROR_STOP=1 -c 'create table nerv_fullstack_isolation_probe(value text); insert into nerv_fullstack_isolation_probe values (''session-one'');'`, then run `exec --user postgres <secondContainerId> psql -d postgres -Atc 'select to_regclass(''public.nerv_fullstack_isolation_probe'');'` and require empty output. This proves writable database isolation without reading or logging a password.

Stop the first session and re-check the second session's discovered gateway URL. With `-InjectFailure`, throw a synthetic failure after both stacks are live; the outer `finally` must still stop every recorded session, run `fullstack gc`, and remove only worktrees created beneath the validated short state-root run directory using governed `git worktree remove --force` calls. The expected injected failure is converted to success only after cleanup assertions prove no owned runtime remains.

- [ ] **Step 3: Run static and governance tests**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/fullstack-session-runtime.Tests.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/check-script-governance.ps1
```

Expected: both exit 0 and no real stack starts yet.

- [ ] **Step 4: Run required two-session acceptance**

Precondition: Docker Desktop is running and AppHost user secrets are initialized.

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/verify-parallel-fullstack-isolation.ps1 -Sessions 2
```

Expected: two isolated stacks become reachable, ownership/isolation assertions pass, both runtime stacks are removed, manifests end in `Stopped`, and artifacts remain.

- [ ] **Step 5: Run failure cleanup acceptance**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/verify-parallel-fullstack-isolation.ps1 -Sessions 2 -InjectFailure
```

Expected: the script observes the intentional scenario failure, confirms both sessions were cleaned, and exits 0 because cleanup behavior is the assertion under test.

- [ ] **Step 6: Optionally run the three-session stress path**

Run when local capacity permits:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/verify-parallel-fullstack-isolation.ps1 -Sessions 3
```

Expected: three sessions run when all three configured slots are free. If an existing session occupies a slot, the excess request waits for the bounded session-count capacity timeout or fails without creating partial resources. There is no physical-memory gate.

- [ ] **Step 7: Commit real acceptance**

```powershell
git add scripts/verify-parallel-fullstack-isolation.ps1 scripts/tests/fullstack-session-runtime.Tests.ps1 scripts/lib/ScriptAutomation.ps1
git commit -m "test: verify parallel Aspire full-stack isolation"
```

Only include `scripts/lib/ScriptAutomation.ps1` in the commit if the governed git helper was required and added.

---

### Task 8: Document Operations And Run Final Gates

**Files:**
- Modify: `README.md`
- Modify: `infra/aspire/README.md`
- Modify: `docs/architecture/deployment-baseline.md`
- Modify: `docs/architecture/script-automation-governance.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `AGENTS.md`

- [ ] **Step 1: Update operator and contributor documentation**

Document these exact distinctions and commands:

```powershell
.\nerv.ps1 dev
.\nerv.ps1 fullstack run -Scenario smoke
.\nerv.ps1 fullstack start
.\nerv.ps1 fullstack url business-console
.\nerv.ps1 fullstack status
.\nerv.ps1 fullstack logs gateway
.\nerv.ps1 fullstack stop
.\nerv.ps1 fullstack list
.\nerv.ps1 fullstack gc
```

State that `dev` is persistent, `fullstack` is ephemeral, ports are discovered from manifests, the default ceiling is three active sessions, there is no minimum-memory rule, and successful/failed automated runs remove runtime resources while preserving `artifacts/fullstack/<sessionId>/`.

In `AGENTS.md`, add one concise rule: agent-owned real full-stack verification must use `fullstack run`; interactive `fullstack start` is diagnostic-only and must be stopped before handoff. Do not change ordinary unit/integration test instructions.

- [ ] **Step 2: Run all fast gates**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/dev-entrypoint.Tests.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/fullstack-session-state.Tests.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/fullstack-session-runtime.Tests.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/check-script-governance.Tests.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/check-script-governance.ps1
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
git diff --check
```

Expected: every command exits 0; AppHost has no warnings or errors; governance baseline gains no exemption.

- [ ] **Step 3: Re-run the required real gate from a clean session state**

Run:

```powershell
.\nerv.ps1 fullstack gc
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/verify-parallel-fullstack-isolation.ps1 -Sessions 2
.\nerv.ps1 fullstack list
```

Expected: acceptance exits 0, list shows both acceptance sessions as `Stopped`, and Docker/Aspire status has no live resources owned by those session IDs.

- [ ] **Step 4: Verify design coverage and scope**

Inspect the final diff and confirm:

1. Every design command exists.
2. Session IDs bind manifests, volumes, labels, endpoints, processes, and artifacts.
3. Cleanup never uses a generic resource prefix or automated `aspire stop --all`.
4. Persistent `nerv.ps1 dev` volume names are unchanged.
5. No physical-memory measurement or `NERV_IIP_FULLSTACK_MIN_FREE_GB` exists.
6. No backend API, OpenAPI, generated client, migration, Nginx, or second service topology changed.

- [ ] **Step 5: Commit documentation and final verification record**

```powershell
git add README.md AGENTS.md infra/aspire/README.md docs/architecture/deployment-baseline.md docs/architecture/script-automation-governance.md docs/architecture/implementation-readiness.md
git commit -m "docs: document isolated full-stack test sessions"
```

---

## Completion Evidence

Before claiming completion, report the exact output summaries for:

1. `scripts/check-script-governance.ps1`.
2. Both fast full-stack test scripts.
3. AppHost build.
4. Required two-session real acceptance.
5. Post-acceptance `fullstack list`, Aspire status, and Docker ownership reconciliation.
6. `git diff --check` and final `git status --short`.

If Docker is unavailable, fast tests and AppHost build can still be reported, but the implementation is not complete because the required two-session isolation and cleanup behavior remain unverified.
