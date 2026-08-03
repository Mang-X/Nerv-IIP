# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads test policy, C# test sources, and VSTest evidence
#   Writes:
#     - None; callers own all evidence output paths
#   Cleanup:
#     - No process or external resource ownership
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest

function New-NervTestEvidenceViolation {
    param([string] $Code, [string] $Id, [string] $Message)
    [pscustomobject]@{ code = $Code; id = $Id; message = $Message }
}

function Test-NervTestEvidenceLaneName {
    param([Parameter(Mandatory)] [string] $Lane)
    if ($Lane.Contains('-shard-', [StringComparison]::Ordinal)) {
        return $Lane -cmatch '^[a-z0-9]+(?:-[a-z0-9]+)*-shard-[1-9][0-9]*$'
    }
    return $Lane -cmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$'
}

function Import-NervTestEvidencePolicy {
    param([Parameter(Mandatory)] [string] $Path)
    $policy = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 100
    if ([int] $policy.schemaVersion -ne 1) {
        throw "Unsupported test-evidence policy schemaVersion '$($policy.schemaVersion)'."
    }
    return $policy
}

function Get-NervSourceSkipAssignments {
    param([Parameter(Mandatory)] [string] $RepoRoot)

    $roots = @(
        (Join-Path $RepoRoot 'backend/tests'),
        (Join-Path $RepoRoot 'backend/services'),
        (Join-Path $RepoRoot 'connector-hosts/tests')
    )
    $files = foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        Get-ChildItem -LiteralPath $root -Filter '*.cs' -File -Recurse | Where-Object {
            $relative = [IO.Path]::GetRelativePath($RepoRoot, $_.FullName).Replace('\', '/')
            $relative -match '^(backend/tests/|backend/services/[^/]+/tests/|backend/services/Business/[^/]+/tests/|connector-hosts/tests/)'
        }
    }

    $results = foreach ($file in @($files | Sort-Object FullName -Unique)) {
        $relative = [IO.Path]::GetRelativePath($RepoRoot, $file.FullName).Replace('\', '/')
        $content = Get-Content -LiteralPath $file.FullName -Raw
        $starts = @([regex]::Matches($content, '\bSkip\s*=') | ForEach-Object Index)
        for ($index = 0; $index -lt $starts.Count; $index++) {
            $start = [int]$starts[$index]
            $position = $start
            $quote = [char]0
            $escaped = $false
            $verbatim = $false
            while ($position -lt $content.Length) {
                $character = $content[$position]
                if ($quote -ne [char]0) {
                    if ($verbatim -and $character -eq '"' -and $position + 1 -lt $content.Length -and $content[$position + 1] -eq '"') {
                        $position += 2
                        continue
                    }
                    if (-not $verbatim -and $character -eq '\' -and -not $escaped) {
                        $escaped = $true
                        $position++
                        continue
                    }
                    if ($character -eq $quote -and -not $escaped) {
                        $quote = [char]0
                        $verbatim = $false
                    }
                    $escaped = $false
                }
                elseif ($character -eq '"' -or $character -eq "'") {
                    $quote = $character
                    $verbatim = $character -eq '"' -and $position -gt 0 -and $content[$position - 1] -eq '@'
                }
                elseif ($character -eq ';') {
                    break
                }
                $position++
            }
            if ($position -ge $content.Length) { continue }
            $sourceText = [regex]::Replace($content.Substring($start, $position - $start + 1), '\s+', ' ').Trim()
            [pscustomobject]@{
                sourcePath = $relative
                sourceOrdinal = $index + 1
                sourceText = $sourceText
            }
        }
    }
    @($results)
}

function Test-NervTestEvidencePolicy {
    param(
        [Parameter(Mandatory)] [object] $Policy,
        [Parameter(Mandatory)] [string] $RepoRoot,
        [Parameter(Mandatory)] [DateTimeOffset] $AsOfUtc
    )

    $violations = [Collections.Generic.List[object]]::new()
    $classifications = @('optional', 'environment-gated', 'quarantined')
    foreach ($kind in @('sources', 'rules')) {
        $duplicates = @($Policy.$kind | Group-Object id | Where-Object Count -gt 1)
        foreach ($duplicate in $duplicates) {
            $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' $duplicate.Name "Duplicate $kind id '$($duplicate.Name)'."))
        }
    }
    foreach ($lane in @($Policy.lanes)) {
        try { [void][regex]::new([string]$lane.namePattern) }
        catch { $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$lane.namePattern) 'Invalid lane pattern.')) }
    }
    foreach ($rule in @($Policy.rules)) {
        $sourceMatches = @($Policy.sources | Where-Object { [string]$_.id -ceq [string]$rule.sourceId })
        if ($sourceMatches.Count -ne 1) {
            $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$rule.id) 'Rule sourceId must resolve to exactly one registered source.'))
        }
        if ($classifications -notcontains [string]$rule.classification) {
            $violations.Add((New-NervTestEvidenceViolation 'illegal-quarantine' ([string]$rule.id) "Unknown classification '$($rule.classification)'."))
            continue
        }
        foreach ($patternName in @('testPattern', 'reasonPattern')) {
            $pattern = [string]$rule.$patternName
            if (-not ($pattern.StartsWith('^') -and $pattern.EndsWith('$'))) {
                $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$rule.id) "$patternName must be fully anchored."))
                continue
            }
            try { [void][regex]::new($pattern) }
            catch { $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$rule.id) "Invalid $patternName.")) }
        }
        $identities = if ($rule.PSObject.Properties.Name -contains 'testIdentities') { @($rule.testIdentities) } else { @() }
        $expectedCount = if ($rule.PSObject.Properties.Name -contains 'expectedRuntimeTestCount') { [int]$rule.expectedRuntimeTestCount } else { 0 }
        if (@($identities).Count -eq 0 -or $expectedCount -ne @($identities).Count -or @($identities | Sort-Object -Unique).Count -ne @($identities).Count) {
            $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$rule.id) 'Rule must freeze a non-empty unique test identity set and exact expectedRuntimeTestCount.'))
        }
        foreach ($identity in $identities) {
            if ([string]$identity -cnotmatch [string]$rule.testPattern) {
                $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$rule.id) "Frozen test identity '$identity' does not match testPattern."))
            }
        }
        foreach ($laneName in @($rule.allowedLanes) + @($rule.requiredLane | Where-Object { $_ })) {
            if (-not (Test-NervTestEvidenceLaneName ([string]$laneName))) {
                $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$rule.id) "Invalid lane '$laneName'."))
            }
        }
        if ([string]$rule.classification -eq 'quarantined') {
            $date = [DateTimeOffset]::MinValue
            $validDate = [DateTimeOffset]::TryParseExact(
                [string]$rule.expiresOn,
                'yyyy-MM-dd',
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::AssumeUniversal,
                [ref]$date)
            if ([string]::IsNullOrWhiteSpace([string]$rule.responsibilityIssue) -or
                [string]::IsNullOrWhiteSpace([string]$rule.exitCondition) -or
                -not $validDate -or $date.Date -lt $AsOfUtc.UtcDateTime.Date) {
                $violations.Add((New-NervTestEvidenceViolation 'illegal-quarantine' ([string]$rule.id) 'Quarantine requires issue, valid unexpired ISO date, and exit condition.'))
            }
        }
    }

    $live = @(Get-NervSourceSkipAssignments -RepoRoot $RepoRoot)
    foreach ($assignment in $live) {
        $matches = @($Policy.sources | Where-Object {
            [string]$_.sourcePath -ceq [string]$assignment.sourcePath -and
            [int]$_.sourceOrdinal -eq [int]$assignment.sourceOrdinal -and
            [string]$assignment.sourceText -cmatch [string]$_.sourceReasonPattern
        })
        if ($matches.Count -ne 1) {
            $id = "$($assignment.sourcePath):$($assignment.sourceOrdinal)"
            $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' $id 'Source Skip assignment is missing, duplicated, or reason-mismatched.'))
        }
    }
    foreach ($source in @($Policy.sources)) {
        if (@($Policy.rules | Where-Object { [string]$_.sourceId -ceq [string]$source.id }).Count -eq 0) {
            $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$source.id) 'Registered source is not referenced by any runtime rule.'))
        }
        $matches = @($live | Where-Object {
            [string]$_.sourcePath -ceq [string]$source.sourcePath -and
            [int]$_.sourceOrdinal -eq [int]$source.sourceOrdinal
        })
        if ($matches.Count -ne 1) {
            $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$source.id) 'Registered source does not map to exactly one live Skip assignment.'))
        }
    }
    @($violations)
}

