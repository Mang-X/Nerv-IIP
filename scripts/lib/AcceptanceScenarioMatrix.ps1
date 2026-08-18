# Script-Governance:
#   Category: library
#   SideEffects:
#     - Reads acceptance scenario matrix and FullChain v1 manifest files supplied by the caller
#     - Reads repository directories to verify impact path roots with ordinal casing
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

function Test-NervAcceptanceInteger {
    param([AllowNull()] [object] $Value)

    return $Value -is [byte] -or $Value -is [sbyte] -or
        $Value -is [int16] -or $Value -is [uint16] -or
        $Value -is [int32] -or $Value -is [uint32] -or
        $Value -is [int64] -or $Value -is [uint64]
}

function Assert-NervAcceptanceObjectSchema {
    param(
        [AllowNull()] [object] $Object,
        [Parameter(Mandatory)] [string[]] $AllowedFields,
        [Parameter(Mandatory)] [string[]] $RequiredFields,
        [Parameter(Mandatory)] [string] $Context
    )

    if ($null -eq $Object -or $Object -isnot [pscustomobject]) {
        throw "$Context must be an object."
    }
    $allowed = [Collections.Generic.HashSet[string]]::new($AllowedFields, [StringComparer]::Ordinal)
    $present = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($property in $Object.PSObject.Properties) {
        $name = [string]$property.Name
        if (-not $allowed.Contains($name)) { throw "$Context has unknown field '$name'." }
        [void]$present.Add($name)
    }
    foreach ($required in $RequiredFields) {
        if (-not $present.Contains($required)) { throw "$Context is missing required field '$required'." }
    }
}

function Assert-NervAcceptanceString {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [string] $Context
    )

    $text = [string]$Value
    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($text) -or
        -not [string]::Equals($text, $text.Trim(), [StringComparison]::Ordinal)) {
        throw "$Context must be a trimmed non-empty string."
    }
}

function Test-NervAcceptanceObjectProperty {
    param(
        [AllowNull()] [object] $Object,
        [Parameter(Mandatory)] [string] $Name
    )

    if ($null -eq $Object) { return $false }
    foreach ($property in $Object.PSObject.Properties) {
        if ([string]::Equals([string]$property.Name, $Name, [StringComparison]::Ordinal)) { return $true }
    }
    return $false
}

function Test-NervAcceptanceRepositoryPath {
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $RelativePath,
        [switch] $RequireDirectory
    )

    $current = [IO.Path]::GetFullPath($RepositoryRoot)
    foreach ($segment in $RelativePath.Split('/')) {
        $matches = @(Get-ChildItem -LiteralPath $current -Force -ErrorAction SilentlyContinue | Where-Object {
            [string]::Equals([string]$_.Name, $segment, [StringComparison]::Ordinal)
        })
        if ($matches.Count -ne 1) { return $false }
        $current = $matches[0].FullName
    }
    if ($RequireDirectory) { return Test-Path -LiteralPath $current -PathType Container }
    return Test-Path -LiteralPath $current
}

function Assert-NervAcceptanceImpactPath {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $ScenarioId,
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )

    Assert-NervAcceptanceString -Value $Path -Context "scenario '$ScenarioId' impact path"
    if ($Path.StartsWith('/', [StringComparison]::Ordinal) -or $Path.Contains('\', [StringComparison]::Ordinal) -or
        $Path.Contains('../', [StringComparison]::Ordinal) -or $Path.EndsWith('/..', [StringComparison]::Ordinal)) {
        throw "scenario '$ScenarioId' impact path '$Path' must be repository-relative and normalized."
    }
    $hasWildcard = $Path.Contains('*', [StringComparison]::Ordinal) -or $Path.Contains('?', [StringComparison]::Ordinal) -or $Path.Contains('[', [StringComparison]::Ordinal)
    if ($hasWildcard) {
        if (-not $Path.EndsWith('/**', [StringComparison]::Ordinal) -or $Path.Substring(0, $Path.Length - 3).Contains('*', [StringComparison]::Ordinal) -or
            $Path.Substring(0, $Path.Length - 3).Contains('?', [StringComparison]::Ordinal) -or $Path.Substring(0, $Path.Length - 3).Contains('[', [StringComparison]::Ordinal)) {
            throw "scenario '$ScenarioId' impact path '$Path' uses an unsupported glob; only an exact '/**' suffix is allowed."
        }
        $staticRoot = $Path.Substring(0, $Path.Length - 3)
        if (-not (Test-NervAcceptanceRepositoryPath -RepositoryRoot $RepositoryRoot -RelativePath $staticRoot -RequireDirectory)) {
            throw "scenario '$ScenarioId' impact path static root must exist with exact casing: '$staticRoot'."
        }
        return
    }
    if (-not (Test-NervAcceptanceRepositoryPath -RepositoryRoot $RepositoryRoot -RelativePath $Path)) {
        throw "scenario '$ScenarioId' exact impact path must exist with exact casing: '$Path'."
    }
}

