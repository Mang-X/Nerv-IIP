# Script-Governance:
#   Category: library, check
#   SideEffects:
#     - Reads a supplied GitHub Actions workflow through the acceptance planning contract
#     - Invokes only a caller-supplied in-process runtime action after all preflight checks pass
#   Writes:
#     - A caller-declared runtime summary through atomic file replacement
#   Cleanup:
#     - Removes owned temporary summary files after each persistence attempt
#   Requires:
#     - PowerShell 7

. (Join-Path $PSScriptRoot 'AcceptanceScenarioMatrix.ps1')

function Test-NervAcceptanceRuntimeRunCommand {
    param([AllowNull()] [object] $Run)

    if ($Run -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$Run)) { return $false }
    foreach ($line in @(([string]$Run) -split "`r?`n")) {
        $command = ([string]$line).Trim()
        if ($command -cmatch '^pwsh(?:\.exe)?(?:\s+-(?:NoLogo|NoProfile|NonInteractive))*\s+(?:-File\s+)?["'']?(?:\./)?scripts/run-acceptance-scenario-matrix\.ps1["'']?(?:\s|$)') {
            return $true
        }
    }
    return $false
}

function Get-NervAcceptanceRuntimeWorkflowBudget {
    param(
        [Parameter(Mandatory)] [string] $WorkflowPath,
        [Parameter(Mandatory)] [string] $JobName,
        [Parameter(Mandatory)] [string] $StepName
    )

    $jobs = Get-NervCiWorkflowBudgets -Path $WorkflowPath
    $jobMatches = @($jobs | Where-Object { [string]::Equals([string]$_.Name, $JobName, [StringComparison]::Ordinal) })
    if ($jobMatches.Count -ne 1) { throw "Workflow '$WorkflowPath' must define exactly one '$JobName' runtime job." }
    $stepMatches = @($jobMatches[0].Steps | Where-Object { [string]::Equals([string]$_.Name, $StepName, [StringComparison]::Ordinal) })
    if ($stepMatches.Count -ne 1 -or $null -eq $stepMatches[0].TimeoutMinutes) {
        throw "Workflow '$WorkflowPath' job '$JobName' must define exactly one timed '$StepName' runtime step."
    }
    if (-not (Test-NervAcceptanceRuntimeRunCommand -Run $stepMatches[0].Run)) {
        throw "Workflow '$WorkflowPath' job '$JobName' timed step '$StepName' must invoke scripts/run-acceptance-scenario-matrix.ps1."
    }
    $timeoutSeconds = ConvertTo-NervAcceptanceCheckedInt64 -Value (([Numerics.BigInteger]$stepMatches[0].TimeoutMinutes) * 60) -Context 'runtime workflow step timeout'
    if ($timeoutSeconds -le 0) { throw "Workflow '$WorkflowPath' runtime step timeout must be positive." }
    return [pscustomobject]@{ jobName = $JobName; stepName = $StepName; stepTimeoutSeconds = $timeoutSeconds }
}

function Assert-NervAcceptanceRuntimeBudgetFits {
    param(
        [Parameter(Mandatory)] [object] $ExecutionBudget,
        [Parameter(Mandatory)] [int64] $StepTimeoutSeconds,
        [Parameter(Mandatory)] [string] $ScenarioId
    )

    Assert-NervAcceptanceExecutionBudget -Budget $ExecutionBudget -ScenarioId $ScenarioId
    if ($StepTimeoutSeconds -le 0) { throw 'Runtime workflow step timeout must be positive.' }
    $required = [Numerics.BigInteger]::Zero
    foreach ($field in @(
        'dependencyReadinessSeconds',
        'executionTimeoutSeconds',
        'diagnosticsSeconds',
        'cleanupSeconds',
        'evidenceWriteSeconds',
        'safetyMarginSeconds'
    )) {
        $required += [Numerics.BigInteger][int64]$ExecutionBudget.PSObject.Properties[$field].Value
    }
    $requiredSeconds = ConvertTo-NervAcceptanceCheckedInt64 -Value $required -Context "scenario '$ScenarioId' runtime budget"
    if ($requiredSeconds -ge $StepTimeoutSeconds) {
        throw "Scenario '$ScenarioId' runtime budget $requiredSeconds seconds must be strictly less than workflow step timeout $StepTimeoutSeconds seconds."
    }
    return $requiredSeconds
}

