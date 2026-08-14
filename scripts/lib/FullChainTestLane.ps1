# Script-Governance:
#   Category: library
#   SideEffects:
#     - Reads a FullChain lane manifest and VSTest TRX files supplied by the caller
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

function Import-NervFullChainTestLaneManifest {
    param(
        [Parameter(Mandatory)] [string] $ManifestPath,
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )

    $manifest = Get-Content -LiteralPath (Resolve-Path $ManifestPath) -Raw | ConvertFrom-Json -Depth 20
    if ([int]$manifest.schemaVersion -ne 1) { throw "Unsupported FullChain lane manifest schemaVersion '$($manifest.schemaVersion)'." }
    $members = @($manifest.members)
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $identities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $allowedKinds = [Collections.Generic.HashSet[string]]::new([string[]]@('fullstack', 'script', 'dotnet'), [StringComparer]::Ordinal)
    foreach ($member in $members) {
        $id = [string]$member.id
        if ($id -cnotmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$' -or -not $ids.Add($id)) { throw "FullChain lane member id '$id' must be unique and canonical." }
        if (-not [string]::Equals([string]$member.tier, 'core', [StringComparison]::Ordinal) -or -not [string]::Equals([string]$member.status, 'active', [StringComparison]::Ordinal)) { throw "FullChain lane member '$id' must be active/core." }
        $project = [string]$member.project
        if ($project -cnotmatch '^backend/.+\.csproj$' -or -not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $project) -PathType Leaf)) { throw "FullChain lane member '$id' project is missing or outside backend." }
        if ([string]::IsNullOrWhiteSpace([string]$member.filter) -or -not ([string]$member.filter).StartsWith('FullyQualifiedName=', [StringComparison]::Ordinal)) { throw "FullChain lane member '$id' must declare one exact FullyQualifiedName filter." }
        $memberIdentities = @($member.expectedTestIdentities | ForEach-Object { [string]$_ })
        if ($memberIdentities.Count -ne 1 -or [string]::IsNullOrWhiteSpace($memberIdentities[0]) -or -not $identities.Add($memberIdentities[0])) { throw "FullChain lane member '$id' must freeze exactly one globally unique identity." }
        if (-not [string]::Equals([string]$member.filter, "FullyQualifiedName=$($memberIdentities[0])", [StringComparison]::Ordinal)) { throw "FullChain lane member '$id' filter must equal its frozen identity." }
        $schemas = @($member.diagnosticSchemas | ForEach-Object { [string]$_ })
        $schemaSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        if ($schemas.Count -eq 0 -or @($schemas | Where-Object { $_ -cnotmatch '^[a-z][a-z0-9_]{0,62}$' -or -not $schemaSet.Add($_) }).Count -gt 0) { throw "FullChain lane member '$id' must declare unique canonical diagnosticSchemas." }
        $kind = [string]$member.entrypoint.kind
        if (-not $allowedKinds.Contains($kind)) { throw "FullChain lane member '$id' has invalid entrypoint kind '$kind'." }
        if ([string]::Equals($kind, 'fullstack', [StringComparison]::Ordinal) -and [string]$member.entrypoint.scenario -cnotmatch '^man-[0-9]+$') { throw "FullChain lane member '$id' has an invalid fullstack scenario." }
        if ([string]::Equals($kind, 'script', [StringComparison]::Ordinal)) {
            $path = [string]$member.entrypoint.path
            if ($path -cnotmatch '^scripts/.+\.ps1$' -or -not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $path) -PathType Leaf)) { throw "FullChain lane member '$id' script entrypoint is missing." }
        }
        if (-not [bool]$member.dependencies.postgres) { throw "FullChain lane member '$id' must require PostgreSQL." }
    }
    if ($members.Count -ne 5) { throw "FullChain lane manifest must contain exactly 5 active/core members; observed $($members.Count)." }
    return [pscustomobject]@{ schemaVersion = 1; members = $members }
}

