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
    [string] $ManifestPath = (Join-Path $PSScriptRoot 'backend-test-shards.json'),

    [string] $WorkflowPath = (Join-Path $PSScriptRoot '../.github/workflows/ci.yml')
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

function ConvertFrom-CiWorkflowYaml {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $WorkingDirectory
    )

    $rubyProgram = "require 'yaml'; require 'json'; puts JSON.generate(YAML.safe_load(File.read(ARGV.fetch(0))))"
    $result = Invoke-NativeCommandOutput -Command 'ruby' -Arguments @(
        '-ryaml',
        '-rjson',
        '-e', $rubyProgram,
        $Path
    ) -WorkingDirectory $WorkingDirectory -Name 'parse-ci-workflow'

    return ($result.Stdout | ConvertFrom-Json -ErrorAction Stop)
}

function Get-WorkflowStepValues {
    param(
        [AllowNull()] [object[]] $Steps,
        [Parameter(Mandatory)] [string] $PropertyName
    )

    return @(
        foreach ($step in @($Steps)) {
            $property = $step.PSObject.Properties[$PropertyName]
            if ($null -ne $property) {
                [string] $property.Value
            }
        }
    )
}

function Get-OptionalObjectArrayProperty {
    param(
        [Parameter(Mandatory)] [object] $Object,
        [Parameter(Mandatory)] [string] $PropertyName
    )

    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property) {
        return @()
    }

    return @($property.Value)
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

    if ([string]::IsNullOrWhiteSpace([string] $shard.excludedTestLane)) {
        Add-ValidationError -Errors $errors -Message "Fast shard '$($shard.id)' must declare the heavy lane that owns its excluded real tests."
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

    if ([string]::IsNullOrWhiteSpace([string] $lane.ownerScript)) {
        Add-ValidationError -Errors $errors -Message "Heavy lane '$($lane.id)' must declare an executable ownerScript."
    }
    elseif (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot ([string] $lane.ownerScript)) -PathType Leaf)) {
        Add-ValidationError -Errors $errors -Message "Heavy lane '$($lane.id)' ownerScript does not exist: $($lane.ownerScript)."
    }
}

$allLaneIds = @($fastShards.id) + @($heavyLanes.id)
foreach ($duplicateLaneId in $allLaneIds | Group-Object | Where-Object Count -gt 1) {
    Add-ValidationError -Errors $errors -Message "Duplicate shard or lane id: $($duplicateLaneId.Name)."
}

