# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs backend test-determinism checker fixture cases
#   Writes:
#     - Temporary baseline JSON files under the operating-system temp directory
#     - artifacts/script-logs/backend-test-determinism-fixture-*/**
#     - artifacts/script-tests/backend-test-determinism-*/**
#   Cleanup:
#     - Removes every temporary baseline, source fixture, and governed command log created by this test
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')

$checker = Join-Path $repoRoot 'scripts/check-backend-test-determinism.ps1'
$fixtureRoot = Join-Path $repoRoot 'scripts/tests/fixtures/backend-test-determinism'
$validBaselinePath = Join-Path $fixtureRoot 'valid-baseline.json'
$runId = [Guid]::NewGuid().ToString('N')
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-backend-test-determinism-$runId"
$scriptLogName = "backend-test-determinism-fixture-$runId"
$scriptLogRoot = Join-Path $repoRoot "artifacts/script-logs/$scriptLogName"
$generatedFixtureRoot = Join-Path $repoRoot "artifacts/script-tests/backend-test-determinism-$runId"

function Assert-True {
    param(
        [Parameter(Mandatory)]
        [bool] $Condition,

        [Parameter(Mandatory)]
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [object] $Value
    )

    $json = $Value | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($Path, "$json`n", [System.Text.UTF8Encoding]::new($false))
}

function Invoke-CheckerCase {
    param(
        [Parameter(Mandatory)]
        [string] $SourceRoot,

        [Parameter(Mandatory)]
        [string] $BaselinePath,

        [string[]] $PermanentAllowlist
    )

    $arguments = @('-SourceRoot', $SourceRoot, '-BaselinePath', $BaselinePath)
    if ($PSBoundParameters.ContainsKey('PermanentAllowlist')) {
        $arguments += '-PermanentAllowlist'
        $arguments += $PermanentAllowlist
    }

    $exitCode = 0
    $logDirectory = $null
    try {
        $result = Invoke-PwshScript `
            -ScriptPath $checker `
            -Arguments $arguments `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 60 `
            -Name $scriptLogName
        $logDirectory = $result.LogDirectory
    }
    catch {
        if ($_.Exception.Message -notmatch "exited with (?<exitCode>\d+)") {
            throw
        }

        $exitCode = [int] $Matches['exitCode']
        $latestLogDirectory = Get-ChildItem -LiteralPath $scriptLogRoot -Directory |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
        if ($null -eq $latestLogDirectory) {
            throw "Governed checker process failed without a log directory: $($_.Exception.Message)"
        }
        $logDirectory = $latestLogDirectory.FullName
    }

    $stdout = Get-Content -LiteralPath (Join-Path $logDirectory 'stdout.log') -Raw
    $stderr = Get-Content -LiteralPath (Join-Path $logDirectory 'stderr.log') -Raw
    $output = ($stdout, $stderr) -join [Environment]::NewLine

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = $output
    }
}

function New-OccurrenceCase {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [int] $ActualCount,

        [Parameter(Mandatory)]
        [object] $ExpectedCount
    )

    $caseRoot = Join-Path $generatedFixtureRoot $Name
    [System.IO.Directory]::CreateDirectory($caseRoot) | Out-Null
    $sourcePath = Join-Path $caseRoot 'occurrences.cs'
    $sourceLines = @('public static class OccurrenceFixture', '{')
    for ($index = 0; $index -lt $ActualCount; $index++) {
        $sourceLines += '    Task.Delay(1);'
    }
    $sourceLines += '}'
    [System.IO.File]::WriteAllLines($sourcePath, $sourceLines, [System.Text.UTF8Encoding]::new($false))

    $relativePath = [System.IO.Path]::GetRelativePath($repoRoot, $sourcePath) -replace '\\', '/'
    $baselinePath = Join-Path $tempRoot "$Name.json"
    Write-JsonFile -Path $baselinePath -Value ([ordered]@{
        schema = 3
        exceptions = @([ordered]@{
            path = $relativePath
            pattern = 'Task.Delay'
            lineTextSha256 = '1106b2c99718c440becaeed61063d2b2dd38c61c1236e256560b49fdbcf5b2bf'
            occurrenceCount = $ExpectedCount
            classification = 'expiring-debt'
            ownerIssue = 'MAN-662'
            registeredByIssue = '#1487'
            reason = 'Fixture intentionally repeats one identical source line to verify occurrence accounting.'
            exitCondition = 'Delete when occurrence accounting no longer uses this fixture.'
            registeredOn = '2026-08-08'
            expiresOn = '2026-09-22'
        })
    })

    return [pscustomobject]@{
        SourcePath = $sourcePath
        BaselinePath = $baselinePath
    }
}

function New-RawContinuationCase {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $OpeningLine,

        [Parameter(Mandatory)]
        [string] $ClosingLine,

        [Parameter(Mandatory)]
        [int] $DelayMilliseconds
    )

    $caseRoot = Join-Path $generatedFixtureRoot $Name
    [System.IO.Directory]::CreateDirectory($caseRoot) | Out-Null
    $sourcePath = Join-Path $caseRoot 'continuation.cs'
    $sourceLines = @(
        'using System.Threading.Tasks;',
        '',
        'public static class RawEmptyContinuationFixture',
        '{',
        '    public static async Task RunAsync()',
        '    {',
        "        $OpeningLine",
        '',
        "        $ClosingLine",
        "        await Task.Delay($DelayMilliseconds);",
        '    }',
        '}'
    )
    [System.IO.File]::WriteAllLines($sourcePath, $sourceLines, [System.Text.UTF8Encoding]::new($false))

    return $sourcePath
}

