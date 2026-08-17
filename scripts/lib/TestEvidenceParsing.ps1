# Script-Governance:
#   Category: library
#   SideEffects:
#     - None; defines TestEvidence TRX and console parsing functions
#   Writes:
#     - None
#   Requires:
#     - PowerShell 7

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
        [Parameter(Mandatory)] [object] $RunMetadata
    )

    if (-not (Test-NervTestEvidenceLaneName ([string]$RunMetadata.lane))) {
        throw "Invalid evidence lane '$($RunMetadata.lane)'."
    }
    $records = [Collections.Generic.List[object]]::new()
    $trxElapsedMilliseconds = 0.0
    $trxRuns = [Collections.Generic.List[object]]::new()
    foreach ($trxPath in @(Get-NervOrdinalSorted -Values @($Path | ForEach-Object { [string]$_ }))) {
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
        $normalizedIdentityNamespace = 'urn:nerv-iip:test-evidence:assembly-identity:v1'
        $definitionNodes = @($document.SelectNodes("//*[local-name()='TestDefinitions']/*[local-name()='UnitTest']"))
        $reservedAssemblyIdentityAttributes = [Collections.Generic.List[object]]::new()
        foreach ($definitionNode in $definitionNodes) {
            foreach ($attribute in @($definitionNode.Attributes)) {
                if ([string]::Equals([string]$attribute.LocalName, 'assemblyIdentity', [StringComparison]::Ordinal)) {
                    $reservedAssemblyIdentityAttributes.Add($attribute)
                }
            }
        }
        $hasReservedAssemblyIdentityMarker = $reservedAssemblyIdentityAttributes.Count -gt 0
        if ($hasReservedAssemblyIdentityMarker) {
            # Fail closed on the reserved local name in any other namespace, duplicate marker
            # attributes, or a partially marked definition set. The namespace URI is the authority;
            # the XML prefix is intentionally irrelevant.
            foreach ($definitionNode in $definitionNodes) {
                $definitionMarkerAttributes = @($definitionNode.Attributes | Where-Object {
                    [string]::Equals([string]$_.LocalName, 'assemblyIdentity', [StringComparison]::Ordinal)
                })
                if ($definitionMarkerAttributes.Count -ne 1 -or
                    -not [string]::Equals([string]$definitionMarkerAttributes[0].NamespaceURI, $normalizedIdentityNamespace, [StringComparison]::Ordinal)) {
                    throw [IO.InvalidDataException]::new("TRX assembly identity marker metadata is malformed or uses an unsupported namespace in '$([IO.Path]::GetFullPath($trxPath))'.")
                }
            }
            if ($persistedHeadSha -notmatch '^[0-9a-f]{40}$' -or $persistedTestedSha -notmatch '^[0-9a-f]{40}$' -or
                -not [string]::Equals($persistedHeadSha, [string]$RunMetadata.headSha, [StringComparison]::Ordinal) -or
                -not [string]::Equals($persistedTestedSha, [string]$RunMetadata.testedSha, [StringComparison]::Ordinal)) {
                throw [IO.InvalidDataException]::new("TRX assembly identity markers require exact normalized head and tested provenance in '$([IO.Path]::GetFullPath($trxPath))'.")
            }
        }
        elseif (-not [string]::IsNullOrWhiteSpace($persistedHeadSha) -or -not [string]::IsNullOrWhiteSpace($persistedTestedSha)) {
            # Ordinal (#1509): a commit SHA is an identifier and this is the guard that stops a
            # normalized TRX from being read under someone else's provenance. `-cne` is culture-aware,
            # so a persisted SHA carrying an ignorable character would compare equal and pass.
            if (-not [string]::Equals($persistedHeadSha, [string]$RunMetadata.headSha, [StringComparison]::Ordinal) -or
                -not [string]::Equals($persistedTestedSha, [string]$RunMetadata.testedSha, [StringComparison]::Ordinal)) {
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
        foreach ($definition in $definitionNodes) {
            $method = $definition.SelectSingleNode("./*[local-name()='TestMethod']")
            $hasAssemblyIdentityMarker = $definition.HasAttribute('assemblyIdentity', $normalizedIdentityNamespace)
            $assemblyIdentityMarker = $definition.GetAttribute('assemblyIdentity', $normalizedIdentityNamespace)
            if ($hasAssemblyIdentityMarker -and
                ([string]::Equals($assemblyIdentityMarker, 'null', [StringComparison]::Ordinal) -or [string]::Equals($assemblyIdentityMarker, 'empty', [StringComparison]::Ordinal)) -and
                -not [string]::IsNullOrEmpty([string]$definition.storage)) {
                throw [IO.InvalidDataException]::new("TRX assembly identity markers require empty standard storage in '$([IO.Path]::GetFullPath($trxPath))'.")
            }
            if ($hasAssemblyIdentityMarker -and [string]::Equals($assemblyIdentityMarker, 'verbatim', [StringComparison]::Ordinal) -and
                ([string]::IsNullOrWhiteSpace([string]$definition.storage) -or [string]$definition.storage -notmatch '[/\\]')) {
                throw [IO.InvalidDataException]::new("TRX verbatim assembly identity marker requires non-empty canonical path storage in '$([IO.Path]::GetFullPath($trxPath))'.")
            }
            $assembly = if (-not $hasAssemblyIdentityMarker) { [IO.Path]::GetFileName([string]$definition.storage) }
                elseif ([string]::Equals($assemblyIdentityMarker, 'null', [StringComparison]::Ordinal)) { $null }
                elseif ([string]::Equals($assemblyIdentityMarker, 'empty', [StringComparison]::Ordinal)) { '' }
                elseif ([string]::Equals($assemblyIdentityMarker, 'verbatim', [StringComparison]::Ordinal)) { [string]$definition.storage }
                else { throw [IO.InvalidDataException]::new("TRX has an unsupported normalized assembly identity marker in '$([IO.Path]::GetFullPath($trxPath))'.") }
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
        $actualPassed = @($results | Where-Object { Test-NervOrdinalEquals ([string]$_.outcome) 'Passed' }).Count
        $actualFailed = @($results | Where-Object { Test-NervOrdinalEquals ([string]$_.outcome) 'Failed' }).Count
        $actualSkipped = @($results | Where-Object { Test-NervOrdinalEquals ([string]$_.outcome) 'NotExecuted' }).Count
        if ($counterTotal -ne $results.Count -or $counterExecuted -ne ($counterPassed + $counterFailed) -or
            $counterPassed -ne $actualPassed -or $counterFailed -ne $actualFailed -or $counterSkipped -ne $actualSkipped) {
            throw [IO.InvalidDataException]::new("TRX ResultSummary/Counters do not match Results in '$([IO.Path]::GetFullPath($trxPath))'.")
        }
        # Assembly is an identity-bearing nullable value in normalized TRX. Do not route it through
        # Get-NervOrdinalSorted: that wrapper intentionally accepts ordinary non-empty identifiers,
        # and a [string] projection would collapse null and empty before it could validate them.
        $assembliesInRun = @(Get-NervOrdinalGroups -Items @($definitions.Values) -KeySelector {
            param($row) Get-NervOrdinalCompositeKey -Components @($row.assembly)
        } | ForEach-Object { $_.Group[0].assembly })
        if ($assembliesInRun.Count -gt 1) { throw [IO.InvalidDataException]::new("TRX contains multiple assemblies in '$([IO.Path]::GetFullPath($trxPath))'.") }
        $trxRuns.Add([pscustomobject][ordered]@{
            lane = [string]$RunMetadata.lane
            # Preserve the identity restored from the normalized marker. This projection feeds the
            # summary timing join; casting here would collapse null into empty after the record rows
            # had already restored it correctly.
            assembly = if ($assembliesInRun.Count -eq 1) { $assembliesInRun[0] } else { [IO.Path]::GetFileNameWithoutExtension($trxPath) }
            elapsedMilliseconds = $elapsed
            total = $counterTotal
            executed = $counterExecuted
            passed = $counterPassed
            failed = $counterFailed
            skipped = $counterSkipped
        })

        $ordinals = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
        foreach ($result in $results) {
            $rawOutcome = [string]$result.outcome
            $outcomeMapping = Resolve-NervTrxOutcomeMapping -TrxOutcome $rawOutcome
            if ($null -eq $outcomeMapping) {
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
            $ordinalKey = Get-NervOrdinalCompositeKey -Components @($definition.testName, $displayName)
            $ordinal = if ($ordinals.ContainsKey($ordinalKey)) { [int]$ordinals[$ordinalKey] + 1 } else { 1 }
            $ordinals[$ordinalKey] = $ordinal
            $rawError = if (Test-NervOrdinalEquals $rawOutcome 'Failed') {
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
                assembly = $definition.assembly
                testName = [string]$definition.testName
                displayName = $displayName
                testClassName = [string]$definition.className
                testMethodName = [string]$definition.methodName
                definitionId = Get-NervStableEvidenceGuid (Get-NervOrdinalCompositeKey -Components @($definition.assembly, $definition.testName))
                testInstanceId = if ($hasPersistedExecutionId) { $persistedExecutionId.ToString() } else { Get-NervStableEvidenceGuid (Get-NervOrdinalCompositeKey -Components @($definition.assembly, $definition.testName, $displayName, [string]$ordinal)) }
                durationTicks = [long]$duration.Ticks
                durationMilliseconds = [double]$duration.TotalMilliseconds
                outcome = [string]$outcomeMapping.NormalizedOutcome
                skipReason = if (Test-NervOrdinalEquals $rawOutcome 'NotExecuted') { Get-NervTrxSkipReason -UnitTestResult $result } else { $null }
                errorMessage = ConvertTo-NervRetainedFailureText $rawError
                redactionCount = if ($hasPersistedRedactionCount) { $persistedRedactionCount } else { [int]$retainedDisplay.redactionCount + $(if ([string]::IsNullOrWhiteSpace($rawError)) { 0 } else { 1 }) }
            })
        }
    }
    return [pscustomobject][ordered]@{
        Records = @($records)
        TrxElapsedMilliseconds = [double]$trxElapsedMilliseconds
        TrxRuns = @($trxRuns)
    }
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
        $tailMilliseconds = if (Test-NervOrdinalEquals ([string]$summaryMatch.Groups['unit'].Value) 'ms') { [double]$summaryMatch.Groups['value'].Value } else { [double]$summaryMatch.Groups['value'].Value * 1000.0 }
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
    $duplicates = @(Get-NervOrdinalGroups -Items @($assemblies) -KeySelector { param($row) [string]$row.assembly } | Where-Object { @($_.Group).Count -gt 1 })
    if ($duplicates.Count -gt 0) { throw "Ambiguous console summaries for assembly '$($duplicates[0].Name)'." }
    [pscustomobject][ordered]@{
        schemaVersion = 1
        granularity = 'project'
        durationMetric = 'project-wall-clock'
        lane = 'backend'
        assemblies = @(Get-NervOrdinalSortedBy -Items @($assemblies) -KeySelector { param($row) [string]$row.assembly })
    }
}
