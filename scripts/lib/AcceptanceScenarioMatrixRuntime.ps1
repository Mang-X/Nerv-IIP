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
    if (-not [string]::Equals($commandName, 'pwsh', [StringComparison]::Ordinal)) {
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
    if ([string]::Equals($Event, 'push', [StringComparison]::Ordinal)) {
        if (-not [string]::Equals($selectionMode, 'main-active-core', [StringComparison]::Ordinal) -or
            -not (Test-NervAcceptanceOrdinalSequenceEqual -Left $selectionReasons -Right ([string[]]@('main'))) -or
            -not (Test-NervAcceptanceOrdinalSequenceEqual -Left ([string[]]@($selectedScenarios | ForEach-Object { [string]$_.id })) -Right ([string[]]@($activeCore | ForEach-Object { [string]$_.id })))) {
            throw 'Runtime push planning artifact must preserve the main active/core selection provenance.'
        }
    }
    elseif ([string]::Equals($Event, 'schedule', [StringComparison]::Ordinal)) {
        if (-not [string]::Equals($selectionMode, 'nightly-active', [StringComparison]::Ordinal) -or
            -not (Test-NervAcceptanceOrdinalSequenceEqual -Left $selectionReasons -Right ([string[]]@('nightly'))) -or
            -not (Test-NervAcceptanceOrdinalSequenceEqual -Left ([string[]]@($selectedScenarios | ForEach-Object { [string]$_.id })) -Right ([string[]]@($activeCore | ForEach-Object { [string]$_.id })))) {
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
                -not (Test-NervAcceptanceOrdinalSequenceEqual -Left ([string[]]@($selectedScenarios | ForEach-Object { [string]$_.id })) -Right ([string[]]@($activeCore | ForEach-Object { [string]$_.id })))) {
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
            -not (Test-NervAcceptanceOrdinalSequenceEqual -Left ([string[]]@($selectedScenarios | ForEach-Object { [string]$_.id })) -Right ([string[]]@($activeCore | ForEach-Object { [string]$_.id })))) {
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
        [Parameter(Mandatory)] [string] $ManifestPath,
        [Parameter(Mandatory)] [string] $Event,
        [Parameter(Mandatory)] [string] $WorkflowPath,
        [Parameter(Mandatory)] [string] $WorkflowJobName,
        [Parameter(Mandatory)] [string] $WorkflowStepName,
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
        -RunAttempt $RunAttempt `
        -ManifestPath $ManifestPath `
        -ManifestDigest $ExpectedManifestDigest `
        -Event $Event | Out-Null

    $workflowBudget = Get-NervAcceptanceRuntimeWorkflowBudget -WorkflowPath $WorkflowPath -JobName $WorkflowJobName -StepName $WorkflowStepName
    $salesMatches = @($selection.scenarios | Where-Object { [string]::Equals([string]$_.id, 'sales-order-demand', [StringComparison]::Ordinal) })
    if ($salesMatches.Count -gt 1) { throw "Runtime planning artifact must contain at most one selected 'sales-order-demand' scenario." }
    $selected = $salesMatches.Count -eq 1
    $scenario = if ($selected) { Get-NervAcceptanceSalesOrderRuntimeScenario -Manifest $manifest } else { $null }
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
            scenarioId = 'sales-order-demand'
        }
        artifactDigest = $artifactSnapshot.digest
        requiredSeconds = $requiredSeconds
        workflowBudget = $workflowBudget
    }
}

function New-NervAcceptanceScenarioRuntimeSummary {
    return [pscustomobject][ordered]@{
        schemaVersion = 1
        scenarioId = 'sales-order-demand'
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
    if ($RunAttempt -cnotmatch '^[1-9][0-9]*$') {
        throw 'Acceptance scenario runtime run attempt must be a canonical positive integer.'
    }
    $parsedRunAttempt = [Numerics.BigInteger]::Parse(
        $RunAttempt,
        [Globalization.NumberStyles]::None,
        [Globalization.CultureInfo]::InvariantCulture)
    if ($parsedRunAttempt -gt [int]::MaxValue) {
        throw 'Acceptance scenario runtime run attempt must fit Int32.'
    }

    return [pscustomobject][ordered]@{
        runAttempt = [int]$parsedRunAttempt
        event = $Event
    }
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
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $ManifestPath,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $Event,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $WorkflowPath,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $WorkflowJobName,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $WorkflowStepName,
        [Parameter(Mandatory)] [string] $SummaryPath,
        [Parameter(Mandatory)] [scriptblock] $RuntimeAction,
        [scriptblock] $ReadFileBytesAction
    )

    $summary = New-NervAcceptanceScenarioRuntimeSummary
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
            -ManifestPath $validatedInputs.manifestPath `
            -Event $validatedInputs.event `
            -WorkflowPath $validatedInputs.workflowPath `
            -WorkflowJobName $validatedInputs.workflowJobName `
            -WorkflowStepName $validatedInputs.workflowStepName `
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

function New-NervAcceptanceScenarioEquivalenceVector {
    param(
        [Parameter(Mandatory)] [object] $Result,
        [Parameter(Mandatory)] [object] $ValidatedScenario,
        [Parameter(Mandatory)] [object] $ExpectedProvenance
    )

    $scenario = Get-NervAcceptanceSalesOrderRuntimeScenario -Manifest ([pscustomobject]@{ scenarios = @($ValidatedScenario) })
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

    $businessFactFields = @('sourceStateCommittedBeforeMutation', 'changeV2Converged', 'changeV3Converged', 'duplicateConverged', 'outOfOrderConverged', 'cancellationConverged')
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
    Assert-NervAcceptanceObjectSchema -Object $Result.volatile.ports -AllowedFields @('masterData', 'erp', 'demandPlanning') -RequiredFields @('masterData', 'erp', 'demandPlanning') -Context 'runtime equivalence volatile ports'
    foreach ($name in @('masterData', 'erp', 'demandPlanning')) {
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
    foreach ($name in @('sourceStateCommittedBeforeMutation', 'changeV2Converged', 'changeV3Converged', 'duplicateConverged', 'outOfOrderConverged', 'cancellationConverged')) {
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
