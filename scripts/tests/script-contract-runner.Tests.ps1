# Script-Governance:
#   Category: check
#   SideEffects:
#     - Executes bounded PowerShell child fixtures
#   Writes:
#     - Owned temporary fixture root and ScriptAutomation logs
#   Cleanup:
#     - Removes owned fixture root in finally; helper stops timed-out children
#   Requires:
#     - PowerShell 7
#     - Ruby with the yaml and json standard libraries

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptTestSelection.ps1')

function Assert-Runner([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

$root = Join-Path ([IO.Path]::GetTempPath()) "nerv-script-suite-$([guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory((Join-Path $root 'scripts/tests')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $root '.github/workflows')) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts/lib') -Destination (Join-Path $root 'scripts/lib') -Recurse
    Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts/run-script-contract-tests.ps1') -Destination (Join-Path $root 'scripts/run-script-contract-tests.ps1')
    $runner = Join-Path $root 'scripts/run-script-contract-tests.ps1'
    $workflow = "name: Fixture`non: [push]`njobs:`n  suite:`n    runs-on: ubuntu-latest`n    steps:`n"
    # 用现有闭集生成合法边界夹具，不在生产端另建成员表。
    foreach ($entry in (Get-NervScriptTestOutOfBandRegistry)) {
        $source = '# fixture'
        if ([string]::Equals($entry.Kind, 'nested', [StringComparison]::Ordinal)) {
            $parentPath = Join-Path $root "scripts/tests/$($entry.Parent)"
            [IO.File]::WriteAllText($parentPath, "& (Join-Path `$PSScriptRoot '$($entry.Name)')")
            $workflow += "      - run: ./scripts/tests/$($entry.Parent)`n"
        }
        elseif ([string]::Equals($entry.Kind, 'excluded', [StringComparison]::Ordinal)) {
            $source = "# Script-Governance:`n#   Requires:`n#     - $($entry.Requirement)"
        }
        [IO.File]::WriteAllText((Join-Path $root "scripts/tests/$($entry.Name)"), $source)
    }
    [IO.File]::WriteAllText((Join-Path $root '.github/workflows/ci.yml'), $workflow)
    $first = Join-Path $root 'scripts/tests/a.Tests.ps1'
    $last = Join-Path $root 'scripts/tests/z-new.Tests.ps1'
    [IO.File]::WriteAllText($first, "Add-Content -LiteralPath (Join-Path `$PSScriptRoot 'executed.txt') -Value 'a'")
    [IO.File]::WriteAllText($last, "Add-Content -LiteralPath (Join-Path `$PSScriptRoot 'executed.txt') -Value 'z-new'")
    $marker = Join-Path $root 'scripts/tests/executed.txt'
    $control = @(& pwsh -NoProfile -NonInteractive -File $runner 2>&1) -join "`n"
    Assert-Runner ($LASTEXITCODE -eq 0) "Control suite must pass: $control"
    Assert-Runner ([string]::Equals((([IO.File]::ReadAllText($marker).Trim() -split '\r?\n') -join ','), 'a,z-new', [StringComparison]::Ordinal)) 'Discovery must execute exactly the two unregistered tests once, including the new test; named and out-of-band tests must not run again.'
    Assert-Runner ($control.Contains('PASS', [StringComparison]::Ordinal) -and $control.Contains('z-new.Tests.ps1', [StringComparison]::Ordinal)) 'Every executed file must be identified.'
    Write-Host 'PASS control / exact discovery set / new unregistered contract'

    [IO.File]::WriteAllText($first, "Write-Error 'fixture-original-error'; exit 7")
    Remove-Item -LiteralPath $marker
    $failed = @(& pwsh -NoProfile -NonInteractive -File $runner 2>&1) -join "`n"
    Assert-Runner ($LASTEXITCODE -ne 0) 'An earlier child failure must survive a later successful child.'
    Assert-Runner ([string]::Equals([IO.File]::ReadAllText($marker).Trim(), 'z-new', [StringComparison]::Ordinal)) 'The later successful child must actually execute.'
    Assert-Runner ($failed.Contains('FAIL', [StringComparison]::Ordinal) -and $failed.Contains('fixture-original-error', [StringComparison]::Ordinal) -and $failed.Contains('z-new.Tests.ps1', [StringComparison]::Ordinal)) 'Failure must name its file and preserve the original error beside the later success.'
    Write-Host 'PASS earlier nonzero / later success / original diagnostic'

    [IO.File]::WriteAllText($first, 'Start-Sleep -Seconds 60')
    $timed = @(& pwsh -NoProfile -NonInteractive -File $runner -TimeoutSeconds 1 2>&1) -join "`n"
    Assert-Runner ($LASTEXITCODE -ne 0) 'A timed-out child must fail the suite.'
    Assert-Runner ($timed.Contains('a.Tests.ps1', [StringComparison]::Ordinal) -and $timed.Contains('tim', [StringComparison]::OrdinalIgnoreCase)) 'Timeout must retain file-level diagnostics.'
    Write-Host 'PASS timeout / bounded diagnostic'

    [IO.File]::WriteAllText($first, 'exit 0')
    $explicit = @(& pwsh -NoProfile -NonInteractive -File $runner -ScriptPath 'scripts/tests/a.Tests.ps1' 2>&1) -join "`n"
    Assert-Runner ($LASTEXITCODE -eq 0 -and $explicit.Contains('explicit=1', [StringComparison]::Ordinal) -and -not $explicit.Contains('z-new.Tests.ps1', [StringComparison]::Ordinal)) 'An explicit suite must execute only its caller-owned commands.'
    [IO.File]::WriteAllText($first, 'exit 7')
    $launcher = Join-Path $root 'explicit-suite.ps1'
    [IO.File]::WriteAllText($launcher, '& (Join-Path $PSScriptRoot "scripts/run-script-contract-tests.ps1") -ScriptPath @("scripts/tests/a.Tests.ps1", "scripts/tests/z-new.Tests.ps1"); exit $LASTEXITCODE')
    $explicitFailed = @(& pwsh -NoProfile -NonInteractive -File $launcher 2>&1) -join "`n"
    Assert-Runner ($LASTEXITCODE -ne 0 -and $explicitFailed.Contains('PASS', [StringComparison]::Ordinal) -and $explicitFailed.Contains('FAIL', [StringComparison]::Ordinal)) 'Explicit suite must also preserve earlier failure after a later success.'
    Write-Host 'PASS explicit suite / independent failure propagation'
}
finally { if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force } }
