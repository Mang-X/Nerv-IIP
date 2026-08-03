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