function Get-NervAcceptanceSalesOrderRuntimeScenario {
    param([Parameter(Mandatory)] [object] $Manifest)

    $matches = @($Manifest.scenarios | Where-Object {
        [string]::Equals([string]$_.id, 'sales-order-demand', [StringComparison]::Ordinal)
    })
    if ($matches.Count -ne 1) { throw "Runtime manifest must contain exactly one 'sales-order-demand' scenario." }
    $scenario = $matches[0]
    if (-not [string]::Equals([string]$scenario.status, 'active', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$scenario.tier, 'core', [StringComparison]::Ordinal)) {
        throw "Runtime scenario 'sales-order-demand' must be active/core."
    }
    if (-not [string]::Equals([string]$scenario.v1Alias, 'sales-order-demand-planning', [StringComparison]::Ordinal)) {
        throw "Runtime scenario 'sales-order-demand' v1Alias drifted from 'sales-order-demand-planning'."
    }
    if ($scenario.entrypoint -isnot [pscustomobject] -or
        -not [string]::Equals([string]$scenario.entrypoint.kind, 'script', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$scenario.entrypoint.path, 'scripts/verify-erp-sales-order-demand-planning.ps1', [StringComparison]::Ordinal)) {
        throw "Runtime scenario 'sales-order-demand' entrypoint drifted from the governed v1 script."
    }
    $projects = @($scenario.testProjects)
    if ($projects.Count -ne 1 -or
        -not [string]::Equals([string]$projects[0].path, 'backend/tests/Nerv.IIP.Business.FullChain.Tests/Nerv.IIP.Business.FullChain.Tests.csproj', [StringComparison]::Ordinal)) {
        throw "Runtime scenario 'sales-order-demand' project drifted from the governed FullChain project."
    }
    $identities = @($projects[0].frozenTestIdentities)
    $expectedIdentity = 'Nerv.IIP.Business.FullChain.Tests.SalesOrderDemandPlanningPostgresRedisAcceptanceTests.External_process_injects_duplicate_and_out_of_order_sales_order_events'
    if ($identities.Count -ne 1 -or -not [string]::Equals([string]$identities[0], $expectedIdentity, [StringComparison]::Ordinal)) {
        throw "Runtime scenario 'sales-order-demand' frozen identity drifted from the governed v1 identity."
    }
    return $scenario
}

