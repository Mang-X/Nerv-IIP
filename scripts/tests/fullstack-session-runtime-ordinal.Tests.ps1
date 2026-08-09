# Script-Governance:
#   Category: check
#   SideEffects:
#     - None
#   Writes:
#     - None
#   Cleanup:
#     - No process or external resource ownership
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
. (Join-Path $repoRoot 'scripts/lib/FullStackSessionRuntime.ps1')

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

$sessionId = 'nerv-abcd-123456'
$softHyphen = [string][char]0x00AD
$foldedContainer = [pscustomobject]@{
    Id = "owned-container-id$softHyphen"
    Config = [pscustomobject]@{
        Labels = [pscustomobject]@{ 'com.nerv-iip.session' = $sessionId }
    }
}
Assert-True (-not (Test-NervDockerResourceOwnership -InspectObject $foldedContainer -SessionId $sessionId -RecordedIds @('owned-container-id'))) 'Docker IDs differing by U+00AD must not satisfy recorded-resource ownership.'

$ordinalIds = @(Merge-NervSessionContainerIds `
    -RecordedIds @('apple') `
    -DiscoveredRecords @([pscustomobject]@{ id = 'Banana' }, [pscustomobject]@{ id = 'apple' }))
Assert-True ([string]::Equals(($ordinalIds -join '|'), 'Banana|apple', [StringComparison]::Ordinal)) 'Container IDs must deduplicate and sort with StringComparer.Ordinal.'

Write-Host 'Full-stack runtime ordinal tests passed.'