function Get-NervTrxSkipReason {
    param([Parameter(Mandatory)] [Xml.XmlElement] $UnitTestResult)

    $message = $UnitTestResult.SelectSingleNode("./*[local-name()='Output']/*[local-name()='ErrorInfo']/*[local-name()='Message']")
    if ($null -ne $message -and -not [string]::IsNullOrWhiteSpace($message.InnerText)) {
        return $message.InnerText.Trim()
    }
    $stdout = $UnitTestResult.SelectSingleNode("./*[local-name()='Output']/*[local-name()='StdOut']")
    if ($null -ne $stdout) {
        foreach ($line in ($stdout.InnerText -split '\r?\n')) {
            if (-not [string]::IsNullOrWhiteSpace($line) -and $line.Contains('SKIP', [StringComparison]::OrdinalIgnoreCase)) {
                return $line.Trim()
            }
        }
    }
    return $null
}

function Get-NervStableEvidenceGuid {
    param([Parameter(Mandatory)] [string] $Value)
    $bytes = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Value))
    $guidBytes = [byte[]]::new(16)
    [Array]::Copy($bytes, $guidBytes, 16)
    ([Guid]::new($guidBytes)).ToString()
}

function ConvertTo-NervRetainedFailureText {
    param([AllowNull()] [string] $Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    return 'Test failed; raw failure details are intentionally omitted by evidence privacy policy.'
}

function Get-NervRetainedSkipReason {
    param([Parameter(Mandatory)] [object] $Record)
    if (-not ($Record.PSObject.Properties.Name -contains 'skipPolicyId') -or [string]::IsNullOrWhiteSpace([string]$Record.skipPolicyId)) {
        return 'Skipped; raw reason omitted because no approved policy matched.'
    }
    $safe = Protect-NervTestEvidenceText ([string]$Record.skipReason)
    if ($safe.Length -gt 512) { return $safe.Substring(0, 512) }
    return $safe
}

function Read-NervTrxResults {
    param(
        [Parameter(Mandatory)] [string[]] $Path,
        [Parameter(Mandatory)] [hashtable] $RunMetadata
    )

    if (-not (Test-NervTestEvidenceLaneName ([string]$RunMetadata.lane))) {
        throw "Invalid evidence lane '$($RunMetadata.lane)'."
    }
    $outcomeMap = @{ Passed = 'passed'; Failed = 'failed'; NotExecuted = 'skipped' }
    $records = [Collections.Generic.List[object]]::new()
    $trxElapsedMilliseconds = 0.0
    $trxRuns = [Collections.Generic.List[object]]::new()
    foreach ($trxPath in @($Path | Sort-Object)) {
        try {
            $document = [Xml.XmlDocument]::new()
            $document.PreserveWhitespace = $false
            $document.Load($trxPath)
        }
        catch {
            $safePath = [IO.Path]::GetFullPath($trxPath)
            throw [IO.InvalidDataException]::new("Failed to parse TRX '$safePath'.")
        }

        $times = $document.SelectSingleNode("//*[local-name()='Times']")
        if ($null -eq $times -or [string]::IsNullOrWhiteSpace([string]$times.start) -or [string]::IsNullOrWhiteSpace([string]$times.finish)) {
            throw [IO.InvalidDataException]::new("TRX is missing valid Times metadata in '$([IO.Path]::GetFullPath($trxPath))'.")
        }
        $start = [DateTimeOffset]::MinValue
        $finish = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParse([string]$times.start, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$start) -or
            -not [DateTimeOffset]::TryParse([string]$times.finish, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$finish) -or $finish -lt $start) {
            throw [IO.InvalidDataException]::new("TRX has invalid Times metadata in '$([IO.Path]::GetFullPath($trxPath))'.")
        }
        $elapsed = [double]($finish - $start).TotalMilliseconds
        $trxElapsedMilliseconds += $elapsed

        $definitions = @{}
        foreach ($definition in @($document.SelectNodes("//*[local-name()='TestDefinitions']/*[local-name()='UnitTest']"))) {
            $method = $definition.SelectSingleNode("./*[local-name()='TestMethod']")
            $assembly = [IO.Path]::GetFileName([string]$definition.storage)
            $testName = if ($null -ne $method -and -not [string]::IsNullOrWhiteSpace([string]$method.className)) {
                "$($method.className).$($method.name)"
            }
            else { [string]$definition.name }
            $definitions[[string]$definition.id] = [pscustomobject]@{
                assembly = $assembly
                testName = $testName
                className = if ($null -ne $method) { [string]$method.className } else { '' }
                methodName = if ($null -ne $method) { [string]$method.name } else { [string]$definition.name }
            }
        }

        $results = @($document.SelectNodes("//*[local-name()='Results']/*[local-name()='UnitTestResult']"))
        $counters = $document.SelectSingleNode("//*[local-name()='ResultSummary']/*[local-name()='Counters']")
        if ($null -eq $counters) { throw [IO.InvalidDataException]::new("TRX is missing ResultSummary/Counters in '$([IO.Path]::GetFullPath($trxPath))'.") }
        $counterTotal = [int]$counters.total
        $counterExecuted = [int]$counters.executed
        $counterPassed = [int]$counters.passed
        $counterFailed = [int]$counters.failed
        $counterSkipped = $counterTotal - $counterExecuted
        $actualPassed = @($results | Where-Object outcome -ceq 'Passed').Count
        $actualFailed = @($results | Where-Object outcome -ceq 'Failed').Count
        $actualSkipped = @($results | Where-Object outcome -ceq 'NotExecuted').Count
        if ($counterTotal -ne $results.Count -or $counterExecuted -ne ($counterPassed + $counterFailed) -or
            $counterPassed -ne $actualPassed -or $counterFailed -ne $actualFailed -or $counterSkipped -ne $actualSkipped) {
            throw [IO.InvalidDataException]::new("TRX ResultSummary/Counters do not match Results in '$([IO.Path]::GetFullPath($trxPath))'.")
        }
        $assembliesInRun = @($definitions.Values | ForEach-Object { [string]$_.assembly } | Sort-Object -Unique)
        if ($assembliesInRun.Count -gt 1) { throw [IO.InvalidDataException]::new("TRX contains multiple assemblies in '$([IO.Path]::GetFullPath($trxPath))'.") }
        $trxRuns.Add([pscustomobject][ordered]@{
            lane = [string]$RunMetadata.lane
            assembly = if ($assembliesInRun.Count -eq 1) { [string]$assembliesInRun[0] } else { [IO.Path]::GetFileNameWithoutExtension($trxPath) }
            elapsedMilliseconds = $elapsed
            total = $counterTotal
            executed = $counterExecuted
            passed = $counterPassed
            failed = $counterFailed
            skipped = $counterSkipped
        })

        $ordinals = @{}
        foreach ($result in $results) {
            $rawOutcome = [string]$result.outcome
            if (-not $outcomeMap.ContainsKey($rawOutcome)) {
                throw [IO.InvalidDataException]::new("Unsupported TRX outcome '$rawOutcome' in '$([IO.Path]::GetFullPath($trxPath))'.")
            }
            $definition = $definitions[[string]$result.testId]
            if ($null -eq $definition) {
                throw [IO.InvalidDataException]::new("TRX result references an unknown test definition in '$([IO.Path]::GetFullPath($trxPath))'.")
            }
            $duration = [TimeSpan]::Zero
            if (-not [string]::IsNullOrWhiteSpace([string]$result.duration)) {
                $duration = [TimeSpan]::Parse([string]$result.duration, [Globalization.CultureInfo]::InvariantCulture)
            }
            $displayName = Protect-NervTestEvidenceText $result.GetAttribute('testName')
            if ([string]::IsNullOrWhiteSpace($displayName)) { $displayName = [string]$definition.testName }
            if ($displayName.Length -gt 512) { $displayName = $displayName.Substring(0, 512) }
            $ordinalKey = "$($definition.testName)|$displayName"
            $ordinal = if ($ordinals.ContainsKey($ordinalKey)) { [int]$ordinals[$ordinalKey] + 1 } else { 1 }
            $ordinals[$ordinalKey] = $ordinal
            $rawError = if ($rawOutcome -eq 'Failed') {
                $node = $result.SelectSingleNode("./*[local-name()='Output']/*[local-name()='ErrorInfo']/*[local-name()='Message']")
                if ($null -ne $node) { $node.InnerText.Trim() } else { $null }
            } else { $null }
            $records.Add([pscustomobject][ordered]@{
                schemaVersion = 1
                workflowRunId = [string]$RunMetadata.workflowRunId
                runAttempt = [int]$RunMetadata.runAttempt
                commitSha = [string]$RunMetadata.commitSha
                lane = [string]$RunMetadata.lane
                project = [IO.Path]::GetFileNameWithoutExtension([string]$definition.assembly)
                assembly = [string]$definition.assembly
                testName = [string]$definition.testName
                displayName = $displayName
                testClassName = [string]$definition.className
                testMethodName = [string]$definition.methodName
                definitionId = Get-NervStableEvidenceGuid "$($definition.assembly)|$($definition.testName)"
                testInstanceId = Get-NervStableEvidenceGuid "$($definition.assembly)|$($definition.testName)|$displayName|$ordinal"
                durationMilliseconds = [double]$duration.TotalMilliseconds
                outcome = [string]$outcomeMap[$rawOutcome]
                skipReason = if ($rawOutcome -eq 'NotExecuted') { Get-NervTrxSkipReason -UnitTestResult $result } else { $null }
                errorMessage = ConvertTo-NervRetainedFailureText $rawError
                redactionCount = if ([string]::IsNullOrWhiteSpace($rawError)) { 0 } else { 1 }
            })
        }
    }
    $RunMetadata.trxElapsedMilliseconds = [double]$trxElapsedMilliseconds
    $RunMetadata.trxRuns = @($trxRuns)
    @($records)
}