function Assert-NervAcceptanceScenarioRuntimePreflight {
    param(
        [Parameter(Mandatory)] [object] $Artifact,
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [string] $Repository,
        [Parameter(Mandatory)] [string] $TestedSha,
        [Parameter(Mandatory)] [string] $RunId,
        [Parameter(Mandatory)] [int] $RunAttempt,
        [Parameter(Mandatory)] [string] $ManifestPath,
        [Parameter(Mandatory)] [string] $ManifestDigest,
        [Parameter(Mandatory)] [string] $Event,
        [Parameter(Mandatory)] [string] $WorkflowPath,
        [Parameter(Mandatory)] [string] $WorkflowJobName,
        [Parameter(Mandatory)] [string] $WorkflowStepName
    )

    if (-not (Test-NervAcceptanceObjectProperty -Object $Artifact -Name 'scenarios') -or $Artifact.scenarios -isnot [array]) {
        throw 'Runtime planning artifact scenarios must be an array.'
    }
    $artifactScenarios = @($Artifact.scenarios)
    if ($artifactScenarios.Count -ne 1) { throw 'Runtime planning artifact must contain exactly one selected scenario.' }
    if (-not [string]::Equals([string]$artifactScenarios[0].id, 'sales-order-demand', [StringComparison]::Ordinal)) {
        throw "Runtime planning artifact must select only 'sales-order-demand'."
    }

    $scenario = Get-NervAcceptanceSalesOrderRuntimeScenario -Manifest $Manifest
    $selection = [pscustomobject]@{
        selectionMode = if (Test-NervAcceptanceObjectProperty -Object $Artifact -Name 'selectionMode') { $Artifact.selectionMode } else { $null }
        reasons = @(if (Test-NervAcceptanceObjectProperty -Object $Artifact -Name 'selectionReasons') { $Artifact.selectionReasons })
        scenarios = @($scenario)
    }
    Assert-NervAcceptancePlanningArtifact `
        -Artifact $Artifact `
        -Manifest $Manifest `
        -Selection $selection `
        -Repository $Repository `
        -TestedSha $TestedSha `
        -RunId $RunId `
        -RunAttempt $RunAttempt `
        -ManifestPath $ManifestPath `
        -ManifestDigest $ManifestDigest `
        -Event $Event | Out-Null

    $workflowBudget = Get-NervAcceptanceRuntimeWorkflowBudget -WorkflowPath $WorkflowPath -JobName $WorkflowJobName -StepName $WorkflowStepName
    $requiredSeconds = Assert-NervAcceptanceRuntimeBudgetFits -ExecutionBudget $scenario.executionBudget -StepTimeoutSeconds $workflowBudget.stepTimeoutSeconds -ScenarioId ([string]$scenario.id)
    return [pscustomobject][ordered]@{
        scenario = $scenario
        artifact = $Artifact
        requiredSeconds = $requiredSeconds
        workflowBudget = $workflowBudget
    }
}

function New-NervAcceptanceScenarioRuntimeSummary {
    param([Parameter(Mandatory)] [object] $Contract)

    return [pscustomobject][ordered]@{
        schemaVersion = 1
        scenarioId = [string]$Contract.scenario.id
        repository = [string]$Contract.artifact.repository
        testedSha = [string]$Contract.artifact.testedSha
        runId = [string]$Contract.artifact.runId
        runAttempt = [int]$Contract.artifact.runAttempt
        event = [string]$Contract.artifact.event
        status = 'running'
        transitions = @(
            [pscustomobject][ordered]@{ sequence = 1; state = 'preflight-passed' }
        )
    }
}