function Assert-NervAcceptanceStringArray {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [string] $Context,
        [switch] $AllowEmpty
    )

    if ($Value -isnot [array]) { throw "$Context must be an array." }
    $items = @($Value)
    if (-not $AllowEmpty -and $items.Count -eq 0) { throw "$Context must not be empty." }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($item in $items) {
        Assert-NervAcceptanceString -Value $item -Context "$Context item"
        if (-not $seen.Add([string]$item)) { throw "$Context values must be ordinal-unique." }
    }
}

function Assert-NervAcceptanceBoolean {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [string] $Context
    )

    if ($Value -isnot [bool]) { throw "$Context must be a boolean." }
}

function Assert-NervAcceptanceBudgetValue {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [int64] $Maximum,
        [Parameter(Mandatory)] [string] $Context
    )

    if (-not (Test-NervAcceptanceInteger -Value $Value) -or [int64]$Value -le 0 -or [uint64]$Value -gt [uint64]$Maximum) {
        throw "$Context must be a positive integer within schema limit $Maximum."
    }
}

function Test-NervAcceptanceOrdinalSequenceEqual {
    param(
        [AllowEmptyCollection()] [string[]] $Left,
        [AllowEmptyCollection()] [string[]] $Right
    )

    if ($Left.Count -ne $Right.Count) { return $false }
    for ($index = 0; $index -lt $Left.Count; $index++) {
        if (-not [string]::Equals($Left[$index], $Right[$index], [StringComparison]::Ordinal)) { return $false }
    }
    return $true
}

function Assert-NervAcceptancePlanningBudget {
    param([Parameter(Mandatory)] [object] $Budget)

    $fields = @('restorePerProjectSeconds', 'discoveryPerProjectSeconds', 'artifactWriteSeconds', 'safetyMarginSeconds')
    Assert-NervAcceptanceObjectSchema -Object $Budget -AllowedFields $fields -RequiredFields $fields -Context 'planningBudget'
    Assert-NervAcceptanceBudgetValue -Value $Budget.restorePerProjectSeconds -Maximum 1800 -Context 'planningBudget.restorePerProjectSeconds'
    Assert-NervAcceptanceBudgetValue -Value $Budget.discoveryPerProjectSeconds -Maximum 900 -Context 'planningBudget.discoveryPerProjectSeconds'
    Assert-NervAcceptanceBudgetValue -Value $Budget.artifactWriteSeconds -Maximum 300 -Context 'planningBudget.artifactWriteSeconds'
    Assert-NervAcceptanceBudgetValue -Value $Budget.safetyMarginSeconds -Maximum 900 -Context 'planningBudget.safetyMarginSeconds'
}

