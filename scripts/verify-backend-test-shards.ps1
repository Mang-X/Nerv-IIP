# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads every backend project file, the backend solution, and the shard solution filters
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7
#     - Ruby 3.4 with yaml/json standard libraries

[CmdletBinding()]
param(
    [string] $ManifestPath = (Join-Path $PSScriptRoot 'backend-test-shards.json'),

    [string] $WorkflowPath = (Join-Path $PSScriptRoot '../.github/workflows/ci.yml'),

    [string] $PolicyPath = (Join-Path $PSScriptRoot 'test-evidence-policy.json')
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'lib/BackendTestShardSelectors.ps1')

function Get-RepoRelativePath {
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Path
    )

    return ([System.IO.Path]::GetRelativePath($RepositoryRoot, $Path) -replace '\\', '/')
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

function Get-WorkflowStepsById {
    param(
        [AllowNull()] [object[]] $Steps,
        [Parameter(Mandatory)] [string] $StepId
    )

    return @(
        foreach ($step in @($Steps)) {
            $property = $step.PSObject.Properties['id']
            if ($null -ne $property -and [string] $property.Value -eq $StepId) {
                $step
            }
        }
    )
}

function Get-WorkflowStringValue {
    param(
        [Parameter(Mandatory)] [object] $Object,
        [Parameter(Mandatory)] [string] $PropertyName
    )

    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property) {
        return ''
    }

    return [string] $property.Value
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resolvedManifestPath = (Resolve-Path $ManifestPath).Path
$manifest = Get-Content -LiteralPath $resolvedManifestPath -Raw | ConvertFrom-Json
$errors = [System.Collections.Generic.List[string]]::new()

if ($manifest.schemaVersion -ne 1) {
    $errors.Add('backend test shard manifest schemaVersion must be 1.')
}

$fastShards = @($manifest.fastShards)
$heavyLanes = @($manifest.heavyLanes)
if ($fastShards.Count -ne 4) {
    $errors.Add('backend test shard manifest must define exactly four fast shards for phase 1.')
}

$classificationEntries = @()
foreach ($shard in $fastShards) {
    if ([string]::IsNullOrWhiteSpace($shard.id)) {
        $errors.Add('Fast shard is missing id.')
        continue
    }

    if ([string]::IsNullOrWhiteSpace($shard.solutionFilter)) {
        $errors.Add("Fast shard '$($shard.id)' is missing solutionFilter.")
    }

    foreach ($project in @($shard.projects)) {
        $classificationEntries += [pscustomobject]@{ Lane = $shard.id; Project = [string] $project; Fast = $true }
    }

    if ($null -eq $shard.PSObject.Properties['excludedTestLanes']) {
        $errors.Add("Fast shard '$($shard.id)' must declare excludedTestLanes, even when empty.")
    }

    if ([string] $shard.evidenceLane -notmatch '^backend-shard-[1-9][0-9]*$') {
        $errors.Add("Fast shard '$($shard.id)' must declare a schema-v1 backend shard evidence lane, not '$($shard.evidenceLane)'.")
    }

    if ([string] $shard.jobName -notmatch '^Backend Tests - \S.*$') {
        $errors.Add("Fast shard '$($shard.id)' must declare the CI job name that owns its evidence lane.")
    }
}

foreach ($duplicateEvidenceLane in @($fastShards.evidenceLane) | Group-Object | Where-Object Count -gt 1) {
    $errors.Add("Duplicate fast shard evidence lane: $($duplicateEvidenceLane.Name).")
}
foreach ($duplicateJobName in @($fastShards.jobName) | Group-Object | Where-Object Count -gt 1) {
    $errors.Add("Duplicate fast shard job name: $($duplicateJobName.Name).")
}
foreach ($lane in $heavyLanes) {
    if ([string]::IsNullOrWhiteSpace($lane.id)) {
        $errors.Add('Heavy lane is missing id.')
        continue
    }

    foreach ($project in @($lane.projects)) {
        $classificationEntries += [pscustomobject]@{ Lane = $lane.id; Project = [string] $project; Fast = $false }
    }

    if ([string]::IsNullOrWhiteSpace([string] $lane.ownerScript)) {
        $errors.Add("Heavy lane '$($lane.id)' must declare an executable ownerScript.")
    }
    elseif (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot ([string] $lane.ownerScript)) -PathType Leaf)) {
        $errors.Add("Heavy lane '$($lane.id)' ownerScript does not exist: $($lane.ownerScript).")
    }
}

