# Script-Governance:
#   Category: library
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

function Get-NervTestEvidenceLaneJobs {
    # The allowlisted lane-to-job binding. One physical job owns one lane, so a job can never
    # certify a sibling shard. The unsharded `backend` lane is deliberately absent: since MAN-669
    # no job produces it, and `Backend Tests` is now a test-free aggregate that must never be able
    # to certify a lane. `backend` remains a valid logical base lane for `-SelectedLanes`.
    return [ordered]@{
        'backend-shard-1' = 'Backend Tests - BusinessGateway'
        'backend-shard-2' = 'Backend Tests - Platform'
        'backend-shard-3' = 'Backend Tests - Business Core A'
        'backend-shard-4' = 'Backend Tests - Business Core B'
        'connector-host' = 'Connector Host Tests'
    }
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

function Test-NervQuarantineRuleMetadata {
    param(
        [Parameter(Mandatory)] [object] $Rule,
        [Parameter(Mandatory)] [DateTimeOffset] $AsOfUtc
    )

    $expiry = [DateTimeOffset]::MinValue
    $validDate = [DateTimeOffset]::TryParseExact(
        [string]$Rule.expiresOn,
        'yyyy-MM-dd',
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal,
        [ref]$expiry)
    return -not [string]::IsNullOrWhiteSpace([string]$Rule.responsibilityIssue) -and
        -not [string]::IsNullOrWhiteSpace([string]$Rule.exitCondition) -and
        $validDate -and
        $expiry.Date -ge $AsOfUtc.UtcDateTime.Date
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
        # Uniqueness is ordinal: a frozen identity is an identifier, and `Sort-Object -Unique` is
        # culture-aware, so two rows differing only by an ignorable character (U+00AD is the one
        # #1509 measured) collapse into one and the count check reports the wrong reason.
        $uniqueIdentities = [Collections.Generic.HashSet[string]]::new(
            [string[]] @($identities | ForEach-Object { [string]$_ }), [StringComparer]::Ordinal)
        if (@($identities).Count -eq 0 -or $expectedCount -ne @($identities).Count -or $uniqueIdentities.Count -ne @($identities).Count) {
            $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$rule.id) 'Rule must freeze a non-empty unique test identity set and exact expectedRuntimeTestCount.'))
        }
        foreach ($identity in $identities) {
            # Padding ruling (#1509): every consumer compares a frozen identity by ordinal equality
            # or ordinal prefix — Get-BackendTestShardPolicyIdentityMatches, the runtime rule matcher
            # below, the shard exclusion gate. None of them trims, and none of them should: trimming
            # at the point of comparison would let two rows MAN-661 stores as distinct strings
            # resolve to the same selector while the padding survives into the evidence key. So the
            # padding is rejected here, at the only boundary where the policy text is authored, and
            # `identity as written == identity as compared` holds everywhere downstream. An anchored
            # testPattern already rejects *leading* whitespace as a side effect; trailing whitespace
            # used to pass, because `.+$` happily consumes it.
            $identityText = [string]$identity
            if ($identityText.Length -ne $identityText.Trim().Length) {
                $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$rule.id) "Frozen test identity '$identityText' must not carry leading or trailing whitespace; identities are compared as written."))
            }
            if ($identityText -cnotmatch [string]$rule.testPattern) {
                $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$rule.id) "Frozen test identity '$identity' does not match testPattern."))
            }
        }
        foreach ($laneName in @($rule.allowedLanes) + @($rule.requiredLane | Where-Object { $_ })) {
            if (-not (Test-NervTestEvidenceLaneName ([string]$laneName))) {
                $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$rule.id) "Invalid lane '$laneName'."))
            }
        }
        if ([string]$rule.classification -eq 'quarantined') {
            if (-not (Test-NervQuarantineRuleMetadata -Rule $rule -AsOfUtc $AsOfUtc)) {
                $violations.Add((New-NervTestEvidenceViolation 'illegal-quarantine' ([string]$rule.id) 'Quarantine requires issue, valid unexpired ISO date, and exit condition.'))
            }
        }
    }

    $live = @(Get-NervSourceSkipAssignments -RepoRoot $RepoRoot)
    foreach ($assignment in $live) {
        $matchedSources = @($Policy.sources | Where-Object {
            [string]$_.sourcePath -ceq [string]$assignment.sourcePath -and
            [int]$_.sourceOrdinal -eq [int]$assignment.sourceOrdinal -and
            [string]$assignment.sourceText -cmatch [string]$_.sourceReasonPattern
        })
        if ($matchedSources.Count -ne 1) {
            $id = "$($assignment.sourcePath):$($assignment.sourceOrdinal)"
            $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' $id 'Source Skip assignment is missing, duplicated, or reason-mismatched.'))
        }
    }
    foreach ($source in @($Policy.sources)) {
        if (@($Policy.rules | Where-Object { [string]$_.sourceId -ceq [string]$source.id }).Count -eq 0) {
            $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$source.id) 'Registered source is not referenced by any runtime rule.'))
        }
        $matchedAssignments = @($live | Where-Object {
            [string]$_.sourcePath -ceq [string]$source.sourcePath -and
            [int]$_.sourceOrdinal -eq [int]$source.sourceOrdinal
        })
        if ($matchedAssignments.Count -ne 1) {
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

function ConvertTo-NervRetainedDisplayName {
    param([AllowNull()] [string] $Text)

    $source = if ($null -eq $Text) { '' } else { $Text }
    if ([string]::IsNullOrWhiteSpace($source)) {
        return [pscustomobject]@{ text = (Protect-NervTestEvidenceText $source); redactionCount = 0 }
    }

    $pattern = [regex]::new('(?i)(?<prefix>(?:^|[(,]\s*))(?<label>(?:body|requestBody|responseBody)\s*:\s*)')
    $builder = [Text.StringBuilder]::new()
    $position = 0
    $redactionCount = 0
    while ($position -lt $source.Length) {
        $match = $pattern.Match($source, $position)
        if (-not $match.Success) {
            [void]$builder.Append($source.Substring($position))
            break
        }

        [void]$builder.Append($source.Substring($position, $match.Index - $position))
        [void]$builder.Append($match.Groups['prefix'].Value)
        [void]$builder.Append($match.Groups['label'].Value)
        $valueStart = $match.Index + $match.Length
        $valueEnd = $valueStart
        if ($valueStart -lt $source.Length -and ($source[$valueStart] -eq '"' -or $source[$valueStart] -eq "'")) {
            $quote = $source[$valueStart]
            $valueEnd++
            while ($valueEnd -lt $source.Length) {
                if ($source[$valueEnd] -eq $quote) {
                    $slashes = 0
                    for ($lookBehind = $valueEnd - 1; $lookBehind -ge $valueStart -and $source[$lookBehind] -eq '\'; $lookBehind--) { $slashes++ }
                    if (($slashes % 2) -eq 0) { $valueEnd++; break }
                }
                $valueEnd++
            }
        }
        else {
            $depth = 0
            $quote = [char]0
            $escaped = $false
            while ($valueEnd -lt $source.Length) {
                $character = $source[$valueEnd]
                if ($quote -ne [char]0) {
                    if ($character -eq '\' -and -not $escaped) { $escaped = $true; $valueEnd++; continue }
                    if ($character -eq $quote -and -not $escaped) { $quote = [char]0 }
                    $escaped = $false
                }
                elseif ($character -eq '"' -or $character -eq "'") { $quote = $character }
                elseif ($character -in @('{', '[', '(')) { $depth++ }
                elseif ($character -in @('}', ']')) { if ($depth -gt 0) { $depth-- } }
                elseif ($character -eq ')' -and $depth -eq 0) { break }
                elseif ($character -eq ')' -and $depth -gt 0) { $depth-- }
                elseif ($character -eq ',' -and $depth -eq 0) { break }
                $valueEnd++
            }
        }

        $rawValue = $source.Substring($valueStart, $valueEnd - $valueStart)
        if ($rawValue -cmatch '^["'']<redacted-body:[0-9a-f]{16}>["'']$') {
            [void]$builder.Append($rawValue)
            $position = $valueEnd
            continue
        }
        $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($rawValue))).ToLowerInvariant().Substring(0, 16)
        [void]$builder.Append("`"<redacted-body:$digest>`"")
        $redactionCount++
        $position = $valueEnd
    }
    [pscustomobject]@{ text = (Protect-NervTestEvidenceText $builder.ToString()); redactionCount = $redactionCount }
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

        $root = $document.DocumentElement
        $persistedHeadSha = $root.GetAttribute('headSha')
        $persistedTestedSha = $root.GetAttribute('testedSha')
        if (-not [string]::IsNullOrWhiteSpace($persistedHeadSha) -or -not [string]::IsNullOrWhiteSpace($persistedTestedSha)) {
            if ($persistedHeadSha -cne [string]$RunMetadata.headSha -or $persistedTestedSha -cne [string]$RunMetadata.testedSha) {
                throw [IO.InvalidDataException]::new("Normalized TRX provenance does not match run metadata in '$([IO.Path]::GetFullPath($trxPath))'.")
            }
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
            $retainedDisplay = ConvertTo-NervRetainedDisplayName $result.GetAttribute('testName')
            $displayName = [string]$retainedDisplay.text
            if ([string]::IsNullOrWhiteSpace($displayName)) { $displayName = [string]$definition.testName }
            if ($displayName.Length -gt 512) { $displayName = $displayName.Substring(0, 512) }
            $ordinalKey = "$($definition.testName)|$displayName"
            $ordinal = if ($ordinals.ContainsKey($ordinalKey)) { [int]$ordinals[$ordinalKey] + 1 } else { 1 }
            $ordinals[$ordinalKey] = $ordinal
            $rawError = if ($rawOutcome -eq 'Failed') {
                $node = $result.SelectSingleNode("./*[local-name()='Output']/*[local-name()='ErrorInfo']/*[local-name()='Message']")
                if ($null -ne $node) { $node.InnerText.Trim() } else { $null }
            } else { $null }
            $persistedExecutionId = [Guid]::Empty
            $hasPersistedExecutionId = [Guid]::TryParse($result.GetAttribute('executionId'), [ref]$persistedExecutionId) -and $persistedExecutionId -ne [Guid]::Empty
            $persistedRedactionCount = 0
            $hasPersistedRedactionCount = -not [string]::IsNullOrWhiteSpace($persistedHeadSha) -and
                [int]::TryParse($result.GetAttribute('redactionCount'), [ref]$persistedRedactionCount) -and $persistedRedactionCount -ge 0
            $records.Add([pscustomobject][ordered]@{
                schemaVersion = 1
                workflowRunId = [string]$RunMetadata.workflowRunId
                runAttempt = [int]$RunMetadata.runAttempt
                headSha = [string]$RunMetadata.headSha
                testedSha = [string]$RunMetadata.testedSha
                lane = [string]$RunMetadata.lane
                project = [IO.Path]::GetFileNameWithoutExtension([string]$definition.assembly)
                assembly = [string]$definition.assembly
                testName = [string]$definition.testName
                displayName = $displayName
                testClassName = [string]$definition.className
                testMethodName = [string]$definition.methodName
                definitionId = Get-NervStableEvidenceGuid "$($definition.assembly)|$($definition.testName)"
                testInstanceId = if ($hasPersistedExecutionId) { $persistedExecutionId.ToString() } else { Get-NervStableEvidenceGuid "$($definition.assembly)|$($definition.testName)|$displayName|$ordinal" }
                durationTicks = [long]$duration.Ticks
                durationMilliseconds = [double]$duration.TotalMilliseconds
                outcome = [string]$outcomeMap[$rawOutcome]
                skipReason = if ($rawOutcome -eq 'NotExecuted') { Get-NervTrxSkipReason -UnitTestResult $result } else { $null }
                errorMessage = ConvertTo-NervRetainedFailureText $rawError
                redactionCount = if ($hasPersistedRedactionCount) { $persistedRedactionCount } else { [int]$retainedDisplay.redactionCount + $(if ([string]::IsNullOrWhiteSpace($rawError)) { 0 } else { 1 }) }
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
        if (-not (Test-NervQuarantineRuleMetadata -Rule $rule -AsOfUtc ([DateTimeOffset]::UtcNow))) {
            $violations.Add((New-NervTestEvidenceViolation 'illegal-quarantine' ([string]$rule.id) 'Quarantine metadata is missing, invalid, or expired.'))
        }
    }

    foreach ($record in @($safeRecords | Where-Object outcome -eq 'skipped')) {
        $matchedRules = @($Policy.rules | Where-Object {
            @($_.testIdentities) -ccontains [string]$record.testName -and
            [string]$record.testName -cmatch [string]$_.testPattern -and
            [string]$record.skipReason -cmatch [string]$_.reasonPattern -and
            (Test-NervRuleApplies -Rule $_ -SelectedLanes $SelectedLanes -RunnerOs $RunnerOs)
        })
        if ($matchedRules.Count -ne 1) {
            $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$record.testName) "Runtime skip matched $($matchedRules.Count) applicable rules."))
        }
        else {
            $record | Add-Member -NotePropertyName skipClassification -NotePropertyValue ([string]$matchedRules[0].classification) -Force
            $record | Add-Member -NotePropertyName skipPolicyId -NotePropertyValue ([string]$matchedRules[0].id) -Force
        }
    }

    $selectedLaneContracts = [Collections.Generic.List[object]]::new()
    foreach ($selectedLane in @($SelectedLanes | Sort-Object -Unique)) {
        $laneMatches = @($Policy.lanes | Where-Object { $selectedLane -cmatch [string]$_.namePattern })
        if ($laneMatches.Count -ne 1) {
            $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' $selectedLane "Selected lane matched $($laneMatches.Count) lane contracts."))
            continue
        }
        $selectedLaneContracts.Add([pscustomobject]@{
            selectedLane = $selectedLane
            baseLane = ($selectedLane -replace '-shard-[1-9][0-9]*$', '')
            realDependency = [bool]$laneMatches[0].realDependency
        })
    }
    foreach ($laneGroup in @($selectedLaneContracts | Group-Object baseLane)) {
        if (-not [bool]$laneGroup.Group[0].realDependency) { continue }
        $selectors = @($laneGroup.Group.selectedLane)
        $baseLane = [string]$laneGroup.Name
        $executed = @($safeRecords | Where-Object {
            if ($_.outcome -notin @('passed', 'failed')) { return $false }
            $recordLane = [string]$_.lane
            if ($selectors.Count -eq 1 -and [string]$selectors[0] -cne $baseLane) {
                return $recordLane -ceq [string]$selectors[0]
            }
            return ($recordLane -replace '-shard-[1-9][0-9]*$', '') -ceq $baseLane
        }).Count
        if ($executed -eq 0) {
            $violationId = if ($selectors.Count -eq 1) { [string]$selectors[0] } else { $baseLane }
            $violations.Add((New-NervTestEvidenceViolation 'zero-execution' $violationId 'Selected real-dependency lane executed no passed or failed tests.'))
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
    [string[]] $selectedLanes = if ($RunMetadata.ContainsKey('selectedLanes')) { @($RunMetadata.selectedLanes | Sort-Object -Unique) } else { @([string]$RunMetadata.lane) }
    $selectedLaneResults = @($selectedLanes | Group-Object { $_ -replace '-shard-[1-9][0-9]*$', '' } | Sort-Object Name | ForEach-Object {
        $baseLane = [string]$_.Name
        [string[]] $selectors = @($_.Group | Sort-Object -Unique)
        $laneRecords = @($safeRecords | Where-Object { ([string]$_.lane -replace '-shard-[1-9][0-9]*$', '') -ceq $baseLane })
        $zeroExecution = @($safeViolations | Where-Object { $_.code -ceq 'zero-execution' -and ([string]$_.id -ceq $baseLane -or $selectors -ccontains [string]$_.id) }).Count -gt 0
        $invalidSelection = @($safeViolations | Where-Object { $_.code -ceq 'unregistered-skip' -and $selectors -ccontains [string]$_.id }).Count -gt 0
        [pscustomobject][ordered]@{
            baseLane = $baseLane
            selectors = $selectors
            observedLanes = [string[]]@($laneRecords | ForEach-Object { [string]$_.lane } | Sort-Object -Unique)
            passed = @($laneRecords | Where-Object outcome -eq 'passed').Count
            failed = @($laneRecords | Where-Object outcome -eq 'failed').Count
            skipped = @($laneRecords | Where-Object outcome -eq 'skipped').Count
            executed = @($laneRecords | Where-Object { $_.outcome -in @('passed', 'failed') }).Count
            total = $laneRecords.Count
            gateResult = if ($zeroExecution) { 'zero-execution' } elseif ($invalidSelection) { 'invalid-selection' } else { 'pass' }
        }
    })
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
    $baselineSchemaVersion = 0
    $baselineUnavailableReason = if ($null -eq $Baseline) {
        'baseline-not-provided'
    }
    # The reader must know which `source` shape it is looking at, or "schema 1's flat runner trio is
    # the first lane's, schema 2's laneProvenance is per lane" is a comment rather than a rule. Both
    # known versions compare identically (the comparison key is the assembly, no lane and no runner
    # field participates); an unknown or missing version fails closed to report-only unavailable
    # rather than comparing against a file whose layout this code has never seen.
    #
    # TryParse, not `[int]`: the cast mishandled three shapes a hand-edited JSON file can hold, each in
    # a different way. Which shapes, and why no NumberStyles/culture argument is needed, are in
    # docs/architecture/test-evidence-governance.md ("Run identity versus per-job environment").
    elseif (-not ($Baseline.PSObject.Properties.Name -contains 'schemaVersion') -or
        -not [int]::TryParse([string]$Baseline.schemaVersion, [ref]$baselineSchemaVersion) -or
        $baselineSchemaVersion -notin @(1, 2)) {
        'unsupported-baseline-schema-version'
    }
    elseif (-not ($Baseline.PSObject.Properties.Name -contains 'granularity') -or -not ($Baseline.PSObject.Properties.Name -contains 'durationMetric')) {
        'baseline-metadata-incomplete'
    }
    elseif ([string]$Baseline.granularity -cne 'test' -or [string]$Baseline.durationMetric -cne 'trx-elapsed') {
        'incompatible-granularity-or-duration-metric'
    }
    else { $null }
    # The comparison key is the **assembly alone** (#1507). It used to be lane plus assembly, which
    # made a pure "how we run the tests" change invalidate keys no test had touched: MAN-669 PR-A
    # re-homed 17 of 64 backend assemblies between shards and every one of those rows fell to
    # "not in baseline" until a human regenerated and re-committed the snapshot. Timing is a
    # measurement, not a governed list; a measurement of an assembly does not become a different
    # measurement because the assembly moved to another job. Lane survives on the row for display
    # and provenance and is used only to disambiguate, never to look up.
    #
    # Known residual, report-only: a baseline holding one row for an assembly while the current run
    # splits that assembly across two lanes compares both current rows against the whole previous
    # measurement, so both deltas overstate the change. Recorded rather than silently tolerated in
    # docs/architecture/test-evidence-governance.md, "One known report-only artefact".
    $deltas = @($assemblies | ForEach-Object {
        $current = $_
        $compatible = $null -eq $baselineUnavailableReason
        # Ordinal, not `-ceq`. The `c` prefix only disables case-insensitivity; the comparison still
        # runs through the collation table, which reports "equal" for strings that differ by an
        # ignorable character. An assembly name is an identifier, so it is compared as bytes.
        [object[]]$assemblyMatches = if ($compatible) { @($baselineAssemblies | Where-Object { [string]::Equals([string]$_.assembly, [string]$current.assembly, [StringComparison]::Ordinal) }) } else { @() }
        # One assembly classified into two lanes would give two rows that are not the same
        # measurement. Prefer this lane's row; with no lane match the comparison is genuinely
        # ambiguous and stays report-only unavailable rather than picking one arbitrarily.
        #
        # `Merge-NervShardTimingObservations` in scripts/lib/BackendTestShardTimings.ps1 resolves the
        # very same situation by *summing* the two rows, and the divergence is intentional. That one
        # builds a shard budget, where the answer wanted is total work and two lanes are two halves
        # of one number. This one compares one lane's run against one baseline row, where the answer
        # wanted is a row identity — summing would invent a measurement nobody took. Neither rule is
        # a fallback for the other; changing either does not imply changing the other.
        [object[]]$previous = if (@($assemblyMatches).Count -le 1) {
            @($assemblyMatches)
        }
        else {
            @(@($assemblyMatches) | Where-Object { [string]::Equals([string]$_.lane, [string]$current.lane, [StringComparison]::Ordinal) } | Select-Object -First 1)
        }
        $baselineDuration = if (@($previous).Count -eq 1) { [double]@($previous)[0].elapsedMilliseconds } else { $null }
        $unavailableReason = if ($null -ne $baselineUnavailableReason) { $baselineUnavailableReason }
            elseif (@($previous).Count -ne 1 -and @($assemblyMatches).Count -gt 1) { 'ambiguous-assembly-in-baseline' }
            elseif (@($previous).Count -ne 1) { 'assembly-not-in-baseline' }
            elseif ($baselineDuration -le 0) { 'baseline-duration-not-positive' }
            else { $null }
        [pscustomobject][ordered]@{
            lane = $current.lane
            assembly = $current.assembly
            metric = 'trx-elapsed'
            available = $null -eq $unavailableReason
            unavailableReason = $unavailableReason
            baselineDurationMilliseconds = $baselineDuration
            currentDurationMilliseconds = [double]$current.elapsedMilliseconds
            deltaPercent = if ($null -eq $unavailableReason) { [Math]::Round((([double]$current.elapsedMilliseconds - $baselineDuration) / $baselineDuration) * 100, 2) } else { $null }
        }
    })
    $baselineAvailable = @($deltas | Where-Object available).Count -gt 0
    $summaryBaselineUnavailableReason = if ($baselineAvailable) { $null } elseif ($null -ne $baselineUnavailableReason) { $baselineUnavailableReason } else { 'no-compatible-assembly' }
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
        headSha = [string]$RunMetadata.headSha
        testedSha = [string]$RunMetadata.testedSha
        lane = [string]$RunMetadata.lane
        selectedLanes = $selectedLanes
        selectedLaneResults = $selectedLaneResults
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
        retentionLocation = if ($RunMetadata.ContainsKey('retentionLocation')) { [string]$RunMetadata.retentionLocation } else { 'local-output' }
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
            available = $baselineAvailable
            unavailableReason = $summaryBaselineUnavailableReason
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

function ConvertTo-NervEvidenceIdentity {
    param(
        [AllowNull()] [string] $Text,
        [Parameter(Mandatory)] [string] $Pattern,
        [Parameter(Mandatory)] [string] $Fallback,
        [ValidateRange(1, 256)] [int] $MaximumLength = 128
    )
    $safe = Protect-NervTestEvidenceText $Text
    if ([string]::IsNullOrWhiteSpace($safe) -or $safe.Length -gt $MaximumLength -or $safe -cnotmatch $Pattern) { return $Fallback }
    return $safe
}

function Write-NervEvidenceOutputPath {
    param([Parameter(Mandatory)] [string] $Path, [AllowNull()] [string] $ManifestPath)
    if ([string]::IsNullOrWhiteSpace($ManifestPath)) { return }
    [IO.File]::AppendAllText($ManifestPath, "evidence-path=$Path`n", [Text.UTF8Encoding]::new($false))
}

function Write-NervTestEvidenceArtifacts {
    param(
        [Parameter(Mandatory)] [object[]] $Records,
        [Parameter(Mandatory)] [object] $Summary,
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
                headSha = [string]$record.headSha
                testedSha = [string]$record.testedSha
                lane = [string]$record.lane
                project = [string]$record.project
                assembly = [string]$record.assembly
                testName = [string]$record.testName
                displayName = [string]$record.displayName
                testClassName = [string]$record.testClassName
                testMethodName = [string]$record.testMethodName
                definitionId = [string]$record.definitionId
                testInstanceId = [string]$record.testInstanceId
                durationTicks = [long]$record.durationTicks
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
            "- Run: $($Summary.workflowRunId), attempt $($Summary.runAttempt), head $($Summary.headSha), tested $($Summary.testedSha)",
            "- Selected lanes: $([string]::Join(', ', @($Summary.selectedLanes)))",
            "- Counts: passed=$($Summary.passed), failed=$($Summary.failed), skipped=$($Summary.skipped), executed=$($Summary.executed), total=$($Summary.total)",
            "- Duration: summed tests=$($Summary.testDurationMilliseconds)ms, TRX elapsed=$($Summary.trxElapsedMilliseconds)ms",
            "- Attempt: $($Summary.attemptClassification) (prior: $($Summary.priorAttemptStatus))",
            "- Provenance: job=$($Summary.jobName), outcome=$($Summary.currentTestOutcome), runner=$($Summary.runnerOs)/$($Summary.runnerImage), dotnet=$($Summary.dotnetSdk)",
            "- Baseline source: $baselineSource",
            $(if ([bool]$Summary.baseline.available) { '- Baseline comparison: available' } else { "- Baseline comparison: unavailable ($($Summary.baseline.unavailableReason))" }),
            "- Privacy redactions: $($Summary.redactionCount)",
            '- Timing and baseline deltas: report-only',
            "- Retained artifact: $($Summary.artifactName), retention=$($Summary.retentionDays) days, location=$($Summary.retentionLocation); tests.jsonl, summary.json, summary.md, diagnostics.log, normalized trx/",
            '',
            '## Selected lane results',
            '',
            '| Logical lane | Selectors | Observed lanes | Passed | Failed | Skipped | Executed | Total | Gate result |',
            '|---|---|---|---:|---:|---:|---:|---:|---|'
        )
        foreach ($laneResult in @($Summary.selectedLaneResults)) {
            $markdown += "| $($laneResult.baseLane) | $([string]::Join(', ', @($laneResult.selectors))) | $([string]::Join(', ', @($laneResult.observedLanes))) | $($laneResult.passed) | $($laneResult.failed) | $($laneResult.skipped) | $($laneResult.executed) | $($laneResult.total) | $($laneResult.gateResult) |"
        }
        $markdown += @(
            '',
            '## Assemblies',
            '',
            '| Lane | Assembly | Passed | Failed | Skipped | Executed | Total | Test duration (ms) | TRX elapsed (ms) |',
            '|---|---|---:|---:|---:|---:|---:|---:|---:|'
        )
        foreach ($assembly in @($Summary.assemblies)) {
            $markdown += "| $($assembly.lane) | $($assembly.assembly) | $($assembly.passed) | $($assembly.failed) | $($assembly.skipped) | $($assembly.executed) | $($assembly.total) | $($assembly.testDurationMilliseconds) | $($assembly.elapsedMilliseconds) |"
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
            if ([bool]$delta.available) {
                $markdown += "- $($delta.assembly): current=$($delta.currentDurationMilliseconds)ms, baseline=$($delta.baselineDurationMilliseconds)ms, delta=$($delta.deltaPercent)%"
            }
            else {
                $markdown += "- $($delta.assembly): current=$($delta.currentDurationMilliseconds)ms, unavailable ($($delta.unavailableReason))"
            }
        }
        Write-NervUtf8NoBom (Join-Path $temporary 'summary.md') ((Protect-NervTestEvidenceText ([string]::Join("`n", $markdown))) + "`n")
        Write-NervUtf8NoBom (Join-Path $temporary 'diagnostics.log') ''

        $sha8 = ([string]$Summary.testedSha).Substring(0, [Math]::Min(8, ([string]$Summary.testedSha).Length))
        foreach ($group in @($Records | Group-Object lane, assembly | Sort-Object Name)) {
            $groupRecords = @($group.Group | Sort-Object testName, displayName, testInstanceId)
            $assemblyName = [regex]::Replace([string]$groupRecords[0].assembly, '[^A-Za-z0-9_.-]', '_')
            $fileName = "$($Summary.lane)-$assemblyName-$sha8-attempt-$($Summary.runAttempt).trx"
            $xmlRows = foreach ($record in $groupRecords) {
                $name = [Security.SecurityElement]::Escape([string]$record.displayName)
                $outcome = switch ([string]$record.outcome) { 'passed' { 'Passed' } 'failed' { 'Failed' } default { 'NotExecuted' } }
                $duration = [TimeSpan]::FromTicks([long]$record.durationTicks).ToString('c', [Globalization.CultureInfo]::InvariantCulture)
                $message = if ($record.outcome -eq 'skipped') { Get-NervRetainedSkipReason $record } elseif ($record.outcome -eq 'failed') { ConvertTo-NervRetainedFailureText ([string]$record.errorMessage) } else { $null }
                $output = if ([string]::IsNullOrWhiteSpace($message)) { '' } else { "<Output><ErrorInfo><Message>$([Security.SecurityElement]::Escape($message))</Message></ErrorInfo></Output>" }
                "<UnitTestResult executionId=`"$($record.testInstanceId)`" testId=`"$($record.definitionId)`" testName=`"$name`" duration=`"$duration`" outcome=`"$outcome`" redactionCount=`"$([int]$record.redactionCount)`">$output</UnitTestResult>"
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
            $safeXml = "<?xml version=`"1.0`" encoding=`"utf-8`"?><TestRun id=`"$runId`" headSha=`"$($Summary.headSha)`" testedSha=`"$($Summary.testedSha)`" xmlns=`"http://microsoft.com/schemas/VisualStudio/TeamTest/2010`"><Times creation=`"$($start.ToString('o'))`" queuing=`"$($start.ToString('o'))`" start=`"$($start.ToString('o'))`" finish=`"$($finish.ToString('o'))`" /><Results>$([string]::Join('', @($xmlRows)))</Results><TestDefinitions>$([string]::Join('', @($xmlDefinitions)))</TestDefinitions><ResultSummary outcome=`"Completed`"><Counters total=`"$($groupRecords.Count)`" executed=`"$executedCount`" passed=`"$passedCount`" failed=`"$failedCount`" notExecuted=`"$skippedCount`" /></ResultSummary></TestRun>"
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
    $target = $OutputDirectory
    if (Test-Path -LiteralPath $target) {
        $target = "$OutputDirectory.failure"
        $suffix = 1
        while (Test-Path -LiteralPath $target) {
            $suffix++
            $target = "$OutputDirectory.failure-$suffix"
        }
    }
    $parent = Split-Path -Parent $target
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    $temporary = "$target.tmp-$([Guid]::NewGuid().ToString('N'))"
    [IO.Directory]::CreateDirectory($temporary) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $temporary 'trx')) | Out-Null
    $safeDiagnostic = Protect-NervTestEvidenceText $Diagnostic
    if ($safeDiagnostic.Length -gt 1024) { $safeDiagnostic = $safeDiagnostic.Substring(0, 1024) }
    $safeLane = ConvertTo-NervEvidenceIdentity ([string]$RunMetadata.lane) '^[a-z0-9]+(?:-[a-z0-9]+)*(?:-shard-[1-9][0-9]*)?$' 'invalid-lane' 64
    $safeRun = ConvertTo-NervEvidenceIdentity ([string]$RunMetadata.workflowRunId) '^[A-Za-z0-9._-]+$' 'invalid-run' 64
    $safeHeadSha = ConvertTo-NervEvidenceIdentity ([string]$RunMetadata.headSha) '^[0-9a-f]{40}$' 'invalid-head-sha' 40
    $safeTestedSha = ConvertTo-NervEvidenceIdentity ([string]$RunMetadata.testedSha) '^[0-9a-f]{40}$' 'invalid-tested-sha' 40
    $safeRepository = ConvertTo-NervEvidenceIdentity ([string]$RunMetadata.repository) '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' 'invalid-repository' 128
    $safeJob = ConvertTo-NervEvidenceIdentity ([string]$RunMetadata.jobName) '^[A-Za-z0-9 ._/-]+$' 'invalid-job' 128
    $failure = [pscustomobject][ordered]@{
        schemaVersion = 1
        collectionStatus = 'failed'
        workflowRunId = $safeRun
        runAttempt = if ([int]$RunMetadata.runAttempt -ge 1 -and [int]$RunMetadata.runAttempt -le 1000) { [int]$RunMetadata.runAttempt } else { 0 }
        headSha = $safeHeadSha
        testedSha = $safeTestedSha
        lane = $safeLane
        repository = $safeRepository
        jobName = $safeJob
        passed = 0; failed = 0; skipped = 0; executed = 0; total = 0
        violations = @([pscustomobject]@{ code = 'evidence-collection-failed'; id = $safeLane; message = $safeDiagnostic })
    }
    Write-NervUtf8NoBom (Join-Path $temporary 'tests.jsonl') ''
    Write-NervUtf8NoBom (Join-Path $temporary 'summary.json') (($failure | ConvertTo-Json -Depth 20) + "`n")
    $safeMarkdown = "# Test evidence collection failed`n`n- run: $safeRun`n- lane: $safeLane`n- repository: $safeRepository`n- job: $safeJob`n- evidence-collection-failed: $safeDiagnostic`n"
    Write-NervUtf8NoBom (Join-Path $temporary 'summary.md') (Protect-NervTestEvidenceText $safeMarkdown)
    Write-NervUtf8NoBom (Join-Path $temporary 'diagnostics.log') ($safeDiagnostic + "`n")
    [IO.Directory]::Move($temporary, $target)
    return $target
}

function ConvertFrom-NervDotNetConsoleSummary {
    param(
        [Parameter(Mandatory)] [string] $Text,
        [Parameter(Mandatory)] [object] $RunMetadata
    )

    $pattern = '(?im)^.*?(?:Passed|Failed)!\s*-\s*Failed:\s*(?<failed>\d+),\s*Passed:\s*(?<passed>\d+),\s*Skipped:\s*(?<skipped>\d+),\s*Total:\s*(?<total>\d+),\s*Duration:\s*(?:(?<minutes>\d+)\s*m\s*)?(?<value>\d+(?:\.\d+)?)\s*(?<unit>ms|s)\s*-\s*(?<assembly>[^\s]+\.dll)\s*\('
    $summaryMatches = [regex]::Matches($Text, $pattern)
    if ($summaryMatches.Count -eq 0) { throw 'No unambiguous dotnet test project summaries were found.' }
    $assemblies = foreach ($summaryMatch in $summaryMatches) {
        $minutes = if ($summaryMatch.Groups['minutes'].Success) { [double]$summaryMatch.Groups['minutes'].Value } else { 0.0 }
        $tailMilliseconds = if ($summaryMatch.Groups['unit'].Value -ceq 'ms') { [double]$summaryMatch.Groups['value'].Value } else { [double]$summaryMatch.Groups['value'].Value * 1000.0 }
        [pscustomobject][ordered]@{
            lane = if ($RunMetadata.ContainsKey('lane')) { [string]$RunMetadata.lane } else { 'backend' }
            assembly = $summaryMatch.Groups['assembly'].Value
            passed = [int]$summaryMatch.Groups['passed'].Value
            failed = [int]$summaryMatch.Groups['failed'].Value
            skipped = [int]$summaryMatch.Groups['skipped'].Value
            executed = [int]$summaryMatch.Groups['passed'].Value + [int]$summaryMatch.Groups['failed'].Value
            total = [int]$summaryMatch.Groups['total'].Value
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

function ConvertTo-NervResolvedRunnerImage {
    param([Parameter(Mandatory)] [string] $Image)

    $regexMatch = [regex]::Match($Image, '^ubuntu-(?<major>[0-9]{2})\.04$')
    if ($regexMatch.Success) { return "ubuntu$($regexMatch.Groups['major'].Value)" }
    return $Image
}

function Get-NervGitHubRunnerProvenance {
    param([Parameter(Mandatory)] [string] $Text)
    $lines = @($Text -split '\r?\n')
    $image = $null
    $imageVersion = $null
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $match = [regex]::Match($lines[$index], 'Image:\s*(?<value>(?:ubuntu|windows|macos)-[^\s]+)\s*$')
        if (-not $match.Success) { continue }
        $image = $match.Groups['value'].Value
        for ($next = $index + 1; $next -lt [Math]::Min($lines.Count, $index + 6); $next++) {
            $versionMatch = [regex]::Match($lines[$next], 'Version:\s*(?<value>[0-9][0-9A-Za-z._-]+)\s*$')
            if ($versionMatch.Success) { $imageVersion = $versionMatch.Groups['value'].Value; break }
        }
        break
    }
    $sdkMatch = [regex]::Match($Text, "(?im)(?:\.NET Core SDK with version\s+'|dotnet-sdk=|dotnet-sdk\s+)(?<sdk>[0-9]+\.[0-9]+\.[0-9]+)'?")
    if ([string]::IsNullOrWhiteSpace($image) -or [string]::IsNullOrWhiteSpace($imageVersion) -or -not $sdkMatch.Success) {
        throw 'Actions log does not contain resolved runner image/version and exact dotnet SDK provenance.'
    }
    $normalizedImage = ConvertTo-NervResolvedRunnerImage -Image $image
    $runnerOs = if ($image.StartsWith('ubuntu-', [StringComparison]::Ordinal)) { 'Linux' }
        elseif ($image.StartsWith('windows-', [StringComparison]::Ordinal)) { 'Windows' }
        elseif ($image.StartsWith('macos-', [StringComparison]::Ordinal)) { 'macOS' }
        else { throw "Unsupported Actions runner image '$image'." }
    $testedShaCandidates = [Collections.Generic.List[string]]::new()
    foreach ($match in [regex]::Matches($Text, '(?im)^.*tested-sha=(?<sha>[0-9a-f]{40})\s*$')) {
        $testedShaCandidates.Add($match.Groups['sha'].Value)
    }
    $checkoutPattern = '(?im)^.*\[command\].*git\s+log\s+-1\s+--format=%H\s*$\r?\n^.*?(?<sha>[0-9a-f]{40})\s*$'
    foreach ($match in [regex]::Matches($Text, $checkoutPattern)) {
        $testedShaCandidates.Add($match.Groups['sha'].Value)
    }
    $uniqueTestedShas = @($testedShaCandidates | Sort-Object -Unique)
    if ($uniqueTestedShas.Count -ne 1) {
        throw 'Actions log must contain exactly one authoritative tested SHA from the checkout log or tested-sha marker.'
    }
    [pscustomobject]@{
        runnerOs = $runnerOs
        runnerImage = "$normalizedImage@$imageVersion"
        dotnetSdk = $sdkMatch.Groups['sdk'].Value
        testedSha = [string]$uniqueTestedShas[0]
    }
}

function Assert-NervGitHubRunCheckoutProvenance {
    param(
        [Parameter(Mandatory)] [object] $Run,
        [Parameter(Mandatory)] [object] $RunnerProvenance
    )

    $eventName = [string]$Run.event
    $headSha = [string]$Run.headSha
    $testedSha = [string]$RunnerProvenance.testedSha
    if ($eventName -notin @('push', 'pull_request') -or $headSha -notmatch '^[0-9a-f]{40}$' -or $testedSha -notmatch '^[0-9a-f]{40}$') {
        throw 'GitHub run checkout provenance requires a supported event and authoritative head/tested SHAs.'
    }
    if ($eventName -ceq 'push' -and $headSha -cne $testedSha) {
        throw 'Push checkout provenance requires the authoritative tested SHA to equal the run head SHA.'
    }
    [pscustomobject][ordered]@{ headSha = $headSha; testedSha = $testedSha }
}

function Resolve-NervPriorAttemptAuthority {
    param(
        [Parameter(Mandatory)] [object] $Run,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Jobs,
        [Parameter(Mandatory)] [string] $WorkflowRunId,
        [Parameter(Mandatory)] [string] $HeadSha,
        [Parameter(Mandatory)] [int] $RunAttempt,
        [Parameter(Mandatory)] [string] $Lane,
        [Parameter(Mandatory)] [string] $JobName
    )

    $result = [pscustomobject][ordered]@{ verified = $false; outcome = $null }
    if ($RunAttempt -le 1) { return $result }
    $expectedJobs = Get-NervTestEvidenceLaneJobs
    if (-not $expectedJobs.Contains($Lane) -or [string]$expectedJobs[$Lane] -cne $JobName -or
        [string]$Run.id -cne $WorkflowRunId -or [string]$Run.head_sha -cne $HeadSha -or
        [int]$Run.run_attempt -ne $RunAttempt) {
        return $result
    }
    $priorAttempt = $RunAttempt - 1
    $failedJobs = @($Jobs | Where-Object {
        [string]$_.name -ceq $JobName -and [int]$_.run_attempt -eq $priorAttempt -and [string]$_.conclusion -ceq 'failure'
    })
    if ($failedJobs.Count -ne 1) { return $result }
    $result.verified = $true
    $result.outcome = 'failure'
    return $result
}

# Provenance splits in two, because the two halves have different kinds of truth behind them.
#
# `Get-NervEvidenceRunIdentityFields` names the run itself. Five jobs of one workflow run share one
# run id, attempt, head/tested SHA, repository, event, branch and run URL by construction, so
# cross-lane inequality there means the summaries came from different runs and the set is not one
# baseline. Equality is a real check and stays byte-for-byte strict.
#
# `Get-NervEvidenceLaneEnvironmentFields` names the machine a single job happened to land on. There
# is no such thing as "this run's runner image": GitHub schedules each job independently, and during
# an image rollout one run legitimately spans two images (run 31149427664 on 2026-08-07 mixed
# ubuntu24@20260720.247.2 and ubuntu24@20260804.265.1 across its five lanes, in a different mix from
# the run before it). Requiring cross-lane equality there asserted a property the platform never
# promised; it held only while the hosted fleet happened to be homogeneous, and it blocked baseline
# refresh the moment that stopped. Per-summary shape validation is kept, and the load-bearing check
# is `Assert-NervEvidenceRootAuthority`, which re-derives each lane's environment from *that lane's
# own* job log — strictly stronger than any cross-lane comparison could be.
function Get-NervEvidenceRunIdentityFields {
    return , @('workflowRunId', 'runAttempt', 'headSha', 'testedSha', 'repository', 'event', 'headBranch', 'sourceUrl')
}

function Get-NervEvidenceLaneEnvironmentFields {
    return , @('runnerOs', 'runnerImage', 'dotnetSdk')
}

function New-NervEvidenceRunIdentity {
    param([Parameter(Mandatory)] [object] $Summary)
    # Deliberately narrow: callers get the run identity and nothing else, so no downstream consumer
    # can reach through the "first summary" and quietly promote one lane's runner environment into a
    # run-wide fact. Under Set-StrictMode that is a hard error, not a silent empty string.
    [pscustomobject][ordered]@{
        workflowRunId = [string]$Summary.workflowRunId
        runAttempt = [int]$Summary.runAttempt
        headSha = [string]$Summary.headSha
        testedSha = [string]$Summary.testedSha
        repository = [string]$Summary.repository
        event = [string]$Summary.event
        headBranch = [string]$Summary.headBranch
        sourceUrl = [string]$Summary.sourceUrl
    }
}

function Get-NervEvidenceLaneProvenance {
    param([Parameter(Mandatory)] [object[]] $SourceSummaries)
    @($SourceSummaries | Sort-Object { [string]$_.lane } | ForEach-Object {
        [pscustomobject][ordered]@{
            lane = [string]$_.lane
            jobName = [string]$_.jobName
            runnerOs = [string]$_.runnerOs
            runnerImage = [string]$_.runnerImage
            dotnetSdk = [string]$_.dotnetSdk
        }
    })
}

function Assert-NervEvidenceSourceSummaries {
    param([Parameter(Mandatory)] [object[]] $SourceSummaries)

    if ($SourceSummaries.Count -eq 0) { throw 'Evidence baseline requires at least one summary.' }
    $first = $SourceSummaries[0]
    $runIdentityFields = Get-NervEvidenceRunIdentityFields
    $laneEnvironmentFields = Get-NervEvidenceLaneEnvironmentFields
    foreach ($summary in $SourceSummaries) {
        foreach ($field in $runIdentityFields) {
            if ([string]::IsNullOrWhiteSpace([string]$summary.$field)) { throw "Evidence summary provenance field '$field' must be nonempty." }
            if ([string]$summary.$field -cne [string]$first.$field) { throw "Evidence summaries have mixed provenance field '$field'." }
        }
        foreach ($field in $laneEnvironmentFields) {
            if ([string]::IsNullOrWhiteSpace([string]$summary.$field)) { throw "Evidence summary per-job environment field '$field' must be nonempty." }
        }
        foreach ($field in @('lane', 'jobName', 'artifactName')) {
            if ([string]::IsNullOrWhiteSpace([string]$summary.$field)) { throw "Evidence summary metadata field '$field' must be nonempty." }
        }
        if ([int]$summary.runAttempt -ne 1 -or [string]$summary.attemptClassification -cne 'initial' -or
            [string]$summary.currentTestOutcome -cne 'success' -or [string]$summary.collectionStatus -cne 'succeeded' -or
            [int]$summary.failed -ne 0 -or [int]$summary.executed -le 0 -or @($summary.violations).Count -ne 0 -or
            [string]$summary.event -cne 'push' -or [string]$summary.headBranch -cne 'main' -or
            [string]$summary.headSha -notmatch '^[0-9a-f]{40}$' -or [string]$summary.testedSha -notmatch '^[0-9a-f]{40}$' -or
            [string]$summary.testedSha -cne [string]$summary.headSha -or
            [string]$summary.sourceUrl -cne "https://github.com/$($summary.repository)/actions/runs/$($summary.workflowRunId)" -or
            [string]$summary.runnerOs -cnotmatch '^(?:Linux|Windows|macOS)$' -or
            [string]$summary.runnerImage -notmatch '^(?:ubuntu[0-9]{2}|(?:ubuntu|windows|macos)-[^@\s]+)@[0-9A-Za-z._-]+$' -or
            [string]$summary.dotnetSdk -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$' -or -not (Test-NervTestEvidenceLaneName ([string]$summary.lane))) {
            throw 'Evidence baseline requires clean successful attempt-1 initial summaries from one main push.'
        }
    }
    if (@($SourceSummaries.lane | Sort-Object -Unique).Count -ne $SourceSummaries.Count) { throw 'Evidence summaries must have unique lane metadata.' }
    return New-NervEvidenceRunIdentity -Summary $first
}

function Assert-NervEvidenceRootAuthority {
    param(
        [Parameter(Mandatory)] [object[]] $SourceSummaries,
        [Parameter(Mandatory)] [object] $Run,
        [Parameter(Mandatory)] [object[]] $LatestRuns,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $JobLogs
    )

    $first = Assert-NervEvidenceSourceSummaries -SourceSummaries $SourceSummaries
    if ([string]$Run.event -cne 'push' -or [string]$Run.headBranch -cne 'main' -or [int]$Run.attempt -ne 1 -or
        [string]$Run.conclusion -cne 'success' -or [string]$Run.headSha -cne [string]$first.headSha -or
        [string]$Run.url -cne [string]$first.sourceUrl -or [string]$Run.workflowName -cne 'CI' -or
        [string]$Run.databaseId -cne [string]$first.workflowRunId) {
        throw 'Evidence source is not an authoritative successful attempt-1 main CI run.'
    }
    if ($LatestRuns.Count -ne 1 -or [string]$LatestRuns[0].databaseId -cne [string]$first.workflowRunId -or
        [int]$LatestRuns[0].attempt -ne 1 -or [string]$LatestRuns[0].headSha -cne [string]$first.headSha -or
        [string]$LatestRuns[0].conclusion -cne 'success' -or [string]$LatestRuns[0].event -cne 'push' -or
        [string]$LatestRuns[0].headBranch -cne 'main') {
        throw 'Evidence source is not the latest qualifying successful attempt-1 main CI run.'
    }
    $jobByLane = Get-NervTestEvidenceLaneJobs
    $actualLanes = @($SourceSummaries.lane | Sort-Object -Unique)
    $shardFamily = @($jobByLane.Keys | Where-Object { [string]$_ -cmatch '^backend-shard-[1-9][0-9]*$' } | Sort-Object)
    $observedShardLanes = @($actualLanes | Where-Object { [string]$_ -cmatch '^backend-shard-[1-9][0-9]*$' } | Sort-Object)
    if (@($observedShardLanes).Count -gt 0 -and (@($observedShardLanes) -join '|') -cne (@($shardFamily) -join '|')) {
        throw 'Evidence baseline requires one summary for every backend fast shard lane.'
    }
    $requiredJobs = @(@('Backend Tests', 'Connector Host Tests') + @($SourceSummaries.jobName) | Sort-Object -Unique)
    foreach ($requiredJob in $requiredJobs) {
        if (@($Run.jobs | Where-Object { [string]$_.name -ceq $requiredJob -and [string]$_.conclusion -ceq 'success' }).Count -ne 1) {
            throw "Required evidence job '$requiredJob' is missing, ambiguous, or unsuccessful."
        }
    }
    foreach ($summary in $SourceSummaries) {
        if (-not $jobByLane.Contains([string]$summary.lane) -or [string]$summary.jobName -cne [string]$jobByLane[[string]$summary.lane]) {
            throw "Evidence lane '$($summary.lane)' has the wrong authoritative job name."
        }
        if (-not $JobLogs.Contains([string]$summary.jobName) -or [string]::IsNullOrWhiteSpace([string]$JobLogs[[string]$summary.jobName])) {
            throw "Authoritative Actions log for job '$($summary.jobName)' is missing."
        }
        $authority = Get-NervGitHubRunnerProvenance -Text (Protect-ScriptAutomationText ([string]$JobLogs[[string]$summary.jobName]))
        $checkout = Assert-NervGitHubRunCheckoutProvenance -Run $Run -RunnerProvenance $authority
        if ([string]$summary.headSha -cne [string]$checkout.headSha -or
            [string]$summary.testedSha -cne [string]$checkout.testedSha -or
            [string]$summary.runnerOs -cne [string]$authority.runnerOs -or
            [string]$summary.runnerImage -cne [string]$authority.runnerImage -or
            [string]$summary.dotnetSdk -cne [string]$authority.dotnetSdk) {
            throw "Evidence runner provenance for lane '$($summary.lane)' does not match the authoritative Actions log."
        }
    }
    return $first
}

function New-NervTestEvidenceBaseline {
    param(
        [Parameter(Mandatory)] [object[]] $Summaries,
        [Parameter(Mandatory)] [object] $SourceMetadata,
        [Parameter(Mandatory)] [DateTimeOffset] $GeneratedAtUtc
    )

    if ([string]$SourceMetadata.headSha -notmatch '^[0-9a-f]{40}$' -or [string]$SourceMetadata.testedSha -notmatch '^[0-9a-f]{40}$' -or
        ([string]$SourceMetadata.event -ceq 'push' -and [string]$SourceMetadata.headSha -cne [string]$SourceMetadata.testedSha)) {
        throw 'Baseline provenance requires valid headSha/testedSha values; push sources require equality.'
    }
    # Runner environment is recorded per lane and only per lane. There is no run-wide runnerImage
    # field to write, so no reader can mistake one lane's machine for the whole baseline's.
    #
    # "Per lane" is only honest if the rows actually cover the lanes the baseline records. A partial
    # `laneProvenance` would be worse than the old flat trio, not better: the flat field at least
    # claimed to be run-wide, whereas one row against five lanes of timing is a silent partial record
    # that reads as complete. So coverage is checked both directions — no missing lane, no stray lane.
    [object[]] $laneProvenance = @($SourceMetadata.laneProvenance)
    if ($laneProvenance.Count -eq 0) { throw 'Baseline provenance requires at least one per-lane runner environment row.' }
    $laneJobs = Get-NervTestEvidenceLaneJobs
    foreach ($row in $laneProvenance) {
        if (-not (Test-NervTestEvidenceLaneName ([string]$row.lane)) -or
            [string]$row.runnerOs -cnotmatch '^(?:Linux|Windows|macOS)$' -or
            [string]$row.runnerImage -notmatch '^(?:ubuntu[0-9]{2}|(?:ubuntu|windows|macos)-[^@\s]+)@[0-9A-Za-z._-]+$' -or
            [string]$row.runnerImage -match '(?i)latest' -or [string]$row.dotnetSdk -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
            throw "Baseline provenance requires a resolved runner image/version and exact dotnet SDK for lane '$($row.lane)'."
        }
        # `jobName` is written into the retained baseline, so it is provenance and must be checked like
        # provenance. For an allowlisted lane the binding is exact — a row cannot claim a sibling's job.
        # A lane outside the allowlist (only the legacy console import's unsharded `backend`, which
        # `Get-NervTestEvidenceLaneJobs` deliberately omits) still has to name some job.
        if ([string]::IsNullOrWhiteSpace([string]$row.jobName)) {
            throw "Baseline lane provenance for lane '$($row.lane)' must name the job that produced it."
        }
        if ($laneJobs.Contains([string]$row.lane) -and [string]$row.jobName -cne [string]$laneJobs[[string]$row.lane]) {
            throw "Baseline lane provenance for lane '$($row.lane)' names the wrong authoritative job '$($row.jobName)'."
        }
    }
    if (@($laneProvenance | ForEach-Object { [string]$_.lane } | Sort-Object -Unique).Count -ne $laneProvenance.Count) {
        throw 'Baseline lane provenance rows must name unique lanes.'
    }
    [string[]] $recordedLanes = @($Summaries | ForEach-Object { @($_.assemblies) } | ForEach-Object { [string]$_.lane } | Sort-Object -Unique)
    [string[]] $provenanceLanes = @($laneProvenance | ForEach-Object { [string]$_.lane } | Sort-Object -Unique)
    if (($provenanceLanes -join '|') -cne ($recordedLanes -join '|')) {
        throw "Baseline lane provenance must cover exactly the lanes the baseline records; provenance=[$($provenanceLanes -join ', ')] recorded=[$($recordedLanes -join ', ')]."
    }
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
        # schema 2 replaced the flat source.runnerOs/runnerImage/dotnetSdk trio with source.laneProvenance.
        # A schema-1 file's flat trio is the *first lane's* environment and must never be read as run-wide.
        schemaVersion = 2
        toolVersion = 'MAN-661-v2'
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
            headSha = [string]$SourceMetadata.headSha
            testedSha = [string]$SourceMetadata.testedSha
            sourceUrl = [string]$SourceMetadata.sourceUrl
            event = [string]$SourceMetadata.event
            headBranch = [string]$SourceMetadata.headBranch
            conclusion = [string]$SourceMetadata.conclusion
            jobConclusion = [string]$SourceMetadata.jobConclusion
            laneProvenance = @($laneProvenance | Sort-Object { [string]$_.lane } | ForEach-Object {
                [pscustomobject][ordered]@{
                    lane = [string]$_.lane
                    jobName = [string]$_.jobName
                    runnerOs = [string]$_.runnerOs
                    runnerImage = [string]$_.runnerImage
                    dotnetSdk = [string]$_.dotnetSdk
                }
            })
            selectedLanes = @($SourceMetadata.selectedLanes)
            generatorCommand = [string]$SourceMetadata.generatorCommand
        }
        assemblies = $assemblies
    }
}
