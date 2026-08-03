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
        $matches = [regex]::Matches($content, '\bSkip\s*=\s*(?<reason>.*?);', [Text.RegularExpressions.RegexOptions]::Singleline)
        for ($index = 0; $index -lt $matches.Count; $index++) {
            $sourceText = [regex]::Replace($matches[$index].Value, '\s+', ' ').Trim()
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

        $definitions = @{}
        foreach ($definition in @($document.SelectNodes("//*[local-name()='TestDefinitions']/*[local-name()='UnitTest']"))) {
            $method = $definition.SelectSingleNode("./*[local-name()='TestMethod']")
            $assembly = [IO.Path]::GetFileName([string]$definition.storage)
            $testName = if ($null -ne $method -and -not [string]::IsNullOrWhiteSpace([string]$method.className)) {
                "$($method.className).$($method.name)"
            }
            else { [string]$definition.name }
            $definitions[[string]$definition.id] = [pscustomobject]@{ assembly = $assembly; testName = $testName }
        }

        foreach ($result in @($document.SelectNodes("//*[local-name()='Results']/*[local-name()='UnitTestResult']"))) {
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
            $records.Add([pscustomobject][ordered]@{
                schemaVersion = 1
                workflowRunId = [string]$RunMetadata.workflowRunId
                runAttempt = [int]$RunMetadata.runAttempt
                commitSha = [string]$RunMetadata.commitSha
                lane = [string]$RunMetadata.lane
                project = [IO.Path]::GetFileNameWithoutExtension([string]$definition.assembly)
                assembly = [string]$definition.assembly
                testName = [string]$definition.testName
                durationMilliseconds = [double]$duration.TotalMilliseconds
                outcome = [string]$outcomeMap[$rawOutcome]
                skipReason = if ($rawOutcome -eq 'NotExecuted') { Get-NervTrxSkipReason -UnitTestResult $result } else { $null }
                errorMessage = if ($rawOutcome -eq 'Failed') {
                    $node = $result.SelectSingleNode("./*[local-name()='Output']/*[local-name()='ErrorInfo']/*[local-name()='Message']")
                    if ($null -ne $node) { $node.InnerText.Trim() } else { $null }
                } else { $null }
            })
        }
    }
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
            [string]$record.testName -cmatch [string]$_.testPattern -and
            [string]$record.skipReason -cmatch [string]$_.reasonPattern -and
            (Test-NervRuleApplies -Rule $_ -SelectedLanes $SelectedLanes -RunnerOs $RunnerOs)
        })
        if ($matches.Count -ne 1) {
            $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$record.testName) "Runtime skip matched $($matches.Count) applicable rules."))
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
                ([string]$_.lane -ceq $selectedLane -or ([string]$_.lane -replace '-shard-[1-9][0-9]*$', '') -ceq ($selectedLane -replace '-shard-[1-9][0-9]*$', ''))
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
        [Parameter(Mandatory)] [object[]] $Records,
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
    $assemblies = @($safeRecords | Group-Object assembly | Sort-Object Name | ForEach-Object {
        $items = @($_.Group)
        [pscustomobject][ordered]@{
            assembly = $_.Name
            passed = @($items | Where-Object outcome -eq 'passed').Count
            failed = @($items | Where-Object outcome -eq 'failed').Count
            skipped = @($items | Where-Object outcome -eq 'skipped').Count
            executed = @($items | Where-Object { $_.outcome -in @('passed', 'failed') }).Count
            total = $items.Count
            durationMilliseconds = [double](($items | Measure-Object durationMilliseconds -Sum).Sum)
        }
    })
    $baselineAssemblies = if ($null -ne $Baseline -and $Baseline.PSObject.Properties.Name -contains 'assemblies') { @($Baseline.assemblies) } else { @() }
    $deltas = @($assemblies | ForEach-Object {
        $current = $_
        $previous = @($baselineAssemblies | Where-Object assembly -eq $current.assembly | Select-Object -First 1)
        $baselineDuration = if ($previous.Count -eq 1) { [double]$previous[0].durationMilliseconds } else { $null }
        [pscustomobject][ordered]@{
            assembly = $current.assembly
            baselineDurationMilliseconds = $baselineDuration
            currentDurationMilliseconds = [double]$current.durationMilliseconds
            deltaPercent = if ($null -ne $baselineDuration -and $baselineDuration -gt 0) { [Math]::Round((([double]$current.durationMilliseconds - $baselineDuration) / $baselineDuration) * 100, 2) } else { $null }
        }
    })
    $attemptClassification = if ([int]$RunMetadata.runAttempt -eq 1) {
        'initial'
    }
    elseif ($PriorAttemptOutcome -eq 'failure' -and $failed -eq 0 -and $safeViolations.Count -eq 0) {
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
        passed = $passed
        failed = $failed
        skipped = $skipped
        executed = $passed + $failed
        total = $safeRecords.Count
        testDurationMilliseconds = [double](($safeRecords | Measure-Object durationMilliseconds -Sum).Sum)
        trxElapsedMilliseconds = if ($RunMetadata.ContainsKey('trxElapsedMilliseconds')) { [double]$RunMetadata.trxElapsedMilliseconds } else { $null }
        assemblies = $assemblies
        slowestAssemblies = @($assemblies | Sort-Object @{ Expression = 'durationMilliseconds'; Descending = $true }, @{ Expression = 'assembly'; Descending = $false } | Select-Object -First $TopCount)
        slowestTests = @($safeRecords | Sort-Object @{ Expression = 'durationMilliseconds'; Descending = $true }, @{ Expression = 'testName'; Descending = $false } | Select-Object -First $TopCount | ForEach-Object { [pscustomobject]@{ testName = $_.testName; assembly = $_.assembly; durationMilliseconds = $_.durationMilliseconds } })
        skipReasons = @($safeRecords | Where-Object outcome -eq 'skipped' | Group-Object skipReason | Sort-Object Name | ForEach-Object { [pscustomobject]@{ reason = Protect-NervTestEvidenceText $_.Name; count = $_.Count } })
        violations = $safeViolations
        baseline = [pscustomobject][ordered]@{ enforcement = 'report-only'; assemblies = $deltas }
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
                durationMilliseconds = [double]$record.durationMilliseconds
                outcome = [string]$record.outcome
                skipReason = Protect-NervTestEvidenceText ([string]$record.skipReason)
            }
            $safeRecord | ConvertTo-Json -Compress -Depth 20
        }
        Write-NervUtf8NoBom (Join-Path $temporary 'tests.jsonl') ([string]::Join("`n", @($recordLines)) + $(if (@($recordLines).Count -gt 0) { "`n" } else { '' }))
        $safeSummaryJson = Protect-NervTestEvidenceText ($Summary | ConvertTo-Json -Depth 100)
        Write-NervUtf8NoBom (Join-Path $temporary 'summary.json') ($safeSummaryJson + "`n")
        $markdown = @(
            "# Test evidence: $($Summary.lane)",
            '',
            "- Run: $($Summary.workflowRunId), attempt $($Summary.runAttempt), commit $($Summary.commitSha)",
            "- Counts: passed=$($Summary.passed), failed=$($Summary.failed), skipped=$($Summary.skipped), executed=$($Summary.executed), total=$($Summary.total)",
            "- Attempt: $($Summary.attemptClassification) (prior: $($Summary.priorAttemptStatus))",
            '- Timing and baseline deltas: report-only',
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
        foreach ($group in @($Records | Group-Object assembly | Sort-Object Name)) {
            $assemblyName = [regex]::Replace([string]$group.Name, '[^A-Za-z0-9_.-]', '_')
            $fileName = "$($Summary.lane)-$assemblyName-$sha8-attempt-$($Summary.runAttempt).trx"
            $xmlRows = foreach ($record in @($group.Group | Sort-Object testName)) {
                $name = [Security.SecurityElement]::Escape([string]$record.testName)
                $outcome = switch ([string]$record.outcome) { 'passed' { 'Passed' } 'failed' { 'Failed' } default { 'NotExecuted' } }
                $duration = [TimeSpan]::FromMilliseconds([double]$record.durationMilliseconds).ToString('c', [Globalization.CultureInfo]::InvariantCulture)
                $message = if ($record.outcome -eq 'skipped') { Protect-NervTestEvidenceText ([string]$record.skipReason) } elseif ($record.outcome -eq 'failed') { Protect-NervTestEvidenceText ([string]$record.errorMessage) } else { $null }
                $output = if ([string]::IsNullOrWhiteSpace($message)) { '' } else { "<Output><ErrorInfo><Message>$([Security.SecurityElement]::Escape($message))</Message></ErrorInfo></Output>" }
                "<UnitTestResult testName=`"$name`" duration=`"$duration`" outcome=`"$outcome`">$output</UnitTestResult>"
            }
            $safeXml = "<?xml version=`"1.0`" encoding=`"utf-8`"?><TestRun><Results>$([string]::Join('', @($xmlRows)))</Results></TestRun>"
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
