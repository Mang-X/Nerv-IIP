# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads backend test projects and solution filters
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

[CmdletBinding()]
param(
    [string] $ManifestPath = (Join-Path $PSScriptRoot 'backend-test-shards.json')
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')

function Get-RepoRelativePath {
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Path
    )

    return ([System.IO.Path]::GetRelativePath($RepositoryRoot, $Path) -replace '\\', '/')
}

function Add-ValidationError {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [System.Collections.Generic.List[string]] $Errors,
        [Parameter(Mandatory)] [string] $Message
    )

    $Errors.Add($Message)
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resolvedManifestPath = (Resolve-Path $ManifestPath).Path
$manifest = Get-Content -LiteralPath $resolvedManifestPath -Raw | ConvertFrom-Json
$errors = [System.Collections.Generic.List[string]]::new()

if ($manifest.schemaVersion -ne 1) {
    Add-ValidationError -Errors $errors -Message 'backend test shard manifest schemaVersion must be 1.'
}

$fastShards = @($manifest.fastShards)
$heavyLanes = @($manifest.heavyLanes)
if ($fastShards.Count -ne 4) {
    Add-ValidationError -Errors $errors -Message 'backend test shard manifest must define exactly four fast shards for phase 1.'
}

$classificationEntries = @()
foreach ($shard in $fastShards) {
    if ([string]::IsNullOrWhiteSpace($shard.id)) {
        Add-ValidationError -Errors $errors -Message 'Fast shard is missing id.'
        continue
    }

    if ([string]::IsNullOrWhiteSpace($shard.solutionFilter)) {
        Add-ValidationError -Errors $errors -Message "Fast shard '$($shard.id)' is missing solutionFilter."
    }

    foreach ($project in @($shard.projects)) {
        $classificationEntries += [pscustomobject]@{ Lane = $shard.id; Project = [string] $project; Fast = $true }
    }
}
foreach ($lane in $heavyLanes) {
    if ([string]::IsNullOrWhiteSpace($lane.id)) {
        Add-ValidationError -Errors $errors -Message 'Heavy lane is missing id.'
        continue
    }

    foreach ($project in @($lane.projects)) {
        $classificationEntries += [pscustomobject]@{ Lane = $lane.id; Project = [string] $project; Fast = $false }
    }
}

$allLaneIds = @($fastShards.id) + @($heavyLanes.id)
foreach ($duplicateLaneId in $allLaneIds | Group-Object | Where-Object Count -gt 1) {
    Add-ValidationError -Errors $errors -Message "Duplicate shard or lane id: $($duplicateLaneId.Name)."
}

$projectOwners = @{}
foreach ($entry in $classificationEntries) {
    $project = $entry.Project -replace '\\', '/'
    if ([string]::IsNullOrWhiteSpace($project)) {
        Add-ValidationError -Errors $errors -Message "Shard or lane '$($entry.Lane)' contains an empty project path."
        continue
    }

    if ($projectOwners.ContainsKey($project)) {
        Add-ValidationError -Errors $errors -Message "Backend test project is classified more than once: $project ($($projectOwners[$project]), $($entry.Lane))."
        continue
    }

    $projectOwners[$project] = $entry.Lane
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $project) -PathType Leaf)) {
        Add-ValidationError -Errors $errors -Message "Classified backend test project does not exist: $project."
    }
}

$backendRoot = Join-Path $repositoryRoot 'backend'
$discoveredProjects = @(
    Get-ChildItem -LiteralPath $backendRoot -Recurse -File -Filter '*.Tests.csproj' |
        Where-Object { $_.FullName -notmatch '[/\\](bin|obj)[/\\]' } |
        ForEach-Object { Get-RepoRelativePath -RepositoryRoot $repositoryRoot -Path $_.FullName } |
        Sort-Object -Unique
)

$unclassifiedProjects = @($discoveredProjects | Where-Object { -not $projectOwners.ContainsKey($_) })
if ($unclassifiedProjects.Count -gt 0) {
    Add-ValidationError -Errors $errors -Message "Unclassified backend test projects: $($unclassifiedProjects -join ', ')."
}

$unknownClassifications = @($projectOwners.Keys | Where-Object { $discoveredProjects -notcontains $_ })
if ($unknownClassifications.Count -gt 0) {
    Add-ValidationError -Errors $errors -Message "Classified projects are not discovered backend test projects: $($unknownClassifications -join ', ')."
}

$solutionPath = Join-Path $repositoryRoot ([string] $manifest.solution)
if (-not (Test-Path -LiteralPath $solutionPath -PathType Leaf)) {
    Add-ValidationError -Errors $errors -Message "Configured backend solution does not exist: $($manifest.solution)."
}
else {
    $solutionProjects = @(
        Get-Content -LiteralPath $solutionPath |
            ForEach-Object {
                if ($_ -match '"(?<path>[^" ]*\.Tests\.csproj)"') {
                    'backend/' + ($Matches.path -replace '\\', '/')
                }
            } |
            Sort-Object -Unique
    )
    $projectsMissingFromSolution = @($discoveredProjects | Where-Object { $solutionProjects -notcontains $_ })
    if ($projectsMissingFromSolution.Count -gt 0) {
        Add-ValidationError -Errors $errors -Message "Backend test projects must also be in backend/Nerv.IIP.sln: $($projectsMissingFromSolution -join ', ')."
    }
}

foreach ($shard in $fastShards) {
    if ([string]::IsNullOrWhiteSpace($shard.solutionFilter)) {
        continue
    }

    $filterPath = Join-Path $repositoryRoot ([string] $shard.solutionFilter)
    if (-not (Test-Path -LiteralPath $filterPath -PathType Leaf)) {
        Add-ValidationError -Errors $errors -Message "Fast shard '$($shard.id)' solution filter does not exist: $($shard.solutionFilter)."
        continue
    }

    try {
        $filter = Get-Content -LiteralPath $filterPath -Raw | ConvertFrom-Json
        $filterSolutionPath = Join-Path (Split-Path -Parent $filterPath) $filter.solution.path
        $filterSolutionDirectory = Split-Path -Parent $filterSolutionPath
        $filterProjects = @(
            @($filter.solution.projects) |
                ForEach-Object {
                    Get-RepoRelativePath -RepositoryRoot $repositoryRoot -Path (Join-Path $filterSolutionDirectory $_)
                } |
                Sort-Object -Unique
        )
        $manifestProjects = @($shard.projects | ForEach-Object { $_ -replace '\\', '/' } | Sort-Object -Unique)
        $missingFromFilter = @($manifestProjects | Where-Object { $filterProjects -notcontains $_ })
        $unexpectedInFilter = @($filterProjects | Where-Object { $manifestProjects -notcontains $_ })
        if ($missingFromFilter.Count -gt 0 -or $unexpectedInFilter.Count -gt 0) {
            Add-ValidationError -Errors $errors -Message "Fast shard '$($shard.id)' solution filter must match manifest projects exactly. Missing: $($missingFromFilter -join ', '); unexpected: $($unexpectedInFilter -join ', ')."
        }
    }
    catch {
        Add-ValidationError -Errors $errors -Message "Fast shard '$($shard.id)' solution filter is invalid JSON: $($_.Exception.Message)"
    }
}

if ($errors.Count -gt 0) {
    throw ("Backend test shard governance failed:`n  " + ($errors -join "`n  "))
}

Write-Output "Backend test shard governance passed: $($discoveredProjects.Count) projects classified exactly once across $($fastShards.Count) fast shards and $($heavyLanes.Count) heavy lanes."