function Test-NervRuleApplies {
    param(
        [Parameter(Mandatory)] [object] $Rule,
        [Parameter(Mandatory)] [string[]] $SelectedLanes,
        [Parameter(Mandatory)] [string] $RunnerOs
    )

    $baseLanes = @($SelectedLanes | ForEach-Object { $_ -replace '-shard-[1-9][0-9]*$', '' })
    if (@($Rule.allowedLanes).Count -gt 0 -and @($baseLanes | Where-Object { @($Rule.allowedLanes) -ccontains $_ }).Count -eq 0) {
        return $false
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$Rule.requiredLane) -and @($baseLanes) -ccontains [string]$Rule.requiredLane) {
        return $false
    }
    if (@($Rule.allowedOperatingSystems).Count -gt 0 -and -not (@($Rule.allowedOperatingSystems) -ccontains $RunnerOs)) {
        return $false
    }
    return $true
}

function Get-NervTestEvidenceViolations {
    param(
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyCollection()] [object[]] $Records,
        [Parameter(Mandatory)] [object] $Policy,
        [Parameter(Mandatory)] [string[]] $SelectedLanes,
        [Parameter(Mandatory)] [string] $RunnerOs
    )

    $violations = [Collections.Generic.List[object]]::new()
    $safeRecords = if ($null -eq $Records) { @() } else { @($Records) }
    foreach ($rule in @($Policy.rules | Where-Object classification -eq 'quarantined')) {
        $expiry = [DateTimeOffset]::MinValue
        $validDate = [DateTimeOffset]::TryParseExact(
            [string]$rule.expiresOn, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$expiry)
        if ([string]::IsNullOrWhiteSpace([string]$rule.responsibilityIssue) -or
            [string]::IsNullOrWhiteSpace([string]$rule.exitCondition) -or
            -not $validDate -or $expiry.Date -lt [DateTimeOffset]::UtcNow.UtcDateTime.Date) {
            $violations.Add((New-NervTestEvidenceViolation 'illegal-quarantine' ([string]$rule.id) 'Quarantine metadata is missing, invalid, or expired.'))
        }
    }

    foreach ($record in @($safeRecords | Where-Object outcome -eq 'skipped')) {
        $matches = @($Policy.rules | Where-Object {
            @($_.testIdentities) -ccontains [string]$record.testName -and
            [string]$record.testName -cmatch [string]$_.testPattern -and
            [string]$record.skipReason -cmatch [string]$_.reasonPattern -and
            (Test-NervRuleApplies -Rule $_ -SelectedLanes $SelectedLanes -RunnerOs $RunnerOs)
        })
        if ($matches.Count -ne 1) {
            $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$record.testName) "Runtime skip matched $($matches.Count) applicable rules."))
        }
        else {
            $record | Add-Member -NotePropertyName skipClassification -NotePropertyValue ([string]$matches[0].classification) -Force
            $record | Add-Member -NotePropertyName skipPolicyId -NotePropertyValue ([string]$matches[0].id) -Force
        }
    }

    foreach ($selectedLane in $SelectedLanes) {
        $laneMatches = @($Policy.lanes | Where-Object { $selectedLane -cmatch [string]$_.namePattern })
        if ($laneMatches.Count -ne 1) {
            $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' $selectedLane "Selected lane matched $($laneMatches.Count) lane contracts."))
            continue
        }
        if ([bool]$laneMatches[0].realDependency) {
            $executed = @($safeRecords | Where-Object {
                $_.outcome -in @('passed', 'failed') -and
                [string]$_.lane -ceq $selectedLane
            }).Count
            if ($executed -eq 0) {
                $violations.Add((New-NervTestEvidenceViolation 'zero-execution' $selectedLane 'Selected real-dependency lane executed no passed or failed tests.'))
            }
        }
    }
    @($violations)
}

