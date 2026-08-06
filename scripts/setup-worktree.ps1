# Script-Governance:
#   Category: release-install
#   SideEffects:
#     - Mirrors the main worktree's installed agent skills into a fresh worktree (idempotent)
#     - Installs skills in the MAIN worktree via the skills CLI only when they are missing there
#     - Restores frontend pnpm dependencies for a freshly created worktree (idempotent)
#     - Optionally restores backend/.NET solutions when NERV_SETUP_BACKEND=1
#   Writes:
#     - .agents/skills/**
#     - .claude/skills/**
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

# --- Agent skills ---------------------------------------------------------
# `npx skills` installs the real skill payload into .agents/skills/ and exposes it to
# each agent runtime through a directory of relative symlinks (.claude/skills/<name> ->
# ../../.agents/skills/<name>). Both layers are gitignored, so a fresh worktree starts
# with .claude/skills/ empty and every repo-level skill silently missing. Reinstalling
# from the network per worktree costs minutes; the main worktree already holds a
# resolved, hash-locked copy, so mirror that instead (same trick as the codex
# .codex/environments/environment.toml [setup] block, which copies .agents/skills/).
$agentSkillsRelative = '.agents/skills'
$claudeSkillsRelative = '.claude/skills'

function Test-SkillsPayloadPresent([string] $repoRoot) {
  # Guard on content, not existence: a mirror that fails midway leaves an empty
  # .agents/skills behind, and an existence check would treat that as "installed"
  # forever after.
  $payloadRoot = Join-Path $repoRoot $agentSkillsRelative
  if (-not (Test-Path $payloadRoot)) { return $false }
  return @(Get-ChildItem -LiteralPath $payloadRoot -Force).Count -gt 0
}

function Get-MainWorktreeRoot([string] $worktreeRoot) {
  # A linked worktree's .git is a file pointing at <main>/.git/worktrees/<name>; the
  # common git dir is always <main>/.git, so its parent is the main worktree root.
  $result = Invoke-NativeCommandOutput -Command 'git' -Arguments @('rev-parse', '--path-format=absolute', '--git-common-dir') -WorkingDirectory $worktreeRoot -TimeoutSeconds 30 -Name 'worktree-git-common-dir'
  $commonGitDir = $result.Stdout.Trim()
  if ([string]::IsNullOrWhiteSpace($commonGitDir)) { return $worktreeRoot }
  return (Split-Path -Parent $commonGitDir)
}

function Copy-SkillLinkLayer([string] $sourceRoot, [string] $targetRoot) {
  # Recreate .claude/skills as links rather than copying it: the source entries are
  # relative symlinks, and a plain copy would either dereference them (duplicating
  # 9.5 MB a second time) or carry links that resolve outside the worktree.
  $sourceLinkDir = Join-Path $sourceRoot $claudeSkillsRelative
  if (-not (Test-Path $sourceLinkDir)) { return }

  $targetLinkDir = Join-Path $targetRoot $claudeSkillsRelative
  New-Item -ItemType Directory -Path $targetLinkDir -Force | Out-Null

  foreach ($entry in Get-ChildItem -LiteralPath $sourceLinkDir -Force) {
    $targetEntry = Join-Path $targetLinkDir $entry.Name
    if (Test-Path -LiteralPath $targetEntry) { continue }

    $payload = Join-Path (Join-Path $targetRoot $agentSkillsRelative) $entry.Name
    if (-not (Test-Path -LiteralPath $payload)) { continue }

    try {
      New-Item -ItemType SymbolicLink -Path $targetEntry -Target (Join-Path '..' (Join-Path '..' (Join-Path $agentSkillsRelative $entry.Name))) -Force | Out-Null
    }
    catch {
      # Windows without developer mode cannot create symlinks; a real copy still works.
      Copy-Item -LiteralPath $payload -Destination $targetEntry -Recurse -Force
    }
  }
}

$mainRoot = $null
try {
  $mainRoot = Get-MainWorktreeRoot -worktreeRoot $root
}
catch {
  Write-Warning "[setup] could not resolve the main worktree root: $($_.Exception.Message)"
}

if ($null -eq $mainRoot) {
  Write-SetupStep 'skills: skipped (main worktree root unknown)'
}
elseif (Test-SkillsPayloadPresent -repoRoot $root) {
  Write-SetupStep 'skills present - skipping'
  Copy-SkillLinkLayer -sourceRoot $mainRoot -targetRoot $root
}
else {
  $mainSkills = Join-Path $mainRoot $agentSkillsRelative
  if (-not (Test-SkillsPayloadPresent -repoRoot $mainRoot)) {
    # Only ever install in the main worktree, so every future worktree copies from it.
    Write-SetupStep 'skills: npx skills experimental_install (main worktree)'
    try {
      Invoke-NativeCommandWithTimeout -Command 'npx' -Arguments @('skills', 'experimental_install') -WorkingDirectory $mainRoot -TimeoutSeconds 900 -Name 'worktree-skills-install' | Out-Null
    }
    catch {
      Write-Warning "[setup] skills install failed: $($_.Exception.Message)"
    }
  }

  if (Test-SkillsPayloadPresent -repoRoot $mainRoot) {
    Write-SetupStep "skills: mirroring $agentSkillsRelative from the main worktree"
    try {
      $targetSkills = Join-Path $root $agentSkillsRelative
      New-Item -ItemType Directory -Path $targetSkills -Force | Out-Null
      foreach ($skill in Get-ChildItem -LiteralPath $mainSkills -Force) {
        Copy-Item -LiteralPath $skill.FullName -Destination (Join-Path $targetSkills $skill.Name) -Recurse -Force
      }
      Copy-SkillLinkLayer -sourceRoot $mainRoot -targetRoot $root
    }
    catch {
      Write-Warning "[setup] skills mirror failed: $($_.Exception.Message)"
    }
  }
  else {
    Write-SetupStep 'skills: unavailable in the main worktree - skipping'
  }
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