function New-SixQuoteRawFollowedByDelayCase {
    $caseRoot = Join-Path $generatedFixtureRoot 'raw-six-quote-followed-by-delay'
    [System.IO.Directory]::CreateDirectory($caseRoot) | Out-Null
    $sourcePath = Join-Path $caseRoot 'followed-by-delay.cs'
    $sourceLines = @(
        'using System.Threading.Tasks;',
        '',
        'public static class SixQuoteRawFollowedByDelayFixture',
        '{',
        '    public static async Task RunAsync()',
        '    {',
        '        var literalText = """"""Task.Delay(802)"""""";',
        '        await Task.Delay(803);',
        '    }',
        '}'
    )
    [System.IO.File]::WriteAllLines($sourcePath, $sourceLines, [System.Text.UTF8Encoding]::new($false))

    return $sourcePath
}

function New-PermanentClassificationSource {
    param(
        [Parameter(Mandatory)]
        [string] $Name
    )

    $caseRoot = Join-Path $generatedFixtureRoot $Name
    [System.IO.Directory]::CreateDirectory($caseRoot) | Out-Null
    $sourcePath = Join-Path $caseRoot 'permanent.cs'
    $mutationLine = 'Environment.SetEnvironmentVariable("NERV_PERMANENT_FIXTURE", "value");'
    $sourceLines = @(
        'using System;',
        '',
        'public static class PermanentClassificationFixture',
        '{',
        '    public static void MutateProcessState()',
        '    {',
        "        $mutationLine",
        '    }',
        '}'
    )
    [System.IO.File]::WriteAllLines($sourcePath, $sourceLines, [System.Text.UTF8Encoding]::new($false))

    $hashBytes = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($mutationLine))

    return [pscustomobject]@{
        SourcePath = $sourcePath
        RelativePath = [System.IO.Path]::GetRelativePath($repoRoot, $sourcePath) -replace '\\', '/'
        LineTextSha256 = [System.Convert]::ToHexString($hashBytes).ToLowerInvariant()
    }
}

function New-OtherPatternSource {
    param(
        [Parameter(Mandatory)]
        [string] $Name
    )

    $caseRoot = Join-Path $generatedFixtureRoot $Name
    [System.IO.Directory]::CreateDirectory($caseRoot) | Out-Null
    $sourcePath = Join-Path $caseRoot 'other-pattern.cs'
    $sleepLine = 'Thread.Sleep(25);'
    $sourceLines = @(
        'using System.Threading;',
        '',
        'public static class OtherPatternFixture',
        '{',
        '    public static void Pause()',
        '    {',
        "        $sleepLine",
        '    }',
        '}'
    )
    [System.IO.File]::WriteAllLines($sourcePath, $sourceLines, [System.Text.UTF8Encoding]::new($false))

    $hashBytes = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($sleepLine))

    return [pscustomobject]@{
        SourcePath = $sourcePath
        RelativePath = [System.IO.Path]::GetRelativePath($repoRoot, $sourcePath) -replace '\\', '/'
        LineTextSha256 = [System.Convert]::ToHexString($hashBytes).ToLowerInvariant()
    }
}

function New-PermanentClassificationRow {
    param(
        [Parameter(Mandatory)]
        [object] $Source
    )

    return [ordered]@{
        path = $Source.RelativePath
        pattern = 'StaticSetter'
        lineTextSha256 = $Source.LineTextSha256
        occurrenceCount = 1
        classification = 'permanent'
        reason = 'Fixture proves a permanent classification is admitted only on an allow-listed path.'
        rationale = 'The mutation is the behaviour under test, so there is nothing to expire towards.'
    }
}

function Assert-CheckerCase {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $SourceRoot,

        [Parameter(Mandatory)]
        [string] $BaselinePath,

        [Parameter(Mandatory)]
        [int] $ExpectedExitCode,

        [string[]] $ExpectedOutput = @(),

        [string[]] $UnexpectedOutput = @(),

        [hashtable] $MinimumOccurrences = @{},

        [string[]] $PermanentAllowlist
    )

    $result = if ($PSBoundParameters.ContainsKey('PermanentAllowlist')) {
        Invoke-CheckerCase -SourceRoot $SourceRoot -BaselinePath $BaselinePath -PermanentAllowlist $PermanentAllowlist
    }
    else {
        Invoke-CheckerCase -SourceRoot $SourceRoot -BaselinePath $BaselinePath
    }
    if ($result.ExitCode -ne $ExpectedExitCode) {
        Write-Host $result.Output
        throw "Expected '$Name' to exit $ExpectedExitCode, got $($result.ExitCode)."
    }

    foreach ($expected in $ExpectedOutput) {
        Assert-True -Condition $result.Output.Contains($expected) -Message "Expected '$Name' output to contain '$expected'. Output: $($result.Output)"
    }

    foreach ($unexpected in $UnexpectedOutput) {
        Assert-True -Condition (-not $result.Output.Contains($unexpected)) -Message "Expected '$Name' output not to contain '$unexpected'. Output: $($result.Output)"
    }

    foreach ($entry in $MinimumOccurrences.GetEnumerator()) {
        $count = [regex]::Matches($result.Output, [regex]::Escape("$($entry.Key)")).Count
        Assert-True -Condition ($count -ge [int] $entry.Value) -Message "Expected '$Name' output to contain '$($entry.Key)' at least $($entry.Value) times, got $count. Output: $($result.Output)"
    }
}

