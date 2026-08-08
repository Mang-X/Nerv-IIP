# Script-Governance:
#   Category: generate
#   SideEffects:
#     - Reads GitHub Actions run metadata and downloads retained test-evidence artifacts (read-only)
#   Writes:
#     - The exact timing cache passed through OutputPath (default artifacts/backend-test-shard-timings.json)
#     - A temporary artifact download directory under the OS temp directory, removed on completion
#   Cleanup:
#     - Removes its temporary download directory in finally
#   Requires:
#     - PowerShell 7
#     - GitHub CLI with read access, only to refresh; absent access degrades to a warning

# Refreshes the backend fast-shard timing cache from the most recent successful `main` push CI runs.
#
# This is a cache refresher, not a baseline generator. The file it writes lives under the gitignored
# `artifacts/` tree, is never committed, carries no hash and gates nothing; a stale or missing cache
# only makes the shard-balance report estimate more rows. Why timing is a cache and policy is a
# governed asset: scripts/lib/BackendTestShardTimings.ps1 and
# docs/architecture/test-evidence-governance.md ("Timing data is a cache, not a governed asset").
#
# It exits 0 when the refresh cannot happen. A missing GitHub CLI, missing token, offline runner or
# expired artifacts are all normal conditions for a cache, and turning any of them into a nonzero
# exit would rebuild exactly the human "refresh ceremony" #1507 removed.

[CmdletBinding()]
param(
    [string] $Repository = 'Mang-X/Nerv-IIP',

    [ValidateRange(1, 20)] [int] $RunCount = 5,

    [string] $OutputPath = (Join-Path $PSScriptRoot '../artifacts/backend-test-shard-timings.json')
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/BackendTestShardTimings.ps1')

$cache = Update-NervShardTimingCache `
    -Repository $Repository `
    -OutputPath $OutputPath `
    -WorkingDirectory $repoRoot `
    -RunCount $RunCount

if ($null -eq $cache) {
    Write-Host 'Backend test shard timing cache was not refreshed; the previous cache (if any) is unchanged. This is report-only and never fails a gate.'
    exit 0
}

Write-Host "Backend test shard timing cache refreshed: $(@($cache.assemblies).Count) assemblies from $(@($cache.runs).Count) successful main run(s) -> $OutputPath"
exit 0