function Assert-NervAcceptanceExecutionBudget {
    param(
        [Parameter(Mandatory)] [object] $Budget,
        [Parameter(Mandatory)] [string] $ScenarioId
    )

    $fields = @('dependencyReadinessSeconds', 'executionTimeoutSeconds', 'diagnosticsSeconds', 'cleanupSeconds', 'evidenceWriteSeconds', 'safetyMarginSeconds')
    $context = "scenario '$ScenarioId' executionBudget"
    Assert-NervAcceptanceObjectSchema -Object $Budget -AllowedFields $fields -RequiredFields $fields -Context $context
    Assert-NervAcceptanceBudgetValue -Value $Budget.dependencyReadinessSeconds -Maximum 900 -Context "$context.dependencyReadinessSeconds"
    Assert-NervAcceptanceBudgetValue -Value $Budget.executionTimeoutSeconds -Maximum 7200 -Context "$context.executionTimeoutSeconds"
    Assert-NervAcceptanceBudgetValue -Value $Budget.diagnosticsSeconds -Maximum 900 -Context "$context.diagnosticsSeconds"
    Assert-NervAcceptanceBudgetValue -Value $Budget.cleanupSeconds -Maximum 900 -Context "$context.cleanupSeconds"
    Assert-NervAcceptanceBudgetValue -Value $Budget.evidenceWriteSeconds -Maximum 300 -Context "$context.evidenceWriteSeconds"
    Assert-NervAcceptanceBudgetValue -Value $Budget.safetyMarginSeconds -Maximum 900 -Context "$context.safetyMarginSeconds"
}

function Assert-NervAcceptanceEntrypoint {
    param(
        [Parameter(Mandatory)] [object] $Entrypoint,
        [Parameter(Mandatory)] [string] $ScenarioId
    )

    Assert-NervAcceptanceObjectSchema -Object $Entrypoint -AllowedFields @('kind', 'scenario', 'path') -RequiredFields @('kind') -Context "scenario '$ScenarioId' entrypoint"
    Assert-NervAcceptanceString -Value $Entrypoint.kind -Context "scenario '$ScenarioId' entrypoint.kind"
    $kind = [string]$Entrypoint.kind
    if ([string]::Equals($kind, 'fullstack', [StringComparison]::Ordinal)) {
        Assert-NervAcceptanceObjectSchema -Object $Entrypoint -AllowedFields @('kind', 'scenario') -RequiredFields @('kind', 'scenario') -Context "scenario '$ScenarioId' entrypoint"
        Assert-NervAcceptanceString -Value $Entrypoint.scenario -Context "scenario '$ScenarioId' entrypoint.scenario"
        if ([string]$Entrypoint.scenario -cnotmatch '^man-[0-9]+$') { throw "scenario '$ScenarioId' entrypoint.scenario must be canonical." }
        return
    }
    if ([string]::Equals($kind, 'script', [StringComparison]::Ordinal)) {
        Assert-NervAcceptanceObjectSchema -Object $Entrypoint -AllowedFields @('kind', 'path') -RequiredFields @('kind', 'path') -Context "scenario '$ScenarioId' entrypoint"
        Assert-NervAcceptanceString -Value $Entrypoint.path -Context "scenario '$ScenarioId' entrypoint.path"
        if ([string]$Entrypoint.path -cnotmatch '^scripts/.+\.ps1$') { throw "scenario '$ScenarioId' entrypoint.path must be canonical." }
        return
    }
    if ([string]::Equals($kind, 'dotnet', [StringComparison]::Ordinal)) {
        Assert-NervAcceptanceObjectSchema -Object $Entrypoint -AllowedFields @('kind') -RequiredFields @('kind') -Context "scenario '$ScenarioId' entrypoint"
        return
    }
    throw "scenario '$ScenarioId' has invalid entrypoint kind '$kind'."
}