function Protect-NervTestEvidenceText {
    param([AllowNull()] [string] $Text)
    Protect-ScriptAutomationText -Text $Text
}

function New-NervTestEvidenceSummary {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Records,
        [Parameter(Mandatory)] [hashtable] $RunMetadata,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyCollection()] [object[]] $Violations,
        [AllowNull()] [object] $Baseline,
        [AllowNull()] [string] $PriorAttemptOutcome,
        [int] $TopCount = 10
    )

    [object[]] $safeRecords = @($Records)
    [object[]] $safeViolations = @()
    if ($null -ne $Violations) { $safeViolations = @($Violations) }
    $passed = @($safeRecords | Where-Object outcome -eq 'passed').Count
    $failed = @($safeRecords | Where-Object outcome -eq 'failed').Count
    $skipped = @($safeRecords | Where-Object outcome -eq 'skipped').Count
    $trxRuns = if ($RunMetadata.ContainsKey('trxRuns')) { @($RunMetadata.trxRuns) } else { @() }
    $assemblies = @($safeRecords | Group-Object lane, assembly | Sort-Object Name | ForEach-Object {
        $items = @($_.Group)
        $laneName = [string]$items[0].lane
        $assemblyName = [string]$items[0].assembly
        $runRows = @($trxRuns | Where-Object { [string]$_.lane -ceq $laneName -and [string]$_.assembly -ceq $assemblyName })
        [pscustomobject][ordered]@{
            lane = $laneName
            assembly = $assemblyName
            passed = @($items | Where-Object outcome -eq 'passed').Count
            failed = @($items | Where-Object outcome -eq 'failed').Count
            skipped = @($items | Where-Object outcome -eq 'skipped').Count
            executed = @($items | Where-Object { $_.outcome -in @('passed', 'failed') }).Count
            total = $items.Count
            testDurationMilliseconds = [double](($items | Measure-Object durationMilliseconds -Sum).Sum)
            elapsedMilliseconds = if (@($runRows).Count -gt 0) { [double](($runRows | Measure-Object elapsedMilliseconds -Sum).Sum) } else { 0.0 }
        }
    })
    $baselineAssemblies = if ($null -ne $Baseline -and $Baseline.PSObject.Properties.Name -contains 'assemblies') { @($Baseline.assemblies) } else { @() }
    $deltas = @($assemblies | ForEach-Object {
        $current = $_
        $compatible = $null -ne $Baseline -and $Baseline.PSObject.Properties.Name -contains 'granularity' -and $Baseline.PSObject.Properties.Name -contains 'durationMetric' -and [string]$Baseline.granularity -ceq 'test' -and [string]$Baseline.durationMetric -ceq 'trx-elapsed'
        [object[]]$previous = if ($compatible) { @($baselineAssemblies | Where-Object { [string]$_.lane -ceq $current.lane -and [string]$_.assembly -ceq $current.assembly } | Select-Object -First 1) } else { @() }
        $baselineDuration = if (@($previous).Count -eq 1) { [double]@($previous)[0].elapsedMilliseconds } else { $null }
        [pscustomobject][ordered]@{
            lane = $current.lane
            assembly = $current.assembly
            metric = 'trx-elapsed'
            available = @($previous).Count -eq 1
            baselineDurationMilliseconds = $baselineDuration
            currentDurationMilliseconds = [double]$current.elapsedMilliseconds
            deltaPercent = if ($null -ne $baselineDuration -and $baselineDuration -gt 0) { [Math]::Round((([double]$current.elapsedMilliseconds - $baselineDuration) / $baselineDuration) * 100, 2) } else { $null }
        }
    })
    $attemptClassification = if ([int]$RunMetadata.runAttempt -eq 1) {
        'initial'
    }
    elseif ($PriorAttemptOutcome -eq 'failure' -and $RunMetadata.ContainsKey('priorAttemptVerified') -and [bool]$RunMetadata.priorAttemptVerified -and
        $RunMetadata.ContainsKey('currentTestOutcome') -and [string]$RunMetadata.currentTestOutcome -ceq 'success' -and
        ($passed + $failed) -gt 0 -and $failed -eq 0 -and $safeViolations.Count -eq 0) {
        'recovered-after-rerun'
    }
    else { 'rerun' }
    $priorStatus = if ([string]::IsNullOrWhiteSpace($PriorAttemptOutcome)) { 'prior-attempt-unavailable' } else { $PriorAttemptOutcome }

    [pscustomobject][ordered]@{
        schemaVersion = 1
        workflowRunId = [string]$RunMetadata.workflowRunId
        runAttempt = [int]$RunMetadata.runAttempt
        commitSha = [string]$RunMetadata.commitSha
        lane = [string]$RunMetadata.lane
        repository = if ($RunMetadata.ContainsKey('repository')) { [string]$RunMetadata.repository } else { '' }
        event = if ($RunMetadata.ContainsKey('event')) { [string]$RunMetadata.event } else { '' }
        headBranch = if ($RunMetadata.ContainsKey('headBranch')) { [string]$RunMetadata.headBranch } else { '' }
        jobName = if ($RunMetadata.ContainsKey('jobName')) { [string]$RunMetadata.jobName } else { '' }
        currentTestOutcome = if ($RunMetadata.ContainsKey('currentTestOutcome')) { [string]$RunMetadata.currentTestOutcome } else { '' }
        sourceUrl = if ($RunMetadata.ContainsKey('sourceUrl')) { [string]$RunMetadata.sourceUrl } else { '' }
        runnerOs = if ($RunMetadata.ContainsKey('runnerOs')) { [string]$RunMetadata.runnerOs } else { '' }
        runnerImage = if ($RunMetadata.ContainsKey('runnerImage')) { [string]$RunMetadata.runnerImage } else { '' }
        dotnetSdk = if ($RunMetadata.ContainsKey('dotnetSdk')) { [string]$RunMetadata.dotnetSdk } else { '' }
        artifactName = if ($RunMetadata.ContainsKey('artifactName')) { [string]$RunMetadata.artifactName } else { '' }
        retentionDays = if ($RunMetadata.ContainsKey('retentionDays')) { [int]$RunMetadata.retentionDays } else { 0 }
        passed = $passed
        failed = $failed
        skipped = $skipped
        executed = $passed + $failed
        total = $safeRecords.Count
        testDurationMilliseconds = if ($safeRecords.Count -gt 0) { [double](($safeRecords | Measure-Object durationMilliseconds -Sum).Sum) } else { 0.0 }
        trxElapsedMilliseconds = if ($RunMetadata.ContainsKey('trxElapsedMilliseconds')) { [double]$RunMetadata.trxElapsedMilliseconds } else { $null }
        assemblies = $assemblies
        slowestAssemblies = @($assemblies | Sort-Object @{ Expression = 'elapsedMilliseconds'; Descending = $true }, @{ Expression = 'assembly'; Descending = $false } | Select-Object -First $TopCount)
        slowestTests = @($safeRecords | Sort-Object @{ Expression = 'durationMilliseconds'; Descending = $true }, @{ Expression = 'testName'; Descending = $false } | Select-Object -First $TopCount | ForEach-Object { [pscustomobject]@{ lane = $_.lane; testName = $_.testName; displayName = $_.displayName; assembly = $_.assembly; durationMilliseconds = $_.durationMilliseconds } })
        skipReasons = @($safeRecords | Where-Object outcome -eq 'skipped' | Group-Object { Get-NervRetainedSkipReason $_ } | Sort-Object Name | ForEach-Object { [pscustomobject]@{ reason = $_.Name; count = $_.Count } })
        skipClassifications = @($safeRecords | Where-Object { $_.outcome -eq 'skipped' -and $_.PSObject.Properties.Name -contains 'skipClassification' } | Group-Object skipClassification | Sort-Object Name | ForEach-Object { [pscustomobject]@{ classification = $_.Name; count = $_.Count } })
        skipPolicies = @($safeRecords | Where-Object { $_.outcome -eq 'skipped' -and $_.PSObject.Properties.Name -contains 'skipPolicyId' } | Group-Object skipPolicyId | Sort-Object Name | ForEach-Object {
            [pscustomobject]@{ policyId = $_.Name; classification = [string]$_.Group[0].skipClassification; count = $_.Count }
        })
        violations = $safeViolations
        redactionCount = $(if ($safeRecords.Count -gt 0) { [int](($safeRecords | Measure-Object redactionCount -Sum).Sum) } else { 0 }) + @($safeRecords | Where-Object { $_.outcome -eq 'skipped' -and (-not ($_.PSObject.Properties.Name -contains 'skipPolicyId') -or [string]::IsNullOrWhiteSpace([string]$_.skipPolicyId)) }).Count
        baseline = [pscustomobject][ordered]@{
            enforcement = 'report-only'
            source = if ($null -ne $Baseline -and $Baseline.PSObject.Properties.Name -contains 'source') { $Baseline.source } else { $null }
            assemblies = $deltas
        }
        priorAttemptStatus = $priorStatus
        attemptClassification = $attemptClassification
    }
}

