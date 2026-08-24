# Script-Governance:
#   Category: library, check
#   SideEffects:
#     - Reads a supplied GitHub Actions workflow through the acceptance planning contract
#     - Invokes only a caller-supplied in-process runtime action after all preflight checks pass
#     - Deletes an exact diagnostic artifact path when its post-write scan detects retained secret material
#   Writes:
#     - A caller-declared runtime summary through atomic file replacement
#     - Same-directory diagnostic artifact and .<leaf>.<guid>.tmp files through atomic file replacement
#   Cleanup:
#     - Removes owned temporary summary files after each persistence attempt
#     - Removes owned diagnostic temp files after each persistence attempt
#     - Removes the exact diagnostic temp or published artifact whose scan detects retained secret material
#   Requires:
#     - PowerShell 7

. (Join-Path $PSScriptRoot 'AcceptanceScenarioMatrix.ps1')
. (Join-Path $PSScriptRoot 'ScriptVariableBinding.ps1')

function Assert-NervAcceptanceCanonicalPhysicalPath {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Context,
        [Parameter(Mandatory)] [ValidateSet('File', 'Directory')] [string] $PathType
    )

    Assert-NervAcceptanceString -Value $Path -Context "$Context path"
    $fullPath = [IO.Path]::GetFullPath($Path)
    $providedPath = if ([string]::Equals($PathType, 'Directory', [StringComparison]::Ordinal)) {
        [IO.Path]::TrimEndingDirectorySeparator($Path)
    }
    else { $Path }
    $canonicalPath = if ([string]::Equals($PathType, 'Directory', [StringComparison]::Ordinal)) {
        [IO.Path]::TrimEndingDirectorySeparator($fullPath)
    }
    else { $fullPath }
    if (-not [string]::Equals($providedPath, $canonicalPath, [StringComparison]::Ordinal)) {
        throw "$Context must be a canonical absolute path."
    }
    $testPathType = if ([string]::Equals($PathType, 'Directory', [StringComparison]::Ordinal)) { 'Container' } else { 'Leaf' }
    if (-not (Test-Path -LiteralPath $canonicalPath -PathType $testPathType)) {
        throw "$Context must identify one existing $($PathType.ToLowerInvariant())."
    }

    $item = Get-Item -LiteralPath $canonicalPath -Force
    while ($null -ne $item) {
        if (-not [string]::IsNullOrEmpty([string]$item.LinkTarget) -or
            ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Context must not contain a symbolic link or reparse point."
        }
        $item = if ($item -is [IO.FileInfo]) { $item.Directory } else { $item.Parent }
    }
    return $canonicalPath
}

function Resolve-NervAcceptanceCanonicalOutputPath {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Context
    )

    $canonicalRepositoryRoot = Assert-NervAcceptanceCanonicalPhysicalPath -Path $RepositoryRoot -Context "$Context repository root" -PathType Directory
    if ([string]::IsNullOrWhiteSpace($Path) -or -not [string]::Equals($Path, $Path.Trim(), [StringComparison]::Ordinal)) {
        throw "$Context must be a canonical absolute path inside the repository root."
    }
    try { $canonicalPath = [IO.Path]::GetFullPath($Path) }
    catch { throw "$Context must be a canonical absolute path inside the repository root." }
    if (-not [string]::Equals($Path, $canonicalPath, [StringComparison]::Ordinal)) {
        throw "$Context must be a canonical absolute path inside the repository root."
    }
    $relativePath = [IO.Path]::GetRelativePath($canonicalRepositoryRoot, $canonicalPath).Replace([IO.Path]::DirectorySeparatorChar, '/')
    if (-not (Test-NervAcceptanceChangedPath -Path $relativePath)) {
        throw "$Context must remain inside the repository root."
    }

    $candidate = Split-Path -Parent $canonicalPath
    while (-not [string]::IsNullOrEmpty($candidate)) {
        if (Test-Path -LiteralPath $candidate) {
            $item = Get-Item -LiteralPath $candidate -Force
            if (-not [string]::IsNullOrEmpty([string]$item.LinkTarget) -or
                ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "$Context must not contain a symbolic link or reparse point."
            }
        }
        if ([string]::Equals([IO.Path]::TrimEndingDirectorySeparator($candidate), $canonicalRepositoryRoot, [StringComparison]::Ordinal)) { break }
        $parent = Split-Path -Parent $candidate
        if ([string]::Equals($parent, $candidate, [StringComparison]::Ordinal)) { break }
        $candidate = $parent
    }
    if (Test-Path -LiteralPath $canonicalPath) {
        $target = Get-Item -LiteralPath $canonicalPath -Force
        if ($target.PSIsContainer -or -not [string]::IsNullOrEmpty([string]$target.LinkTarget) -or
            ($target.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Context must identify a regular file path without symbolic links or reparse points."
        }
    }
    return $canonicalPath
}