$allLaneIds = @($fastShards.id) + @($heavyLanes.id)
foreach ($duplicateLaneId in $allLaneIds | Group-Object | Where-Object Count -gt 1) {
    $errors.Add("Duplicate shard or lane id: $($duplicateLaneId.Name).")
}

$excludedClassOwners = @{}
foreach ($shard in $fastShards) {
    foreach ($excludedLane in @($shard.excludedTestLanes | ForEach-Object { [string] $_ })) {
        if (@($heavyLanes.id) -notcontains $excludedLane) {
            $errors.Add("Fast shard '$($shard.id)' must assign excluded tests to a declared heavy lane, not '$excludedLane'.")
        }
    }

    foreach ($testName in @(Get-BackendTestShardExcludedSelectors -Shard $shard)) {
        if ($testName -notmatch '^[A-Za-z_][A-Za-z0-9_.]+$') {
            $errors.Add("Fast shard '$($shard.id)' has an invalid excluded test selector: $testName.")
            continue
        }
        if ($excludedClassOwners.ContainsKey($testName)) {
            $errors.Add("Excluded real test selector is assigned more than once: $testName ($($excludedClassOwners[$testName]), $($shard.id)).")
            continue
        }
        $excludedClassOwners[$testName] = $shard.id
    }
}

if (-not (Test-Path -LiteralPath $PolicyPath -PathType Leaf)) {
    $errors.Add("MAN-661 test evidence policy does not exist: $PolicyPath.")
}
else {
    $policy = Get-Content -LiteralPath (Resolve-Path $PolicyPath).Path -Raw | ConvertFrom-Json
    $policySourcePaths = @{}
    foreach ($source in @($policy.sources)) {
        $policySourcePaths[[string] $source.id] = [string] $source.sourcePath
    }

    $realDependencyLaneByIdentity = @{}
    $sourceIdByIdentity = @{}
    foreach ($rule in @($policy.rules)) {
        if ([string] $rule.classification -cne 'environment-gated') { continue }
        $requiredLane = [string] $rule.requiredLane
        if ([string]::IsNullOrWhiteSpace($requiredLane)) { continue }
        $laneMatches = @($policy.lanes | Where-Object { $requiredLane -cmatch [string] $_.namePattern })
        if ($laneMatches.Count -ne 1 -or -not [bool] $laneMatches[0].realDependency) { continue }
        foreach ($identity in @($rule.testIdentities)) {
            $realDependencyLaneByIdentity[[string] $identity] = $requiredLane
            $sourceIdByIdentity[[string] $identity] = [string] $rule.sourceId
        }
    }

    $heavyLaneByPolicyLane = @{}
    foreach ($lane in $heavyLanes) {
        $policyLane = [string] $lane.policyLane
        if ([string]::IsNullOrWhiteSpace($policyLane)) {
            $errors.Add("Heavy lane '$($lane.id)' must declare the MAN-661 policy lane it owns.")
            continue
        }
        $heavyLaneByPolicyLane[$policyLane] = [string] $lane.id
    }

    foreach ($shard in $fastShards) {
        $ownerLanes = [System.Collections.Generic.HashSet[string]]::new()
        foreach ($selector in @(Get-BackendTestShardExcludedSelectors -Shard $shard)) {
            # A fast shard may only filter away work that MAN-661 has registered as an
            # environment-gated real-dependency skip. Without this the exclusion list is a private
            # escape hatch for dropping anything from the default gate.
            $covering = @(
                $realDependencyLaneByIdentity.Keys | Where-Object {
                    $_ -ceq $selector -or $_.StartsWith("$selector.", [StringComparison]::Ordinal)
                }
            )
            if ($covering.Count -eq 0) {
                $errors.Add("Fast shard exclusion '$selector' is not registered in the MAN-661 evidence policy as an environment-gated real-dependency skip.")
                continue
            }
            foreach ($identity in $covering) {
                $policyLane = [string] $realDependencyLaneByIdentity[$identity]
                if (-not $heavyLaneByPolicyLane.ContainsKey($policyLane)) {
                    $errors.Add("MAN-661 policy lane '$policyLane' required by '$selector' has no owning heavy lane in the shard manifest.")
                    continue
                }
                [void] $ownerLanes.Add([string] $heavyLaneByPolicyLane[$policyLane])
            }
        }

        # The declared owner lanes must equal the lanes MAN-661 actually requires, so a shard cannot
        # attribute a full-chain or performance exclusion to the real-postgres owner script.
        $declaredLanes = @($shard.excludedTestLanes | ForEach-Object { [string] $_ } | Sort-Object -Unique)
        $derivedLanes = @($ownerLanes | Sort-Object)
        if ((@($declaredLanes) -join '|') -cne (@($derivedLanes) -join '|')) {
            $errors.Add("Fast shard '$($shard.id)' must declare excludedTestLanes [$(@($derivedLanes) -join ', ')] to match the MAN-661 requiredLane of its exclusions; it declares [$(@($declaredLanes) -join ', ')].")
        }

        # A method selector stays a substring filter, so a sibling method whose name merely extends
        # it would be silently excluded too. Class selectors are anchored with a trailing dot in the
        # runner and cannot collide this way.
        foreach ($methodSelector in @(Get-BackendTestShardExcludedSelectors -Shard $shard -Kind 'method')) {
            $sourceIds = @(
                $sourceIdByIdentity.Keys |
                    Where-Object { $_ -ceq $methodSelector -or $_.StartsWith("$methodSelector.", [StringComparison]::Ordinal) } |
                    ForEach-Object { [string] $sourceIdByIdentity[$_] } |
                    Sort-Object -Unique
            )
            if ($sourceIds.Count -eq 0) {
                $errors.Add("Method selector '$methodSelector' has no MAN-661 source registration to scan for prefix collisions.")
                continue
            }
            $methodName = $methodSelector.Substring($methodSelector.LastIndexOf('.') + 1)
            foreach ($sourceId in $sourceIds) {
                $sourcePath = Join-Path $repositoryRoot ([string] $policySourcePaths[$sourceId])
                if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
                    $errors.Add("MAN-661 source '$sourceId' for '$methodSelector' does not exist: $($policySourcePaths[$sourceId]).")
                    continue
                }
                $sourceText = Get-Content -LiteralPath $sourcePath -Raw
                $collisions = @([regex]::Matches($sourceText, "\b$([regex]::Escape($methodName))[A-Za-z0-9_]+\s*[(<]") | ForEach-Object { $_.Value })
                if ($collisions.Count -gt 0) {
                    $errors.Add("Method selector '$methodSelector' would also substring-exclude a sibling member in $($policySourcePaths[$sourceId]): $(@($collisions) -join ', ').")
                }
            }
        }
    }
}

