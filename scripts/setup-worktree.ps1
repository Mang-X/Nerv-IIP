# Script-Governance:
#   Category: release-install
#   SideEffects:
#     - Restores frontend pnpm dependencies for a freshly created worktree (idempotent)
#     - Optionally restores backend/.NET solutions when NERV_SETUP_BACKEND=1
#   Writes:
#     - frontend/node_modules/**
#     - backend/**/obj/**
#     - connector-hosts/**/obj/**
#     - artifacts/script-logs/**
#   Cleanup:
#     - Stops managed pnpm/dotnet process trees when they time out through ScriptAutomation.ps1
#   Requires:
#     - PowerShell 7
#     - Node.js 22.22.3
#     - pnpm 11.13.1
#     - .NET SDK 10 (only when NERV_SETUP_BACKEND=1)
#
# Worktree environment setup — the Claude Code equivalent of
# .codex/environments/environment.toml [setup]. Invoked from the .claude/settings.json
# SessionStart hook so a freshly created git worktree restores its environment
# automatically. Idempotent: heavy steps are guarded by their output artifacts, so
# repeat sessions are a near-instant no-op. Backend restore is opt-in (slow; not needed
# for frontend work) via:  $env:NERV_SETUP_BACKEND = '1'   (or run /setup-env).

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
. (Join-Path $root 'scripts/lib/ScriptAutomation.ps1')

function Write-SetupStep([string] $message) {
  Write-Host "[setup] $message"
}

# --- Frontend dependencies (needed for typecheck / test / build / preview) ---
if (-not (Test-Path (Join-Path $root 'frontend/node_modules'))) {
  Write-SetupStep 'frontend: pnpm install --frozen-lockfile'
  try {
    Invoke-Pnpm -Arguments @('-C', 'frontend', 'install', '--frozen-lockfile', '--config.confirmModulesPurge=false') -WorkingDirectory $root -TimeoutSeconds 900 -Name 'worktree-frontend-install' | Out-Null
  }
  catch {
    Write-Warning "[setup] frontend install failed: $($_.Exception.Message)"
  }
}
else {
  Write-SetupStep 'frontend deps present - skipping'
}

# --- Backend (.NET) restore - opt-in (slow; not needed for frontend work) ---
if ($env:NERV_SETUP_BACKEND -eq '1') {
  $marker = Join-Path $root 'backend/services/Iam/src/Nerv.IIP.Iam.Web/obj/project.assets.json'
  if (-not (Test-Path $marker)) {
    Write-SetupStep 'backend: dotnet restore (NERV_SETUP_BACKEND=1)'
    try {
      Invoke-DotNet -Arguments @('restore', (Join-Path $root 'backend/Nerv.IIP.sln')) -WorkingDirectory $root -TimeoutSeconds 900 -Name 'worktree-backend-restore' | Out-Null
      Invoke-DotNet -Arguments @('restore', (Join-Path $root 'connector-hosts/Nerv.IIP.ConnectorHost.sln')) -WorkingDirectory $root -TimeoutSeconds 900 -Name 'worktree-connector-restore' | Out-Null
    }
    catch {
      Write-Warning "[setup] backend restore failed: $($_.Exception.Message)"
    }
  }
  else {
    Write-SetupStep 'backend restore present - skipping'
  }
}
else {
  Write-SetupStep 'backend restore skipped (set NERV_SETUP_BACKEND=1 or run /setup-env for full parity)'
}

Write-SetupStep 'done'