function Write-NervAcceptanceScenarioRuntimeSummary {
    param(
        [Parameter(Mandatory)] [object] $Summary,
        [Parameter(Mandatory)] [string] $Path
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $directory = Split-Path -Parent $fullPath
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporaryPath = Join-Path $directory ".$([IO.Path]::GetFileName($fullPath)).$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        [IO.File]::WriteAllText($temporaryPath, (($Summary | ConvertTo-Json -Depth 50) + "`n"), [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporaryPath, $fullPath, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) { Remove-Item -LiteralPath $temporaryPath -Force }
    }
}

function Add-NervAcceptanceScenarioRuntimeTransition {
    param(
        [Parameter(Mandatory)] [object] $Summary,
        [Parameter(Mandatory)] [string] $State,
        [Parameter(Mandatory)] [string] $SummaryPath,
        [string] $Status = 'running'
    )

    $transitions = @($Summary.transitions)
    $Summary.transitions = @($transitions) + @(
        [pscustomobject][ordered]@{ sequence = $transitions.Count + 1; state = $State }
    )
    $Summary.status = $Status
    Write-NervAcceptanceScenarioRuntimeSummary -Summary $Summary -Path $SummaryPath
}

function Invoke-NervAcceptanceScenarioRuntime {
    param(
        [Parameter(Mandatory)] [object] $Artifact,
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [string] $Repository,
        [Parameter(Mandatory)] [string] $TestedSha,
        [Parameter(Mandatory)] [string] $RunId,
        [Parameter(Mandatory)] [int] $RunAttempt,
        [Parameter(Mandatory)] [string] $ManifestPath,
        [Parameter(Mandatory)] [string] $ManifestDigest,
        [Parameter(Mandatory)] [string] $Event,
        [Parameter(Mandatory)] [string] $WorkflowPath,
        [Parameter(Mandatory)] [string] $WorkflowJobName,
        [Parameter(Mandatory)] [string] $WorkflowStepName,
        [Parameter(Mandatory)] [string] $SummaryPath,
        [Parameter(Mandatory)] [scriptblock] $RuntimeAction
    )

    $contract = Assert-NervAcceptanceScenarioRuntimePreflight `
        -Artifact $Artifact `
        -Manifest $Manifest `
        -Repository $Repository `
        -TestedSha $TestedSha `
        -RunId $RunId `
        -RunAttempt $RunAttempt `
        -ManifestPath $ManifestPath `
        -ManifestDigest $ManifestDigest `
        -Event $Event `
        -WorkflowPath $WorkflowPath `
        -WorkflowJobName $WorkflowJobName `
        -WorkflowStepName $WorkflowStepName

    $summary = New-NervAcceptanceScenarioRuntimeSummary -Contract $contract
    Write-NervAcceptanceScenarioRuntimeSummary -Summary $summary -Path $SummaryPath
    Add-NervAcceptanceScenarioRuntimeTransition -Summary $summary -State 'action-started' -SummaryPath $SummaryPath
    try {
        $actionResult = & $RuntimeAction $contract
        Add-NervAcceptanceScenarioRuntimeTransition -Summary $summary -State 'action-completed' -Status 'completed' -SummaryPath $SummaryPath
        return [pscustomobject][ordered]@{ contract = $contract; summary = $summary; actionResult = $actionResult }
    }
    catch {
        Add-NervAcceptanceScenarioRuntimeTransition -Summary $summary -State 'action-failed' -Status 'failed' -SummaryPath $SummaryPath
        throw
    }
}

function Assert-NervAcceptanceRuntimeIntegerField {
    param(
        [Parameter(Mandatory)] [object] $Object,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Context
    )

    $value = $Object.PSObject.Properties[$Name].Value
    if (-not (Test-NervAcceptanceInteger -Value $value) -or [int64]$value -lt 0) {
        throw "$Context $Name must be a non-negative JSON integer."
    }
    return [int64]$value
}

function New-NervAcceptanceScenarioEquivalenceVector {
    param([Parameter(Mandatory)] [object] $Result)

    Assert-NervAcceptanceObjectSchema -Object $Result `
        -AllowedFields @('schemaVersion', 'scenarioId', 'conclusion', 'test', 'checkpoints', 'diagnostics', 'cleanup', 'volatile') `
        -RequiredFields @('schemaVersion', 'scenarioId', 'conclusion', 'test', 'checkpoints', 'diagnostics', 'cleanup', 'volatile') `
        -Context 'runtime equivalence result'
    if (-not (Test-NervAcceptanceInteger -Value $Result.schemaVersion) -or [int64]$Result.schemaVersion -ne 1) { throw 'Runtime equivalence result schemaVersion must be 1.' }
    if (-not [string]::Equals([string]$Result.scenarioId, 'sales-order-demand', [StringComparison]::Ordinal)) { throw "Runtime equivalence result scenarioId must be 'sales-order-demand'." }
    Assert-NervAcceptanceString -Value $Result.conclusion -Context 'runtime equivalence conclusion'

    Assert-NervAcceptanceObjectSchema -Object $Result.test `
        -AllowedFields @('identity', 'expected', 'discovered', 'passed', 'failed', 'skipped') `
        -RequiredFields @('identity', 'expected', 'discovered', 'passed', 'failed', 'skipped') `
        -Context 'runtime equivalence test'
    Assert-NervAcceptanceString -Value $Result.test.identity -Context 'runtime equivalence test identity'
    $testCounts = [ordered]@{}
    foreach ($name in @('expected', 'discovered', 'passed', 'failed', 'skipped')) {
        $testCounts[$name] = Assert-NervAcceptanceRuntimeIntegerField -Object $Result.test -Name $name -Context 'runtime equivalence test'
    }

    $checkpointFields = @('sourceStateCommittedBeforeMutation', 'http200BusinessErrorRejected', 'duplicateConverged', 'outOfOrderConverged', 'firstConsumeFailureRecovered')
    Assert-NervAcceptanceObjectSchema -Object $Result.checkpoints -AllowedFields $checkpointFields -RequiredFields $checkpointFields -Context 'runtime equivalence checkpoints'
    $checkpoints = [ordered]@{}
    foreach ($name in $checkpointFields) {
        Assert-NervAcceptanceBoolean -Value $Result.checkpoints.PSObject.Properties[$name].Value -Context "runtime equivalence checkpoint '$name'"
        $checkpoints[$name] = [bool]$Result.checkpoints.PSObject.Properties[$name].Value
    }

    Assert-NervAcceptanceObjectSchema -Object $Result.diagnostics `
        -AllowedFields @('schemas', 'capturedBeforeCleanup', 'secretsRedacted') `
        -RequiredFields @('schemas', 'capturedBeforeCleanup', 'secretsRedacted') `
        -Context 'runtime equivalence diagnostics'
    Assert-NervAcceptanceStringArray -Value $Result.diagnostics.schemas -Context 'runtime equivalence diagnostic schemas'
    $schemas = [string[]]@($Result.diagnostics.schemas)
    [Array]::Sort($schemas, [StringComparer]::Ordinal)
    foreach ($name in @('capturedBeforeCleanup', 'secretsRedacted')) {
        Assert-NervAcceptanceBoolean -Value $Result.diagnostics.PSObject.Properties[$name].Value -Context "runtime equivalence diagnostic '$name'"
    }

    Assert-NervAcceptanceObjectSchema -Object $Result.cleanup `
        -AllowedFields @('managedProcessesRemaining', 'disposableDatabasesRemaining', 'ownedResourcesRemaining', 'errors') `
        -RequiredFields @('managedProcessesRemaining', 'disposableDatabasesRemaining', 'ownedResourcesRemaining', 'errors') `
        -Context 'runtime equivalence cleanup'
    $cleanupCounts = [ordered]@{}
    foreach ($name in @('managedProcessesRemaining', 'disposableDatabasesRemaining', 'ownedResourcesRemaining')) {
        $cleanupCounts[$name] = Assert-NervAcceptanceRuntimeIntegerField -Object $Result.cleanup -Name $name -Context 'runtime equivalence cleanup'
    }
    Assert-NervAcceptanceStringArray -Value $Result.cleanup.errors -Context 'runtime equivalence cleanup errors' -AllowEmpty
    $cleanupErrors = [string[]]@($Result.cleanup.errors)
    [Array]::Sort($cleanupErrors, [StringComparer]::Ordinal)

    Assert-NervAcceptanceObjectSchema -Object $Result.volatile `
        -AllowedFields @('databaseName', 'processIds', 'capSuffix', 'startedAtUtc', 'completedAtUtc') `
        -RequiredFields @('databaseName', 'processIds', 'capSuffix', 'startedAtUtc', 'completedAtUtc') `
        -Context 'runtime equivalence volatile fields'

    return [pscustomobject][ordered]@{
        schemaVersion = 1
        scenarioId = [string]$Result.scenarioId
        conclusion = [string]$Result.conclusion
        test = [pscustomobject][ordered]@{
            identity = [string]$Result.test.identity
            expected = $testCounts.expected
            discovered = $testCounts.discovered
            passed = $testCounts.passed
            failed = $testCounts.failed
            skipped = $testCounts.skipped
        }
        checkpoints = [pscustomobject]$checkpoints
        diagnostics = [pscustomobject][ordered]@{
            schemas = @($schemas)
            capturedBeforeCleanup = [bool]$Result.diagnostics.capturedBeforeCleanup
            secretsRedacted = [bool]$Result.diagnostics.secretsRedacted
        }
        cleanup = [pscustomobject][ordered]@{
            managedProcessesRemaining = $cleanupCounts.managedProcessesRemaining
            disposableDatabasesRemaining = $cleanupCounts.disposableDatabasesRemaining
            ownedResourcesRemaining = $cleanupCounts.ownedResourcesRemaining
            errors = @($cleanupErrors)
        }
    }
}
