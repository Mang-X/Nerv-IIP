# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs backend test-determinism checker fixture cases
#   Writes:
#     - Temporary baseline JSON files under the operating-system temp directory
#   Cleanup:
#     - Removes every temporary baseline file created by this test
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$checker = Join-Path $repoRoot 'scripts/check-backend-test-determinism.ps1'
$fixtureRoot = Join-Path $repoRoot 'scripts/tests/fixtures/backend-test-determinism'
$validBaselinePath = Join-Path $fixtureRoot 'valid-baseline.json'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-backend-test-determinism-$([Guid]::NewGuid().ToString('N'))"

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

    $output = & pwsh -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $checker `
        -SourceRoot $SourceRoot `
        -BaselinePath $BaselinePath 2>&1

    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = ($output | Out-String)
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

    $validBaseline = Get-Content -LiteralPath $validBaselinePath -Raw | ConvertFrom-Json
    $matchingSource = Join-Path $repoRoot $validBaseline.exceptions[0].path

    $missingFieldRow = $validBaseline.exceptions[0].PSObject.Copy()
    $missingFieldRow.PSObject.Properties.Remove('reason')
    $missingFieldPath = Join-Path $tempRoot 'missing-field.json'
    Write-JsonFile -Path $missingFieldPath -Value ([ordered]@{ schema = 1; exceptions = @($missingFieldRow) })
    Assert-CheckerCase -Name 'missing baseline metadata' -SourceRoot $matchingSource -BaselinePath $missingFieldPath -ExpectedExitCode 1 -ExpectedOutput @('missing required field')

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
}
