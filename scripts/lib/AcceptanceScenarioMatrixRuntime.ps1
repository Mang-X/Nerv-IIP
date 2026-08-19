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

function Get-NervAcceptanceRuntimeFileDigest {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Context
    )

    Assert-NervAcceptanceString -Value $Path -Context "$Context path"
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Context path '$Path' must identify one existing file." }
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return ([Convert]::ToHexString($sha256.ComputeHash([IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Path).Path)))).ToLowerInvariant()
    }
    finally { $sha256.Dispose() }
}

function Assert-NervAcceptanceRuntimeFileDigest {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $ExpectedDigest,
        [Parameter(Mandatory)] [string] $Context
    )

    if ($ExpectedDigest -cnotmatch '^[0-9a-f]{64}$') { throw "$Context expected digest must be a lowercase SHA-256 digest." }
    $actualDigest = Get-NervAcceptanceRuntimeFileDigest -Path $Path -Context $Context
    if (-not [string]::Equals($actualDigest, $ExpectedDigest, [StringComparison]::Ordinal)) {
        throw "$Context bytes do not match the expected SHA-256 digest."
    }
    return $actualDigest
}

function Import-NervAcceptanceRuntimePlanningArtifact {
    param(
        [Parameter(Mandatory)] [string] $ArtifactPath,
        [Parameter(Mandatory)] [string] $ExpectedArtifactDigest
    )

    [void](Assert-NervAcceptanceRuntimeFileDigest -Path $ArtifactPath -ExpectedDigest $ExpectedArtifactDigest -Context 'runtime planning artifact')
    try { return Get-Content -LiteralPath (Resolve-Path -LiteralPath $ArtifactPath).Path -Raw | ConvertFrom-Json -Depth 50 }
    catch { throw "Runtime planning artifact '$ArtifactPath' is not valid JSON: $($_.Exception.Message)" }
}