function Assert-NervAcceptanceScenarioShape {
    param(
        [Parameter(Mandatory)] [object] $Scenario,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [Collections.Generic.HashSet[string]] $Ids,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [Collections.Generic.HashSet[string]] $Aliases,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [Collections.Generic.HashSet[string]] $Identities,
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )

    $requiredFields = @(
        'id', 'v1Alias', 'services', 'ownerIssue', 'status', 'tier', 'contracts', 'entrypoint',
        'testProjects', 'dependencies', 'impact', 'runPolicy', 'expectedRuntimeTestCount',
        'executionBudget', 'diagnosticProtocol', 'evidenceProtocol', 'cleanupProtocol'
    )
    $allowedFields = @($requiredFields + @('blockedReason', 'deferredReason'))
    Assert-NervAcceptanceObjectSchema -Object $Scenario -AllowedFields $allowedFields -RequiredFields $requiredFields -Context 'scenario'

    Assert-NervAcceptanceString -Value $Scenario.id -Context 'scenario.id'
    $id = [string]$Scenario.id
    if ($id -cnotmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$' -or -not $Ids.Add($id)) { throw "scenario '$id' must have a unique canonical id." }

    if ($null -ne $Scenario.v1Alias) {
        Assert-NervAcceptanceString -Value $Scenario.v1Alias -Context "scenario '$id' v1Alias"
        if ([string]$Scenario.v1Alias -cnotmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$' -or -not $Aliases.Add([string]$Scenario.v1Alias)) {
            throw "scenario '$id' v1Alias must be ordinal-unique and canonical."
        }
    }

    Assert-NervAcceptanceStringArray -Value $Scenario.services -Context "scenario '$id' services"
    Assert-NervAcceptanceString -Value $Scenario.ownerIssue -Context "scenario '$id' ownerIssue"
    if ([string]$Scenario.ownerIssue -cnotmatch '^#[1-9][0-9]*$') { throw "scenario '$id' ownerIssue must be a canonical GitHub issue reference." }

    $status = [string]$Scenario.status
    $allowedStatuses = [Collections.Generic.HashSet[string]]::new([string[]]@('active', 'deferred', 'blocked'), [StringComparer]::Ordinal)
    if (-not $allowedStatuses.Contains($status)) { throw "scenario '$id' has invalid status '$status'." }
    $tier = [string]$Scenario.tier
    $allowedTiers = [Collections.Generic.HashSet[string]]::new([string[]]@('core', 'extended'), [StringComparer]::Ordinal)
    if (-not $allowedTiers.Contains($tier)) { throw "scenario '$id' has invalid tier '$tier'." }
    if ([string]::Equals($status, 'blocked', [StringComparison]::Ordinal)) {
        if ($null -ne $Scenario.v1Alias) { throw "blocked scenario '$id' v1Alias must be null." }
        Assert-NervAcceptanceString -Value $Scenario.blockedReason -Context "blocked scenario '$id' blockedReason"
        if (Test-NervAcceptanceObjectProperty -Object $Scenario -Name 'deferredReason') { throw "blocked scenario '$id' must not declare deferredReason." }
    }
    elseif ([string]::Equals($status, 'deferred', [StringComparison]::Ordinal)) {
        Assert-NervAcceptanceString -Value $Scenario.deferredReason -Context "deferred scenario '$id' deferredReason"
        if (Test-NervAcceptanceObjectProperty -Object $Scenario -Name 'blockedReason') { throw "deferred scenario '$id' must not declare blockedReason." }
    }
    elseif ((Test-NervAcceptanceObjectProperty -Object $Scenario -Name 'blockedReason') -or (Test-NervAcceptanceObjectProperty -Object $Scenario -Name 'deferredReason')) {
        throw "active scenario '$id' must not declare blockedReason or deferredReason."
    }

    Assert-NervAcceptanceStringArray -Value $Scenario.contracts -Context "scenario '$id' contracts"
    Assert-NervAcceptanceEntrypoint -Entrypoint $Scenario.entrypoint -ScenarioId $id

    if ($Scenario.testProjects -isnot [array] -or @($Scenario.testProjects).Count -eq 0) { throw "scenario '$id' testProjects must be a non-empty array." }
    foreach ($project in @($Scenario.testProjects)) {
        Assert-NervAcceptanceObjectSchema -Object $project -AllowedFields @('path', 'frozenTestIdentities') -RequiredFields @('path', 'frozenTestIdentities') -Context "scenario '$id' test project"
        Assert-NervAcceptanceString -Value $project.path -Context "scenario '$id' test project path"
        if ([string]$project.path -cnotmatch '^backend/.+\.csproj$') { throw "scenario '$id' test project path must be canonical." }
        Assert-NervAcceptanceStringArray -Value $project.frozenTestIdentities -Context "scenario '$id' frozenTestIdentities"
        foreach ($identity in @($project.frozenTestIdentities)) {
            if ([string]$identity -cnotmatch '^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*){2,}$') { throw "scenario '$id' frozen identity must be a canonical FullyQualifiedName." }
            if (-not $Identities.Add([string]$identity)) { throw "scenario '$id' frozen identity must be ordinal-unique." }
        }
    }

    Assert-NervAcceptanceObjectSchema -Object $Scenario.dependencies -AllowedFields @('postgres', 'redis', 'externalProcesses') -RequiredFields @('postgres', 'redis', 'externalProcesses') -Context "scenario '$id' dependencies"
    Assert-NervAcceptanceBoolean -Value $Scenario.dependencies.postgres -Context "scenario '$id' dependencies.postgres"
    Assert-NervAcceptanceBoolean -Value $Scenario.dependencies.redis -Context "scenario '$id' dependencies.redis"
    Assert-NervAcceptanceBoolean -Value $Scenario.dependencies.externalProcesses -Context "scenario '$id' dependencies.externalProcesses"

    Assert-NervAcceptanceObjectSchema -Object $Scenario.impact -AllowedFields @('paths', 'owners') -RequiredFields @('paths', 'owners') -Context "scenario '$id' impact"
    Assert-NervAcceptanceStringArray -Value $Scenario.impact.paths -Context "scenario '$id' impact.paths"
    foreach ($impactPath in @($Scenario.impact.paths)) { Assert-NervAcceptanceImpactPath -Path ([string]$impactPath) -ScenarioId $id -RepositoryRoot $RepositoryRoot }
    Assert-NervAcceptanceStringArray -Value $Scenario.impact.owners -Context "scenario '$id' impact.owners"

    Assert-NervAcceptanceObjectSchema -Object $Scenario.runPolicy -AllowedFields @('pullRequest', 'main', 'nightly', 'workflowDispatch') -RequiredFields @('pullRequest', 'main', 'nightly', 'workflowDispatch') -Context "scenario '$id' runPolicy"
    foreach ($property in $Scenario.runPolicy.PSObject.Properties) { Assert-NervAcceptanceString -Value $property.Value -Context "scenario '$id' runPolicy.$($property.Name)" }
    if ([string]::Equals($status, 'active', [StringComparison]::Ordinal)) {
        if (-not [string]::Equals([string]$Scenario.runPolicy.pullRequest, 'impact', [StringComparison]::Ordinal) -or
            -not [string]::Equals([string]$Scenario.runPolicy.main, 'always', [StringComparison]::Ordinal) -or
            -not [string]::Equals([string]$Scenario.runPolicy.nightly, 'always', [StringComparison]::Ordinal) -or
            -not [string]::Equals([string]$Scenario.runPolicy.workflowDispatch, 'selectable', [StringComparison]::Ordinal)) {
            throw "active scenario '$id' has invalid runPolicy."
        }
    }
    elseif (-not [string]::Equals([string]$Scenario.runPolicy.pullRequest, 'never', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$Scenario.runPolicy.main, 'never', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$Scenario.runPolicy.nightly, 'never', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$Scenario.runPolicy.workflowDispatch, 'forbidden', [StringComparison]::Ordinal)) {
        throw "non-active scenario '$id' has invalid runPolicy."
    }

    Assert-NervAcceptanceBudgetValue -Value $Scenario.expectedRuntimeTestCount -Maximum 1000 -Context "scenario '$id' expectedRuntimeTestCount"
    $identityCount = 0
    foreach ($project in @($Scenario.testProjects)) { $identityCount += @($project.frozenTestIdentities).Count }
    if ([int64]$Scenario.expectedRuntimeTestCount -ne $identityCount) { throw "scenario '$id' expectedRuntimeTestCount must equal its frozen identity count." }
    Assert-NervAcceptanceExecutionBudget -Budget $Scenario.executionBudget -ScenarioId $id

    Assert-NervAcceptanceObjectSchema -Object $Scenario.diagnosticProtocol -AllowedFields @('schemas', 'captureBeforeCleanup', 'redactSecrets') -RequiredFields @('schemas', 'captureBeforeCleanup', 'redactSecrets') -Context "scenario '$id' diagnosticProtocol"
    Assert-NervAcceptanceStringArray -Value $Scenario.diagnosticProtocol.schemas -Context "scenario '$id' diagnosticProtocol.schemas"
    Assert-NervAcceptanceBoolean -Value $Scenario.diagnosticProtocol.captureBeforeCleanup -Context "scenario '$id' diagnosticProtocol.captureBeforeCleanup"
    Assert-NervAcceptanceBoolean -Value $Scenario.diagnosticProtocol.redactSecrets -Context "scenario '$id' diagnosticProtocol.redactSecrets"
    if (-not [bool]$Scenario.diagnosticProtocol.captureBeforeCleanup) { throw "scenario '$id' diagnosticProtocol.captureBeforeCleanup must be true." }
    if (-not [bool]$Scenario.diagnosticProtocol.redactSecrets) { throw "scenario '$id' diagnosticProtocol.redactSecrets must be true." }

    Assert-NervAcceptanceObjectSchema -Object $Scenario.evidenceProtocol -AllowedFields @('machineReadableResult', 'exactIdentitySet', 'incrementalSummary', 'requireBusinessKeys', 'requireDependencyVersions') -RequiredFields @('machineReadableResult', 'exactIdentitySet', 'incrementalSummary', 'requireBusinessKeys', 'requireDependencyVersions') -Context "scenario '$id' evidenceProtocol"
    Assert-NervAcceptanceString -Value $Scenario.evidenceProtocol.machineReadableResult -Context "scenario '$id' evidenceProtocol.machineReadableResult"
    if (-not [string]::Equals([string]$Scenario.evidenceProtocol.machineReadableResult, 'trx', [StringComparison]::Ordinal)) { throw "scenario '$id' evidenceProtocol must require TRX." }
    foreach ($name in @('exactIdentitySet', 'incrementalSummary', 'requireBusinessKeys', 'requireDependencyVersions')) {
        Assert-NervAcceptanceBoolean -Value $Scenario.evidenceProtocol.PSObject.Properties[$name].Value -Context "scenario '$id' evidenceProtocol.$name"
        if (-not [bool]$Scenario.evidenceProtocol.PSObject.Properties[$name].Value) { throw "scenario '$id' evidenceProtocol.$name must be true." }
    }

    Assert-NervAcceptanceObjectSchema -Object $Scenario.cleanupProtocol -AllowedFields @('ownedResourcesOnly', 'requireZeroRemaining', 'preserveDiagnosticsBeforeCleanup', 'prohibitedActions') -RequiredFields @('ownedResourcesOnly', 'requireZeroRemaining', 'preserveDiagnosticsBeforeCleanup', 'prohibitedActions') -Context "scenario '$id' cleanupProtocol"
    foreach ($name in @('ownedResourcesOnly', 'requireZeroRemaining', 'preserveDiagnosticsBeforeCleanup')) {
        Assert-NervAcceptanceBoolean -Value $Scenario.cleanupProtocol.PSObject.Properties[$name].Value -Context "scenario '$id' cleanupProtocol.$name"
        if (-not [bool]$Scenario.cleanupProtocol.PSObject.Properties[$name].Value) { throw "scenario '$id' cleanupProtocol.$name must be true." }
    }
    Assert-NervAcceptanceStringArray -Value $Scenario.cleanupProtocol.prohibitedActions -Context "scenario '$id' cleanupProtocol.prohibitedActions"
    $prohibitedActions = [Collections.Generic.HashSet[string]]::new([string[]]@($Scenario.cleanupProtocol.prohibitedActions), [StringComparer]::Ordinal)
    foreach ($requiredAction in @('broad-process-kill', 'unknown-database-delete', 'docker-prune', 'redis-flushall')) {
        if (-not $prohibitedActions.Contains($requiredAction)) { throw "scenario '$id' cleanupProtocol.prohibitedActions must contain '$requiredAction'." }
    }
}

function Assert-NervAcceptanceV1Closure {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [object] $V1Manifest,
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )

    if (-not (Test-NervAcceptanceInteger -Value $V1Manifest.schemaVersion) -or [int64]$V1Manifest.schemaVersion -ne 1) { throw 'FullChain v1 manifest schemaVersion must be 1.' }
    $v1Members = @($V1Manifest.members)
    foreach ($v1Member in $v1Members) {
        foreach ($dependencyName in @('postgres', 'redis', 'externalProcesses')) {
            if (-not (Test-NervAcceptanceObjectProperty -Object $v1Member.dependencies -Name $dependencyName) -or
                $v1Member.dependencies.PSObject.Properties[$dependencyName].Value -isnot [bool]) {
                throw "FullChain v1 dependency must be a JSON boolean: member '$($v1Member.id)', field '$dependencyName'."
            }
        }
    }
    $active = @($Manifest.scenarios | Where-Object { [string]::Equals([string]$_.status, 'active', [StringComparison]::Ordinal) -and [string]::Equals([string]$_.tier, 'core', [StringComparison]::Ordinal) })
    $activeAliases = @($active | ForEach-Object { [string]$_.v1Alias })
    $v1Ids = @($v1Members | ForEach-Object { [string]$_.id })
    [Array]::Sort($activeAliases, [StringComparer]::Ordinal)
    [Array]::Sort($v1Ids, [StringComparer]::Ordinal)
    if (-not (Test-NervAcceptanceOrdinalSequenceEqual -Left $activeAliases -Right $v1Ids)) { throw 'The active/core v2 v1 alias set must exactly match the FullChain v1 member set.' }

    $expectedAliases = @('sales-order-demand-planning', 'erp-wms-delivery-completion', 'mes-inventory-produced-lot', 'maintenance-runtime-hours', 'erp-return-closure')
    for ($index = 0; $index -lt $active.Count; $index++) {
        $scenario = $active[$index]
        $id = [string]$scenario.id
        if (-not [string]::Equals([string]$scenario.v1Alias, $expectedAliases[$index], [StringComparison]::Ordinal)) { throw "scenario '$id' must retain its approved v1Alias." }
        $v1Matches = @($v1Members | Where-Object { [string]::Equals([string]$_.id, [string]$scenario.v1Alias, [StringComparison]::Ordinal) })
        if ($v1Matches.Count -ne 1) { throw "scenario '$id' must resolve exactly one v1 member." }
        $v1 = $v1Matches[0]
        if (@($scenario.testProjects).Count -ne 1 -or -not [string]::Equals([string]$scenario.testProjects[0].path, [string]$v1.project, [StringComparison]::Ordinal)) {
            throw "scenario '$id' project must equal v1 project."
        }
        $v2Identities = @($scenario.testProjects[0].frozenTestIdentities | ForEach-Object { [string]$_ })
        $v1Identities = @($v1.expectedTestIdentities | ForEach-Object { [string]$_ })
        if (-not (Test-NervAcceptanceOrdinalSequenceEqual -Left $v2Identities -Right $v1Identities)) { throw "scenario '$id' identities must equal v1 identities." }

        $v2Kind = [string]$scenario.entrypoint.kind
        $v1Kind = [string]$v1.entrypoint.kind
        $entrypointMatches = [string]::Equals($v2Kind, $v1Kind, [StringComparison]::Ordinal)
        if ($entrypointMatches -and [string]::Equals($v2Kind, 'script', [StringComparison]::Ordinal)) { $entrypointMatches = [string]::Equals([string]$scenario.entrypoint.path, [string]$v1.entrypoint.path, [StringComparison]::Ordinal) }
        if ($entrypointMatches -and [string]::Equals($v2Kind, 'fullstack', [StringComparison]::Ordinal)) { $entrypointMatches = [string]::Equals([string]$scenario.entrypoint.scenario, [string]$v1.entrypoint.scenario, [StringComparison]::Ordinal) }
        if (-not $entrypointMatches) { throw "scenario '$id' entrypoint must equal v1 entrypoint." }

        foreach ($name in @('postgres', 'redis', 'externalProcesses')) {
            if ([bool]$scenario.dependencies.PSObject.Properties[$name].Value -ne [bool]$v1.dependencies.PSObject.Properties[$name].Value) { throw "scenario '$id' dependencies must equal v1 dependencies." }
        }
        $v2Schemas = @($scenario.diagnosticProtocol.schemas | ForEach-Object { [string]$_ })
        $v1Schemas = @($v1.diagnosticSchemas | ForEach-Object { [string]$_ })
        if (-not (Test-NervAcceptanceOrdinalSequenceEqual -Left $v2Schemas -Right $v1Schemas)) { throw "scenario '$id' diagnostic schemas must equal v1 diagnostic schemas." }

        $projectPath = Join-Path $RepositoryRoot ([string]$scenario.testProjects[0].path)
        if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) { throw "active scenario '$id' project file does not exist." }
        if ([string]::Equals($v2Kind, 'script', [StringComparison]::Ordinal) -and -not (Test-Path -LiteralPath (Join-Path $RepositoryRoot ([string]$scenario.entrypoint.path)) -PathType Leaf)) {
            throw "active scenario '$id' script entrypoint does not exist."
        }
    }
}

