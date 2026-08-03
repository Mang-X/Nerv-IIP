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
        [string] $BaselinePath
    )

    $exitCode = 0
    $logDirectory = $null
    try {
        $result = Invoke-PwshScript `
            -ScriptPath $checker `
            -Arguments @('-SourceRoot', $SourceRoot, '-BaselinePath', $BaselinePath) `
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
        schema = 1
        exceptions = @([ordered]@{
            path = $relativePath
            pattern = 'Task.Delay'
            lineTextSha256 = '1106b2c99718c440becaeed61063d2b2dd38c61c1236e256560b49fdbcf5b2bf'
            occurrenceCount = $ExpectedCount
            ownerIssue = 'MAN-662'
            reason = 'Fixture intentionally repeats one identical source line to verify occurrence accounting.'
            exitCondition = 'Delete when occurrence accounting no longer uses this fixture.'
            expiresOn = '2026-09-03'
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

        [hashtable] $MinimumOccurrences = @{}
    )

    $result = Invoke-CheckerCase -SourceRoot $SourceRoot -BaselinePath $BaselinePath
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
    Write-JsonFile -Path $emptyBaselinePath -Value ([ordered]@{ schema = 1; exceptions = @() })

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

    $missingFieldRow = $validBaseline.exceptions[0].PSObject.Copy()
    $missingFieldRow.PSObject.Properties.Remove('reason')
    $missingFieldPath = Join-Path $tempRoot 'missing-field.json'
    Write-JsonFile -Path $missingFieldPath -Value ([ordered]@{ schema = 1; exceptions = @($missingFieldRow) })
    Assert-CheckerCase -Name 'missing baseline metadata' -SourceRoot $matchingSource -BaselinePath $missingFieldPath -ExpectedExitCode 1 -ExpectedOutput @('missing required field')

    $nonIntegerOccurrenceRow = $validBaseline.exceptions[0].PSObject.Copy()
    $nonIntegerOccurrenceRow.occurrenceCount = '1'
    $nonIntegerOccurrencePath = Join-Path $tempRoot 'non-integer-occurrence.json'
    Write-JsonFile -Path $nonIntegerOccurrencePath -Value ([ordered]@{ schema = 1; exceptions = @($nonIntegerOccurrenceRow) })
    Assert-CheckerCase -Name 'non-integer occurrence count' -SourceRoot $matchingSource -BaselinePath $nonIntegerOccurrencePath -ExpectedExitCode 1 -ExpectedOutput @('occurrenceCount must be a positive integer')

    $zeroOccurrenceRow = $validBaseline.exceptions[0].PSObject.Copy()
    $zeroOccurrenceRow.occurrenceCount = 0
    $zeroOccurrencePath = Join-Path $tempRoot 'zero-occurrence.json'
    Write-JsonFile -Path $zeroOccurrencePath -Value ([ordered]@{ schema = 1; exceptions = @($zeroOccurrenceRow) })
    Assert-CheckerCase -Name 'zero occurrence count' -SourceRoot $matchingSource -BaselinePath $zeroOccurrencePath -ExpectedExitCode 1 -ExpectedOutput @('occurrenceCount must be a positive integer')

    $numericReasonRow = $validBaseline.exceptions[0].PSObject.Copy()
    $numericReasonRow.reason = 123
    $numericReasonPath = Join-Path $tempRoot 'numeric-reason.json'
    Write-JsonFile -Path $numericReasonPath -Value ([ordered]@{ schema = 1; exceptions = @($numericReasonRow) })
    Assert-CheckerCase -Name 'numeric string metadata' -SourceRoot $matchingSource -BaselinePath $numericReasonPath -ExpectedExitCode 1 -ExpectedOutput @('reason must be a non-empty string')

    $objectExitConditionRow = $validBaseline.exceptions[0].PSObject.Copy()
    $objectExitConditionRow.exitCondition = [ordered]@{ text = 'not a string' }
    $objectExitConditionPath = Join-Path $tempRoot 'object-exit-condition.json'
    Write-JsonFile -Path $objectExitConditionPath -Value ([ordered]@{ schema = 1; exceptions = @($objectExitConditionRow) })
    Assert-CheckerCase -Name 'object string metadata' -SourceRoot $matchingSource -BaselinePath $objectExitConditionPath -ExpectedExitCode 1 -ExpectedOutput @('exitCondition must be a non-empty string')

    $expiredRow = $validBaseline.exceptions[0].PSObject.Copy()
    $expiredRow.expiresOn = '2026-01-01'
    $expiredPath = Join-Path $tempRoot 'expired.json'
    Write-JsonFile -Path $expiredPath -Value ([ordered]@{ schema = 1; exceptions = @($expiredRow) })
    Assert-CheckerCase -Name 'expired baseline row' -SourceRoot $matchingSource -BaselinePath $expiredPath -ExpectedExitCode 1 -ExpectedOutput @('expired')

    $hashMismatchRow = $validBaseline.exceptions[0].PSObject.Copy()
    $hashMismatchRow.lineTextSha256 = ('0' * 64)
    $hashMismatchPath = Join-Path $tempRoot 'hash-mismatch.json'
    Write-JsonFile -Path $hashMismatchPath -Value ([ordered]@{ schema = 1; exceptions = @($hashMismatchRow) })
    Assert-CheckerCase -Name 'hash mismatch' -SourceRoot $matchingSource -BaselinePath $hashMismatchPath -ExpectedExitCode 1 -ExpectedOutput @('hash no longer matches')

    $duplicatePath = Join-Path $tempRoot 'duplicate.json'
    Write-JsonFile -Path $duplicatePath -Value ([ordered]@{ schema = 1; exceptions = @($validBaseline.exceptions[0], $validBaseline.exceptions[0]) })
    Assert-CheckerCase -Name 'duplicate rows' -SourceRoot $matchingSource -BaselinePath $duplicatePath -ExpectedExitCode 1 -ExpectedOutput @('duplicate baseline row')

    $staleRow = $validBaseline.exceptions[0].PSObject.Copy()
    $staleRow.path = 'scripts/tests/fixtures/backend-test-determinism/clean.cs'
    $stalePath = Join-Path $tempRoot 'stale.json'
    Write-JsonFile -Path $stalePath -Value ([ordered]@{ schema = 1; exceptions = @($staleRow) })
    Assert-CheckerCase -Name 'stale rows' -SourceRoot (Join-Path $fixtureRoot 'clean.cs') -BaselinePath $stalePath -ExpectedExitCode 1 -ExpectedOutput @('does not match a current finding')

    $wrongSchemaPath = Join-Path $tempRoot 'wrong-schema.json'
    Write-JsonFile -Path $wrongSchemaPath -Value ([ordered]@{ schema = 2; exceptions = @() })
    Assert-CheckerCase -Name 'unsupported schema' -SourceRoot (Join-Path $fixtureRoot 'clean.cs') -BaselinePath $wrongSchemaPath -ExpectedExitCode 1 -ExpectedOutput @('schema must equal 1')

    $stringSchemaPath = Join-Path $tempRoot 'string-schema.json'
    Write-JsonFile -Path $stringSchemaPath -Value ([ordered]@{ schema = '1'; exceptions = @() })
    Assert-CheckerCase -Name 'non-numeric schema' -SourceRoot (Join-Path $fixtureRoot 'clean.cs') -BaselinePath $stringSchemaPath -ExpectedExitCode 1 -ExpectedOutput @('schema must equal 1 as a JSON number')

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