function Test-NervAcceptanceRuntimeRunCommand {
    param([AllowNull()] [object] $Run)

    if ($Run -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$Run)) { return $false }
    $tokens = $null
    $parseErrors = $null
    $ast = [Management.Automation.Language.Parser]::ParseInput([string]$Run, [ref]$tokens, [ref]$parseErrors)
    if (@($parseErrors).Count -ne 0 -or @($ast.EndBlock.Statements).Count -ne 1) { return $false }
    $statement = $ast.EndBlock.Statements[0]
    if ($statement -isnot [Management.Automation.Language.PipelineAst] -or @($statement.PipelineElements).Count -ne 1) { return $false }
    $command = $statement.PipelineElements[0]
    if ($command -isnot [Management.Automation.Language.CommandAst] -or
        $command.InvocationOperator -ne [Management.Automation.Language.TokenKind]::Unknown -or
        @($command.Redirections).Count -ne 0) {
        return $false
    }
    $commandName = $command.GetCommandName()
    if (-not [string]::Equals($commandName, 'pwsh', [StringComparison]::Ordinal) -and
        -not [string]::Equals($commandName, 'pwsh.exe', [StringComparison]::Ordinal)) {
        return $false
    }

    $elements = @($command.CommandElements)
    $index = 1
    while ($index -lt $elements.Count -and $elements[$index] -is [Management.Automation.Language.CommandParameterAst]) {
        $parameterName = [string]$elements[$index].ParameterName
        if ([string]::Equals($parameterName, 'File', [StringComparison]::Ordinal)) { $index++; break }
        if (-not [Collections.Generic.HashSet[string]]::new(
            [string[]]@('NoLogo', 'NoProfile', 'NonInteractive'),
            [StringComparer]::Ordinal).Contains($parameterName)) {
            return $false
        }
        $index++
    }
    if ($index -ge $elements.Count -or $elements[$index] -isnot [Management.Automation.Language.StringConstantExpressionAst]) { return $false }
    $scriptPath = [string]$elements[$index].Value
    return [string]::Equals($scriptPath, 'scripts/run-acceptance-scenario-matrix.ps1', [StringComparison]::Ordinal) -or
        [string]::Equals($scriptPath, './scripts/run-acceptance-scenario-matrix.ps1', [StringComparison]::Ordinal)
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
        [Parameter(Mandatory)] [string] $ArtifactPath,
        [Parameter(Mandatory)] [string] $ExpectedArtifactDigest,
        [Parameter(Mandatory)] [string] $ManifestFilePath,
        [Parameter(Mandatory)] [string] $ExpectedManifestDigest,
        [Parameter(Mandatory)] [string] $V1ManifestPath,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Repository,
        [Parameter(Mandatory)] [string] $TestedSha,
        [Parameter(Mandatory)] [string] $RunId,
        [Parameter(Mandatory)] [int] $RunAttempt,
        [Parameter(Mandatory)] [string] $ManifestPath,
        [Parameter(Mandatory)] [string] $Event,
        [Parameter(Mandatory)] [string] $WorkflowPath,
        [Parameter(Mandatory)] [string] $WorkflowJobName,
        [Parameter(Mandatory)] [string] $WorkflowStepName
    )

    $artifact = Import-NervAcceptanceRuntimePlanningArtifact -ArtifactPath $ArtifactPath -ExpectedArtifactDigest $ExpectedArtifactDigest
    [void](Assert-NervAcceptanceRuntimeFileDigest -Path $ManifestFilePath -ExpectedDigest $ExpectedManifestDigest -Context 'runtime acceptance manifest')
    $manifest = Import-NervAcceptanceScenarioMatrixManifest -ManifestPath $ManifestFilePath -V1ManifestPath $V1ManifestPath -RepositoryRoot $RepositoryRoot

    if (-not (Test-NervAcceptanceObjectProperty -Object $artifact -Name 'scenarios') -or $artifact.scenarios -isnot [array]) {
        throw 'Runtime planning artifact scenarios must be an array.'
    }
    $artifactScenarios = @($artifact.scenarios)
    if ($artifactScenarios.Count -ne 1) { throw 'Runtime planning artifact must contain exactly one selected scenario.' }
    if (-not [string]::Equals([string]$artifactScenarios[0].id, 'sales-order-demand', [StringComparison]::Ordinal)) {
        throw "Runtime planning artifact must select only 'sales-order-demand'."
    }

    $scenario = Get-NervAcceptanceSalesOrderRuntimeScenario -Manifest $manifest
    $selection = Select-NervAcceptanceScenarioMatrix -Manifest $manifest -Event $Event -DispatchSelection 'sales-order-demand'
    if (@($selection.scenarios).Count -ne 1 -or
        -not [string]::Equals([string]$selection.scenarios[0].id, 'sales-order-demand', [StringComparison]::Ordinal)) {
        throw "Runtime event '$Event' does not derive the trusted sales-only selection."
    }
    Assert-NervAcceptancePlanningArtifact `
        -Artifact $artifact `
        -Manifest $manifest `
        -Selection $selection `
        -Repository $Repository `
        -TestedSha $TestedSha `
        -RunId $RunId `
        -RunAttempt $RunAttempt `
        -ManifestPath $ManifestPath `
        -ManifestDigest $ExpectedManifestDigest `
        -Event $Event | Out-Null

    $workflowBudget = Get-NervAcceptanceRuntimeWorkflowBudget -WorkflowPath $WorkflowPath -JobName $WorkflowJobName -StepName $WorkflowStepName
    $requiredSeconds = Assert-NervAcceptanceRuntimeBudgetFits -ExecutionBudget $scenario.executionBudget -StepTimeoutSeconds $workflowBudget.stepTimeoutSeconds -ScenarioId ([string]$scenario.id)
    return [pscustomobject][ordered]@{
        scenario = $scenario
        artifact = $artifact
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
        [Parameter(Mandatory)] [string] $ArtifactPath,
        [Parameter(Mandatory)] [string] $ExpectedArtifactDigest,
        [Parameter(Mandatory)] [string] $ManifestFilePath,
        [Parameter(Mandatory)] [string] $ExpectedManifestDigest,
        [Parameter(Mandatory)] [string] $V1ManifestPath,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Repository,
        [Parameter(Mandatory)] [string] $TestedSha,
        [Parameter(Mandatory)] [string] $RunId,
        [Parameter(Mandatory)] [int] $RunAttempt,
        [Parameter(Mandatory)] [string] $ManifestPath,
        [Parameter(Mandatory)] [string] $Event,
        [Parameter(Mandatory)] [string] $WorkflowPath,
        [Parameter(Mandatory)] [string] $WorkflowJobName,
        [Parameter(Mandatory)] [string] $WorkflowStepName,
        [Parameter(Mandatory)] [string] $SummaryPath,
        [Parameter(Mandatory)] [scriptblock] $RuntimeAction
    )

    $contract = Assert-NervAcceptanceScenarioRuntimePreflight `
        -ArtifactPath $ArtifactPath `
        -ExpectedArtifactDigest $ExpectedArtifactDigest `
        -ManifestFilePath $ManifestFilePath `
        -ExpectedManifestDigest $ExpectedManifestDigest `
        -V1ManifestPath $V1ManifestPath `
        -RepositoryRoot $RepositoryRoot `
        -Repository $Repository `
        -TestedSha $TestedSha `
        -RunId $RunId `
        -RunAttempt $RunAttempt `
        -ManifestPath $ManifestPath `
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
        -AllowedFields @('managedProcessesRemaining', 'disposableDatabasesRemaining', 'ownedResourcesRemaining', 'errorCodes') `
        -RequiredFields @('managedProcessesRemaining', 'disposableDatabasesRemaining', 'ownedResourcesRemaining', 'errorCodes') `
        -Context 'runtime equivalence cleanup'
    $cleanupCounts = [ordered]@{}
    foreach ($name in @('managedProcessesRemaining', 'disposableDatabasesRemaining', 'ownedResourcesRemaining')) {
        $cleanupCounts[$name] = Assert-NervAcceptanceRuntimeIntegerField -Object $Result.cleanup -Name $name -Context 'runtime equivalence cleanup'
    }
    Assert-NervAcceptanceStringArray -Value $Result.cleanup.errorCodes -Context 'runtime equivalence cleanup errorCodes' -AllowEmpty
    $cleanupErrorCodes = [string[]]@($Result.cleanup.errorCodes)
    foreach ($errorCode in $cleanupErrorCodes) {
        if ($errorCode -cnotmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$') {
            throw "Runtime equivalence cleanup errorCode '$errorCode' must be canonical."
        }
    }
    [Array]::Sort($cleanupErrorCodes, [StringComparer]::Ordinal)

    Assert-NervAcceptanceObjectSchema -Object $Result.volatile `
        -AllowedFields @('databaseName', 'processIds', 'capSuffix', 'startedAtUtc', 'completedAtUtc', 'cleanupErrors') `
        -RequiredFields @('databaseName', 'processIds', 'capSuffix', 'startedAtUtc', 'completedAtUtc', 'cleanupErrors') `
        -Context 'runtime equivalence volatile fields'
    Assert-NervAcceptanceStringArray -Value $Result.volatile.cleanupErrors -Context 'runtime equivalence volatile cleanupErrors' -AllowEmpty

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
            errorCodes = @($cleanupErrorCodes)
        }
    }
}