function Import-NervAcceptanceScenarioMatrixManifest {
    param(
        [Parameter(Mandatory)] [string] $ManifestPath,
        [Parameter(Mandatory)] [string] $V1ManifestPath,
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )

    $manifest = Get-Content -LiteralPath (Resolve-Path $ManifestPath) -Raw | ConvertFrom-Json -Depth 50
    Assert-NervAcceptanceObjectSchema -Object $manifest -AllowedFields @('schemaVersion', 'lane', 'planningBudget', 'scenarios') -RequiredFields @('schemaVersion', 'lane', 'planningBudget', 'scenarios') -Context 'manifest'
    if (-not (Test-NervAcceptanceInteger -Value $manifest.schemaVersion) -or [int64]$manifest.schemaVersion -ne 2) { throw "Unsupported acceptance scenario manifest schemaVersion '$($manifest.schemaVersion)'." }
    if (-not [string]::Equals([string]$manifest.lane, 'full-chain', [StringComparison]::Ordinal)) { throw "Acceptance scenario manifest lane must be 'full-chain'." }
    Assert-NervAcceptancePlanningBudget -Budget $manifest.planningBudget

    if ($manifest.scenarios -isnot [array]) { throw 'Acceptance scenario manifest scenarios must be an array.' }
    $scenarios = @($manifest.scenarios)
    if ($scenarios.Count -ne 6) { throw "Acceptance scenario manifest must contain exactly 6 scenarios; observed $($scenarios.Count)." }
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $aliases = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $identities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($scenario in $scenarios) { Assert-NervAcceptanceScenarioShape -Scenario $scenario -Ids $ids -Aliases $aliases -Identities $identities -RepositoryRoot $RepositoryRoot }

    $expectedIds = @('sales-order-demand', 'wms-delivery-erp', 'mes-produced-lot-inventory', 'telemetry-runtime-maintenance', 'erp-return-closure', 'equipment-unavailable-scheduling-mes')
    $observedIds = @($scenarios | ForEach-Object { [string]$_.id })
    if (-not (Test-NervAcceptanceOrdinalSequenceEqual -Left $observedIds -Right $expectedIds)) { throw 'Acceptance scenario manifest ids are not in the approved stable order.' }
    for ($index = 0; $index -lt 5; $index++) {
        if (-not [string]::Equals([string]$scenarios[$index].status, 'active', [StringComparison]::Ordinal) -or -not [string]::Equals([string]$scenarios[$index].tier, 'core', [StringComparison]::Ordinal)) {
            throw "scenario '$($scenarios[$index].id)' must be active/core."
        }
    }
    $blocked = $scenarios[5]
    if (-not [string]::Equals([string]$blocked.status, 'blocked', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$blocked.tier, 'extended', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$blocked.ownerIssue, '#1240', [StringComparison]::Ordinal)) {
        throw "scenario '$($blocked.id)' must be blocked/extended and owned by #1240."
    }

    $v1Manifest = Get-Content -LiteralPath (Resolve-Path $V1ManifestPath) -Raw | ConvertFrom-Json -Depth 30
    Assert-NervAcceptanceV1Closure -Manifest $manifest -V1Manifest $v1Manifest -RepositoryRoot $RepositoryRoot
    return $manifest
}