function Write-NervUtf8NoBom {
    param([string] $Path, [AllowNull()] [string] $Text)
    [IO.File]::WriteAllText($Path, $(if ($null -eq $Text) { '' } else { $Text }), [Text.UTF8Encoding]::new($false))
}

function Write-NervTestEvidenceArtifacts {
    param(
        [Parameter(Mandatory)] [object[]] $Records,
        [Parameter(Mandatory)] [object] $Summary,
        [Parameter(Mandatory)] [string[]] $SourceTrxPaths,
        [Parameter(Mandatory)] [string] $OutputDirectory
    )

    $parent = Split-Path -Parent $OutputDirectory
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    if (Test-Path -LiteralPath $OutputDirectory) { throw "Evidence output already exists: '$OutputDirectory'." }
    $temporary = "$OutputDirectory.tmp-$([Guid]::NewGuid().ToString('N'))"
    try {
        [IO.Directory]::CreateDirectory($temporary) | Out-Null
        [IO.Directory]::CreateDirectory((Join-Path $temporary 'trx')) | Out-Null
        $recordLines = foreach ($record in @($Records | Sort-Object assembly, testName)) {
            $safeRecord = [ordered]@{
                schemaVersion = [int]$record.schemaVersion
                workflowRunId = [string]$record.workflowRunId
                runAttempt = [int]$record.runAttempt
                commitSha = [string]$record.commitSha
                lane = [string]$record.lane
                project = [string]$record.project
                assembly = [string]$record.assembly
                testName = [string]$record.testName
                displayName = [string]$record.displayName
                testClassName = [string]$record.testClassName
                testMethodName = [string]$record.testMethodName
                definitionId = [string]$record.definitionId
                testInstanceId = [string]$record.testInstanceId
                durationMilliseconds = [double]$record.durationMilliseconds
                outcome = [string]$record.outcome
                skipReason = if ($record.outcome -eq 'skipped') { Get-NervRetainedSkipReason $record } else { $null }
                skipClassification = if ($record.PSObject.Properties.Name -contains 'skipClassification') { [string]$record.skipClassification } else { $null }
                skipPolicyId = if ($record.PSObject.Properties.Name -contains 'skipPolicyId') { [string]$record.skipPolicyId } else { $null }
                redactionCount = [int]$record.redactionCount
            }
            $safeRecord | ConvertTo-Json -Compress -Depth 20
        }
        Write-NervUtf8NoBom (Join-Path $temporary 'tests.jsonl') ([string]::Join("`n", @($recordLines)) + $(if (@($recordLines).Count -gt 0) { "`n" } else { '' }))
        $safeSummaryJson = Protect-NervTestEvidenceText ($Summary | ConvertTo-Json -Depth 100)
        Write-NervUtf8NoBom (Join-Path $temporary 'summary.json') ($safeSummaryJson + "`n")
        $baselineSource = if ($null -ne $Summary.baseline.source) { [string]$Summary.baseline.source.sourceUrl } else { 'unavailable' }
        $markdown = @(
            "# Test evidence: $($Summary.lane)",
            '',
            "- Run: $($Summary.workflowRunId), attempt $($Summary.runAttempt), commit $($Summary.commitSha)",
            "- Counts: passed=$($Summary.passed), failed=$($Summary.failed), skipped=$($Summary.skipped), executed=$($Summary.executed), total=$($Summary.total)",
            "- Duration: summed tests=$($Summary.testDurationMilliseconds)ms, TRX elapsed=$($Summary.trxElapsedMilliseconds)ms",
            "- Attempt: $($Summary.attemptClassification) (prior: $($Summary.priorAttemptStatus))",
            "- Provenance: job=$($Summary.jobName), outcome=$($Summary.currentTestOutcome), runner=$($Summary.runnerOs)/$($Summary.runnerImage), dotnet=$($Summary.dotnetSdk)",
            "- Baseline source: $baselineSource",
            "- Privacy redactions: $($Summary.redactionCount)",
            '- Timing and baseline deltas: report-only',
            "- Retained artifact: $($Summary.artifactName), retention=$($Summary.retentionDays) days; tests.jsonl, summary.json, summary.md, diagnostics.log, normalized trx/",
            '',
            '## Assemblies',
            '',
            '| Lane | Assembly | Passed | Failed | Skipped | Test duration (ms) | TRX elapsed (ms) |',
            '|---|---|---:|---:|---:|---:|---:|'
        )
        foreach ($assembly in @($Summary.assemblies)) {
            $markdown += "| $($assembly.lane) | $($assembly.assembly) | $($assembly.passed) | $($assembly.failed) | $($assembly.skipped) | $($assembly.testDurationMilliseconds) | $($assembly.elapsedMilliseconds) |"
        }
        $markdown += @(
            '',
            '## Slowest assemblies and tests',
            ''
        )
        foreach ($assembly in @($Summary.slowestAssemblies)) { $markdown += "- Assembly $($assembly.lane)/$($assembly.assembly): $($assembly.elapsedMilliseconds)ms elapsed" }
        foreach ($test in @($Summary.slowestTests)) { $markdown += "- Test $($test.testName): $($test.durationMilliseconds)ms" }
        $markdown += @(
            '',
            '## Skip reasons',
            ''
        )
        if (@($Summary.skipReasons).Count -eq 0) { $markdown += '- None.' }
        foreach ($reason in @($Summary.skipReasons)) { $markdown += "- $($reason.reason): $($reason.count)" }
        $markdown += @(
            '',
            '## Skip policy matches',
            ''
        )
        if (@($Summary.skipPolicies).Count -eq 0) {
            $markdown += '- No registered runtime skips.'
        }
        foreach ($policyMatch in @($Summary.skipPolicies)) {
            $markdown += "- $($policyMatch.classification) / $($policyMatch.policyId): $($policyMatch.count)"
        }
        $markdown += @(
            '',
            '## Evidence policy gates',
            ''
        )
        if (@($Summary.violations).Count -eq 0) {
            $markdown += '- unregistered-skip: pass'
            $markdown += '- illegal-quarantine: pass'
            $markdown += '- zero-execution: pass'
        }
        else {
            foreach ($gateName in @('unregistered-skip', 'illegal-quarantine', 'zero-execution')) {
                $markdown += "- $gateName`: $(@($Summary.violations | Where-Object code -eq $gateName).Count) violation(s)"
            }
        }
        $markdown += @(
            '',
            '## Assembly baseline deltas',
            ''
        )
        foreach ($delta in @($Summary.baseline.assemblies)) {
            $markdown += "- $($delta.assembly): current=$($delta.currentDurationMilliseconds)ms, baseline=$($delta.baselineDurationMilliseconds)ms, delta=$($delta.deltaPercent)%"
        }
        Write-NervUtf8NoBom (Join-Path $temporary 'summary.md') ((Protect-NervTestEvidenceText ([string]::Join("`n", $markdown))) + "`n")
        Write-NervUtf8NoBom (Join-Path $temporary 'diagnostics.log') ''

        $sha8 = ([string]$Summary.commitSha).Substring(0, [Math]::Min(8, ([string]$Summary.commitSha).Length))
        foreach ($group in @($Records | Group-Object lane, assembly | Sort-Object Name)) {
            $groupRecords = @($group.Group | Sort-Object testName, displayName, testInstanceId)
            $assemblyName = [regex]::Replace([string]$groupRecords[0].assembly, '[^A-Za-z0-9_.-]', '_')
            $fileName = "$($Summary.lane)-$assemblyName-$sha8-attempt-$($Summary.runAttempt).trx"
            $xmlRows = foreach ($record in $groupRecords) {
                $name = [Security.SecurityElement]::Escape([string]$record.displayName)
                $outcome = switch ([string]$record.outcome) { 'passed' { 'Passed' } 'failed' { 'Failed' } default { 'NotExecuted' } }
                $duration = [TimeSpan]::FromMilliseconds([double]$record.durationMilliseconds).ToString('c', [Globalization.CultureInfo]::InvariantCulture)
                $message = if ($record.outcome -eq 'skipped') { Get-NervRetainedSkipReason $record } elseif ($record.outcome -eq 'failed') { ConvertTo-NervRetainedFailureText ([string]$record.errorMessage) } else { $null }
                $output = if ([string]::IsNullOrWhiteSpace($message)) { '' } else { "<Output><ErrorInfo><Message>$([Security.SecurityElement]::Escape($message))</Message></ErrorInfo></Output>" }
                "<UnitTestResult executionId=`"$($record.testInstanceId)`" testId=`"$($record.definitionId)`" testName=`"$name`" duration=`"$duration`" outcome=`"$outcome`">$output</UnitTestResult>"
            }
            $xmlDefinitions = foreach ($definitionGroup in @($groupRecords | Group-Object definitionId | Sort-Object Name)) {
                $record = $definitionGroup.Group[0]
                "<UnitTest id=`"$($record.definitionId)`" name=`"$([Security.SecurityElement]::Escape([string]$record.testName))`" storage=`"$([Security.SecurityElement]::Escape([string]$record.assembly))`"><TestMethod className=`"$([Security.SecurityElement]::Escape([string]$record.testClassName))`" name=`"$([Security.SecurityElement]::Escape([string]$record.testMethodName))`" /></UnitTest>"
            }
            $passedCount = @($groupRecords | Where-Object outcome -eq 'passed').Count
            $failedCount = @($groupRecords | Where-Object outcome -eq 'failed').Count
            $skippedCount = @($groupRecords | Where-Object outcome -eq 'skipped').Count
            $executedCount = $passedCount + $failedCount
            $assemblySummary = @($Summary.assemblies | Where-Object { [string]$_.lane -ceq [string]$groupRecords[0].lane -and [string]$_.assembly -ceq [string]$groupRecords[0].assembly })[0]
            $start = [DateTimeOffset]'2000-01-01T00:00:00Z'
            $finish = $start.AddMilliseconds([double]$assemblySummary.elapsedMilliseconds)
            $runId = Get-NervStableEvidenceGuid "$($Summary.workflowRunId)|$($Summary.runAttempt)|$($groupRecords[0].lane)|$($groupRecords[0].assembly)"
            $safeXml = "<?xml version=`"1.0`" encoding=`"utf-8`"?><TestRun id=`"$runId`" xmlns=`"http://microsoft.com/schemas/VisualStudio/TeamTest/2010`"><Times creation=`"$($start.ToString('o'))`" queuing=`"$($start.ToString('o'))`" start=`"$($start.ToString('o'))`" finish=`"$($finish.ToString('o'))`" /><Results>$([string]::Join('', @($xmlRows)))</Results><TestDefinitions>$([string]::Join('', @($xmlDefinitions)))</TestDefinitions><ResultSummary outcome=`"Completed`"><Counters total=`"$($groupRecords.Count)`" executed=`"$executedCount`" passed=`"$passedCount`" failed=`"$failedCount`" notExecuted=`"$skippedCount`" /></ResultSummary></TestRun>"
            Write-NervUtf8NoBom (Join-Path $temporary "trx/$fileName") $safeXml
        }
        [IO.Directory]::Move($temporary, $OutputDirectory)
    }
    catch {
        if (Test-Path -LiteralPath $temporary) {
            Write-NervUtf8NoBom (Join-Path $temporary 'diagnostics.log') ((Protect-NervTestEvidenceText $_.Exception.Message) + "`n")
        }
        throw
    }
}

