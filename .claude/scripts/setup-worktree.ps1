#!/usr/bin/env pwsh
# Idempotent worktree environment setup — the Claude Code equivalent of
# .codex/environments/environment.toml [setup]. Wired to SessionStart in
# .claude/settings.json so a freshly created git worktree restores its environment
# automatically. Heavy steps are guarded by their output artifacts, so repeat
# sessions are a near-instant no-op.
#
# This is harness config (outside the governed scripts/ tree), so it may call pnpm /
# dotnet directly.
#
# Backend (.NET) restore is OPT-IN to keep frontend-focused worktrees fast — enable
# full codex-parity setup with:  $env:NERV_SETUP_BACKEND = '1'   (or run /setup-env)

$ErrorActionPreference = 'Stop'
$repoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName

function Step($message) { Write-Host "[setup] $message" }

# --- Frontend dependencies (needed for typecheck / test / build / preview) ---
if (-not (Test-Path (Join-Path $repoRoot 'frontend/node_modules'))) {
  Step 'frontend: pnpm install --frozen-lockfile'
  try {
    pnpm -C (Join-Path $repoRoot 'frontend') install --frozen-lockfile --config.confirmModulesPurge=false
  }
  catch {
    Write-Warning "[setup] frontend install failed: $($_.Exception.Message)"
  }
}
else {
  Step 'frontend deps present - skipping'
}

# --- Backend (.NET) restore — opt-in (slow; not needed for frontend work) ---
if ($env:NERV_SETUP_BACKEND -eq '1') {
  $marker = Join-Path $repoRoot 'backend/services/Iam/src/Nerv.IIP.Iam.Web/obj/project.assets.json'
  if (-not (Test-Path $marker)) {
    Step 'backend: dotnet restore (NERV_SETUP_BACKEND=1)'
    try {
      dotnet restore (Join-Path $repoRoot 'backend/Nerv.IIP.sln')
      dotnet restore (Join-Path $repoRoot 'connector-hosts/Nerv.IIP.ConnectorHost.sln')
    }
    catch {
      Write-Warning "[setup] backend restore failed: $($_.Exception.Message)"
    }
  }
  else {
    Step 'backend restore present - skipping'
  }
}
else {
  Step 'backend restore skipped (set NERV_SETUP_BACKEND=1 or run /setup-env for full parity)'
}

Step 'done'