function Get-NervFullChainTrxResult {
    param(
        [Parameter(Mandatory)] [string] $ResultsDirectory,
        [Parameter(Mandatory)] [string[]] $ExpectedTestIdentities,
        [switch] $AllowInvalid
    )

    $trxFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File -Recurse)
    if ($trxFiles.Count -ne 1) { throw "FullChain lane member must produce exactly one TRX file; observed $($trxFiles.Count)." }
    [xml]$trx = Get-Content -LiteralPath $trxFiles[0].FullName -Raw
    $results = @($trx.SelectNodes("//*[local-name()='UnitTestResult']"))
    $definitions = @($trx.SelectNodes("//*[local-name()='UnitTest']"))
    $definitionById = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($definition in $definitions) {
        $method = $definition.SelectSingleNode("./*[local-name()='TestMethod']")
        if ($null -ne $method) { $definitionById[[string]$definition.id] = "$($method.className).$($method.name)" }
    }
    $observed = @($results | ForEach-Object { if ($definitionById.ContainsKey([string]$_.testId)) { $definitionById[[string]$_.testId] } else { [string]$_.testName } })
    $expected = @($ExpectedTestIdentities)
    [Array]::Sort($observed, [StringComparer]::Ordinal)
    [Array]::Sort($expected, [StringComparer]::Ordinal)
    $identitiesMatch = [string]::Equals(($observed -join "`n"), ($expected -join "`n"), [StringComparison]::Ordinal)
    $passed = @($results | Where-Object { [string]::Equals([string]$_.outcome, 'Passed', [StringComparison]::Ordinal) }).Count
    $failed = @($results | Where-Object { [string]::Equals([string]$_.outcome, 'Failed', [StringComparison]::Ordinal) }).Count
    $skipped = $results.Count - $passed - $failed
    $valid = $identitiesMatch -and $passed -eq $expected.Count -and $failed -eq 0 -and $skipped -eq 0
    $result = [pscustomobject]@{ total = $results.Count; passed = $passed; failed = $failed; skipped = $skipped; identities = @($observed); identitiesMatch = $identitiesMatch; valid = $valid }
    if (-not $AllowInvalid -and -not $identitiesMatch) { throw 'FullChain lane TRX identities do not equal the frozen member identities.' }
    if (-not $AllowInvalid -and -not $valid) { throw "FullChain lane requires $($expected.Count) passed, 0 failed and 0 skipped; observed $passed passed, $failed failed and $skipped skipped." }
    return $result
}

function Test-NervFullChainEvidenceProperty {
    param([AllowNull()] [object] $Object, [Parameter(Mandatory)] [string] $Name)
    if ($null -eq $Object) { return $false }
    return @($Object.PSObject.Properties | Where-Object { [string]::Equals([string]$_.Name, $Name, [StringComparison]::Ordinal) }).Count -eq 1
}