$projectOwners = @{}
foreach ($entry in $classificationEntries) {
    $project = $entry.Project -replace '\\', '/'
    if ([string]::IsNullOrWhiteSpace($project)) {
        $errors.Add("Shard or lane '$($entry.Lane)' contains an empty project path.")
        continue
    }

    if ($projectOwners.ContainsKey($project)) {
        $errors.Add("Backend test project is classified more than once: $project ($($projectOwners[$project]), $($entry.Lane)).")
        continue
    }

    $projectOwners[$project] = $entry.Lane
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $project) -PathType Leaf)) {
        $errors.Add("Classified backend test project does not exist: $project.")
    }
}

$backendRoot = Join-Path $repositoryRoot 'backend'
$discoveredProjects = @(
    Get-ChildItem -LiteralPath $backendRoot -Recurse -File -Filter '*.Tests.csproj' |
        Where-Object { $_.FullName -notmatch '[/\\](bin|obj)[/\\]' } |
        ForEach-Object { Get-RepoRelativePath -RepositoryRoot $repositoryRoot -Path $_.FullName } |
        Sort-Object -Unique
)

$discoveredBackendProjects = @(
    Get-ChildItem -LiteralPath $backendRoot -Recurse -File -Filter '*.csproj' |
        Where-Object { $_.FullName -notmatch '[/\\](bin|obj)[/\\]' } |
        ForEach-Object { Get-RepoRelativePath -RepositoryRoot $repositoryRoot -Path $_.FullName } |
        Sort-Object -Unique
)

