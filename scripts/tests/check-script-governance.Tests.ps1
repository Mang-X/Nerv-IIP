# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs script-governance fixture checks
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$checker = Join-Path $repoRoot 'scripts/check-script-governance.ps1'
$fixtures = Join-Path $repoRoot 'scripts/tests/fixtures/script-governance'
$helper = Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1'

. $helper

function Invoke-GovernanceCase {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [int] $ExpectedExitCode
    )

    $target = Join-Path $fixtures $Name
    $output = & pwsh -NoProfile -ExecutionPolicy Bypass -File $checker -Path $target 2>&1
    $actualExitCode = $LASTEXITCODE

    if ($actualExitCode -ne $ExpectedExitCode) {
        $output | ForEach-Object { Write-Host $_ }
        throw "Expected $Name to exit $ExpectedExitCode, got $actualExitCode."
    }
}

function Invoke-GovernanceScriptCase {
    param(
        [Parameter(Mandatory)]
        [string] $RelativePath
    )

    $emptyBaseline = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-empty-script-governance-baseline-$([System.Guid]::NewGuid().ToString('N')).json"
    [System.IO.File]::WriteAllText($emptyBaseline, '{"schema":1,"exemptions":[]}', [System.Text.UTF8Encoding]::new($false))

    try {
        $target = Join-Path $repoRoot $RelativePath
        $output = & pwsh -NoProfile -ExecutionPolicy Bypass -File $checker -Path $target -BaselinePath $emptyBaseline 2>&1
        $actualExitCode = $LASTEXITCODE

        if ($actualExitCode -ne 0) {
            $output | ForEach-Object { Write-Host $_ }
            throw "Expected $RelativePath to pass without baseline exemptions, got $actualExitCode."
        }
    }
    finally {
        Remove-Item -LiteralPath $emptyBaseline -Force -ErrorAction SilentlyContinue
    }
}

Invoke-GovernanceCase -Name 'allowed-check.ps1' -ExpectedExitCode 0
Invoke-GovernanceCase -Name 'missing-helper.ps1' -ExpectedExitCode 1
Invoke-GovernanceCase -Name 'direct-dotnet.ps1' -ExpectedExitCode 1
Invoke-GovernanceCase -Name 'direct-start-job.ps1' -ExpectedExitCode 1
Invoke-GovernanceCase -Name 'dynamic-invocation.ps1' -ExpectedExitCode 1

Invoke-GovernanceScriptCase -RelativePath 'scripts/verify-fifth-slice-persistence-foundation.ps1'
Invoke-GovernanceScriptCase -RelativePath 'scripts/verify-fourth-slice-real-infra.ps1'

$smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-script-governance-$([System.Guid]::NewGuid().ToString('N'))"
try {
    Invoke-NativeCommandWithTimeout -Command 'pwsh' -Arguments @('-NoProfile', '-Command', 'Write-Output helper-smoke') -TimeoutSeconds 15 -Name 'helper-smoke' -LogDirectory (Join-Path $smokeRoot 'helper-smoke') | Out-Null
}
finally {
    $resolvedSmokeRoot = Resolve-Path $smokeRoot -ErrorAction SilentlyContinue
    if ($resolvedSmokeRoot) {
        $tempRoot = [System.IO.Path]::GetTempPath()
        if (-not $resolvedSmokeRoot.Path.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove helper smoke directory outside temp: $($resolvedSmokeRoot.Path)"
        }

        Remove-Item -LiteralPath $resolvedSmokeRoot.Path -Recurse -Force
    }
}

$interactiveResult = Invoke-NativeCommandInteractive -Command 'pwsh' -Arguments @('-NoProfile', '-Command', 'exit 7') -Name 'interactive-exit-code-smoke'
if ($interactiveResult.ExitCode -ne 7) {
    throw "Expected interactive helper to return ExitCode 7, got $($interactiveResult.ExitCode)."
}

$backgroundRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-background-arguments-$([System.Guid]::NewGuid().ToString('N'))"
$backgroundScript = Join-Path $backgroundRoot 'print-arguments.ps1'
$background = $null
try {
    New-Item -ItemType Directory -Force -Path $backgroundRoot | Out-Null
    [System.IO.File]::WriteAllText($backgroundScript, 'Write-Output "count=$($args.Count)"; Write-Output "arg0=$($args[0])"', [System.Text.UTF8Encoding]::new($false))
    $background = Start-ManagedBackgroundProcess -Command 'pwsh' -Arguments @('-NoProfile', '-File', $backgroundScript, 'a b') -Name 'background-argument-smoke' -LogDirectory (Join-Path $backgroundRoot 'background-argument-smoke')

    if (-not $background.Process.WaitForExit(15000)) {
        throw 'Background argument smoke process did not exit in time.'
    }

    & $background.Stop 'Background argument smoke cleanup'
    $stdout = Get-Content -Path $background.StdoutPath -Raw
    $background = $null

    foreach ($expected in @('count=1', 'arg0=a b')) {
        if (-not $stdout.Contains($expected)) {
            throw "Expected background stdout to contain '$expected'. Output: $stdout"
        }
    }
}
finally {
    if ($background) {
        & $background.Stop 'Background argument smoke final cleanup'
    }

    $resolvedBackgroundRoot = Resolve-Path $backgroundRoot -ErrorAction SilentlyContinue
    if ($resolvedBackgroundRoot) {
        $tempRoot = [System.IO.Path]::GetTempPath()
        if (-not $resolvedBackgroundRoot.Path.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove background smoke directory outside temp: $($resolvedBackgroundRoot.Path)"
        }

        Remove-Item -LiteralPath $resolvedBackgroundRoot.Path -Recurse -Force
    }
}

Write-Host 'Script governance fixture tests passed.'