function Assert-NervFullChainZeroReadback {
    param([Parameter(Mandatory)] [object] $Object, [Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [string] $MemberId)
    if (-not (Test-NervFullChainEvidenceProperty -Object $Object -Name $Name)) { throw "FullChain member '$MemberId' cleanup evidence is missing required '$Name' readback." }
    $value = $Object.PSObject.Properties[$Name].Value
    if ($value -isnot [byte] -and $value -isnot [int16] -and $value -isnot [int32] -and $value -isnot [int64]) { throw "FullChain member '$MemberId' cleanup evidence '$Name' readback must be an integer." }
    if ([int64]$value -ne 0) { throw "FullChain member '$MemberId' cleanup evidence '$Name' readback must be zero." }
}

function Assert-NervFullChainMemberEvidence {
    param(
        [Parameter(Mandatory)] [object] $Member,
        [Parameter(Mandatory)] [string] $MemberResultsDirectory,
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )

    $schemas = @($Member.diagnosticSchemas | ForEach-Object { [string]$_ })
    if ($schemas.Count -eq 0) { throw "FullChain member '$($Member.id)' has no diagnostic schema contract." }
    $kind = [string]$Member.entrypoint.kind
    if ([string]::Equals($kind, 'fullstack', [StringComparison]::Ordinal)) {
        $manifests = @(Get-ChildItem -LiteralPath (Join-Path $MemberResultsDirectory 'fullstack-state/fullstack-sessions') -Filter '*.json' -File -ErrorAction SilentlyContinue)
        if ($manifests.Count -ne 1) { throw "FullChain member '$($Member.id)' must produce exactly one FullStack cleanup manifest; observed $($manifests.Count)." }
        $evidence = Get-Content -LiteralPath $manifests[0].FullName -Raw | ConvertFrom-Json -Depth 30
        if (-not [string]::Equals([string]$evidence.state, 'Stopped', [StringComparison]::OrdinalIgnoreCase) -or
            @($evidence.cleanup.remaining).Count -ne 0 -or @($evidence.cleanup.errors).Count -ne 0 -or
            [string]::IsNullOrWhiteSpace([string]$evidence.cleanup.completedAtUtc)) {
            throw "FullChain member '$($Member.id)' FullStack cleanup manifest is incomplete."
        }
        $artifactPath = [IO.Path]::GetFullPath([string]$evidence.artifactPath)
        $artifactRoot = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot 'artifacts/fullstack')) + [IO.Path]::DirectorySeparatorChar
        if (-not $artifactPath.StartsWith($artifactRoot, [StringComparison]::Ordinal) -or -not (Test-Path -LiteralPath $artifactPath -PathType Container)) {
            throw "FullChain member '$($Member.id)' diagnostic artifact directory is missing or outside the governed root."
        }
        $summaryPath = Join-Path $artifactPath 'summary.json'
        if (-not (Test-Path -LiteralPath $summaryPath -PathType Leaf)) { throw "FullChain member '$($Member.id)' FullStack diagnostic summary is missing." }
        foreach ($schema in $schemas) {
            $resourceName = $schema.Replace('_', '-')
            $diagnosticPath = Join-Path $artifactPath "aspire-logs/business-$resourceName.ndjson"
            if (-not (Test-Path -LiteralPath $diagnosticPath -PathType Leaf)) { throw "FullChain member '$($Member.id)' diagnostic artifact for schema '$schema' is missing." }
        }
        return [pscustomobject]@{ cleanup = 'passed'; diagnosticEvidence = 'fullstack-artifacts-verified'; source = $manifests[0].FullName; diagnosticSchemas = $schemas }
    }

    if ([string]::Equals($kind, 'script', [StringComparison]::Ordinal)) {
        $evidencePath = Join-Path $MemberResultsDirectory 'entrypoint-evidence.json'
        if (-not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) { throw "FullChain member '$($Member.id)' entrypoint cleanup evidence is missing." }
        $evidence = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json -Depth 30
        if ([string]::Equals([string]$Member.id, 'erp-wms-delivery-completion', [StringComparison]::Ordinal)) {
            if (-not (Test-NervFullChainEvidenceProperty -Object $evidence -Name 'cleanup')) { throw "FullChain member '$($Member.id)' cleanup evidence is missing required 'cleanup' object." }
            Assert-NervFullChainZeroReadback -Object $evidence.cleanup -Name 'managedProcessRemaining' -MemberId ([string]$Member.id)
            Assert-NervFullChainZeroReadback -Object $evidence.cleanup -Name 'exactDatabaseRemaining' -MemberId ([string]$Member.id)
            if (-not (Test-NervFullChainEvidenceProperty -Object $evidence.cleanup -Name 'errors') -or $evidence.cleanup.errors -isnot [array] -or @($evidence.cleanup.errors).Count -ne 0) { throw "FullChain member '$($Member.id)' cleanup evidence must contain an empty errors array." }
        }
        elseif ([string]::Equals([string]$Member.id, 'sales-order-demand-planning', [StringComparison]::Ordinal)) {
            foreach ($objectName in @('managedProcesses', 'disposableDatabase', 'composeServices')) {
                if (-not (Test-NervFullChainEvidenceProperty -Object $evidence -Name $objectName)) { throw "FullChain member '$($Member.id)' cleanup evidence is missing required '$objectName' object." }
                Assert-NervFullChainZeroReadback -Object $evidence.PSObject.Properties[$objectName].Value -Name 'remaining' -MemberId ([string]$Member.id)
            }
            if (-not (Test-NervFullChainEvidenceProperty -Object $evidence -Name 'cleanupFailures') -or $evidence.cleanupFailures -isnot [array] -or @($evidence.cleanupFailures).Count -ne 0) { throw "FullChain member '$($Member.id)' cleanup evidence must contain an empty cleanupFailures array." }
        }
        else { throw "FullChain script member '$($Member.id)' has no cleanup evidence validator." }
        return [pscustomobject]@{ cleanup = 'passed'; diagnosticEvidence = 'entrypoint-evidence-verified'; source = $evidencePath; diagnosticSchemas = $schemas }
    }

    if ([string]::Equals($kind, 'dotnet', [StringComparison]::Ordinal) -and -not [bool]$Member.dependencies.externalProcesses) {
        return [pscustomobject]@{ cleanup = 'passed'; diagnosticEvidence = 'trx-and-runner-output-verified'; source = 'runner-owned-dependencies'; diagnosticSchemas = $schemas }
    }
    throw "FullChain member '$($Member.id)' has no governed cleanup and diagnostic evidence contract."
}