$unclassifiedProjects = @($discoveredProjects | Where-Object { -not $projectOwners.ContainsKey($_) })
if ($unclassifiedProjects.Count -gt 0) {
    $errors.Add("Unclassified backend test projects: $($unclassifiedProjects -join ', ').")
}

$unknownClassifications = @($projectOwners.Keys | Where-Object { $discoveredProjects -notcontains $_ })
if ($unknownClassifications.Count -gt 0) {
    $errors.Add("Classified projects are not discovered backend test projects: $($unknownClassifications -join ', ').")
}

$solutionPath = Join-Path $repositoryRoot ([string] $manifest.solution)
if (-not (Test-Path -LiteralPath $solutionPath -PathType Leaf)) {
    $errors.Add("Configured backend solution does not exist: $($manifest.solution).")
}
else {
    $solutionProjects = @(
        Get-Content -LiteralPath $solutionPath |
            ForEach-Object {
                if ($_ -match '"(?<path>[^" ]*\.csproj)"') {
                    'backend/' + ($Matches.path -replace '\\', '/')
                }
            } |
            Sort-Object -Unique
    )
    $projectsMissingFromSolution = @($discoveredProjects | Where-Object { $solutionProjects -notcontains $_ })
    if ($projectsMissingFromSolution.Count -gt 0) {
        $errors.Add("Backend test projects must also be in backend/Nerv.IIP.sln: $($projectsMissingFromSolution -join ', ').")
    }

    # Solution membership is a build-configuration invariant, not bookkeeping, and it covers *every*
    # backend project rather than only the test ones. Each shard builds a `.slnf` over
    # backend/Nerv.IIP.sln with `--configuration Release`, and MSBuild resolves a project's
    # configuration through the solution's configuration map. A project that is only reachable as a
    # transitive ProjectReference has no entry in that map, so it falls back to its own default and
    # is emitted into bin/Debug — the shard then runs Release test assemblies linked against a Debug
    # dependency, on every shard at once, with nothing in the build output that fails. That is
    # exactly what backend/common/Contracts/Nerv.IIP.Contracts.Mes did until MAN-669 PR-B (visible
    # as one `-> …/bin/Debug/net10.0/…` line in each shard log of run 31136085020).
    #
    # There is deliberately NO allowlist and no owner-issue escape hatch here, unlike
    # backend/test-determinism-baseline.json. A registered exception would be a project that is
    # knowingly built under the wrong configuration, which is not a debt anyone can carry — the
    # coverage is currently 163/163 with no gap, and keeping it that way is cheaper than governing
    # exceptions. If a future change genuinely needs a backend project outside the solution, the
    # exemption path is to edit this script (with its own contract test) and go through script
    # governance; see docs/architecture/script-automation-governance.md.
    $backendProjectsMissingFromSolution = @(
        $discoveredBackendProjects |
            Where-Object { $solutionProjects -notcontains $_ -and $projectsMissingFromSolution -notcontains $_ }
    )
    if ($backendProjectsMissingFromSolution.Count -gt 0) {
        $errors.Add("Backend projects must be registered in backend/Nerv.IIP.sln, otherwise a Release shard build resolves them through their own default configuration and emits them into bin/Debug: $($backendProjectsMissingFromSolution -join ', ').")
    }
}