function Write-NervAcceptanceCanonicalJson {
    param(
        [Parameter(Mandatory)] [object] $Value,
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Context
    )

    $canonicalPath = Resolve-NervAcceptanceCanonicalOutputPath -Path $Path -RepositoryRoot $RepositoryRoot -Context $Context
    $directory = Split-Path -Parent $canonicalPath
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporaryPath = Join-Path $directory ".$([IO.Path]::GetFileName($canonicalPath)).$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        [IO.File]::WriteAllText($temporaryPath, (($Value | ConvertTo-Json -Depth 50) + "`n"), [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporaryPath, $canonicalPath, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) { Remove-Item -LiteralPath $temporaryPath -Force }
    }
    return $canonicalPath
}

function Assert-NervAcceptanceRuntimeAuthorityPaths {
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $ManifestPath,
        [Parameter(Mandatory)] [string] $ManifestFilePath,
        [Parameter(Mandatory)] [string] $V1ManifestPath
    )

    $canonicalRepositoryRoot = Assert-NervAcceptanceCanonicalPhysicalPath -Path $RepositoryRoot -Context 'runtime repository root' -PathType Directory
    Assert-NervAcceptanceString -Value $ManifestPath -Context 'runtime manifest repository-relative path'
    if ([IO.Path]::IsPathRooted($ManifestPath)) { throw 'Runtime manifest repository-relative path must not be rooted.' }
    $expectedManifestPath = [IO.Path]::GetFullPath((Join-Path $canonicalRepositoryRoot $ManifestPath))
    $relativeManifestPath = [IO.Path]::GetRelativePath($canonicalRepositoryRoot, $expectedManifestPath).Replace([IO.Path]::DirectorySeparatorChar, '/')
    if (-not [string]::Equals($relativeManifestPath, $ManifestPath, [StringComparison]::Ordinal)) {
        throw 'Runtime manifest repository-relative path must be canonical and remain inside the repository root.'
    }

    $canonicalManifestPath = Assert-NervAcceptanceCanonicalPhysicalPath -Path $ManifestFilePath -Context 'runtime acceptance manifest' -PathType File
    if (-not [string]::Equals($canonicalManifestPath, $expectedManifestPath, [StringComparison]::Ordinal)) {
        throw 'Runtime acceptance manifest path must equal the authoritative repository manifest.'
    }

    $expectedV1ManifestPath = [IO.Path]::GetFullPath((Join-Path $canonicalRepositoryRoot 'scripts/full-chain-test-lane.json'))
    $canonicalV1ManifestPath = Assert-NervAcceptanceCanonicalPhysicalPath -Path $V1ManifestPath -Context 'runtime FullChain v1 manifest' -PathType File
    if (-not [string]::Equals($canonicalV1ManifestPath, $expectedV1ManifestPath, [StringComparison]::Ordinal)) {
        throw 'Runtime FullChain v1 manifest path must equal the authoritative FullChain v1 manifest.'
    }

    return [pscustomobject][ordered]@{
        repositoryRoot = $canonicalRepositoryRoot
        manifestPath = $canonicalManifestPath
        v1ManifestPath = $canonicalV1ManifestPath
    }
}

function Assert-NervAcceptanceNoDuplicateJsonProperties {
    param(
        [Parameter(Mandatory)] [Text.Json.JsonElement] $Element,
        [Parameter(Mandatory)] [string] $Context
    )

    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
        $propertyNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            if (-not $propertyNames.Add($property.Name)) {
                throw "$Context contains duplicate JSON property '$($property.Name)'."
            }
            Assert-NervAcceptanceNoDuplicateJsonProperties -Element $property.Value -Context "$Context.$($property.Name)"
        }
        return
    }
    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
        $index = 0
        foreach ($item in $Element.EnumerateArray()) {
            Assert-NervAcceptanceNoDuplicateJsonProperties -Element $item -Context "$Context[$index]"
            $index++
        }
    }
}

function Read-NervAcceptanceRuntimeJsonSnapshot {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Context,
        [string] $ExpectedDigest,
        [scriptblock] $ReadFileBytesAction
    )

    Assert-NervAcceptanceString -Value $Path -Context "$Context path"
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Context must identify one existing file." }
    if (-not [string]::IsNullOrEmpty($ExpectedDigest) -and $ExpectedDigest -cnotmatch '^[0-9a-f]{64}$') {
        throw "$Context expected digest must be a lowercase SHA-256 digest."
    }

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    $bytes = if ($null -eq $ReadFileBytesAction) {
        [IO.File]::ReadAllBytes($resolvedPath)
    }
    else {
        [byte[]]@(& $ReadFileBytesAction $resolvedPath)
    }
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try { $actualDigest = ([Convert]::ToHexString($sha256.ComputeHash($bytes))).ToLowerInvariant() }
    finally { $sha256.Dispose() }
    if (-not [string]::IsNullOrEmpty($ExpectedDigest) -and
        -not [string]::Equals($actualDigest, $ExpectedDigest, [StringComparison]::Ordinal)) {
        throw "$Context bytes do not match the expected SHA-256 digest."
    }

    try {
        $jsonDocument = [Text.Json.JsonDocument]::Parse([ReadOnlyMemory[byte]]([byte[]]$bytes))
        try { Assert-NervAcceptanceNoDuplicateJsonProperties -Element $jsonDocument.RootElement -Context $Context }
        finally { $jsonDocument.Dispose() }
        $json = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        $value = $json | ConvertFrom-Json -Depth 50 -DateKind String
    }
    catch { throw "$Context is not valid UTF-8 JSON: $($_.Exception.Message)" }
    return [pscustomobject][ordered]@{ digest = $actualDigest; value = $value }
}

function Assert-NervAcceptanceRuntimeV1ManifestObject {
    param([Parameter(Mandatory)] [object] $V1Manifest)

    Assert-NervAcceptanceObjectSchema -Object $V1Manifest -AllowedFields @('schemaVersion', 'members') -RequiredFields @('schemaVersion', 'members') -Context 'FullChain v1 manifest'
    if (-not (Test-NervAcceptanceInteger -Value $V1Manifest.schemaVersion) -or [int64]$V1Manifest.schemaVersion -ne 1) {
        throw 'FullChain v1 manifest schemaVersion must be 1.'
    }
    if ($V1Manifest.members -isnot [array]) { throw 'FullChain v1 manifest members must be an array.' }
    foreach ($member in @($V1Manifest.members)) {
        Assert-NervAcceptanceObjectSchema `
            -Object $member `
            -AllowedFields @('id', 'service', 'tier', 'status', 'project', 'filter', 'entrypoint', 'dependencies', 'diagnosticSchemas', 'expectedTestIdentities') `
            -RequiredFields @('id', 'service', 'tier', 'status', 'project', 'filter', 'entrypoint', 'dependencies', 'diagnosticSchemas', 'expectedTestIdentities') `
            -Context 'FullChain v1 member'
        foreach ($field in @('id', 'service', 'tier', 'status', 'project', 'filter')) {
            Assert-NervAcceptanceString -Value $member.PSObject.Properties[$field].Value -Context "FullChain v1 member.$field"
        }
        $memberId = [string]$member.id
        Assert-NervAcceptanceEntrypoint -Entrypoint $member.entrypoint -ScenarioId "FullChain v1 member '$memberId'"
        Assert-NervAcceptanceObjectSchema -Object $member.dependencies -AllowedFields @('postgres', 'redis', 'externalProcesses') -RequiredFields @('postgres', 'redis', 'externalProcesses') -Context "FullChain v1 member '$memberId' dependencies"
        foreach ($dependencyName in @('postgres', 'redis', 'externalProcesses')) {
            Assert-NervAcceptanceBoolean -Value $member.dependencies.PSObject.Properties[$dependencyName].Value -Context "FullChain v1 member '$memberId' dependencies.$dependencyName"
        }
        Assert-NervAcceptanceStringArray -Value $member.diagnosticSchemas -Context "FullChain v1 member '$memberId' diagnosticSchemas"
        Assert-NervAcceptanceStringArray -Value $member.expectedTestIdentities -Context "FullChain v1 member '$memberId' expectedTestIdentities"
    }
    return $V1Manifest
}

function Assert-NervAcceptanceRuntimeManifestObject {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [object] $V1Manifest,
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )

    Assert-NervAcceptanceObjectSchema -Object $Manifest -AllowedFields @('schemaVersion', 'lane', 'planningBudget', 'scenarios') -RequiredFields @('schemaVersion', 'lane', 'planningBudget', 'scenarios') -Context 'manifest'
    if (-not (Test-NervAcceptanceInteger -Value $Manifest.schemaVersion) -or [int64]$Manifest.schemaVersion -ne 2) { throw "Unsupported acceptance scenario manifest schemaVersion '$($Manifest.schemaVersion)'." }
    if (-not [string]::Equals([string]$Manifest.lane, 'full-chain', [StringComparison]::Ordinal)) { throw "Acceptance scenario manifest lane must be 'full-chain'." }
    Assert-NervAcceptancePlanningBudget -Budget $Manifest.planningBudget

    if ($Manifest.scenarios -isnot [array]) { throw 'Acceptance scenario manifest scenarios must be an array.' }
    $scenarios = @($Manifest.scenarios)
    if ($scenarios.Count -ne 6) { throw "Acceptance scenario manifest must contain exactly 6 scenarios; observed $($scenarios.Count)." }
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $aliases = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $identities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($scenario in $scenarios) { Assert-NervAcceptanceScenarioShape -Scenario $scenario -Ids $ids -Aliases $aliases -Identities $identities -RepositoryRoot $RepositoryRoot }

    $expectedIds = @('sales-order-demand', 'wms-delivery-erp', 'mes-produced-lot-inventory', 'telemetry-runtime-maintenance', 'erp-return-closure', 'equipment-unavailable-scheduling-mes')
    $observedIds = @($scenarios | ForEach-Object { [string]$_.id })
    if (-not (Test-NervAcceptanceOrdinalSequenceEqual -Left $observedIds -Right $expectedIds)) { throw 'Acceptance scenario manifest ids are not in the approved stable order.' }
    for ($index = 0; $index -lt 5; $index++) {
        if (-not [string]::Equals([string]$scenarios[$index].status, 'active', [StringComparison]::Ordinal) -or
            -not [string]::Equals([string]$scenarios[$index].tier, 'core', [StringComparison]::Ordinal)) {
            throw "scenario '$($scenarios[$index].id)' must be active/core."
        }
    }
    $blocked = $scenarios[5]
    if (-not [string]::Equals([string]$blocked.status, 'blocked', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$blocked.tier, 'extended', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$blocked.ownerIssue, '#1240', [StringComparison]::Ordinal)) {
        throw "scenario '$($blocked.id)' must be blocked/extended and owned by #1240."
    }

    $validatedV1Manifest = Assert-NervAcceptanceRuntimeV1ManifestObject -V1Manifest $V1Manifest
    Assert-NervAcceptanceV1Closure -Manifest $Manifest -V1Manifest $validatedV1Manifest -RepositoryRoot $RepositoryRoot
    return $Manifest
}

function Test-NervAcceptanceRuntimePwshRunnerCommand {
    param([Parameter(Mandatory)] [Management.Automation.Language.CommandAst] $Command)

    if ($Command.InvocationOperator -ne [Management.Automation.Language.TokenKind]::Unknown -or
        @($Command.Redirections).Count -ne 0 -or
        -not [string]::Equals($Command.GetCommandName(), 'pwsh', [StringComparison]::Ordinal)) {
        return $false
    }
    $elements = @($command.CommandElements)
    $index = 1
    while ($index -lt $elements.Count -and $elements[$index] -is [Management.Automation.Language.CommandParameterAst]) {
        if ($null -ne $elements[$index].Argument) { return $false }
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
    $pathAllowed = [string]::Equals($scriptPath, 'scripts/run-acceptance-scenario-matrix.ps1', [StringComparison]::Ordinal) -or
        [string]::Equals($scriptPath, './scripts/run-acceptance-scenario-matrix.ps1', [StringComparison]::Ordinal)
    return $pathAllowed -and $index -eq $elements.Count - 1
}

function Test-NervAcceptanceRuntimeDirectRunnerCommand {
    param([Parameter(Mandatory)] [Management.Automation.Language.CommandAst] $Command)

    if ($Command.InvocationOperator -ne [Management.Automation.Language.TokenKind]::Unknown -or
        @($Command.Redirections).Count -ne 0 -or
        -not [string]::Equals($Command.GetCommandName(), './scripts/run-acceptance-scenario-matrix.ps1', [StringComparison]::Ordinal)) {
        return $false
    }
    $expectedParameters = @(
        'ArtifactPath',
        'ExpectedArtifactDigest',
        'ExpectedManifestDigest',
        'Repository',
        'TestedSha',
        'RunId',
        'RunAttempt',
        'PlanningRunAttempt',
        'Event',
        'SummaryPath',
        'CanonicalResultPath',
        'TrackIdentifier'
    )
    $elements = @($Command.CommandElements)
    if ($elements.Count -ne 1 + (2 * $expectedParameters.Count)) { return $false }
    for ($parameterIndex = 0; $parameterIndex -lt $expectedParameters.Count; $parameterIndex++) {
        $commandParameter = $elements[1 + (2 * $parameterIndex)]
        $argument = $elements[2 + (2 * $parameterIndex)]
        if ($commandParameter -isnot [Management.Automation.Language.CommandParameterAst] -or
            $null -ne $commandParameter.Argument -or
            -not [string]::Equals([string]$commandParameter.ParameterName, $expectedParameters[$parameterIndex], [StringComparison]::Ordinal) -or
            ($argument -isnot [Management.Automation.Language.StringConstantExpressionAst] -and
                $argument -isnot [Management.Automation.Language.VariableExpressionAst])) {
            return $false
        }
    }
    return $true
}

function Test-NervAcceptanceRuntimeRunnerCommandCandidate {
    param([Parameter(Mandatory)] [Management.Automation.Language.CommandAst] $Command)

    $commandName = $Command.GetCommandName()
    if ([string]::Equals($commandName, './scripts/run-acceptance-scenario-matrix.ps1', [StringComparison]::Ordinal)) { return $true }
    if (-not [string]::Equals($commandName, 'pwsh', [StringComparison]::Ordinal)) { return $false }
    foreach ($element in @($Command.CommandElements | Select-Object -Skip 1)) {
        if ($element -is [Management.Automation.Language.StringConstantExpressionAst] -and
            ([string]::Equals([string]$element.Value, 'scripts/run-acceptance-scenario-matrix.ps1', [StringComparison]::Ordinal) -or
                [string]::Equals([string]$element.Value, './scripts/run-acceptance-scenario-matrix.ps1', [StringComparison]::Ordinal))) {
            return $true
        }
    }
    return $false
}

function Test-NervAcceptanceRuntimeRunCommand {
    param([AllowNull()] [object] $Run)

    if ($Run -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$Run)) { return $false }
    $tokens = $null
    $parseErrors = $null
    $ast = [Management.Automation.Language.Parser]::ParseInput([string]$Run, [ref]$tokens, [ref]$parseErrors)
    if (@($parseErrors).Count -ne 0) { return $false }

    $runnerCommands = [Collections.Generic.List[Management.Automation.Language.CommandAst]]::new()
    foreach ($statement in @($ast.EndBlock.Statements)) {
        if ($statement -isnot [Management.Automation.Language.PipelineAst] -or @($statement.PipelineElements).Count -ne 1) { continue }
        $command = $statement.PipelineElements[0]
        if ($command -is [Management.Automation.Language.CommandAst] -and
            (Test-NervAcceptanceRuntimeRunnerCommandCandidate -Command $command)) {
            $runnerCommands.Add($command)
        }
    }
    if ($runnerCommands.Count -ne 1) { return $false }
    $runnerCommand = $runnerCommands[0]
    if ([string]::Equals($runnerCommand.GetCommandName(), 'pwsh', [StringComparison]::Ordinal)) {
        return Test-NervAcceptanceRuntimePwshRunnerCommand -Command $runnerCommand
    }
    return Test-NervAcceptanceRuntimeDirectRunnerCommand -Command $runnerCommand
}

function Get-NervAcceptanceRuntimeWorkflowBudget {
    param(
        [Parameter(Mandatory)] [string] $WorkflowPath,
        [Parameter(Mandatory)] [string] $JobName,
        [Parameter(Mandatory)] [string] $StepName
    )

    $jobs = Get-NervCiWorkflowBudgets -Path $WorkflowPath
    $jobMatches = @($jobs | Where-Object { [string]::Equals([string]$_.Name, $JobName, [StringComparison]::Ordinal) })
    if ($jobMatches.Count -ne 1) { throw 'Runtime workflow must define exactly one configured runtime job.' }
    $stepMatches = @($jobMatches[0].Steps | Where-Object { [string]::Equals([string]$_.Name, $StepName, [StringComparison]::Ordinal) })
    if ($stepMatches.Count -ne 1 -or $null -eq $stepMatches[0].TimeoutMinutes) {
        throw 'Runtime workflow job must define exactly one timed configured runtime step.'
    }
    if (-not (Test-NervAcceptanceRuntimeRunCommand -Run $stepMatches[0].Run)) {
        throw 'Runtime workflow timed step must invoke scripts/run-acceptance-scenario-matrix.ps1.'
    }
    $timeoutSeconds = ConvertTo-NervAcceptanceCheckedInt64 -Value (([Numerics.BigInteger]$stepMatches[0].TimeoutMinutes) * 60) -Context 'runtime workflow step timeout'
    if ($timeoutSeconds -le 0) { throw 'Runtime workflow step timeout must be positive.' }
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

function Get-NervAcceptanceRuntimeScenarioAdapter {
    param([Parameter(Mandatory)] [string] $ScenarioId)

    if ([string]::Equals($ScenarioId, 'sales-order-demand', [StringComparison]::Ordinal)) {
        return [pscustomobject][ordered]@{
            scenarioId = 'sales-order-demand'
            v1Alias = 'sales-order-demand-planning'
            entrypoint = 'scripts/verify-erp-sales-order-demand-planning.ps1'
            identity = 'Nerv.IIP.Business.FullChain.Tests.SalesOrderDemandPlanningPostgresRedisAcceptanceTests.External_process_injects_duplicate_and_out_of_order_sales_order_events'
            businessFactFields = @('sourceStateCommittedBeforeMutation', 'changeV2Converged', 'changeV3Converged', 'duplicateConverged', 'outOfOrderConverged', 'cancellationConverged')
            portFields = @('masterData', 'erp', 'demandPlanning')
        }
    }
    if ([string]::Equals($ScenarioId, 'wms-delivery-erp', [StringComparison]::Ordinal)) {
        return [pscustomobject][ordered]@{
            scenarioId = 'wms-delivery-erp'
            v1Alias = 'erp-wms-delivery-completion'
            entrypoint = 'scripts/verify-erp-wms-delivery-completion.ps1'
            identity = 'Nerv.IIP.Business.FullChain.Tests.ErpWmsDeliveryCompletionPostgresRedisAcceptanceTests.External_process_replays_completed_wms_event_without_duplicate_delivery_or_receivable_facts'
            businessFactFields = @('outboundAssigned', 'pickingLifecycleCompleted', 'outboundCompleted', 'deliveryCompleted', 'receivableCreated', 'completionReplayConverged', 'repeatedEventConverged')
            portFields = @('erp', 'wms', 'inventory')
        }
    }
    throw "Runtime scenarioId is not supported by an explicit canonical adapter."
}

function Get-NervAcceptanceRuntimeScenario {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [string] $ScenarioId
    )

    $adapter = Get-NervAcceptanceRuntimeScenarioAdapter -ScenarioId $ScenarioId
    $matches = @($Manifest.scenarios | Where-Object {
        [string]::Equals([string]$_.id, $ScenarioId, [StringComparison]::Ordinal)
    })
    if ($matches.Count -ne 1) { throw "Runtime manifest must contain exactly one '$ScenarioId' scenario." }
    $scenario = $matches[0]
    if (-not [string]::Equals([string]$scenario.status, 'active', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$scenario.tier, 'core', [StringComparison]::Ordinal)) {
        throw "Runtime scenario '$ScenarioId' must be active/core."
    }
    if (-not [string]::Equals([string]$scenario.v1Alias, [string]$adapter.v1Alias, [StringComparison]::Ordinal)) {
        throw "Runtime scenario '$ScenarioId' v1Alias drifted from '$($adapter.v1Alias)'."
    }
    if ($scenario.entrypoint -isnot [pscustomobject] -or
        -not [string]::Equals([string]$scenario.entrypoint.kind, 'script', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$scenario.entrypoint.path, [string]$adapter.entrypoint, [StringComparison]::Ordinal)) {
        throw "Runtime scenario '$ScenarioId' entrypoint drifted from the governed v1 script."
    }
    $projects = @($scenario.testProjects)
    if ($projects.Count -ne 1 -or
        -not [string]::Equals([string]$projects[0].path, 'backend/tests/Nerv.IIP.Business.FullChain.Tests/Nerv.IIP.Business.FullChain.Tests.csproj', [StringComparison]::Ordinal)) {
        throw "Runtime scenario '$ScenarioId' project drifted from the governed FullChain project."
    }
    $identities = @($projects[0].frozenTestIdentities)
    if ($identities.Count -ne 1 -or -not [string]::Equals([string]$identities[0], [string]$adapter.identity, [StringComparison]::Ordinal)) {
        throw "Runtime scenario '$ScenarioId' frozen identity drifted from the governed v1 identity."
    }
    return $scenario
}

function Get-NervAcceptanceSalesOrderRuntimeScenario {
    param([Parameter(Mandatory)] [object] $Manifest)
    return Get-NervAcceptanceRuntimeScenario -Manifest $Manifest -ScenarioId 'sales-order-demand'
}

function Get-NervAcceptanceRuntimeArtifactSelection {
    param(
        [Parameter(Mandatory)] [object] $Artifact,
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [string] $Event
    )

    if (-not (Test-NervAcceptanceObjectProperty -Object $Artifact -Name 'scenarios') -or $Artifact.scenarios -isnot [array]) {
        throw 'Runtime planning artifact scenarios must be an array.'
    }
    Assert-NervAcceptanceStringArray -Value $Artifact.selectionReasons -Context 'runtime planning artifact selectionReasons'
    $selectedScenarios = [Collections.Generic.List[object]]::new()
    $selectedIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($artifactScenario in @($Artifact.scenarios)) {
        Assert-NervAcceptanceObjectSchema -Object $artifactScenario -AllowedFields @('id', 'status', 'tier') -RequiredFields @('id', 'status', 'tier') -Context 'runtime planning artifact scenario'
        Assert-NervAcceptanceString -Value $artifactScenario.id -Context 'runtime planning artifact scenario id'
        $scenarioId = [string]$artifactScenario.id
        if (-not $selectedIds.Add($scenarioId)) { throw "Runtime planning artifact contains duplicate selected scenario '$scenarioId'." }
        $matches = @($Manifest.scenarios | Where-Object { [string]::Equals([string]$_.id, $scenarioId, [StringComparison]::Ordinal) })
        if ($matches.Count -ne 1 -or
            -not [string]::Equals([string]$matches[0].status, 'active', [StringComparison]::Ordinal) -or
            -not [string]::Equals([string]$matches[0].tier, 'core', [StringComparison]::Ordinal)) {
            throw "Runtime planning artifact scenario '$scenarioId' must identify one selected active/core manifest scenario."
        }
        $selectedScenarios.Add($matches[0])
    }

    $selectionMode = [string]$Artifact.selectionMode
    $selectionReasons = [string[]]@($Artifact.selectionReasons)
    $activeCore = @($Manifest.scenarios | Where-Object {
        [string]::Equals([string]$_.status, 'active', [StringComparison]::Ordinal) -and
        [string]::Equals([string]$_.tier, 'core', [StringComparison]::Ordinal)
    })
    $canonicalSelectedIds = [string[]]@($selectedScenarios | ForEach-Object { [string]$_.id })
    $canonicalActiveCoreIds = [string[]]@($activeCore | ForEach-Object { [string]$_.id })
    [Array]::Sort($canonicalSelectedIds, [StringComparer]::Ordinal)
    [Array]::Sort($canonicalActiveCoreIds, [StringComparer]::Ordinal)
    if ([string]::Equals($Event, 'push', [StringComparison]::Ordinal)) {
        if (-not [string]::Equals($selectionMode, 'main-active-core', [StringComparison]::Ordinal) -or
            -not (Test-NervAcceptanceOrdinalSequenceEqual -Left $selectionReasons -Right ([string[]]@('main'))) -or
            -not (Test-NervAcceptanceOrdinalSequenceEqual -Left $canonicalSelectedIds -Right $canonicalActiveCoreIds)) {
            throw 'Runtime push planning artifact must preserve the main active/core selection provenance.'
        }
    }
    elseif ([string]::Equals($Event, 'schedule', [StringComparison]::Ordinal)) {
        if (-not [string]::Equals($selectionMode, 'nightly-active', [StringComparison]::Ordinal) -or
            -not (Test-NervAcceptanceOrdinalSequenceEqual -Left $selectionReasons -Right ([string[]]@('nightly'))) -or
            -not (Test-NervAcceptanceOrdinalSequenceEqual -Left $canonicalSelectedIds -Right $canonicalActiveCoreIds)) {
            throw 'Runtime scheduled planning artifact must preserve the nightly active selection provenance.'
        }
    }
    elseif ([string]::Equals($Event, 'workflow_dispatch', [StringComparison]::Ordinal)) {
        if ([string]::Equals($selectionMode, 'workflow-dispatch-scenario', [StringComparison]::Ordinal)) {
            if ($selectedScenarios.Count -ne 1 -or
                -not (Test-NervAcceptanceOrdinalSequenceEqual -Left $selectionReasons -Right ([string[]]@("dispatch:$($selectedScenarios[0].id)")))) {
                throw 'Runtime workflow_dispatch scenario selection provenance is inconsistent.'
            }
        }
        elseif ([string]::Equals($selectionMode, 'workflow-dispatch-all-active', [StringComparison]::Ordinal)) {
            $allowedReasons = [Collections.Generic.HashSet[string]]::new([string[]]@('dispatch:lane', 'dispatch:full'), [StringComparer]::Ordinal)
            if ($selectionReasons.Count -ne 1 -or -not $allowedReasons.Contains($selectionReasons[0]) -or
                -not (Test-NervAcceptanceOrdinalSequenceEqual -Left $canonicalSelectedIds -Right $canonicalActiveCoreIds)) {
                throw 'Runtime workflow_dispatch all-active selection provenance is inconsistent.'
            }
        }
        else { throw 'Runtime workflow_dispatch selection mode is inconsistent.' }
    }
    elseif ([string]::Equals($selectionMode, 'conservative-active-core', [StringComparison]::Ordinal)) {
        $allowedReasons = [Collections.Generic.HashSet[string]]::new(
            [string[]]@('impact-rules-invalid', 'impact-rules-failed', 'changed-paths-missing-or-invalid'),
            [StringComparer]::Ordinal)
        if ($selectionReasons.Count -ne 1 -or -not $allowedReasons.Contains($selectionReasons[0]) -or
            -not (Test-NervAcceptanceOrdinalSequenceEqual -Left $canonicalSelectedIds -Right $canonicalActiveCoreIds)) {
            throw 'Runtime conservative PR selection provenance is inconsistent.'
        }
    }
    else {
        if (-not [string]::Equals($selectionMode, 'pull-request-impact', [StringComparison]::Ordinal)) {
            throw 'Runtime pull-request selection mode is inconsistent.'
        }
        if ($selectedScenarios.Count -eq 0) {
            if (-not (Test-NervAcceptanceOrdinalSequenceEqual -Left $selectionReasons -Right ([string[]]@('no-impact')))) {
                throw 'Runtime empty PR selection must preserve no-impact provenance.'
            }
        }
        else {
            foreach ($reason in $selectionReasons) {
                if ($reason -cnotmatch '^(?:impact|entrypoint|global-impact):.+$') {
                    throw 'Runtime PR impact selection reason is not canonical.'
                }
            }
        }
    }
    return [pscustomobject][ordered]@{ selectionMode = $selectionMode; reasons = @($selectionReasons); scenarios = $selectedScenarios.ToArray() }
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
        [Parameter(Mandatory)] [int] $PlanningRunAttempt,
        [Parameter(Mandatory)] [string] $ManifestPath,
        [Parameter(Mandatory)] [string] $Event,
        [Parameter(Mandatory)] [string] $WorkflowPath,
        [Parameter(Mandatory)] [string] $WorkflowJobName,
        [Parameter(Mandatory)] [string] $WorkflowStepName,
        [string] $ScenarioId = 'sales-order-demand',
        [scriptblock] $ReadFileBytesAction
    )

    $authorityPaths = Assert-NervAcceptanceRuntimeAuthorityPaths -RepositoryRoot $RepositoryRoot -ManifestPath $ManifestPath -ManifestFilePath $ManifestFilePath -V1ManifestPath $V1ManifestPath
    $artifactSnapshot = Read-NervAcceptanceRuntimeJsonSnapshot -Path $ArtifactPath -ExpectedDigest $ExpectedArtifactDigest -Context 'runtime planning artifact' -ReadFileBytesAction $ReadFileBytesAction
    $manifestSnapshot = Read-NervAcceptanceRuntimeJsonSnapshot -Path $authorityPaths.manifestPath -ExpectedDigest $ExpectedManifestDigest -Context 'runtime acceptance manifest' -ReadFileBytesAction $ReadFileBytesAction
    $v1ManifestSnapshot = Read-NervAcceptanceRuntimeJsonSnapshot -Path $authorityPaths.v1ManifestPath -Context 'runtime FullChain v1 manifest' -ReadFileBytesAction $ReadFileBytesAction
    $artifact = $artifactSnapshot.value
    $manifest = Assert-NervAcceptanceRuntimeManifestObject -Manifest $manifestSnapshot.value -V1Manifest $v1ManifestSnapshot.value -RepositoryRoot $authorityPaths.repositoryRoot

    $selection = Get-NervAcceptanceRuntimeArtifactSelection -Artifact $artifact -Manifest $manifest -Event $Event
    Assert-NervAcceptancePlanningArtifact `
        -Artifact $artifact `
        -Manifest $manifest `
        -Selection $selection `
        -Repository $Repository `
        -TestedSha $TestedSha `
        -RunId $RunId `
        -RunAttempt $PlanningRunAttempt `
        -ManifestPath $ManifestPath `
        -ManifestDigest $ExpectedManifestDigest `
        -Event $Event | Out-Null

    $workflowBudget = Get-NervAcceptanceRuntimeWorkflowBudget -WorkflowPath $WorkflowPath -JobName $WorkflowJobName -StepName $WorkflowStepName
    $adapter = Get-NervAcceptanceRuntimeScenarioAdapter -ScenarioId $ScenarioId
    $scenarioMatches = @($selection.scenarios | Where-Object { [string]::Equals([string]$_.id, [string]$adapter.scenarioId, [StringComparison]::Ordinal) })
    if ($scenarioMatches.Count -gt 1) { throw "Runtime planning artifact must contain at most one selected '$ScenarioId' scenario." }
    $selected = $scenarioMatches.Count -eq 1
    $scenario = if ($selected) { Get-NervAcceptanceRuntimeScenario -Manifest $manifest -ScenarioId $ScenarioId } else { $null }
    $requiredSeconds = if ($selected) {
        Assert-NervAcceptanceRuntimeBudgetFits -ExecutionBudget $scenario.executionBudget -StepTimeoutSeconds $workflowBudget.stepTimeoutSeconds -ScenarioId ([string]$scenario.id)
    }
    else { 0L }
    return [pscustomobject][ordered]@{
        selected = $selected
        scenario = $scenario
        artifact = $artifact
        provenance = [pscustomobject][ordered]@{
            repository = $Repository
            runId = $RunId
            runAttempt = $RunAttempt
            testedSha = $TestedSha
            manifestDigest = $ExpectedManifestDigest
            scenarioId = $ScenarioId
        }
        artifactDigest = $artifactSnapshot.digest
        requiredSeconds = $requiredSeconds
        workflowBudget = $workflowBudget
    }
}

function New-NervAcceptanceScenarioRuntimeSummary {
    param([string] $ScenarioId = 'sales-order-demand')
    return [pscustomobject][ordered]@{
        schemaVersion = 1
        scenarioId = $ScenarioId
        repository = $null
        testedSha = $null
        runId = $null
        runAttempt = $null
        event = $null
        selected = $null
        status = 'running'
        transitions = @(
            [pscustomobject][ordered]@{ sequence = 1; state = 'preflight-started' }
        )
        result = $null
        failureClassification = $null
    }
}

function Resolve-NervAcceptanceScenarioRuntimePhysicalInput {
    param(
        [AllowNull()] [AllowEmptyString()] [string] $Value,
        [Parameter(Mandatory)] [string] $Context,
        [Parameter(Mandatory)] [ValidateSet('File', 'Directory')] [string] $PathType
    )

    $expectedPathType = if ([string]::Equals($PathType, 'Directory', [StringComparison]::Ordinal)) { 'directory' } else { 'file' }
    $failureMessage = "$Context must identify one existing canonical $expectedPathType."
    if ([string]::IsNullOrWhiteSpace($Value) -or
        -not [string]::Equals($Value, $Value.Trim(), [StringComparison]::Ordinal)) {
        throw $failureMessage
    }
    try { $fullPath = [IO.Path]::GetFullPath($Value) }
    catch { throw $failureMessage }
    $providedPath = if ([string]::Equals($PathType, 'Directory', [StringComparison]::Ordinal)) {
        [IO.Path]::TrimEndingDirectorySeparator($Value)
    }
    else { $Value }
    $canonicalPath = if ([string]::Equals($PathType, 'Directory', [StringComparison]::Ordinal)) {
        [IO.Path]::TrimEndingDirectorySeparator($fullPath)
    }
    else { $fullPath }
    $testPathType = if ([string]::Equals($PathType, 'Directory', [StringComparison]::Ordinal)) { 'Container' } else { 'Leaf' }
    if (-not [string]::Equals($providedPath, $canonicalPath, [StringComparison]::Ordinal) -or
        -not (Test-Path -LiteralPath $canonicalPath -PathType $testPathType)) {
        throw $failureMessage
    }
    return $canonicalPath
}

function Assert-NervAcceptanceScenarioRuntimeInvocation {
    param(
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $RunAttempt,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $Event
    )

    $allowedEvents = [Collections.Generic.HashSet[string]]::new(
        [string[]]@('pull_request', 'push', 'schedule', 'workflow_dispatch'),
        [StringComparer]::Ordinal)
    if (-not $allowedEvents.Contains($Event)) {
        throw "Acceptance scenario runtime event must be one of 'pull_request', 'push', 'schedule', or 'workflow_dispatch'."
    }
    $parsedRunAttempt = ConvertTo-NervAcceptanceScenarioRuntimeRunAttempt `
        -Value $RunAttempt `
        -Context 'Acceptance scenario runtime run attempt'

    return [pscustomobject][ordered]@{
        runAttempt = [int]$parsedRunAttempt
        event = $Event
    }
}

function ConvertTo-NervAcceptanceScenarioRuntimeRunAttempt {
    param(
        [AllowNull()] [AllowEmptyString()] [string] $Value,
        [Parameter(Mandatory)] [string] $Context
    )

    if ($Value -cnotmatch '^[1-9][0-9]*$') {
        throw "$Context must be a canonical positive integer."
    }
    $parsedRunAttempt = [Numerics.BigInteger]::Parse(
        $Value,
        [Globalization.NumberStyles]::None,
        [Globalization.CultureInfo]::InvariantCulture)
    if ($parsedRunAttempt -gt [int]::MaxValue) {
        throw "$Context must fit Int32."
    }
    return [int]$parsedRunAttempt
}

function Assert-NervAcceptanceScenarioRuntimeRawInputs {
    param(
        [AllowNull()] [AllowEmptyString()] [string] $ArtifactPath,
        [AllowNull()] [AllowEmptyString()] [string] $ExpectedArtifactDigest,
        [AllowNull()] [AllowEmptyString()] [string] $ManifestFilePath,
        [AllowNull()] [AllowEmptyString()] [string] $ExpectedManifestDigest,
        [AllowNull()] [AllowEmptyString()] [string] $V1ManifestPath,
        [AllowNull()] [AllowEmptyString()] [string] $RepositoryRoot,
        [AllowNull()] [AllowEmptyString()] [string] $Repository,
        [AllowNull()] [AllowEmptyString()] [string] $TestedSha,
        [AllowNull()] [AllowEmptyString()] [string] $RunId,
        [AllowNull()] [AllowEmptyString()] [string] $RunAttempt,
        [AllowNull()] [AllowEmptyString()] [string] $PlanningRunAttempt,
        [AllowNull()] [AllowEmptyString()] [string] $ManifestPath,
        [AllowNull()] [AllowEmptyString()] [string] $Event,
        [AllowNull()] [AllowEmptyString()] [string] $WorkflowPath,
        [AllowNull()] [AllowEmptyString()] [string] $WorkflowJobName,
        [AllowNull()] [AllowEmptyString()] [string] $WorkflowStepName
    )

    $validatedArtifactPath = Resolve-NervAcceptanceScenarioRuntimePhysicalInput -Value $ArtifactPath -Context 'Acceptance scenario runtime planning artifact path' -PathType File
    if ($ExpectedArtifactDigest -cnotmatch '^[0-9a-f]{64}$') {
        throw 'Acceptance scenario runtime artifact digest must be a lowercase SHA-256 digest.'
    }
    $validatedManifestFilePath = Resolve-NervAcceptanceScenarioRuntimePhysicalInput -Value $ManifestFilePath -Context 'Acceptance scenario runtime acceptance manifest path' -PathType File
    if ($ExpectedManifestDigest -cnotmatch '^[0-9a-f]{64}$') {
        throw 'Acceptance scenario runtime manifest digest must be a lowercase SHA-256 digest.'
    }
    $validatedV1ManifestPath = Resolve-NervAcceptanceScenarioRuntimePhysicalInput -Value $V1ManifestPath -Context 'Acceptance scenario runtime FullChain v1 manifest path' -PathType File
    $validatedRepositoryRoot = Resolve-NervAcceptanceScenarioRuntimePhysicalInput -Value $RepositoryRoot -Context 'Acceptance scenario runtime repository root' -PathType Directory
    if (-not (Test-NervAcceptanceRepositoryIdentifier -Repository $Repository)) {
        throw 'Acceptance scenario runtime repository must be a canonical owner/name identifier.'
    }
    if ($TestedSha -cnotmatch '^[0-9a-f]{40}$') {
        throw 'Acceptance scenario runtime tested SHA must be a lowercase 40-character Git SHA.'
    }
    if ($RunId -cnotmatch '^[1-9][0-9]*$') {
        throw 'Acceptance scenario runtime run id must be a canonical positive decimal identifier.'
    }
    $runtimeInvocation = Assert-NervAcceptanceScenarioRuntimeInvocation -RunAttempt $RunAttempt -Event $Event
    $validatedPlanningRunAttempt = ConvertTo-NervAcceptanceScenarioRuntimeRunAttempt `
        -Value $PlanningRunAttempt `
        -Context 'Acceptance scenario runtime planning run attempt'
    if (-not (Test-NervAcceptanceChangedPath -Path $ManifestPath)) {
        throw 'Acceptance scenario runtime manifest repository-relative path must be canonical.'
    }
    $validatedWorkflowPath = Resolve-NervAcceptanceScenarioRuntimePhysicalInput -Value $WorkflowPath -Context 'Acceptance scenario runtime workflow path' -PathType File
    Assert-NervAcceptanceString -Value $WorkflowJobName -Context 'Acceptance scenario runtime workflow job name'
    Assert-NervAcceptanceString -Value $WorkflowStepName -Context 'Acceptance scenario runtime workflow step name'

    return [pscustomobject][ordered]@{
        artifactPath = $validatedArtifactPath
        expectedArtifactDigest = $ExpectedArtifactDigest
        manifestFilePath = $validatedManifestFilePath
        expectedManifestDigest = $ExpectedManifestDigest
        v1ManifestPath = $validatedV1ManifestPath
        repositoryRoot = $validatedRepositoryRoot
        repository = $Repository
        testedSha = $TestedSha
        runId = $RunId
        runAttempt = $runtimeInvocation.runAttempt
        planningRunAttempt = $validatedPlanningRunAttempt
        manifestPath = $ManifestPath
        event = $runtimeInvocation.event
        workflowPath = $validatedWorkflowPath
        workflowJobName = $WorkflowJobName
        workflowStepName = $WorkflowStepName
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
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $ArtifactPath,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $ExpectedArtifactDigest,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $ManifestFilePath,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $ExpectedManifestDigest,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $V1ManifestPath,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $Repository,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $TestedSha,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $RunId,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $RunAttempt,
        [AllowNull()] [AllowEmptyString()] [string] $PlanningRunAttempt = $RunAttempt,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $ManifestPath,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $Event,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $WorkflowPath,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $WorkflowJobName,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $WorkflowStepName,
        [Parameter(Mandatory)] [string] $SummaryPath,
        [Parameter(Mandatory)] [scriptblock] $RuntimeAction,
        [string] $ScenarioId = 'sales-order-demand',
        [scriptblock] $ReadFileBytesAction
    )

    $summary = New-NervAcceptanceScenarioRuntimeSummary -ScenarioId $ScenarioId
    Write-NervAcceptanceScenarioRuntimeSummary -Summary $summary -Path $SummaryPath
    try {
        $validatedInputs = Assert-NervAcceptanceScenarioRuntimeRawInputs `
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
            -PlanningRunAttempt $PlanningRunAttempt `
            -ManifestPath $ManifestPath `
            -Event $Event `
            -WorkflowPath $WorkflowPath `
            -WorkflowJobName $WorkflowJobName `
            -WorkflowStepName $WorkflowStepName
        $contract = Assert-NervAcceptanceScenarioRuntimePreflight `
            -ArtifactPath $validatedInputs.artifactPath `
            -ExpectedArtifactDigest $validatedInputs.expectedArtifactDigest `
            -ManifestFilePath $validatedInputs.manifestFilePath `
            -ExpectedManifestDigest $validatedInputs.expectedManifestDigest `
            -V1ManifestPath $validatedInputs.v1ManifestPath `
            -RepositoryRoot $validatedInputs.repositoryRoot `
            -Repository $validatedInputs.repository `
            -TestedSha $validatedInputs.testedSha `
            -RunId $validatedInputs.runId `
            -RunAttempt $validatedInputs.runAttempt `
            -PlanningRunAttempt $validatedInputs.planningRunAttempt `
            -ManifestPath $validatedInputs.manifestPath `
            -Event $validatedInputs.event `
            -WorkflowPath $validatedInputs.workflowPath `
            -WorkflowJobName $validatedInputs.workflowJobName `
            -WorkflowStepName $validatedInputs.workflowStepName `
            -ScenarioId $ScenarioId `
            -ReadFileBytesAction $ReadFileBytesAction
        $summary.repository = $validatedInputs.repository
        $summary.testedSha = $validatedInputs.testedSha
        $summary.runId = $validatedInputs.runId
        $summary.runAttempt = $validatedInputs.runAttempt
        $summary.event = $validatedInputs.event
        $summary.selected = [bool]$contract.selected
        Write-NervAcceptanceScenarioRuntimeSummary -Summary $summary -Path $SummaryPath
    }
    catch {
        $summary.failureClassification = 'preflight-failed'
        Add-NervAcceptanceScenarioRuntimeTransition -Summary $summary -State 'preflight-failed' -Status 'failed' -SummaryPath $SummaryPath
        throw
    }

    Add-NervAcceptanceScenarioRuntimeTransition -Summary $summary -State 'preflight-passed' -SummaryPath $SummaryPath
    if (-not $contract.selected) {
        Add-NervAcceptanceScenarioRuntimeTransition -Summary $summary -State 'not-selected' -Status 'passed' -SummaryPath $SummaryPath
        return [pscustomobject][ordered]@{
            contract = $contract
            summary = $summary
            actionResult = $null
            equivalenceVector = $null
        }
    }
    Add-NervAcceptanceScenarioRuntimeTransition -Summary $summary -State 'action-started' -SummaryPath $SummaryPath
    try {
        $actionResults = @(& $RuntimeAction $contract)
        Add-NervAcceptanceScenarioRuntimeTransition -Summary $summary -State 'action-completed' -Status 'completed' -SummaryPath $SummaryPath
    }
    catch {
        $summary.failureClassification = 'action-failed'
        Add-NervAcceptanceScenarioRuntimeTransition -Summary $summary -State 'action-failed' -Status 'failed' -SummaryPath $SummaryPath
        throw
    }

    Add-NervAcceptanceScenarioRuntimeTransition -Summary $summary -State 'result-validation-started' -SummaryPath $SummaryPath
    try {
        $resultSnapshot = New-NervAcceptanceScenarioRuntimeResultSnapshot -Results $actionResults -ValidatedScenario $contract.scenario -ExpectedProvenance $contract.provenance
        $summary.result = $resultSnapshot
        Write-NervAcceptanceScenarioRuntimeSummary -Summary $summary -Path $SummaryPath
        $validatedResult = Assert-NervAcceptanceScenarioRuntimeResult -ResultSnapshot $resultSnapshot
        Add-NervAcceptanceScenarioRuntimeTransition -Summary $summary -State 'result-validation-passed' -Status 'passed' -SummaryPath $SummaryPath
        return [pscustomobject][ordered]@{
            contract = $contract
            summary = $summary
            actionResult = $actionResults[0]
            equivalenceVector = $validatedResult
        }
    }
    catch {
        $failureClassification = [string]$_.Exception.Data['NervAcceptanceFailureClassification']
        if ([string]::IsNullOrEmpty($failureClassification)) { $failureClassification = 'result-schema-invalid' }
        $summary.failureClassification = $failureClassification
        Add-NervAcceptanceScenarioRuntimeTransition -Summary $summary -State 'result-validation-failed' -Status 'failed' -SummaryPath $SummaryPath
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

function Test-NervAcceptanceWmsPickingReadbacks {
    param(
        [AllowEmptyCollection()] [object[]] $Readbacks = @(),
        [AllowEmptyCollection()] [decimal[]] $RequestedQuantities = @()
    )

    if ($Readbacks.Count -eq 0 -or $Readbacks.Count -ne $RequestedQuantities.Count) { return $false }
    for ($index = 0; $index -lt $Readbacks.Count; $index++) {
        $readback = $Readbacks[$index]
        if ($null -eq $readback -or
            -not [string]::Equals([string]$readback.status, 'Completed', [StringComparison]::OrdinalIgnoreCase) -or
            [decimal]$readback.executedQuantity -ne [decimal]$readback.plannedQuantity -or
            [decimal]$readback.executedQuantity -ne $RequestedQuantities[$index] -or
            [string]::IsNullOrWhiteSpace([string]$readback.completedAtUtc)) {
            return $false
        }
    }
    return $true
}

function Test-NervAcceptanceWmsCompletionReplay {
    param(
        [Parameter(Mandatory)] [object] $FirstCompletion,
        [Parameter(Mandatory)] [object] $ReplayCompletion
    )

    $firstRequestId = [string]$FirstCompletion.data.requestId
    $replayRequestId = [string]$ReplayCompletion.data.requestId
    return -not [string]::IsNullOrWhiteSpace($firstRequestId) -and
        [string]::Equals($replayRequestId, $firstRequestId, [StringComparison]::Ordinal)
}

function Test-NervAcceptanceWmsCompletedOutboundReadback {
    param([Parameter(Mandatory)] [AllowNull()] [object] $Readback)

    return $null -ne $Readback -and
        [string]::Equals([string]$Readback.status, 'Completed', [StringComparison]::OrdinalIgnoreCase) -and
        -not [string]::IsNullOrWhiteSpace([string]$Readback.completedAtUtc)
}

function Protect-NervAcceptanceWmsDiagnosticText {
    param(
        [AllowNull()] [string] $Text,
        [AllowEmptyCollection()] [string[]] $SensitiveValues = @()
    )

    if ($null -eq $Text) { return $null }
    $protected = Protect-ScriptAutomationText -Text $Text
    foreach ($sensitiveValue in @($SensitiveValues)) {
        if (-not [string]::IsNullOrWhiteSpace($sensitiveValue)) {
            $protected = $protected.Replace($sensitiveValue, '<redacted>', [StringComparison]::Ordinal)
        }
    }
    return $protected
}

function Write-NervAcceptanceWmsDiagnosticArtifact {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [AllowNull()] [string] $Content,
        [AllowEmptyCollection()] [string[]] $SensitiveValues = @()
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $parentPath = Split-Path -Parent $fullPath
    [IO.Directory]::CreateDirectory($parentPath) | Out-Null
    $temporaryPath = Join-Path $parentPath ".$(Split-Path -Leaf $fullPath).$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        $protectedContent = Protect-NervAcceptanceWmsDiagnosticText -Text $Content -SensitiveValues $SensitiveValues
        [IO.File]::WriteAllText($temporaryPath, "$protectedContent`n", [Text.UTF8Encoding]::new($false))
        [void](Assert-NervAcceptanceWmsDiagnosticArtifactRedacted -Path $temporaryPath -SensitiveValues $SensitiveValues)
        [IO.File]::Move($temporaryPath, $fullPath, $true)
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "WMS diagnostic artifact was not persisted: $fullPath"
        }
        [void](Assert-NervAcceptanceWmsDiagnosticArtifactRedacted -Path $fullPath -SensitiveValues $SensitiveValues)
        return [pscustomobject][ordered]@{
            artifactPath = $fullPath
            evidenceWritten = $true
            secretsRedacted = $true
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Assert-NervAcceptanceWmsDiagnosticArtifactRedacted {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [AllowEmptyCollection()] [string[]] $SensitiveValues = @()
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "WMS diagnostic artifact was not persisted: $Path"
    }
    $persistedContent = [IO.File]::ReadAllText($Path)
    $finding = $null
    foreach ($sensitiveValue in @($SensitiveValues)) {
        if (-not [string]::IsNullOrWhiteSpace($sensitiveValue) -and
            $persistedContent.Contains($sensitiveValue, [StringComparison]::Ordinal)) {
            $finding = 'declared sensitive value'
            break
        }
    }
    if ($null -eq $finding) {
        foreach ($pattern in @(
            '(?i)\bBearer\s+(?!<redacted>\b)[A-Za-z0-9._~+/=-]{6,}',
            '(?i)\b(?:password|pwd)\b\s*[=:]\s*["'']?(?!<redacted>\b)[^\s"'',;}]{4,}',
            '(?i)\b[a-z][a-z0-9+.-]*://[^\s/:@]+:(?!<redacted>@)[^\s/@]+@'
        )) {
            if ([regex]::IsMatch($persistedContent, $pattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
                $finding = 'secret pattern'
                break
            }
        }
    }
    if ($null -ne $finding) {
        Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
        throw "WMS diagnostic artifact retained a ${finding}: $Path"
    }
    return $true
}

function New-NervAcceptanceWmsSuccessfulDiagnosticEvidence {
    param(
        [Parameter(Mandatory)] [object] $WriteProof,
        [Parameter(Mandatory)] [object] $FailureCaptureSupported
    )

    Assert-NervAcceptanceObjectSchema -Object $WriteProof -AllowedFields @('artifactPath', 'evidenceWritten', 'secretsRedacted') -RequiredFields @('artifactPath', 'evidenceWritten', 'secretsRedacted') -Context 'MAN-527 successful diagnostic write proof'
    if ($WriteProof.evidenceWritten -isnot [bool] -or $WriteProof.secretsRedacted -isnot [bool] -or
        [string]::IsNullOrWhiteSpace([string]$WriteProof.artifactPath) -or
        -not [bool]$WriteProof.evidenceWritten -or -not [bool]$WriteProof.secretsRedacted) {
        throw 'MAN-527 successful diagnostics require an actual persisted artifact and a passing post-write sensitive-value scan.'
    }
    if ($FailureCaptureSupported -isnot [bool] -or -not [bool]$FailureCaptureSupported) {
        throw 'MAN-527 successful diagnostics require a passing failure-capture contract proof.'
    }
    return [pscustomobject][ordered]@{
        failureCaptureSupported = [bool]$FailureCaptureSupported
        failureDiagnosticsCaptured = $false
        secretsRedacted = [bool]$WriteProof.secretsRedacted
        artifactPaths = @()
        errors = @()
    }
}

function Test-NervAcceptanceWmsVerifierContract {
    param([Parameter(Mandatory)] [string] $Path)

    $tokens = $parseErrors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -ne 0) {
        return [pscustomobject][ordered]@{ failureCaptureSupported = $false; pickingReadbackWired = $false; completionReplayWired = $false; outboundCompletionWired = $false }
    }

    $hasAncestor = {
        param(
            [Management.Automation.Language.Ast] $Node,
            [Management.Automation.Language.Ast] $ExpectedAncestor)
        $ancestor = $Node.Parent
        while ($null -ne $ancestor) {
            if ([object]::ReferenceEquals($ancestor, $ExpectedAncestor)) { return $true }
            $ancestor = $ancestor.Parent
        }
        return $false
    }
    $typedInventory = @($ast.FindAll({
                param($node)
                $node -is [Management.Automation.Language.AssignmentStatementAst] -or
                    $node -is [Management.Automation.Language.CommandAst] -or
                    $node -is [Management.Automation.Language.FunctionDefinitionAst] -or
                    $node -is [Management.Automation.Language.TryStatementAst] -or
                    $node -is [Management.Automation.Language.ReturnStatementAst]
            }, $true))
    $assignments = [Collections.Generic.List[object]]::new()
    $commands = [Collections.Generic.List[object]]::new()
    $functions = [Collections.Generic.List[object]]::new()
    $tryStatements = [Collections.Generic.List[object]]::new()
    $returnStatements = [Collections.Generic.List[object]]::new()
    $conditionalNodeFlags = [Collections.Generic.Dictionary[Management.Automation.Language.Ast, bool]]::new()
    $inactiveNodeFlags = [Collections.Generic.Dictionary[Management.Automation.Language.Ast, bool]]::new()
    $commandsByName = [Collections.Hashtable]::new([StringComparer]::Ordinal)
    foreach ($node in $typedInventory) {
        if ($node -is [Management.Automation.Language.AssignmentStatementAst]) { $assignments.Add($node) }
        elseif ($node -is [Management.Automation.Language.CommandAst]) {
            $commands.Add($node)
            $commandName = [string]$node.GetCommandName()
            if (-not [string]::IsNullOrEmpty($commandName)) {
                if (-not $commandsByName.ContainsKey($commandName)) {
                    $commandsByName[$commandName] = [Collections.Generic.List[object]]::new()
                }
                $commandsByName[$commandName].Add($node)
            }
        }
        elseif ($node -is [Management.Automation.Language.FunctionDefinitionAst]) { $functions.Add($node) }
        elseif ($node -is [Management.Automation.Language.TryStatementAst]) { $tryStatements.Add($node) }
        elseif ($node -is [Management.Automation.Language.ReturnStatementAst]) {
            $returnStatements.Add($node)
            continue
        }
        $conditional = $false
        $inactive = $false
        $ancestor = $node.Parent
        while ($null -ne $ancestor) {
            if ($ancestor -is [Management.Automation.Language.IfStatementAst] -or
                $ancestor -is [Management.Automation.Language.LoopStatementAst] -or
                $ancestor -is [Management.Automation.Language.SwitchStatementAst] -or
                $ancestor -is [Management.Automation.Language.TernaryExpressionAst]) { $conditional = $true }
            if ($ancestor -is [Management.Automation.Language.FunctionDefinitionAst] -or
                $ancestor -is [Management.Automation.Language.ScriptBlockExpressionAst]) { $inactive = $true }
            if ($conditional -and $inactive) { break }
            $ancestor = $ancestor.Parent
        }
        $conditionalNodeFlags[$node] = $conditional
        $inactiveNodeFlags[$node] = $inactive
    }
    $exportFunctions = [Collections.Generic.List[object]]::new()
    foreach ($function in $functions) {
        if ([string]::Equals($function.Name, 'Export-Man527FailureDiagnostics', [StringComparison]::Ordinal) -and
            -not $conditionalNodeFlags[$function] -and -not $inactiveNodeFlags[$function]) {
            $exportFunctions.Add($function)
        }
    }
    $orderedCaptureCalls = [Collections.Generic.List[object]]::new()
    $topLevelTryStatements = [Collections.Generic.List[object]]::new()
    foreach ($tryStatement in $tryStatements) {
        if ($null -ne $tryStatement.Finally -and -not $inactiveNodeFlags[$tryStatement]) {
            $topLevelTryStatements.Add($tryStatement)
        }
    }
    foreach ($tryStatement in $topLevelTryStatements) {
        foreach ($catchClause in @($tryStatement.CatchClauses)) {
            $captureCandidates = if ($commandsByName.ContainsKey('Export-Man527FailureDiagnostics')) { @($commandsByName['Export-Man527FailureDiagnostics']) } else { @() }
            foreach ($captureCall in @($captureCandidates | Where-Object { & $hasAncestor $_ $catchClause.Body })) {
                if (-not $conditionalNodeFlags[$captureCall] -and
                    -not $inactiveNodeFlags[$captureCall]) {
                    [void]$orderedCaptureCalls.Add($captureCall)
                }
            }
        }
    }

    $getAssignmentVariableNames = {
        param([Management.Automation.Language.AssignmentStatementAst] $Assignment)

        $variableNodes = if ($Assignment.Left -is [Management.Automation.Language.VariableExpressionAst]) {
            @($Assignment.Left)
        }
        elseif ($Assignment.Left -is [Management.Automation.Language.ConvertExpressionAst] -and
            $Assignment.Left.Child -is [Management.Automation.Language.VariableExpressionAst]) {
            @($Assignment.Left.Child)
        }
        else {
            @($Assignment.Left.FindAll({ param($node) $node -is [Management.Automation.Language.VariableExpressionAst] }, $true))
        }
        foreach ($variableNode in $variableNodes) {
            Get-NervScriptVariableBindingName -VariablePath $variableNode.VariablePath
        }
    }
    $testAssignmentWritesVariable = {
        param(
            [Management.Automation.Language.AssignmentStatementAst] $Assignment,
            [string] $VariableName)

        foreach ($assignmentName in @(& $getAssignmentVariableNames $Assignment)) {
            if ([string]::Equals([string]$assignmentName, $VariableName, [StringComparison]::OrdinalIgnoreCase)) { return $true }
        }
        return $false
    }
    $contractVariableNames = @(
        'pickingLifecycleCompleted',
        'completionHttpReplayConverged',
        'completedOutboundOrder',
        'businessEvidence')
    $writesByVariable = [Collections.Hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($variableName in $contractVariableNames) {
        $writesByVariable[$variableName] = [Collections.Generic.HashSet[object]]::new()
    }
    foreach ($assignment in $assignments) {
        if ($inactiveNodeFlags[$assignment]) { continue }
        foreach ($assignmentName in @(& $getAssignmentVariableNames $assignment)) {
            if ($writesByVariable.ContainsKey([string]$assignmentName)) {
                [void]$writesByVariable[[string]$assignmentName].Add($assignment)
            }
        }
    }
    foreach ($command in $commands) {
        if ($inactiveNodeFlags[$command]) { continue }
        $commandName = [string]$command.GetCommandName()
        $binderName = Resolve-NervScriptVariableBinderCanonicalName -WrittenName $commandName
        if (-not [string]::IsNullOrEmpty($binderName)) {
            foreach ($boundName in @(Get-NervScriptVariableCommandLiteralBindingNames -Command $command)) {
                if ($writesByVariable.ContainsKey([string]$boundName)) {
                    [void]$writesByVariable[[string]$boundName].Add($command)
                }
            }
            continue
        }
        if ($script:nervScriptVariableSetItemCommands.Contains($commandName)) {
            foreach ($variableName in $contractVariableNames) {
                if (Test-NervScriptVariableSetItemCommandWritesName -Command $command -Name $variableName) {
                    [void]$writesByVariable[$variableName].Add($command)
                }
            }
        }
    }
    $testAssignment = {
        param([string] $CommandName, [string] $VariableName)
        $allWrites = @($writesByVariable[$VariableName])
        $matchingCommands = if ($commandsByName.ContainsKey($CommandName)) { @($commandsByName[$CommandName]) } else { @() }
        $matches = [Collections.Generic.List[object]]::new()
        foreach ($command in $matchingCommands) {
            $assignment = $command.Parent
            while ($null -ne $assignment -and $assignment -isnot [Management.Automation.Language.AssignmentStatementAst]) {
                if ($conditionalNodeFlags[$command]) { break }
                $assignment = $assignment.Parent
            }
            if ($null -ne $assignment -and
                $assignment -is [Management.Automation.Language.AssignmentStatementAst] -and
                (& $testAssignmentWritesVariable $assignment $VariableName) -and
                -not $conditionalNodeFlags[$command] -and
                -not $inactiveNodeFlags[$command]) {
                $matches.Add($command)
            }
        }
        return $allWrites.Count -eq 1 -and $matches.Count -eq 1
    }

    $testOutboundCompletion = {
        if (-not (& $testAssignment 'Wait-WmsOutboundOrder' 'completedOutboundOrder')) { return $false }
        $waitFunctions = @($functions | Where-Object {
                [string]::Equals($_.Name, 'Wait-WmsOutboundOrder', [StringComparison]::Ordinal) -and
                    -not $conditionalNodeFlags[$_] -and
                    -not $inactiveNodeFlags[$_]
                })
        if ($waitFunctions.Count -ne 1) { return $false }
        $completedPredicateCandidates = if ($commandsByName.ContainsKey('Test-NervAcceptanceWmsCompletedOutboundReadback')) { @($commandsByName['Test-NervAcceptanceWmsCompletedOutboundReadback']) } else { @() }
        $completedPredicates = @($completedPredicateCandidates | Where-Object { & $hasAncestor $_ $waitFunctions[0].Body })
        if ($completedPredicates.Count -ne 1) { return $false }
        $predicate = $completedPredicates[0]
        if ($predicate.CommandElements.Count -ne 3 -or
            $predicate.CommandElements[1] -isnot [Management.Automation.Language.CommandParameterAst] -or
            -not [string]::Equals([string]$predicate.CommandElements[1].ParameterName, 'Readback', [StringComparison]::OrdinalIgnoreCase) -or
            $null -ne $predicate.CommandElements[1].Argument -or
            -not [string]::Equals([string]$predicate.CommandElements[2].Extent.Text, '$rows[0]', [StringComparison]::Ordinal)) { return $false }
        $requireCompletedGate = $predicate.Parent
        while ($null -ne $requireCompletedGate -and $requireCompletedGate -isnot [Management.Automation.Language.BinaryExpressionAst]) { $requireCompletedGate = $requireCompletedGate.Parent }
        if ($null -eq $requireCompletedGate -or $requireCompletedGate.Operator -ne [Management.Automation.Language.TokenKind]::Or -or
            $requireCompletedGate.Left -isnot [Management.Automation.Language.UnaryExpressionAst] -or
            $requireCompletedGate.Left.TokenKind -ne [Management.Automation.Language.TokenKind]::Not -or
            $requireCompletedGate.Left.Child -isnot [Management.Automation.Language.VariableExpressionAst] -or
            -not [string]::Equals((Get-NervScriptVariableBindingName -VariablePath $requireCompletedGate.Left.Child.VariablePath), 'RequireCompleted', [StringComparison]::OrdinalIgnoreCase)) { return $false }
        $predicateIfAncestors = @()
        $predicateAncestor = $predicate.Parent
        while ($null -ne $predicateAncestor -and $predicateAncestor -ne $waitFunctions[0]) {
            if ($predicateAncestor -is [Management.Automation.Language.IfStatementAst]) { $predicateIfAncestors += $predicateAncestor }
            $predicateAncestor = $predicateAncestor.Parent
        }
        if ($predicateIfAncestors.Count -ne 1) { return $false }
        $guardCondition = $predicateIfAncestors[0].Clauses[0].Item1
        if ($guardCondition -isnot [Management.Automation.Language.PipelineAst] -or
            $guardCondition.PipelineElements.Count -ne 1 -or
            $guardCondition.PipelineElements[0] -isnot [Management.Automation.Language.CommandExpressionAst] -or
            $guardCondition.PipelineElements[0].Expression -isnot [Management.Automation.Language.BinaryExpressionAst] -or
            $guardCondition.PipelineElements[0].Expression.Operator -ne [Management.Automation.Language.TokenKind]::And) { return $false }
        $combinedGate = $guardCondition.PipelineElements[0].Expression
        $rowsGate = $combinedGate.Left
        if ($rowsGate -isnot [Management.Automation.Language.BinaryExpressionAst] -or
            $rowsGate.Operator -ne [Management.Automation.Language.TokenKind]::Ieq -or
            $rowsGate.Left -isnot [Management.Automation.Language.MemberExpressionAst] -or
            $rowsGate.Left.Expression -isnot [Management.Automation.Language.VariableExpressionAst] -or
            -not [string]::Equals((Get-NervScriptVariableBindingName -VariablePath $rowsGate.Left.Expression.VariablePath), 'rows', [StringComparison]::OrdinalIgnoreCase) -or
            $rowsGate.Left.Member -isnot [Management.Automation.Language.StringConstantExpressionAst] -or
            -not [string]::Equals([string]$rowsGate.Left.Member.Value, 'Count', [StringComparison]::OrdinalIgnoreCase) -or
            $rowsGate.Right -isnot [Management.Automation.Language.ConstantExpressionAst] -or
            $rowsGate.Right.Value -ne 1 -or
            $combinedGate.Right -isnot [Management.Automation.Language.ParenExpressionAst] -or
            $combinedGate.Right.Pipeline.PipelineElements.Count -ne 1 -or
            $combinedGate.Right.Pipeline.PipelineElements[0] -isnot [Management.Automation.Language.CommandExpressionAst] -or
            -not [object]::ReferenceEquals($combinedGate.Right.Pipeline.PipelineElements[0].Expression, $requireCompletedGate)) { return $false }
        $guardReturns = @($returnStatements | Where-Object {
                $guardReturn = $_
                @($predicateIfAncestors[0].Clauses | Where-Object { & $hasAncestor $guardReturn $_.Item2 }).Count -gt 0 -and
                $null -ne $guardReturn.Pipeline -and
                    [string]::Equals([string]$guardReturn.Pipeline.Extent.Text, '$rows[0]', [StringComparison]::Ordinal)
            })
        if ($predicateIfAncestors[0].Clauses.Count -ne 1 -or $guardReturns.Count -ne 1) { return $false }

        $waitCallCandidates = if ($commandsByName.ContainsKey('Wait-WmsOutboundOrder')) { @($commandsByName['Wait-WmsOutboundOrder']) } else { @() }
        $waitCalls = @($waitCallCandidates | Where-Object {
                    $command = $_
                    $assignment = $command.Parent
                    while ($null -ne $assignment -and $assignment -isnot [Management.Automation.Language.AssignmentStatementAst]) { $assignment = $assignment.Parent }
                    $null -ne $assignment -and
                        (& $testAssignmentWritesVariable $assignment 'completedOutboundOrder') -and
                        -not $conditionalNodeFlags[$command] -and
                        -not $inactiveNodeFlags[$command]
                })
        if ($waitCalls.Count -ne 1) { return $false }
        $requireCompletedParameters = @($waitCalls[0].CommandElements | Where-Object {
                $_ -is [Management.Automation.Language.CommandParameterAst] -and
                    [string]::Equals([string]$_.ParameterName, 'RequireCompleted', [StringComparison]::OrdinalIgnoreCase) -and
                    $null -eq $_.Argument
            })
        if ($requireCompletedParameters.Count -ne 1) { return $false }

        $businessEvidenceWrites = @($writesByVariable['businessEvidence'])
        $businessEvidenceAssignments = @($businessEvidenceWrites | Where-Object {
                $_ -is [Management.Automation.Language.AssignmentStatementAst] -and
                    -not $conditionalNodeFlags[$_] -and
                    $_.Right -is [Management.Automation.Language.CommandExpressionAst] -and
                    $_.Right.Expression -is [Management.Automation.Language.ConvertExpressionAst] -and
                    $_.Right.Expression.Child -is [Management.Automation.Language.HashtableAst]
            })
        if ($businessEvidenceWrites.Count -ne 1 -or $businessEvidenceAssignments.Count -ne 1 -or
            -not [object]::ReferenceEquals($businessEvidenceWrites[0], $businessEvidenceAssignments[0])) { return $false }
        $businessEvidenceTable = $businessEvidenceAssignments[0].Right.Expression.Child
        $wmsPairs = @($businessEvidenceTable.KeyValuePairs | Where-Object {
                $_.Item1 -is [Management.Automation.Language.StringConstantExpressionAst] -and
                    [string]::Equals([string]$_.Item1.Value, 'wmsOutboundOrder', [StringComparison]::Ordinal)
            })
        if ($wmsPairs.Count -ne 1 -or
            $wmsPairs[0].Item2 -isnot [Management.Automation.Language.PipelineAst] -or
            $wmsPairs[0].Item2.PipelineElements.Count -ne 1 -or
            $wmsPairs[0].Item2.PipelineElements[0] -isnot [Management.Automation.Language.CommandExpressionAst] -or
            $wmsPairs[0].Item2.PipelineElements[0].Expression -isnot [Management.Automation.Language.ConvertExpressionAst] -or
            $wmsPairs[0].Item2.PipelineElements[0].Expression.Child -isnot [Management.Automation.Language.HashtableAst]) { return $false }
        $wmsTable = $wmsPairs[0].Item2.PipelineElements[0].Expression.Child
        $readbackPairs = @($wmsTable.KeyValuePairs | Where-Object {
                $_.Item1 -is [Management.Automation.Language.StringConstantExpressionAst] -and
                    [string]::Equals([string]$_.Item1.Value, 'completionReadback', [StringComparison]::Ordinal)
            })
        if ($readbackPairs.Count -ne 1 -or
            $readbackPairs[0].Item2 -isnot [Management.Automation.Language.PipelineAst] -or
            $readbackPairs[0].Item2.PipelineElements.Count -ne 1 -or
            $readbackPairs[0].Item2.PipelineElements[0] -isnot [Management.Automation.Language.CommandExpressionAst] -or
            $readbackPairs[0].Item2.PipelineElements[0].Expression -isnot [Management.Automation.Language.ConvertExpressionAst] -or
            $readbackPairs[0].Item2.PipelineElements[0].Expression.Child -isnot [Management.Automation.Language.HashtableAst]) { return $false }
        $readbackTable = $readbackPairs[0].Item2.PipelineElements[0].Expression.Child
        foreach ($field in @('status', 'completedAtUtc')) {
            $fieldPairs = @($readbackTable.KeyValuePairs | Where-Object {
                    $_.Item1 -is [Management.Automation.Language.StringConstantExpressionAst] -and
                        [string]::Equals([string]$_.Item1.Value, $field, [StringComparison]::Ordinal)
                })
            if ($fieldPairs.Count -ne 1 -or
                $fieldPairs[0].Item2 -isnot [Management.Automation.Language.PipelineAst] -or
                $fieldPairs[0].Item2.PipelineElements.Count -ne 1 -or
                $fieldPairs[0].Item2.PipelineElements[0] -isnot [Management.Automation.Language.CommandExpressionAst]) { return $false }
            $memberRead = $fieldPairs[0].Item2.PipelineElements[0].Expression
            if ($memberRead -isnot [Management.Automation.Language.MemberExpressionAst] -or
                $memberRead.Expression -isnot [Management.Automation.Language.VariableExpressionAst] -or
                -not [string]::Equals((Get-NervScriptVariableBindingName -VariablePath $memberRead.Expression.VariablePath), 'completedOutboundOrder', [StringComparison]::OrdinalIgnoreCase) -or
                $memberRead.Member -isnot [Management.Automation.Language.StringConstantExpressionAst] -or
                -not [string]::Equals([string]$memberRead.Member.Value, $field, [StringComparison]::OrdinalIgnoreCase)) { return $false }
        }
        return $true
    }

    return [pscustomobject][ordered]@{
        failureCaptureSupported = $exportFunctions.Count -eq 1 -and $orderedCaptureCalls.Count -eq 1
        pickingReadbackWired = & $testAssignment 'Test-NervAcceptanceWmsPickingReadbacks' 'pickingLifecycleCompleted'
        completionReplayWired = & $testAssignment 'Test-NervAcceptanceWmsCompletionReplay' 'completionHttpReplayConverged'
        outboundCompletionWired = & $testOutboundCompletion
    }
}

function New-NervAcceptanceWmsDeliveryCanonicalResult {
    param(
        [Parameter(Mandatory)] [object] $Provenance,
        [Parameter(Mandatory)] [string] $Track,
        [Parameter(Mandatory)] [object] $BusinessEvidence,
        [Parameter(Mandatory)] [object] $TestCounters,
        [Parameter(Mandatory)] [object] $CleanupEvidence,
        [Parameter(Mandatory)] [object] $DiagnosticEvidence,
        [Parameter(Mandatory)] [object] $Volatile
    )

    Assert-NervAcceptanceObjectSchema -Object $Provenance -AllowedFields @('repository', 'runId', 'runAttempt', 'testedSha', 'manifestDigest', 'scenarioId') -RequiredFields @('repository', 'runId', 'runAttempt', 'testedSha', 'manifestDigest', 'scenarioId') -Context 'MAN-527 canonical provenance'
    if (-not (Test-NervAcceptanceRepositoryIdentifier -Repository ([string]$Provenance.repository))) { throw 'MAN-527 canonical repository must be a canonical owner/name identifier.' }
    if ([string]$Provenance.runId -cnotmatch '^[1-9][0-9]*$') { throw 'MAN-527 canonical runId must be a positive decimal identifier.' }
    if (-not (Test-NervAcceptanceInteger -Value $Provenance.runAttempt) -or [int64]$Provenance.runAttempt -le 0) { throw 'MAN-527 canonical runAttempt must be positive.' }
    if ([string]$Provenance.testedSha -cnotmatch '^[0-9a-f]{40}$') { throw 'MAN-527 canonical testedSha must be a lowercase 40-character Git SHA.' }
    if ([string]$Provenance.manifestDigest -cnotmatch '^[0-9a-f]{64}$') { throw 'MAN-527 canonical manifestDigest must be a lowercase SHA-256 digest.' }
    if (-not [string]::Equals([string]$Provenance.scenarioId, 'wms-delivery-erp', [StringComparison]::Ordinal)) { throw "MAN-527 canonical scenarioId must be 'wms-delivery-erp'." }
    if ($Track -cnotmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$') { throw 'MAN-527 canonical track identifier must be canonical.' }

    Assert-NervAcceptanceObjectSchema -Object $BusinessEvidence -AllowedFields @('verifiedAtUtc', 'scenarioStatus', 'deliveryOrderNo', 'transport', 'persistence', 'wmsOutboundOrder', 'erpDelivery', 'accountReceivable', 'repeatedEvent', 'repeatedEventConverged', 'cleanup', 'diagnostics') -RequiredFields @('scenarioStatus', 'deliveryOrderNo', 'wmsOutboundOrder', 'erpDelivery', 'accountReceivable', 'repeatedEvent', 'repeatedEventConverged') -Context 'MAN-527 business evidence'
    if (-not [string]::Equals([string]$BusinessEvidence.scenarioStatus, 'passed', [StringComparison]::Ordinal)) { throw 'MAN-527 canonical success requires passed business evidence.' }
    Assert-NervAcceptanceObjectSchema -Object $TestCounters -AllowedFields @('total', 'executed', 'passed', 'failed', 'skipped') -RequiredFields @('total', 'executed', 'passed', 'failed', 'skipped') -Context 'MAN-527 canonical TRX counters'
    foreach ($name in @('total', 'executed', 'passed', 'failed', 'skipped')) { [void](Assert-NervAcceptanceRuntimeIntegerField -Object $TestCounters -Name $name -Context 'MAN-527 canonical TRX counters') }
    if ([int64]$TestCounters.total -ne 1 -or [int64]$TestCounters.executed -ne 1 -or [int64]$TestCounters.passed -ne 1 -or [int64]$TestCounters.failed -ne 0 -or [int64]$TestCounters.skipped -ne 0) {
        throw 'MAN-527 canonical success requires exact TRX counts expected=1 discovered=1 passed=1 failed=0 skipped=0.'
    }
    Assert-NervAcceptanceObjectSchema -Object $CleanupEvidence -AllowedFields @('managedProcessIds', 'managedProcessRemaining', 'databaseName', 'exactDatabaseRemaining', 'postgres', 'redis', 'errors') -RequiredFields @('managedProcessRemaining', 'exactDatabaseRemaining', 'postgres', 'redis', 'errors') -Context 'MAN-527 canonical cleanup evidence'
    foreach ($name in @('managedProcessRemaining', 'exactDatabaseRemaining')) { [void](Assert-NervAcceptanceRuntimeIntegerField -Object $CleanupEvidence -Name $name -Context 'MAN-527 canonical cleanup evidence') }
    if ($CleanupEvidence.errors -isnot [array]) { throw 'MAN-527 canonical cleanup errors must be an array.' }
    $cleanupErrors = @($CleanupEvidence.errors)
    $pendingOwnedResources = 0
    foreach ($provider in @('postgres', 'redis')) {
        if ([string]::Equals([string]$CleanupEvidence.PSObject.Properties[$provider].Value, 'owned-pending-cleanup', [StringComparison]::Ordinal)) {
            $pendingOwnedResources++
        }
    }
    $ownedResourcesRemaining = [int64]$CleanupEvidence.managedProcessRemaining + [int64]$CleanupEvidence.exactDatabaseRemaining + $pendingOwnedResources
    $cleanupErrorCodes = @($cleanupErrors | ForEach-Object {
        $separatorIndex = ([string]$_).IndexOf(':', [StringComparison]::Ordinal)
        if ($separatorIndex -gt 0) { ([string]$_).Substring(0, $separatorIndex) } else { 'cleanup-error' }
    })
    if ($ownedResourcesRemaining -ne 0 -or $cleanupErrors.Count -ne 0) {
        throw 'MAN-527 canonical success requires zero cleanup remaining counts and no pending owned resources.'
    }

    $outboundAssigned = -not [string]::IsNullOrWhiteSpace([string]$BusinessEvidence.wmsOutboundOrder.firstAssignment.poolCode) -and -not [string]::IsNullOrWhiteSpace([string]$BusinessEvidence.wmsOutboundOrder.firstAssignment.operatorPrincipalId)
    if ($BusinessEvidence.wmsOutboundOrder.pickingLifecycleCompleted -isnot [bool] -or $BusinessEvidence.wmsOutboundOrder.completionHttpReplayConverged -isnot [bool] -or $BusinessEvidence.repeatedEventConverged -isnot [bool]) {
        throw 'MAN-527 canonical business checkpoint flags must be JSON booleans.'
    }
    $pickingLifecycleCompleted = [bool]$BusinessEvidence.wmsOutboundOrder.pickingLifecycleCompleted
    $outboundCompleted = Test-NervAcceptanceWmsCompletedOutboundReadback -Readback $BusinessEvidence.wmsOutboundOrder.completionReadback
    $deliveryCompleted = [string]::Equals([string]$BusinessEvidence.erpDelivery.status, 'completed', [StringComparison]::OrdinalIgnoreCase) -and [decimal]$BusinessEvidence.erpDelivery.shippedQuantity -eq 2 -and -not [string]::IsNullOrWhiteSpace([string]$BusinessEvidence.erpDelivery.shippedAtUtc) -and -not [string]::IsNullOrWhiteSpace([string]$BusinessEvidence.erpDelivery.completedAtUtc)
    $receivableCreated = -not [string]::IsNullOrWhiteSpace([string]$BusinessEvidence.accountReceivable.receivableNo) -and [string]::Equals([string]$BusinessEvidence.accountReceivable.sourceDocumentNo, [string]$BusinessEvidence.deliveryOrderNo, [StringComparison]::Ordinal)
    $completionReplayConverged = [bool]$BusinessEvidence.wmsOutboundOrder.completionHttpReplayConverged
    $repeatedEventConverged = [bool]$BusinessEvidence.repeatedEventConverged
    if (-not $outboundAssigned -or -not $pickingLifecycleCompleted -or -not $outboundCompleted -or -not $deliveryCompleted -or -not $receivableCreated -or -not $completionReplayConverged -or -not $repeatedEventConverged) { throw 'MAN-527 canonical success requires every business checkpoint to have converged.' }

    Assert-NervAcceptanceObjectSchema -Object $DiagnosticEvidence -AllowedFields @('failureCaptureSupported', 'failureDiagnosticsCaptured', 'secretsRedacted', 'artifactPaths', 'errors') -RequiredFields @('failureCaptureSupported', 'failureDiagnosticsCaptured', 'secretsRedacted', 'artifactPaths', 'errors') -Context 'MAN-527 canonical diagnostic evidence'
    foreach ($name in @('failureCaptureSupported', 'failureDiagnosticsCaptured', 'secretsRedacted')) {
        if ($DiagnosticEvidence.PSObject.Properties[$name].Value -isnot [bool]) { throw "MAN-527 canonical diagnostic evidence $name must be a JSON boolean." }
    }
    if (-not [bool]$DiagnosticEvidence.failureCaptureSupported) { throw 'MAN-527 canonical success requires diagnostic failure capture support.' }
    if ([bool]$DiagnosticEvidence.failureDiagnosticsCaptured) { throw 'MAN-527 canonical success must not claim failure diagnostics were captured.' }
    if (-not [bool]$DiagnosticEvidence.secretsRedacted) { throw 'MAN-527 canonical diagnostic secrets must be redacted.' }
    if ($DiagnosticEvidence.artifactPaths -isnot [array] -or @($DiagnosticEvidence.artifactPaths).Count -ne 0) { throw 'MAN-527 canonical success must not retain failure diagnostic artifacts.' }
    if ($DiagnosticEvidence.errors -isnot [array] -or @($DiagnosticEvidence.errors).Count -ne 0) { throw 'MAN-527 canonical diagnostic capture errors must be empty.' }

    Assert-NervAcceptanceObjectSchema -Object $Volatile -AllowedFields @('databaseName', 'processIds', 'capSuffix', 'startedAtUtc', 'completedAtUtc', 'ports', 'paths') -RequiredFields @('databaseName', 'processIds', 'capSuffix', 'startedAtUtc', 'completedAtUtc', 'ports', 'paths') -Context 'MAN-527 canonical volatile evidence'
    return [pscustomobject][ordered]@{
        schemaVersion = 1
        provenance = $Provenance
        track = $Track
        conclusion = 'passed'
        test = [pscustomobject][ordered]@{ identity = 'Nerv.IIP.Business.FullChain.Tests.ErpWmsDeliveryCompletionPostgresRedisAcceptanceTests.External_process_replays_completed_wms_event_without_duplicate_delivery_or_receivable_facts'; expected = 1; discovered = [int]$TestCounters.total; passed = [int]$TestCounters.passed; failed = [int]$TestCounters.failed; skipped = [int]$TestCounters.skipped }
        businessFacts = [pscustomobject][ordered]@{ outboundAssigned = $outboundAssigned; pickingLifecycleCompleted = $pickingLifecycleCompleted; outboundCompleted = $outboundCompleted; deliveryCompleted = $deliveryCompleted; receivableCreated = $receivableCreated; completionReplayConverged = $completionReplayConverged; repeatedEventConverged = $repeatedEventConverged }
        diagnostics = [pscustomobject][ordered]@{ schemas = @('erp', 'inventory', 'wms'); failureCaptureSupported = [bool]$DiagnosticEvidence.failureCaptureSupported; failureDiagnosticsCaptured = [bool]$DiagnosticEvidence.failureDiagnosticsCaptured; secretsRedacted = [bool]$DiagnosticEvidence.secretsRedacted }
        cleanup = [pscustomobject][ordered]@{ managedProcessesRemaining = [int]$CleanupEvidence.managedProcessRemaining; disposableDatabasesRemaining = [int]$CleanupEvidence.exactDatabaseRemaining; ownedResourcesRemaining = [int]$ownedResourcesRemaining; errorCodes = @($cleanupErrorCodes) }
        volatile = [pscustomobject][ordered]@{ databaseName = [string]$Volatile.databaseName; processIds = @($Volatile.processIds); capSuffix = [string]$Volatile.capSuffix; startedAtUtc = [string]$Volatile.startedAtUtc; completedAtUtc = [string]$Volatile.completedAtUtc; cleanupErrors = @($cleanupErrors); ports = $Volatile.ports; paths = $Volatile.paths }
    }
}

function New-NervAcceptanceScenarioEquivalenceVector {
    param(
        [Parameter(Mandatory)] [object] $Result,
        [Parameter(Mandatory)] [object] $ValidatedScenario,
        [Parameter(Mandatory)] [object] $ExpectedProvenance
    )

    $adapter = Get-NervAcceptanceRuntimeScenarioAdapter -ScenarioId ([string]$ExpectedProvenance.scenarioId)
    $scenario = Get-NervAcceptanceRuntimeScenario -Manifest ([pscustomobject]@{ scenarios = @($ValidatedScenario) }) -ScenarioId ([string]$adapter.scenarioId)
    $expectedIdentity = [string]$scenario.testProjects[0].frozenTestIdentities[0]
    Assert-NervAcceptanceStringArray -Value $scenario.diagnosticProtocol.schemas -Context 'validated runtime scenario diagnostic schemas'
    $expectedSchemas = [string[]]@($scenario.diagnosticProtocol.schemas)
    [Array]::Sort($expectedSchemas, [StringComparer]::Ordinal)

    Assert-NervAcceptanceObjectSchema -Object $Result `
        -AllowedFields @('schemaVersion', 'provenance', 'track', 'conclusion', 'test', 'businessFacts', 'diagnostics', 'cleanup', 'volatile') `
        -RequiredFields @('schemaVersion', 'provenance', 'track', 'conclusion', 'test', 'businessFacts', 'diagnostics', 'cleanup', 'volatile') `
        -Context 'runtime equivalence result'
    if (-not (Test-NervAcceptanceInteger -Value $Result.schemaVersion) -or [int64]$Result.schemaVersion -ne 1) { throw 'Runtime equivalence result schemaVersion must be 1.' }
    Assert-NervAcceptanceObjectSchema -Object $Result.provenance `
        -AllowedFields @('repository', 'runId', 'runAttempt', 'testedSha', 'manifestDigest', 'scenarioId') `
        -RequiredFields @('repository', 'runId', 'runAttempt', 'testedSha', 'manifestDigest', 'scenarioId') `
        -Context 'runtime equivalence provenance'
    foreach ($name in @('repository', 'runId', 'testedSha', 'manifestDigest', 'scenarioId')) {
        Assert-NervAcceptanceString -Value $Result.provenance.PSObject.Properties[$name].Value -Context "runtime equivalence provenance $name"
        if (-not [string]::Equals([string]$Result.provenance.PSObject.Properties[$name].Value, [string]$ExpectedProvenance.PSObject.Properties[$name].Value, [StringComparison]::Ordinal)) {
            throw "Runtime equivalence provenance $name must match the validated runtime input."
        }
    }
    if (-not (Test-NervAcceptanceInteger -Value $Result.provenance.runAttempt) -or [int64]$Result.provenance.runAttempt -ne [int64]$ExpectedProvenance.runAttempt) {
        throw 'Runtime equivalence provenance runAttempt must match the validated runtime input.'
    }
    if ($Result.track -isnot [string] -or [string]$Result.track -cnotmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$') {
        throw 'Runtime equivalence track must be a canonical identifier.'
    }
    if ($Result.conclusion -isnot [string] -or
        -not [Collections.Generic.HashSet[string]]::new([string[]]@('passed', 'failed'), [StringComparer]::Ordinal).Contains([string]$Result.conclusion)) {
        throw "Runtime equivalence conclusion must be one of 'passed' or 'failed'."
    }

    Assert-NervAcceptanceObjectSchema -Object $Result.test `
        -AllowedFields @('identity', 'expected', 'discovered', 'passed', 'failed', 'skipped') `
        -RequiredFields @('identity', 'expected', 'discovered', 'passed', 'failed', 'skipped') `
        -Context 'runtime equivalence test'
    Assert-NervAcceptanceString -Value $Result.test.identity -Context 'runtime equivalence test identity'
    if (-not [string]::Equals([string]$Result.test.identity, $expectedIdentity, [StringComparison]::Ordinal)) {
        throw "Runtime equivalence test identity must equal the validated scenario frozen identity."
    }
    $testCounts = [ordered]@{}
    foreach ($name in @('expected', 'discovered', 'passed', 'failed', 'skipped')) {
        $testCounts[$name] = Assert-NervAcceptanceRuntimeIntegerField -Object $Result.test -Name $name -Context 'runtime equivalence test'
    }

    $businessFactFields = [string[]]@($adapter.businessFactFields)
    Assert-NervAcceptanceObjectSchema -Object $Result.businessFacts -AllowedFields $businessFactFields -RequiredFields $businessFactFields -Context 'runtime equivalence business facts'
    $businessFacts = [ordered]@{}
    foreach ($name in $businessFactFields) {
        Assert-NervAcceptanceBoolean -Value $Result.businessFacts.PSObject.Properties[$name].Value -Context "runtime equivalence business fact '$name'"
        $businessFacts[$name] = [bool]$Result.businessFacts.PSObject.Properties[$name].Value
    }

    Assert-NervAcceptanceObjectSchema -Object $Result.diagnostics `
        -AllowedFields @('schemas', 'failureCaptureSupported', 'failureDiagnosticsCaptured', 'secretsRedacted') `
        -RequiredFields @('schemas', 'failureCaptureSupported', 'failureDiagnosticsCaptured', 'secretsRedacted') `
        -Context 'runtime equivalence diagnostics'
    Assert-NervAcceptanceStringArray -Value $Result.diagnostics.schemas -Context 'runtime equivalence diagnostic schemas'
    $schemas = [string[]]@($Result.diagnostics.schemas)
    [Array]::Sort($schemas, [StringComparer]::Ordinal)
    if (-not (Test-NervAcceptanceOrdinalSequenceEqual -Left $schemas -Right $expectedSchemas)) {
        throw 'Runtime equivalence diagnostic schemas must exactly equal the validated scenario diagnostic schema set.'
    }
    foreach ($name in @('failureCaptureSupported', 'failureDiagnosticsCaptured', 'secretsRedacted')) {
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
    $allowedCleanupErrorCodes = [Collections.Generic.HashSet[string]]::new(
        [string[]]@(
            'action-failed',
            'dependency-readiness-failed',
            'discovery-failed',
            'test-failed',
            'diagnostics-failed',
            'managed-process-cleanup-failed',
            'disposable-database-cleanup-failed',
            'owned-resource-cleanup-failed',
            'cleanup-verification-failed',
            'evidence-write-failed'
        ),
        [StringComparer]::Ordinal)
    foreach ($errorCode in $cleanupErrorCodes) {
        if ($errorCode -cnotmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$') {
            throw "Runtime equivalence cleanup errorCode '$errorCode' must be canonical."
        }
        if (-not $allowedCleanupErrorCodes.Contains($errorCode)) {
            throw "Runtime equivalence cleanup errorCode '$errorCode' is not allowed by schemaVersion 1."
        }
    }
    [Array]::Sort($cleanupErrorCodes, [StringComparer]::Ordinal)

    Assert-NervAcceptanceObjectSchema -Object $Result.volatile `
        -AllowedFields @('databaseName', 'processIds', 'capSuffix', 'startedAtUtc', 'completedAtUtc', 'cleanupErrors', 'ports', 'paths') `
        -RequiredFields @('databaseName', 'processIds', 'capSuffix', 'startedAtUtc', 'completedAtUtc', 'cleanupErrors', 'ports', 'paths') `
        -Context 'runtime equivalence volatile fields'
    Assert-NervAcceptanceStringArray -Value $Result.volatile.cleanupErrors -Context 'runtime equivalence volatile cleanupErrors' -AllowEmpty
    foreach ($name in @('databaseName', 'capSuffix', 'startedAtUtc', 'completedAtUtc')) {
        Assert-NervAcceptanceString -Value $Result.volatile.PSObject.Properties[$name].Value -Context "runtime equivalence volatile $name"
    }
    if ($Result.volatile.processIds -isnot [array]) {
        throw 'Runtime equivalence volatile processIds must be an array.'
    }
    $processIds = [Collections.Generic.HashSet[int64]]::new()
    foreach ($processId in @($Result.volatile.processIds)) {
        if (-not (Test-NervAcceptanceInteger -Value $processId) -or [int64]$processId -lt 0) {
            throw 'Runtime equivalence volatile processIds must contain only non-negative JSON integers.'
        }
        if (-not $processIds.Add([int64]$processId)) {
            throw 'Runtime equivalence volatile processIds must contain unique integer values.'
        }
    }
    $portFields = [string[]]@($adapter.portFields)
    Assert-NervAcceptanceObjectSchema -Object $Result.volatile.ports -AllowedFields $portFields -RequiredFields $portFields -Context 'runtime equivalence volatile ports'
    foreach ($name in $portFields) {
        $port = Assert-NervAcceptanceRuntimeIntegerField -Object $Result.volatile.ports -Name $name -Context 'runtime equivalence volatile ports'
        if ($port -le 0 -or $port -gt 65535) { throw "Runtime equivalence volatile port $name must be between 1 and 65535." }
    }
    Assert-NervAcceptanceObjectSchema -Object $Result.volatile.paths -AllowedFields @('businessEvidence', 'probeTrx', 'cleanupEvidence', 'canonicalResult') -RequiredFields @('businessEvidence', 'probeTrx', 'cleanupEvidence', 'canonicalResult') -Context 'runtime equivalence volatile paths'
    foreach ($name in @('businessEvidence', 'probeTrx', 'cleanupEvidence', 'canonicalResult')) {
        Assert-NervAcceptanceString -Value $Result.volatile.paths.PSObject.Properties[$name].Value -Context "runtime equivalence volatile path $name"
    }

    return [pscustomobject][ordered]@{
        schemaVersion = 1
        provenance = [pscustomobject][ordered]@{
            repository = [string]$Result.provenance.repository
            runId = [string]$Result.provenance.runId
            runAttempt = [int64]$Result.provenance.runAttempt
            testedSha = [string]$Result.provenance.testedSha
            manifestDigest = [string]$Result.provenance.manifestDigest
            scenarioId = [string]$Result.provenance.scenarioId
        }
        conclusion = [string]$Result.conclusion
        test = [pscustomobject][ordered]@{
            identity = [string]$Result.test.identity
            expected = $testCounts.expected
            discovered = $testCounts.discovered
            passed = $testCounts.passed
            failed = $testCounts.failed
            skipped = $testCounts.skipped
        }
        businessFacts = [pscustomobject]$businessFacts
        diagnostics = [pscustomobject][ordered]@{
            schemas = @($schemas)
            failureCaptureSupported = [bool]$Result.diagnostics.failureCaptureSupported
            failureDiagnosticsCaptured = [bool]$Result.diagnostics.failureDiagnosticsCaptured
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

function New-NervAcceptanceScenarioRuntimeValidationException {
    param(
        [Parameter(Mandatory)] [string] $Classification,
        [Parameter(Mandatory)] [string] $Message
    )

    $exception = [InvalidOperationException]::new($Message)
    $exception.Data['NervAcceptanceFailureClassification'] = $Classification
    return $exception
}

function New-NervAcceptanceScenarioRuntimeResultSnapshot {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Results,
        [Parameter(Mandatory)] [object] $ValidatedScenario,
        [Parameter(Mandatory)] [object] $ExpectedProvenance
    )

    $observedResults = @($Results)
    if ($observedResults.Count -ne 1) {
        throw "Acceptance scenario runtime action must produce exactly one result; observed $($observedResults.Count)."
    }
    return New-NervAcceptanceScenarioEquivalenceVector -Result $observedResults[0] -ValidatedScenario $ValidatedScenario -ExpectedProvenance $ExpectedProvenance
}

function Assert-NervAcceptanceScenarioRuntimeResult {
    param([Parameter(Mandatory)] [object] $ResultSnapshot)

    foreach ($name in @('managedProcessesRemaining', 'disposableDatabasesRemaining', 'ownedResourcesRemaining')) {
        $observed = [int64]$ResultSnapshot.cleanup.PSObject.Properties[$name].Value
        if ($observed -ne 0) {
            throw (New-NervAcceptanceScenarioRuntimeValidationException -Classification 'cleanup-failed' -Message "Runtime equivalence cleanup $name must be 0; observed $observed.")
        }
    }
    if (@($ResultSnapshot.cleanup.errorCodes).Count -ne 0) {
        throw (New-NervAcceptanceScenarioRuntimeValidationException -Classification 'cleanup-failed' -Message 'Runtime equivalence cleanup errorCodes must be empty.')
    }
    if (-not [string]::Equals([string]$ResultSnapshot.conclusion, 'passed', [StringComparison]::Ordinal)) {
        throw (New-NervAcceptanceScenarioRuntimeValidationException -Classification 'scenario-failed' -Message "Runtime equivalence conclusion must be 'passed'.")
    }
    $requiredTestCounts = [ordered]@{
        expected = 1L
        discovered = 1L
        passed = 1L
        failed = 0L
        skipped = 0L
    }
    foreach ($name in $requiredTestCounts.Keys) {
        $observed = [int64]$ResultSnapshot.test.PSObject.Properties[$name].Value
        $required = [int64]$requiredTestCounts[$name]
        if ($observed -ne $required) {
            throw (New-NervAcceptanceScenarioRuntimeValidationException -Classification 'test-evidence-failed' -Message "Runtime equivalence test $name must be $required; observed $observed.")
        }
    }
    $adapter = Get-NervAcceptanceRuntimeScenarioAdapter -ScenarioId ([string]$ResultSnapshot.provenance.scenarioId)
    foreach ($name in [string[]]@($adapter.businessFactFields)) {
        if (-not [bool]$ResultSnapshot.businessFacts.PSObject.Properties[$name].Value) {
            throw (New-NervAcceptanceScenarioRuntimeValidationException -Classification 'checkpoint-failed' -Message "Runtime equivalence business fact '$name' must be true.")
        }
    }
    foreach ($name in @('failureCaptureSupported', 'secretsRedacted')) {
        if (-not [bool]$ResultSnapshot.diagnostics.PSObject.Properties[$name].Value) {
            throw (New-NervAcceptanceScenarioRuntimeValidationException -Classification 'diagnostics-failed' -Message "Runtime equivalence diagnostic '$name' must be true.")
        }
    }
    if ([bool]$ResultSnapshot.diagnostics.failureDiagnosticsCaptured) {
        throw (New-NervAcceptanceScenarioRuntimeValidationException -Classification 'diagnostics-failed' -Message "Runtime equivalence diagnostic 'failureDiagnosticsCaptured' must be false on success.")
    }
    return $ResultSnapshot
}