function Write-NervTestEvidenceFailureArtifacts {
    param(
        [Parameter(Mandatory)] [string] $OutputDirectory,
        [Parameter(Mandatory)] [hashtable] $RunMetadata,
        [Parameter(Mandatory)] [string] $Diagnostic
    )
    if (Test-Path -LiteralPath $OutputDirectory) { return }
    $parent = Split-Path -Parent $OutputDirectory
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    $temporary = "$OutputDirectory.tmp-$([Guid]::NewGuid().ToString('N'))"
    [IO.Directory]::CreateDirectory($temporary) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $temporary 'trx')) | Out-Null
    $safeDiagnostic = Protect-NervTestEvidenceText $Diagnostic
    if ($safeDiagnostic.Length -gt 1024) { $safeDiagnostic = $safeDiagnostic.Substring(0, 1024) }
    $failure = [pscustomobject][ordered]@{
        schemaVersion = 1
        collectionStatus = 'failed'
        workflowRunId = [string]$RunMetadata.workflowRunId
        runAttempt = [int]$RunMetadata.runAttempt
        commitSha = [string]$RunMetadata.commitSha
        lane = [string]$RunMetadata.lane
        passed = 0; failed = 0; skipped = 0; executed = 0; total = 0
        violations = @([pscustomobject]@{ code = 'evidence-collection-failed'; id = [string]$RunMetadata.lane; message = $safeDiagnostic })
    }
    Write-NervUtf8NoBom (Join-Path $temporary 'tests.jsonl') ''
    Write-NervUtf8NoBom (Join-Path $temporary 'summary.json') (($failure | ConvertTo-Json -Depth 20) + "`n")
    Write-NervUtf8NoBom (Join-Path $temporary 'summary.md') "# Test evidence collection failed`n`n- lane: $($RunMetadata.lane)`n- evidence-collection-failed: $safeDiagnostic`n"
    Write-NervUtf8NoBom (Join-Path $temporary 'diagnostics.log') ($safeDiagnostic + "`n")
    [IO.Directory]::Move($temporary, $OutputDirectory)
}