foreach ($shard in $fastShards) {
    if ([string]::IsNullOrWhiteSpace($shard.solutionFilter)) {
        continue
    }

    # A shard exists to restore and build only its own dependency closure. Pointing it at
    # backend/Nerv.IIP.sln would keep the "shard" label while every job rebuilt the whole solution
    # again — measured at 195.7-233.2s and 3.03 GB of output, against 62.7-180.9s and 0.41-1.78 GB
    # for the four filters (MAN-669 PR-B, runs 31139435243 / 31139971326 / 31140517256 /
    # 31141123938; narrative in docs/architecture/backend-ci-build-strategy.md). Rejected
    # explicitly, because the JSON parse below would otherwise report it as a malformed solution
    # filter and hide what actually happened.
    #
    # Note this is the narrow case only. A `.slnf` that *lists* the whole solution is already
    # rejected further down by "solution filter must match manifest projects exactly", which
    # predates MAN-669 PR-B. Compared case-insensitively after separator and relative-path
    # normalization, because `./backend/Nerv.IIP.sln`, `backend\Nerv.IIP.sln` and
    # `backend/nerv.iip.sln` all name the same file and must all land in this branch.
    $normalizedFilter = (([string] $shard.solutionFilter) -replace '\\', '/') -replace '^\./', ''
    $normalizedSolution = (([string] $manifest.solution) -replace '\\', '/') -replace '^\./', ''
    if ($normalizedFilter -eq $normalizedSolution) {
        $errors.Add("Fast shard '$($shard.id)' must build its own solution filter, not the whole backend solution.")
        continue
    }

    $filterPath = Join-Path $repositoryRoot ([string] $shard.solutionFilter)
    if (-not (Test-Path -LiteralPath $filterPath -PathType Leaf)) {
        $errors.Add("Fast shard '$($shard.id)' solution filter does not exist: $($shard.solutionFilter).")
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
            $errors.Add("Fast shard '$($shard.id)' solution filter must match manifest projects exactly. Missing: $($missingFromFilter -join ', '); unexpected: $($unexpectedInFilter -join ', ').")
        }
    }
    catch {
        $errors.Add("Fast shard '$($shard.id)' solution filter is invalid JSON: $($_.Exception.Message)")
    }
}