[System.IO.Directory]::CreateDirectory($tempRoot) | Out-Null

try {
    $emptyBaselinePath = Join-Path $tempRoot 'empty.json'
    Write-JsonFile -Path $emptyBaselinePath -Value ([ordered]@{ schema = 3; exceptions = @() })

    Assert-CheckerCase `
        -Name 'clean source' `
        -SourceRoot (Join-Path $fixtureRoot 'clean.cs') `
        -BaselinePath $emptyBaselinePath `
        -ExpectedExitCode 0

    Assert-CheckerCase `
        -Name 'interpolated raw expression remains executable code' `
        -SourceRoot (Join-Path $fixtureRoot 'interpolated-raw-expression.cs') `
        -BaselinePath $emptyBaselinePath `
        -ExpectedExitCode 1 `
        -ExpectedOutput @('Task.Delay')

    Assert-CheckerCase `
        -Name 'ordinary interpolated expression remains executable code' `
        -SourceRoot (Join-Path $fixtureRoot 'interpolated-string-expression.cs') `
        -BaselinePath $emptyBaselinePath `
        -ExpectedExitCode 1 `
        -ExpectedOutput @('Task.Delay(655)')

    Assert-CheckerCase `
        -Name 'verbatim interpolated expression remains executable code' `
        -SourceRoot (Join-Path $fixtureRoot 'interpolated-verbatim-expression.cs') `
        -BaselinePath $emptyBaselinePath `
        -ExpectedExitCode 1 `
        -ExpectedOutput @('Task.Delay(656)')

    Assert-CheckerCase `
        -Name 'nested interpolated expression remains executable code' `
        -SourceRoot (Join-Path $fixtureRoot 'nested-interpolated-string-expression.cs') `
        -BaselinePath $emptyBaselinePath `
        -ExpectedExitCode 1 `
        -ExpectedOutput @('Task.Delay(657)')

    Assert-CheckerCase `
        -Name 'empty raw string does not hide following delay' `
        -SourceRoot (Join-Path $fixtureRoot 'raw-empty-followed-by-delay.cs') `
        -BaselinePath $emptyBaselinePath `
        -ExpectedExitCode 1 `
        -ExpectedOutput @('Task.Delay')

    $rawContinuationCases = @(
        [pscustomobject]@{
            Name = 'empty raw string concatenation'
            SourcePath = New-RawContinuationCase -Name 'raw-empty-concatenation' -OpeningLine 'var value = """' -ClosingLine '""" + string.Empty;' -DelayMilliseconds 701
        },
        [pscustomobject]@{
            Name = 'empty raw string member access'
            SourcePath = New-RawContinuationCase -Name 'raw-empty-member-access' -OpeningLine 'var length = """' -ClosingLine '""".Length;' -DelayMilliseconds 702
        },
        [pscustomobject]@{
            Name = 'empty raw string null coalescing'
            SourcePath = New-RawContinuationCase -Name 'raw-empty-null-coalescing' -OpeningLine 'var fallback = (string?)"""' -ClosingLine '""" ?? string.Empty;' -DelayMilliseconds 703
        },
        [pscustomobject]@{
            Name = 'empty raw string conditional'
            SourcePath = New-RawContinuationCase -Name 'raw-empty-conditional' -OpeningLine 'var selected = true ? """' -ClosingLine '""" : string.Empty;' -DelayMilliseconds 704
        }
    )
    foreach ($rawContinuationCase in $rawContinuationCases) {
        Assert-CheckerCase `
            -Name $rawContinuationCase.Name `
            -SourceRoot $rawContinuationCase.SourcePath `
            -BaselinePath $emptyBaselinePath `
            -ExpectedExitCode 1 `
            -ExpectedOutput @(':10 [Task.Delay]')
    }

    Assert-CheckerCase `
        -Name 'six quote raw text is hidden before a later delay' `
        -SourceRoot (New-SixQuoteRawFollowedByDelayCase) `
        -BaselinePath $emptyBaselinePath `
        -ExpectedExitCode 1 `
        -ExpectedOutput @(':8 [Task.Delay]', 'Task.Delay(803)') `
        -UnexpectedOutput @('Task.Delay(802)')

    Assert-CheckerCase `
        -Name 'raw text and non-interpolation braces stay clean' `
        -SourceRoot (Join-Path $fixtureRoot 'raw-string-clean.cs') `
        -BaselinePath $emptyBaselinePath `
        -ExpectedExitCode 0

    Assert-CheckerCase `
        -Name 'unexplained delays' `
        -SourceRoot (Join-Path $fixtureRoot 'unexplained-delay.cs') `
        -BaselinePath $emptyBaselinePath `
        -ExpectedExitCode 1 `
        -ExpectedOutput @('Task.Delay', 'Thread.Sleep')

    Assert-CheckerCase `
        -Name 'short lease and renewal windows' `
        -SourceRoot (Join-Path $fixtureRoot 'short-lease.cs') `
        -BaselinePath $emptyBaselinePath `
        -ExpectedExitCode 1 `
        -MinimumOccurrences @{ ShortLease = 2 }

    Assert-CheckerCase `
        -Name 'unreachable endpoint sentinels' `
        -SourceRoot (Join-Path $fixtureRoot 'unreachable-address.cs') `
        -BaselinePath $emptyBaselinePath `
        -ExpectedExitCode 1 `
        -MinimumOccurrences @{ UnreachableAddress = 2 }

    Assert-CheckerCase `
        -Name 'process-global setters' `
        -SourceRoot (Join-Path $fixtureRoot 'static-setter.cs') `
        -BaselinePath $emptyBaselinePath `
        -ExpectedExitCode 1 `
        -MinimumOccurrences @{ StaticSetter = 5 }

    Assert-CheckerCase `
        -Name 'complete matching baseline' `
        -SourceRoot $fixtureRoot `
        -BaselinePath $validBaselinePath `
        -ExpectedExitCode 0

    $exactOccurrenceCase = New-OccurrenceCase -Name 'occurrence-exact' -ActualCount 2 -ExpectedCount 2
    Assert-CheckerCase -Name 'matching occurrence count' -SourceRoot $exactOccurrenceCase.SourcePath -BaselinePath $exactOccurrenceCase.BaselinePath -ExpectedExitCode 0

    $growthOccurrenceCase = New-OccurrenceCase -Name 'occurrence-growth' -ActualCount 3 -ExpectedCount 2
    Assert-CheckerCase -Name 'occurrence growth' -SourceRoot $growthOccurrenceCase.SourcePath -BaselinePath $growthOccurrenceCase.BaselinePath -ExpectedExitCode 1 -ExpectedOutput @('occurrence count changed', 'expected 2', 'actual 3', ':5 [Task.Delay]')

    $shrinkOccurrenceCase = New-OccurrenceCase -Name 'occurrence-shrink' -ActualCount 1 -ExpectedCount 2
    Assert-CheckerCase -Name 'occurrence shrink' -SourceRoot $shrinkOccurrenceCase.SourcePath -BaselinePath $shrinkOccurrenceCase.BaselinePath -ExpectedExitCode 1 -ExpectedOutput @('occurrence count changed', 'expected 2', 'actual 1')

    $validBaseline = Get-Content -LiteralPath $validBaselinePath -Raw | ConvertFrom-Json
    $matchingSource = Join-Path $repoRoot $validBaseline.exceptions[0].path
    $validExpiringRow = (Get-Content -LiteralPath $exactOccurrenceCase.BaselinePath -Raw | ConvertFrom-Json).exceptions[0]
    $matchingExpiringSource = $exactOccurrenceCase.SourcePath

    $missingFieldRow = $validBaseline.exceptions[0].PSObject.Copy()
    $missingFieldRow.PSObject.Properties.Remove('reason')
    $missingFieldPath = Join-Path $tempRoot 'missing-field.json'
    Write-JsonFile -Path $missingFieldPath -Value ([ordered]@{ schema = 3; exceptions = @($missingFieldRow) })
    Assert-CheckerCase -Name 'missing baseline metadata' -SourceRoot $matchingSource -BaselinePath $missingFieldPath -ExpectedExitCode 1 -ExpectedOutput @('missing required field')

    $nonIntegerOccurrenceRow = $validBaseline.exceptions[0].PSObject.Copy()
    $nonIntegerOccurrenceRow.occurrenceCount = '1'
    $nonIntegerOccurrencePath = Join-Path $tempRoot 'non-integer-occurrence.json'
    Write-JsonFile -Path $nonIntegerOccurrencePath -Value ([ordered]@{ schema = 3; exceptions = @($nonIntegerOccurrenceRow) })
    Assert-CheckerCase -Name 'non-integer occurrence count' -SourceRoot $matchingSource -BaselinePath $nonIntegerOccurrencePath -ExpectedExitCode 1 -ExpectedOutput @('occurrenceCount must be a positive integer')

    $zeroOccurrenceRow = $validBaseline.exceptions[0].PSObject.Copy()
    $zeroOccurrenceRow.occurrenceCount = 0
    $zeroOccurrencePath = Join-Path $tempRoot 'zero-occurrence.json'
    Write-JsonFile -Path $zeroOccurrencePath -Value ([ordered]@{ schema = 3; exceptions = @($zeroOccurrenceRow) })
    Assert-CheckerCase -Name 'zero occurrence count' -SourceRoot $matchingSource -BaselinePath $zeroOccurrencePath -ExpectedExitCode 1 -ExpectedOutput @('occurrenceCount must be a positive integer')

    $numericReasonRow = $validBaseline.exceptions[0].PSObject.Copy()
    $numericReasonRow.reason = 123
    $numericReasonPath = Join-Path $tempRoot 'numeric-reason.json'
    Write-JsonFile -Path $numericReasonPath -Value ([ordered]@{ schema = 3; exceptions = @($numericReasonRow) })
    Assert-CheckerCase -Name 'numeric string metadata' -SourceRoot $matchingSource -BaselinePath $numericReasonPath -ExpectedExitCode 1 -ExpectedOutput @('reason must be a non-empty string')

    $objectExitConditionRow = $validBaseline.exceptions[0].PSObject.Copy()
    $objectExitConditionRow.exitCondition = [ordered]@{ text = 'not a string' }
    $objectExitConditionPath = Join-Path $tempRoot 'object-exit-condition.json'
    Write-JsonFile -Path $objectExitConditionPath -Value ([ordered]@{ schema = 3; exceptions = @($objectExitConditionRow) })
    Assert-CheckerCase -Name 'object string metadata' -SourceRoot $matchingSource -BaselinePath $objectExitConditionPath -ExpectedExitCode 1 -ExpectedOutput @('exitCondition must be a non-empty string')

    # A follow-up GitHub issue is as valid an owner as a Linear key; the repo baseline uses the former
    # so debts outlive the change that registered them.
    $githubOwnerRows = @(
        $validBaseline.exceptions | ForEach-Object {
            $row = $_.PSObject.Copy()
            $row.ownerIssue = '#1470'
            $row
        }
    )
    $githubOwnerPath = Join-Path $tempRoot 'github-owner.json'
    Write-JsonFile -Path $githubOwnerPath -Value ([ordered]@{ schema = 3; exceptions = $githubOwnerRows })
    Assert-CheckerCase -Name 'github issue owner' -SourceRoot $fixtureRoot -BaselinePath $githubOwnerPath -ExpectedExitCode 0 -ExpectedOutput @('check passed')

    $badOwnerRow = $validBaseline.exceptions[0].PSObject.Copy()
    $badOwnerRow.ownerIssue = 'someone@example.com'
    $badOwnerPath = Join-Path $tempRoot 'bad-owner.json'
    Write-JsonFile -Path $badOwnerPath -Value ([ordered]@{ schema = 3; exceptions = @($badOwnerRow) })
    Assert-CheckerCase -Name 'unowned baseline row' -SourceRoot $matchingSource -BaselinePath $badOwnerPath -ExpectedExitCode 1 -ExpectedOutput @('ownerIssue must be')

    $overlongDebtRow = $validExpiringRow.PSObject.Copy()
    $overlongDebtRow.registeredByIssue = '#1487'
    $overlongDebtRow.registeredOn = '2026-08-08'
    $overlongDebtRow.expiresOn = '2026-09-23'
    $overlongDebtPath = Join-Path $tempRoot 'overlong-debt.json'
    Write-JsonFile -Path $overlongDebtPath -Value ([ordered]@{ schema = 3; exceptions = @($overlongDebtRow) })
    Assert-CheckerCase `
        -Name 'expiring debt registered for 46 days' `
        -SourceRoot $matchingExpiringSource `
        -BaselinePath $overlongDebtPath `
        -ExpectedExitCode 1 `
        -ExpectedOutput @('expiresOn must be no later than 45 days after registeredOn')

    $selfGuaranteedRow = $validExpiringRow.PSObject.Copy()
    $selfGuaranteedRow.registeredByIssue = 'MAN-662'
    $selfGuaranteedRow.registeredOn = '2026-08-08'
    $selfGuaranteedRow.expiresOn = '2026-09-22'
    $selfGuaranteedPath = Join-Path $tempRoot 'self-guaranteed.json'
    Write-JsonFile -Path $selfGuaranteedPath -Value ([ordered]@{ schema = 3; exceptions = @($selfGuaranteedRow) })
    Assert-CheckerCase `
        -Name 'self-guaranteed expiring debt' `
        -SourceRoot $matchingExpiringSource `
        -BaselinePath $selfGuaranteedPath `
        -ExpectedExitCode 1 `
        -ExpectedOutput @('registeredByIssue must differ from ownerIssue')

    $missingRegisteredByRow = $validExpiringRow.PSObject.Copy()
    $missingRegisteredByRow.PSObject.Properties.Remove('registeredByIssue')
    $missingRegisteredByRow.registeredOn = '2026-08-08'
    $missingRegisteredByRow.expiresOn = '2026-09-22'
    $missingRegisteredByPath = Join-Path $tempRoot 'missing-registered-by.json'
    Write-JsonFile -Path $missingRegisteredByPath -Value ([ordered]@{ schema = 3; exceptions = @($missingRegisteredByRow) })
    Assert-CheckerCase `
        -Name 'expiring debt without registeredByIssue' `
        -SourceRoot $matchingExpiringSource `
        -BaselinePath $missingRegisteredByPath `
        -ExpectedExitCode 1 `
        -ExpectedOutput @("classification 'expiring-debt' is missing required field(s): registeredByIssue")

    $malformedRegisteredByRow = $validExpiringRow.PSObject.Copy()
    $malformedRegisteredByRow.registeredByIssue = 'issue-1487'
    $malformedRegisteredByRow.registeredOn = '2026-08-08'
    $malformedRegisteredByRow.expiresOn = '2026-09-22'
    $malformedRegisteredByPath = Join-Path $tempRoot 'malformed-registered-by.json'
    Write-JsonFile -Path $malformedRegisteredByPath -Value ([ordered]@{ schema = 3; exceptions = @($malformedRegisteredByRow) })
    Assert-CheckerCase `
        -Name 'expiring debt with malformed registeredByIssue' `
        -SourceRoot $matchingExpiringSource `
        -BaselinePath $malformedRegisteredByPath `
        -ExpectedExitCode 1 `
        -ExpectedOutput @('registeredByIssue must be a MAN issue key or a #<number> GitHub issue')

    $missingRegisteredOnRow = $validExpiringRow.PSObject.Copy()
    $missingRegisteredOnRow.registeredByIssue = '#1487'
    $missingRegisteredOnRow.PSObject.Properties.Remove('registeredOn')
    $missingRegisteredOnRow.expiresOn = '2026-09-22'
    $missingRegisteredOnPath = Join-Path $tempRoot 'missing-registered-on.json'
    Write-JsonFile -Path $missingRegisteredOnPath -Value ([ordered]@{ schema = 3; exceptions = @($missingRegisteredOnRow) })
    Assert-CheckerCase `
        -Name 'expiring debt without registeredOn' `
        -SourceRoot $matchingExpiringSource `
        -BaselinePath $missingRegisteredOnPath `
        -ExpectedExitCode 1 `
        -ExpectedOutput @("classification 'expiring-debt' is missing required field(s): registeredOn")

    $malformedRegisteredOnRow = $validExpiringRow.PSObject.Copy()
    $malformedRegisteredOnRow.registeredByIssue = '#1487'
    $malformedRegisteredOnRow.registeredOn = '2026/08/08'
    $malformedRegisteredOnRow.expiresOn = '2026-09-22'
    $malformedRegisteredOnPath = Join-Path $tempRoot 'malformed-registered-on.json'
    Write-JsonFile -Path $malformedRegisteredOnPath -Value ([ordered]@{ schema = 3; exceptions = @($malformedRegisteredOnRow) })
    Assert-CheckerCase `
        -Name 'expiring debt with malformed registeredOn' `
        -SourceRoot $matchingExpiringSource `
        -BaselinePath $malformedRegisteredOnPath `
        -ExpectedExitCode 1 `
        -ExpectedOutput @('registeredOn must use yyyy-MM-dd')

    $futureRegisteredOnRow = $validExpiringRow.PSObject.Copy()
    $futureRegisteredOnRow.registeredByIssue = '#1487'
    $futureRegisteredOnRow.registeredOn = '2999-01-01'
    $futureRegisteredOnRow.expiresOn = '2999-01-02'
    $futureRegisteredOnPath = Join-Path $tempRoot 'future-registered-on.json'
    Write-JsonFile -Path $futureRegisteredOnPath -Value ([ordered]@{ schema = 3; exceptions = @($futureRegisteredOnRow) })
    Assert-CheckerCase `
        -Name 'expiring debt registered in the future' `
        -SourceRoot $matchingExpiringSource `
        -BaselinePath $futureRegisteredOnPath `
        -ExpectedExitCode 1 `
        -ExpectedOutput @('registeredOn must not be in the future')

    $expiryBeforeRegistrationRow = $validExpiringRow.PSObject.Copy()
    $expiryBeforeRegistrationRow.registeredByIssue = '#1487'
    $expiryBeforeRegistrationRow.registeredOn = '2026-08-09'
    $expiryBeforeRegistrationRow.expiresOn = '2026-08-08'
    $expiryBeforeRegistrationPath = Join-Path $tempRoot 'expiry-before-registration.json'
    Write-JsonFile -Path $expiryBeforeRegistrationPath -Value ([ordered]@{ schema = 3; exceptions = @($expiryBeforeRegistrationRow) })
    Assert-CheckerCase `
        -Name 'expiring debt that expires before registration' `
        -SourceRoot $matchingExpiringSource `
        -BaselinePath $expiryBeforeRegistrationPath `
        -ExpectedExitCode 1 `
        -ExpectedOutput @('expiresOn must be on or after registeredOn')

    $maximumLifetimeRow = $validExpiringRow.PSObject.Copy()
    $maximumLifetimeRow.registeredByIssue = '#1487'
    $maximumLifetimeRow.registeredOn = '2026-08-08'
    $maximumLifetimeRow.expiresOn = '2026-09-22'
    $maximumLifetimePath = Join-Path $tempRoot 'maximum-lifetime.json'
    Write-JsonFile -Path $maximumLifetimePath -Value ([ordered]@{ schema = 3; exceptions = @($maximumLifetimeRow) })
    Assert-CheckerCase `
        -Name 'expiring debt registered for exactly 45 days' `
        -SourceRoot $matchingExpiringSource `
        -BaselinePath $maximumLifetimePath `
        -ExpectedExitCode 0 `
        -ExpectedOutput @('check passed')

    $expiredRow = $validBaseline.exceptions[0].PSObject.Copy()
    $expiredRow.expiresOn = '2026-01-01'
    $expiredPath = Join-Path $tempRoot 'expired.json'
    Write-JsonFile -Path $expiredPath -Value ([ordered]@{ schema = 3; exceptions = @($expiredRow) })
    Assert-CheckerCase -Name 'expired baseline row' -SourceRoot $matchingSource -BaselinePath $expiredPath -ExpectedExitCode 1 -ExpectedOutput @('expired')

    $hashMismatchRow = $validBaseline.exceptions[0].PSObject.Copy()
    $hashMismatchRow.lineTextSha256 = ('0' * 64)
    $hashMismatchPath = Join-Path $tempRoot 'hash-mismatch.json'
    Write-JsonFile -Path $hashMismatchPath -Value ([ordered]@{ schema = 3; exceptions = @($hashMismatchRow) })
    Assert-CheckerCase -Name 'hash mismatch' -SourceRoot $matchingSource -BaselinePath $hashMismatchPath -ExpectedExitCode 1 -ExpectedOutput @('hash no longer matches')

    $duplicatePath = Join-Path $tempRoot 'duplicate.json'
    Write-JsonFile -Path $duplicatePath -Value ([ordered]@{ schema = 3; exceptions = @($validBaseline.exceptions[0], $validBaseline.exceptions[0]) })
    Assert-CheckerCase -Name 'duplicate rows' -SourceRoot $matchingSource -BaselinePath $duplicatePath -ExpectedExitCode 1 -ExpectedOutput @('duplicate baseline row')

    $staleRow = $validBaseline.exceptions[0].PSObject.Copy()
    $staleRow.path = 'scripts/tests/fixtures/backend-test-determinism/clean.cs'
    $stalePath = Join-Path $tempRoot 'stale.json'
    Write-JsonFile -Path $stalePath -Value ([ordered]@{ schema = 3; exceptions = @($staleRow) })
    Assert-CheckerCase -Name 'stale rows' -SourceRoot (Join-Path $fixtureRoot 'clean.cs') -BaselinePath $stalePath -ExpectedExitCode 1 -ExpectedOutput @('does not match a current finding')

    $wrongSchemaPath = Join-Path $tempRoot 'wrong-schema.json'
    Write-JsonFile -Path $wrongSchemaPath -Value ([ordered]@{ schema = 2; exceptions = @() })
    Assert-CheckerCase -Name 'unsupported schema' -SourceRoot (Join-Path $fixtureRoot 'clean.cs') -BaselinePath $wrongSchemaPath -ExpectedExitCode 1 -ExpectedOutput @('schema must equal 3')

    $stringSchemaPath = Join-Path $tempRoot 'string-schema.json'
    Write-JsonFile -Path $stringSchemaPath -Value ([ordered]@{ schema = '3'; exceptions = @() })
    Assert-CheckerCase -Name 'non-numeric schema' -SourceRoot (Join-Path $fixtureRoot 'clean.cs') -BaselinePath $stringSchemaPath -ExpectedExitCode 1 -ExpectedOutput @('schema must equal 3 as a JSON number')

    # --- permanent classification -------------------------------------------------------------
    # A permanent row is not an exemption: it is only legal on a path the checker itself allow-lists,
    # and it must carry a rationale instead of (never alongside) an owner and an expiry date.
    $permanentSource = New-PermanentClassificationSource -Name 'permanent-classification'

    $permanentAllowedPath = Join-Path $tempRoot 'permanent-allowed.json'
    Write-JsonFile -Path $permanentAllowedPath -Value ([ordered]@{ schema = 3; exceptions = @((New-PermanentClassificationRow -Source $permanentSource)) })
    Assert-CheckerCase `
        -Name 'permanent row on an allow-listed path' `
        -SourceRoot $permanentSource.SourcePath `
        -BaselinePath $permanentAllowedPath `
        -PermanentAllowlist @("$($permanentSource.RelativePath)=StaticSetter") `
        -ExpectedExitCode 0 `
        -ExpectedOutput @('check passed', 'permanentRows=1')

    # This is the case that goes red if the allowlist is ever weakened into "permanent means anywhere":
    # the identical row, checked with the checker's real default allowlist, must be rejected.
    Assert-CheckerCase `
        -Name 'permanent row outside the default allowlist' `
        -SourceRoot $permanentSource.SourcePath `
        -BaselinePath $permanentAllowedPath `
        -ExpectedExitCode 1 `
        -ExpectedOutput @('permanent classification is not allowed for path')

    # Membership is exact: an allowlist naming some other file does not admit this row.
    Assert-CheckerCase `
        -Name 'permanent row against an allowlist for another file' `
        -SourceRoot $permanentSource.SourcePath `
        -BaselinePath $permanentAllowedPath `
        -PermanentAllowlist @('backend/tests/Nerv.IIP.Testing.Tests/SomeOtherTests.cs=StaticSetter') `
        -ExpectedExitCode 1 `
        -ExpectedOutput @('permanent classification is not allowed for path')

    # The allowlist locks a path AND a pattern. A file that legitimately holds one permanent finding
    # must not become a free pass for every other pattern the checker knows about: the rationale that
    # justified the culture setters says nothing about a Thread.Sleep added to the same file later.
    $otherPatternSource = New-OtherPatternSource -Name 'permanent-other-pattern'
    $otherPatternRow = [ordered]@{
        path = $otherPatternSource.RelativePath
        pattern = 'Thread.Sleep'
        lineTextSha256 = $otherPatternSource.LineTextSha256
        occurrenceCount = 1
        classification = 'permanent'
        reason = 'Fixture proves an allow-listed path does not admit a permanent row for a different pattern.'
        rationale = 'Deliberately unjustified: the allowlist entry for this path covers StaticSetter only.'
    }
    $otherPatternPath = Join-Path $tempRoot 'permanent-other-pattern.json'
    Write-JsonFile -Path $otherPatternPath -Value ([ordered]@{ schema = 3; exceptions = @($otherPatternRow) })
    Assert-CheckerCase `
        -Name 'permanent row for a pattern the allowlist does not cover' `
        -SourceRoot $otherPatternSource.SourcePath `
        -BaselinePath $otherPatternPath `
        -PermanentAllowlist @("$($otherPatternSource.RelativePath)=StaticSetter") `
        -ExpectedExitCode 1 `
        -ExpectedOutput @('permanent classification is not allowed for pattern')

    # ...and the same row passes once the allowlist actually names that pattern, so the case above is
    # proving the pattern check rather than some unrelated rejection.
    Assert-CheckerCase `
        -Name 'permanent row for a pattern the allowlist does cover' `
        -SourceRoot $otherPatternSource.SourcePath `
        -BaselinePath $otherPatternPath `
        -PermanentAllowlist @("$($otherPatternSource.RelativePath)=Thread.Sleep") `
        -ExpectedExitCode 0 `
        -ExpectedOutput @('check passed', 'permanentRows=1')

    Assert-CheckerCase `
        -Name 'malformed allowlist entry' `
        -SourceRoot $otherPatternSource.SourcePath `
        -BaselinePath $otherPatternPath `
        -PermanentAllowlist @($otherPatternSource.RelativePath) `
        -ExpectedExitCode 1 `
        -ExpectedOutput @("must use '<path>=<pattern>'")

    Assert-CheckerCase `
        -Name 'allowlist entry naming an unsupported pattern' `
        -SourceRoot $otherPatternSource.SourcePath `
        -BaselinePath $otherPatternPath `
        -PermanentAllowlist @("$($otherPatternSource.RelativePath)=Whatever") `
        -ExpectedExitCode 1 `
        -ExpectedOutput @("names unsupported pattern 'Whatever'")

    $permanentWithExpiryRow = New-PermanentClassificationRow -Source $permanentSource
    $permanentWithExpiryRow['ownerIssue'] = 'MAN-662'
    $permanentWithExpiryRow['registeredByIssue'] = '#1487'
    $permanentWithExpiryRow['exitCondition'] = 'Never.'
    $permanentWithExpiryRow['registeredOn'] = '2026-08-08'
    $permanentWithExpiryRow['expiresOn'] = '2999-12-31'
    $permanentWithExpiryPath = Join-Path $tempRoot 'permanent-with-expiry.json'
    Write-JsonFile -Path $permanentWithExpiryPath -Value ([ordered]@{ schema = 3; exceptions = @($permanentWithExpiryRow) })
    Assert-CheckerCase `
        -Name 'permanent row carrying debt metadata' `
        -SourceRoot $permanentSource.SourcePath `
        -BaselinePath $permanentWithExpiryPath `
        -PermanentAllowlist @("$($permanentSource.RelativePath)=StaticSetter") `
        -ExpectedExitCode 1 `
        -ExpectedOutput @("classification 'permanent' must not carry field(s)", 'registeredByIssue', 'registeredOn', 'expiresOn')

    $permanentWithoutRationaleRow = New-PermanentClassificationRow -Source $permanentSource
    $permanentWithoutRationaleRow.Remove('rationale')
    $permanentWithoutRationalePath = Join-Path $tempRoot 'permanent-without-rationale.json'
    Write-JsonFile -Path $permanentWithoutRationalePath -Value ([ordered]@{ schema = 3; exceptions = @($permanentWithoutRationaleRow) })
    Assert-CheckerCase `
        -Name 'permanent row without a rationale' `
        -SourceRoot $permanentSource.SourcePath `
        -BaselinePath $permanentWithoutRationalePath `
        -PermanentAllowlist @("$($permanentSource.RelativePath)=StaticSetter") `
        -ExpectedExitCode 1 `
        -ExpectedOutput @("classification 'permanent' is missing required field(s): rationale")

    $unknownClassificationRow = New-PermanentClassificationRow -Source $permanentSource
    $unknownClassificationRow['classification'] = 'grandfathered'
    $unknownClassificationPath = Join-Path $tempRoot 'unknown-classification.json'
    Write-JsonFile -Path $unknownClassificationPath -Value ([ordered]@{ schema = 3; exceptions = @($unknownClassificationRow) })
    Assert-CheckerCase `
        -Name 'unknown classification' `
        -SourceRoot $permanentSource.SourcePath `
        -BaselinePath $unknownClassificationPath `
        -PermanentAllowlist @("$($permanentSource.RelativePath)=StaticSetter") `
        -ExpectedExitCode 1 `
        -ExpectedOutput @('classification must be one of')

    $missingClassificationRow = $validBaseline.exceptions[0].PSObject.Copy()
    $missingClassificationRow.PSObject.Properties.Remove('classification')
    $missingClassificationPath = Join-Path $tempRoot 'missing-classification.json'
    Write-JsonFile -Path $missingClassificationPath -Value ([ordered]@{ schema = 3; exceptions = @($missingClassificationRow) })
    Assert-CheckerCase `
        -Name 'missing classification' `
        -SourceRoot $matchingSource `
        -BaselinePath $missingClassificationPath `
        -ExpectedExitCode 1 `
        -ExpectedOutput @('classification must be one of')

    $debtWithRationaleRow = $validBaseline.exceptions[0].PSObject.Copy()
    $debtWithRationaleRow | Add-Member -NotePropertyName 'rationale' -NotePropertyValue 'Debt rows may not claim permanence.'
    $debtWithRationalePath = Join-Path $tempRoot 'debt-with-rationale.json'
    Write-JsonFile -Path $debtWithRationalePath -Value ([ordered]@{ schema = 3; exceptions = @($debtWithRationaleRow) })
    Assert-CheckerCase `
        -Name 'expiring debt row carrying a rationale' `
        -SourceRoot $matchingSource `
        -BaselinePath $debtWithRationalePath `
        -ExpectedExitCode 1 `
        -ExpectedOutput @("classification 'expiring-debt' must not carry field(s): rationale")

    Write-Host 'Backend test determinism checker fixture tests passed.'
}
finally {
    $resolvedTempRoot = Resolve-Path -LiteralPath $tempRoot -ErrorAction SilentlyContinue
    if ($resolvedTempRoot) {
        $operatingSystemTemp = [System.IO.Path]::GetTempPath()
        if (-not $resolvedTempRoot.Path.StartsWith($operatingSystemTemp, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove test directory outside temp: $($resolvedTempRoot.Path)"
        }

        Remove-Item -LiteralPath $resolvedTempRoot.Path -Recurse -Force
    }

    foreach ($generatedPath in @($scriptLogRoot, $generatedFixtureRoot)) {
        $resolvedGeneratedPath = Resolve-Path -LiteralPath $generatedPath -ErrorAction SilentlyContinue
        if ($resolvedGeneratedPath) {
            $allowedArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
            if (-not $resolvedGeneratedPath.Path.StartsWith($allowedArtifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to remove generated test path outside artifacts: $($resolvedGeneratedPath.Path)"
            }

            Remove-Item -LiteralPath $resolvedGeneratedPath.Path -Recurse -Force
        }
    }
}
