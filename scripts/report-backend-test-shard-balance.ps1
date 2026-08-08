# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads the shard manifest and the shard timing cache
#     - Opportunistically refreshes the timing cache from read-only GitHub Actions metadata
#   Writes:
#     - The timing cache passed through TimingCachePath, only when a refresh succeeds
#   Cleanup:
#     - Refresh removes its own temporary download directory
#   Requires:
#     - PowerShell 7
#     - GitHub CLI only to refresh the cache; absent access degrades to the committed fallback

# Report-only balance of the four backend fast shards.
#
# This script deliberately has no failure mode driven by timing data. Missing, stale or absent
# measurements produce warnings and an estimate, and the exit code stays 0. The hard gate over the
# same manifest is scripts/verify-backend-test-shards.ps1, and it governs *policy* only — project
# classification, exclusion registration, solution membership, workflow wiring. Timing never enters
# it, because a measurement is observed rather than decided and cannot sensibly be "violated".
# Rationale and the failure this split removes: scripts/lib/BackendTestShardTimings.ps1 and
# docs/architecture/test-evidence-governance.md ("Timing data is a cache, not a governed asset").
#
# The only nonzero exit is a structurally unusable manifest, which is a defect in a governed file
# rather than in a measurement.

[CmdletBinding()]
param(
    [string] $ManifestPath = (Join-Path $PSScriptRoot 'backend-test-shards.json'),

    [string] $TimingCachePath = (Join-Path $PSScriptRoot '../artifacts/backend-test-shard-timings.json'),

    # Last committed evidence snapshot. It is a *fallback* only: keyed by assembly like the cache, so
    # a shard rearrangement cannot lose a key, and never required to be fresh or complete.
    [string] $FallbackEvidencePath = (Join-Path $PSScriptRoot 'test-evidence-baseline.json'),

    [string] $Repository = 'Mang-X/Nerv-IIP',

    [ValidateRange(1, 20)] [int] $RunCount = 5,

    [ValidateRange(0, 8760)] [int] $MaxCacheAgeHours = 24,

    # Set when the caller wants the report to read only what is already on disk (contract tests,
    # offline use). Refresh is on by default so there is no human "refresh the timings" step.
    [switch] $NoRefresh
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/BackendTestShardTimings.ps1')

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    Write-Host "Backend test shard manifest does not exist: $ManifestPath"
    exit 1
}

try {
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
}
catch {
    Write-Host "Backend test shard manifest is not valid JSON: $(Protect-ScriptAutomationText $_.Exception.Message)"
    exit 1
}

if (@($manifest.fastShards).Count -eq 0) {
    Write-Host 'Backend test shard manifest declares no fast shards.'
    exit 1
}

if (-not $NoRefresh) {
    $cacheAgeHours = if (Test-Path -LiteralPath $TimingCachePath -PathType Leaf) {
        ([DateTimeOffset]::UtcNow - [DateTimeOffset]((Get-Item -LiteralPath $TimingCachePath).LastWriteTimeUtc)).TotalHours
    }
    else { [double]::PositiveInfinity }

    if ($cacheAgeHours -gt $MaxCacheAgeHours) {
        Update-NervShardTimingCache `
            -Repository $Repository `
            -OutputPath $TimingCachePath `
            -WorkingDirectory $repoRoot `
            -RunCount $RunCount | Out-Null
    }
}

$timings = Get-NervShardTimingLookup -CachePath $TimingCachePath -FallbackEvidencePath $FallbackEvidencePath
$report = Get-NervShardBalanceReport -Manifest $manifest -Timings $timings

foreach ($line in @(Format-NervShardBalanceReport -Report $report)) {
    Write-Host $line
}

exit 0