$resolvedWorkflowPath = Resolve-Path $WorkflowPath -ErrorAction SilentlyContinue
if ($null -eq $resolvedWorkflowPath) {
    $errors.Add("Configured CI workflow does not exist: $WorkflowPath.")
}
else {
    try {
        $workflow = ConvertFrom-CiWorkflowYaml -Path $resolvedWorkflowPath.Path -WorkingDirectory $repositoryRoot
        $jobs = $workflow.jobs
        if ($null -eq $jobs) {
            $errors.Add('CI workflow must contain a jobs mapping.')
        }
        else {
            $fastJobIds = @($fastShards | ForEach-Object { "backend-tests-$($_.id)" })
            foreach ($shard in $fastShards) {
                $jobId = "backend-tests-$($shard.id)"
                $job = $jobs.PSObject.Properties[$jobId].Value
                if ($null -eq $job) {
                    $errors.Add("CI workflow is missing fast shard job '$jobId'.")
                    continue
                }

                $lane = [string] $shard.evidenceLane
                $shardJobName = [string] $shard.jobName
                if ((Get-WorkflowStringValue -Object $job -PropertyName 'name') -ne $shardJobName) {
                    $errors.Add("Fast shard job '$jobId' must be named '$shardJobName' so the evidence lane maps to one allowlisted job.")
                }

                $runText = (Get-WorkflowStepValues -Steps @($job.steps) -PropertyName 'run') -join "`n"
                if ($runText -notmatch [regex]::Escape("scripts/run-backend-test-shard.ps1 -ShardId $($shard.id)")) {
                    $errors.Add("Fast shard job '$jobId' must run the governed shard runner for '$($shard.id)'.")
                }
                if ($runText -match '(?m)(?:^|\s)-TestCommand(?:\s|$)') {
                    $errors.Add("Fast shard job '$jobId' must not supply a command replacement parameter.")
                }

                $rawResultsDirectory = "artifacts/test-evidence-raw/`${{ github.run_id }}/attempt-`${{ github.run_attempt }}/$lane"
                $evidenceDirectory = "artifacts/test-evidence/`${{ github.run_id }}/attempt-`${{ github.run_attempt }}/$lane"
                if ($runText -notmatch [regex]::Escape("-ResultsDirectory $rawResultsDirectory")) {
                    $errors.Add("Fast shard job '$jobId' must write raw TRX only to the job-local evidence input '$rawResultsDirectory'.")
                }
                if ($runText -notmatch [regex]::Escape("-TrxFilePrefix $jobId")) {
                    $errors.Add("Fast shard job '$jobId' must use its unique TRX file prefix '$jobId'.")
                }

                $testSteps = @(Get-WorkflowStepsById -Steps @($job.steps) -StepId 'shard-tests')
                if ($testSteps.Count -ne 1) {
                    $errors.Add("Fast shard job '$jobId' must declare exactly one 'shard-tests' step whose native exit code is authoritative.")
                }
                else {
                    $testStepRun = Get-WorkflowStringValue -Object $testSteps[0] -PropertyName 'run'
                    if ($testStepRun -match '\|') {
                        $errors.Add("Fast shard job '$jobId' test step must not wrap the shard runner in a shell pipeline.")
                    }
                    if ($null -ne $testSteps[0].PSObject.Properties['continue-on-error']) {
                        $errors.Add("Fast shard job '$jobId' test step must not set 'continue-on-error'.")
                    }
                }
                if ($null -ne $job.PSObject.Properties['continue-on-error']) {
                    $errors.Add("Fast shard job '$jobId' must not set 'continue-on-error'.")
                }

                $collectSteps = @(Get-WorkflowStepsById -Steps @($job.steps) -StepId 'collect-shard-evidence')
                if ($collectSteps.Count -ne 1) {
                    $errors.Add("Fast shard job '$jobId' must collect MAN-661 evidence in exactly one 'collect-shard-evidence' step.")
                }
                else {
                    $collectStep = $collectSteps[0]
                    $collectRun = Get-WorkflowStringValue -Object $collectStep -PropertyName 'run'
                    if ((Get-WorkflowStringValue -Object $collectStep -PropertyName 'if') -ne 'always()') {
                        $errors.Add("Fast shard job '$jobId' evidence collection must run with if: always().")
                    }
                    if ($null -ne $collectStep.PSObject.Properties['continue-on-error']) {
                        $errors.Add("Fast shard job '$jobId' evidence collection must not set 'continue-on-error'.")
                    }
                    foreach ($requiredArgument in @(
                            'scripts/collect-test-evidence.ps1',
                            "-Lane $lane",
                            "-SelectedLanes $lane",
                            "-ResultsDirectory $rawResultsDirectory",
                            "-OutputDirectory $evidenceDirectory",
                            "-JobName `"$shardJobName`"",
                            '-CurrentTestOutcome ${{ steps.shard-tests.outcome }}',
                            '-RetentionDays 14'
                        )) {
                        if ($collectRun -notmatch [regex]::Escape($requiredArgument)) {
                            $errors.Add("Fast shard job '$jobId' evidence collection must pass '$requiredArgument'.")
                        }
                    }
                    foreach ($siblingLane in @($fastShards | Where-Object { [string] $_.id -ne [string] $shard.id } | ForEach-Object { [string] $_.evidenceLane })) {
                        if ($collectRun -match [regex]::Escape($siblingLane)) {
                            $errors.Add("Fast shard job '$jobId' must not claim the sibling evidence lane '$siblingLane'.")
                        }
                    }
                }

                $uploads = @($job.steps | Where-Object {
                        $uses = $_.PSObject.Properties['uses']
                        $null -ne $uses -and [string] $uses.Value -eq 'actions/upload-artifact@v4'
                    })
                if ($uploads.Count -ne 1 -or (Get-WorkflowStringValue -Object $uploads[0] -PropertyName 'if') -ne 'always()') {
                    $errors.Add("Fast shard job '$jobId' must always upload exactly one redacted evidence artifact.")
                }
                else {
                    $uploadWith = $uploads[0].with
                    if ((Get-WorkflowStringValue -Object $uploadWith -PropertyName 'path') -ne '${{ steps.collect-shard-evidence.outputs.evidence-path }}') {
                        $errors.Add("Fast shard job '$jobId' must upload only the collector-published redacted evidence path.")
                    }
                    if ((Get-WorkflowStringValue -Object $uploadWith -PropertyName 'name') -ne "test-evidence-$lane-`${{ github.run_id }}-`${{ github.run_attempt }}") {
                        $errors.Add("Fast shard job '$jobId' evidence artifact must use its unique lane-scoped artifact name.")
                    }
                    if ((Get-WorkflowStringValue -Object $uploadWith -PropertyName 'if-no-files-found') -ne 'error' -or
                        (Get-WorkflowStringValue -Object $uploadWith -PropertyName 'retention-days') -ne '14') {
                        $errors.Add("Fast shard job '$jobId' evidence artifact must fail closed on missing files and retain for 14 days.")
                    }
                }
                foreach ($upload in $uploads) {
                    if ((Get-WorkflowStringValue -Object $upload.with -PropertyName 'path') -match 'test-evidence-raw') {
                        $errors.Add("Fast shard job '$jobId' must never upload the job-local raw TRX directory.")
                    }
                }
            }

            $aggregate = $jobs.PSObject.Properties['backend-tests'].Value
            if ($null -eq $aggregate) {
                $errors.Add("CI workflow is missing the stable 'backend-tests' aggregate job.")
            }
            else {
                $expectedNeeds = @('backend-test-shard-governance') + $fastJobIds
                $actualNeeds = @($aggregate.needs | ForEach-Object { [string] $_ })
                if ((@($actualNeeds | Sort-Object) -join '|') -ne (@($expectedNeeds | Sort-Object) -join '|')) {
                    $errors.Add("Backend Tests aggregate must need exactly the governance and four fast shard jobs.")
                }
                if ([string] $aggregate.name -ne 'Backend Tests' -or [string] $aggregate.if -ne 'always()') {
                    $errors.Add("Backend Tests aggregate must retain name 'Backend Tests' and if: always().")
                }
                $aggregateHasContinueOnError = $null -ne $aggregate.PSObject.Properties['continue-on-error'] -or @(
                    @($aggregate.steps) | Where-Object { $null -ne $_.PSObject.Properties['continue-on-error'] }
                ).Count -gt 0
                if ($aggregateHasContinueOnError) {
                    $errors.Add("Backend Tests aggregate must not set 'continue-on-error' on the job or any step.")
                }

                $aggregateRun = (Get-WorkflowStepValues -Steps @($aggregate.steps) -PropertyName 'run') -join "`n"
                $aggregateCommands = @(
                    $aggregateRun -split "`r?`n" |
                        ForEach-Object { $_.Trim() } |
                        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
                )
                $requiredAssertions = @()
                foreach ($requiredJob in $expectedNeeds) {
                    $requiredAssertion = 'test "${{ needs.' + $requiredJob + '.result }}" = "success"'
                    $requiredAssertions += $requiredAssertion
                    if ($aggregateCommands -notcontains $requiredAssertion) {
                        $errors.Add("Backend Tests aggregate must fail when '$requiredJob' is not success.")
                    }
                }
                if ($aggregateCommands.Count -ne $requiredAssertions.Count -or ((@($aggregateCommands | Sort-Object) -join '|') -ne (@($requiredAssertions | Sort-Object) -join '|'))) {
                    $errors.Add('Backend Tests aggregate must contain only standalone success assertions for its exact dependencies.')
                }
            }
        }
    }
    catch {
        $errors.Add("CI workflow must be valid structured YAML: $($_.Exception.Message)")
    }
}

if ($errors.Count -gt 0) {
    throw ("Backend test shard governance failed:`n  " + ($errors -join "`n  "))
}

Write-Output "Backend test shard governance passed: $($discoveredProjects.Count) projects classified exactly once across $($fastShards.Count) fast shards and $($heavyLanes.Count) heavy lanes; $($excludedClassOwners.Count) real test selectors are explicitly owned outside fast shards; $($discoveredBackendProjects.Count) backend projects are solution members and therefore build under the shard's own Release configuration."