function Assert-NervFullChainTestLaneSummary {
    param(
        [Parameter(Mandatory)] [string[]] $SelectedMemberIds,
        [Parameter(Mandatory)] [object[]] $MemberSummaries
    )

    if ($MemberSummaries.Count -ne $SelectedMemberIds.Count) { throw "FullChain lane selected $($SelectedMemberIds.Count) members but summarized $($MemberSummaries.Count)." }
    for ($index = 0; $index -lt $SelectedMemberIds.Count; $index++) {
        $id = $SelectedMemberIds[$index]
        $member = $MemberSummaries[$index]
        if (-not [string]::Equals([string]$member.memberId, $id, [StringComparison]::Ordinal)) { throw "FullChain lane member at index $index must be '$id' but was '$($member.memberId)'." }
        if (-not [string]::Equals([string]$member.outcome, 'passed', [StringComparison]::Ordinal)) { throw "FullChain lane member '$id' has outcome '$($member.outcome)'." }
        if (-not [string]::Equals([string]$member.cleanup, 'passed', [StringComparison]::Ordinal)) { throw "FullChain lane member '$id' has cleanup '$($member.cleanup)'." }
        if (-not [string]::Equals([string]$member.dependencyEvidence, 'passed', [StringComparison]::Ordinal)) { throw "FullChain lane member '$id' has dependency evidence '$($member.dependencyEvidence)'." }
        if (-not ([string]$member.diagnosticEvidence).EndsWith('-verified', [StringComparison]::Ordinal)) { throw "FullChain lane member '$id' has unverified diagnostic evidence '$($member.diagnosticEvidence)'." }
        if ([int]$member.expected -ne 1 -or [int]$member.discovered -ne 1) { throw "FullChain lane member '$id' expected 1 test but discovered $($member.discovered)." }
        if ([int]$member.passed -ne 1 -or [int]$member.failed -ne 0 -or [int]$member.skipped -ne 0) { throw "FullChain lane member '$id' expected 1 passed, 0 failed and 0 skipped; observed $($member.passed) passed, $($member.failed) failed and $($member.skipped) skipped." }
    }
}