$excludedClassOwners = @{}
foreach ($shard in $fastShards) {
    $excludedLane = [string] $shard.excludedTestLane
    if ($allLaneIds -notcontains $excludedLane -or $fastShards.id -contains $excludedLane) {
        Add-ValidationError -Errors $errors -Message "Fast shard '$($shard.id)' must assign excluded tests to a declared heavy lane, not '$excludedLane'."
    }

    foreach ($testName in @(
            (Get-OptionalObjectArrayProperty -Object $shard -PropertyName 'excludedTestClasses') +
                (Get-OptionalObjectArrayProperty -Object $shard -PropertyName 'excludedTests') |
                Where-Object { $null -ne $_ -and -not [string]::IsNullOrWhiteSpace([string] $_) } |
                ForEach-Object { [string] $_ }
        )) {
        if ($testName -notmatch '^[A-Za-z_][A-Za-z0-9_.]+$') {
            Add-ValidationError -Errors $errors -Message "Fast shard '$($shard.id)' has an invalid excluded test selector: $testName."
            continue
        }
        if ($excludedClassOwners.ContainsKey($testName)) {
            Add-ValidationError -Errors $errors -Message "Excluded real test selector is assigned more than once: $testName ($($excludedClassOwners[$testName]), $($shard.id))."
            continue
        }
        $excludedClassOwners[$testName] = $shard.id
    }
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

$resolvedWorkflowPath = Resolve-Path $WorkflowPath -ErrorAction SilentlyContinue
if ($null -eq $resolvedWorkflowPath) {
    Add-ValidationError -Errors $errors -Message "Configured CI workflow does not exist: $WorkflowPath."
}
else {
    try {
        $workflow = ConvertFrom-CiWorkflowYaml -Path $resolvedWorkflowPath.Path -WorkingDirectory $repositoryRoot
        $jobs = $workflow.jobs
        if ($null -eq $jobs) {
            Add-ValidationError -Errors $errors -Message 'CI workflow must contain a jobs mapping.'
        }
        else {
            $fastJobIds = @($fastShards | ForEach-Object { "backend-tests-$($_.id)" })
            foreach ($shard in $fastShards) {
                $jobId = "backend-tests-$($shard.id)"
                $job = $jobs.PSObject.Properties[$jobId].Value
                if ($null -eq $job) {
                    Add-ValidationError -Errors $errors -Message "CI workflow is missing fast shard job '$jobId'."
                    continue
                }

                $runText = (Get-WorkflowStepValues -Steps @($job.steps) -PropertyName 'run') -join "`n"
                if ($runText -notmatch [regex]::Escape("scripts/run-backend-test-shard.ps1 -ShardId $($shard.id)")) {
                    Add-ValidationError -Errors $errors -Message "Fast shard job '$jobId' must run the governed shard runner for '$($shard.id)'."
                }

                $resultsDirectory = "TestResults/$jobId"
                if ($runText -notmatch [regex]::Escape("-ResultsDirectory $resultsDirectory")) {
                    Add-ValidationError -Errors $errors -Message "Fast shard job '$jobId' must write its declared results directory '$resultsDirectory'."
                }
                if ($runText -notmatch [regex]::Escape("-TrxFilePrefix $jobId")) {
                    Add-ValidationError -Errors $errors -Message "Fast shard job '$jobId' must use its unique TRX file prefix '$jobId'."
                }

                $uploads = @($job.steps | Where-Object {
                        $uses = $_.PSObject.Properties['uses']
                        $null -ne $uses -and [string] $uses.Value -eq 'actions/upload-artifact@v4'
                    })
                if ($uploads.Count -ne 1 -or [string] $uploads[0].if -ne 'always()') {
                    Add-ValidationError -Errors $errors -Message "Fast shard job '$jobId' must always upload exactly one diagnostic artifact."
                }
                elseif ([string] $uploads[0].with.path -ne $resultsDirectory) {
                    Add-ValidationError -Errors $errors -Message "Fast shard job '$jobId' diagnostic artifact must upload '$resultsDirectory'."
                }
            }

            $aggregate = $jobs.PSObject.Properties['backend-tests'].Value
            if ($null -eq $aggregate) {
                Add-ValidationError -Errors $errors -Message "CI workflow is missing the stable 'backend-tests' aggregate job."
            }
            else {
                $expectedNeeds = @('backend-test-shard-governance') + $fastJobIds
                $actualNeeds = @($aggregate.needs | ForEach-Object { [string] $_ })
                if ((@($actualNeeds | Sort-Object) -join '|') -ne (@($expectedNeeds | Sort-Object) -join '|')) {
                    Add-ValidationError -Errors $errors -Message "Backend Tests aggregate must need exactly the governance and four fast shard jobs."
                }
                if ([string] $aggregate.name -ne 'Backend Tests' -or [string] $aggregate.if -ne 'always()') {
                    Add-ValidationError -Errors $errors -Message "Backend Tests aggregate must retain name 'Backend Tests' and if: always()."
                }

                $aggregateRun = (Get-WorkflowStepValues -Steps @($aggregate.steps) -PropertyName 'run') -join "`n"
                foreach ($requiredJob in $expectedNeeds) {
                    if ($aggregateRun -notmatch [regex]::Escape("needs.$requiredJob.result")) {
                        Add-ValidationError -Errors $errors -Message "Backend Tests aggregate must propagate failure from '$requiredJob'."
                    }
                }
            }
        }
    }
    catch {
        Add-ValidationError -Errors $errors -Message "CI workflow must be valid structured YAML: $($_.Exception.Message)"
    }
}

if ($errors.Count -gt 0) {
    throw ("Backend test shard governance failed:`n  " + ($errors -join "`n  "))
}

Write-Output "Backend test shard governance passed: $($discoveredProjects.Count) projects classified exactly once across $($fastShards.Count) fast shards and $($heavyLanes.Count) heavy lanes; $($excludedClassOwners.Count) real test selectors are explicitly owned outside fast shards."
