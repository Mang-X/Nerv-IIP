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

Invoke-GovernanceCase -Name 'allowed-check.ps1' -ExpectedExitCode 0
Invoke-GovernanceCase -Name 'missing-helper.ps1' -ExpectedExitCode 1
Invoke-GovernanceCase -Name 'direct-dotnet.ps1' -ExpectedExitCode 1
Invoke-GovernanceCase -Name 'direct-start-job.ps1' -ExpectedExitCode 1
Invoke-GovernanceCase -Name 'dynamic-invocation.ps1' -ExpectedExitCode 1

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

Write-Host 'Script governance fixture tests passed.'