function ConvertFrom-NervDotNetConsoleSummary {
    param(
        [Parameter(Mandatory)] [string] $Text,
        [Parameter(Mandatory)] [object] $RunMetadata
    )

    $pattern = '(?im)^.*?(?:Passed|Failed)!\s*-\s*Failed:\s*(?<failed>\d+),\s*Passed:\s*(?<passed>\d+),\s*Skipped:\s*(?<skipped>\d+),\s*Total:\s*(?<total>\d+),\s*Duration:\s*(?:(?<minutes>\d+)\s*m\s*)?(?<value>\d+(?:\.\d+)?)\s*(?<unit>ms|s)\s*-\s*(?<assembly>[^\s]+\.dll)\s*\('
    $matches = [regex]::Matches($Text, $pattern)
    if ($matches.Count -eq 0) { throw 'No unambiguous dotnet test project summaries were found.' }
    $assemblies = foreach ($match in $matches) {
        $minutes = if ($match.Groups['minutes'].Success) { [double]$match.Groups['minutes'].Value } else { 0.0 }
        $tailMilliseconds = if ($match.Groups['unit'].Value -ceq 'ms') { [double]$match.Groups['value'].Value } else { [double]$match.Groups['value'].Value * 1000.0 }
        [pscustomobject][ordered]@{
            lane = if ($RunMetadata.ContainsKey('lane')) { [string]$RunMetadata.lane } else { 'backend' }
            assembly = $match.Groups['assembly'].Value
            passed = [int]$match.Groups['passed'].Value
            failed = [int]$match.Groups['failed'].Value
            skipped = [int]$match.Groups['skipped'].Value
            executed = [int]$match.Groups['passed'].Value + [int]$match.Groups['failed'].Value
            total = [int]$match.Groups['total'].Value
            elapsedMilliseconds = [double]($minutes * 60000.0 + $tailMilliseconds)
        }
    }
    $duplicates = @($assemblies | Group-Object assembly | Where-Object Count -gt 1)
    if ($duplicates.Count -gt 0) { throw "Ambiguous console summaries for assembly '$($duplicates[0].Name)'." }
    [pscustomobject][ordered]@{
        schemaVersion = 1
        granularity = 'project'
        durationMetric = 'project-wall-clock'
        lane = 'backend'
        assemblies = @($assemblies | Sort-Object assembly)
    }
}

function New-NervTestEvidenceBaseline {
    param(
        [Parameter(Mandatory)] [object[]] $Summaries,
        [Parameter(Mandatory)] [object] $SourceMetadata,
        [Parameter(Mandatory)] [DateTimeOffset] $GeneratedAtUtc
    )

    $assemblies = @($Summaries | ForEach-Object { @($_.assemblies) } | Group-Object lane, assembly | Sort-Object Name | ForEach-Object {
        $items = @($_.Group)
        [pscustomobject][ordered]@{
            lane = [string]$items[0].lane
            assembly = [string]$items[0].assembly
            passed = [int](($items | Measure-Object passed -Sum).Sum)
            failed = [int](($items | Measure-Object failed -Sum).Sum)
            skipped = [int](($items | Measure-Object skipped -Sum).Sum)
            executed = [int](($items | Measure-Object executed -Sum).Sum)
            total = [int](($items | Measure-Object total -Sum).Sum)
            elapsedMilliseconds = [double](($items | Measure-Object elapsedMilliseconds -Sum).Sum)
        }
    })
    $granularities = @($Summaries.granularity | Sort-Object -Unique)
    [pscustomobject][ordered]@{
        schemaVersion = 1
        toolVersion = 'MAN-661-v1'
        granularity = if ($granularities.Count -eq 1) { $granularities[0] } else { 'mixed' }
        durationMetric = if ($granularities.Count -eq 1 -and $granularities[0] -ceq 'test') { 'trx-elapsed' } else { 'project-wall-clock' }
        owner = 'Nerv-IIP Platform CI/Test Governance'
        generatedAtUtc = $GeneratedAtUtc.UtcDateTime.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        source = [pscustomobject][ordered]@{
            kind = if ($SourceMetadata.ContainsKey('sourceKind')) { [string]$SourceMetadata.sourceKind } else { 'github-console' }
            repository = if ($SourceMetadata.ContainsKey('repository')) { [string]$SourceMetadata.repository } else { 'Mang-X/Nerv-IIP' }
            workflowRunId = [string]$SourceMetadata.workflowRunId
            runAttempt = [int]$SourceMetadata.runAttempt
            jobId = [string]$SourceMetadata.jobId
            commitSha = [string]$SourceMetadata.commitSha
            sourceUrl = [string]$SourceMetadata.sourceUrl
            event = [string]$SourceMetadata.event
            headBranch = [string]$SourceMetadata.headBranch
            conclusion = [string]$SourceMetadata.conclusion
            jobConclusion = [string]$SourceMetadata.jobConclusion
            runnerOs = [string]$SourceMetadata.runnerOs
            runnerImage = [string]$SourceMetadata.runnerImage
            dotnetSdk = [string]$SourceMetadata.dotnetSdk
            selectedLanes = @($SourceMetadata.selectedLanes)
            generatorCommand = [string]$SourceMetadata.generatorCommand
        }
        assemblies = $assemblies
    }
}
