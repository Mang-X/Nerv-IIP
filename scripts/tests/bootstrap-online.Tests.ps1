# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs the bootstrap .NET SDK prerequisite predicate in a disposable harness
#   Writes:
#     - Temporary test harness files outside the repository
#   Cleanup:
#     - Deletes the disposable test harness directory
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$bootstrapScript = Join-Path $repoRoot 'scripts/bootstrap-online.ps1'
$bootstrapText = Get-Content -LiteralPath $bootstrapScript -Raw
$mainStart = $bootstrapText.IndexOf("if (`$Help) {", [System.StringComparison]::Ordinal)
if ($mainStart -lt 0) {
    throw 'Bootstrap script must retain an explicit help guard after its function definitions.'
}

$harnessRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-bootstrap-online-$([guid]::NewGuid().ToString('N'))"
try {
    $harnessScripts = Join-Path $harnessRoot 'scripts'
    [System.IO.Directory]::CreateDirectory((Join-Path $harnessScripts 'lib')) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1') -Destination (Join-Path $harnessScripts 'lib/ScriptAutomation.ps1')

    $harness = @"
$($bootstrapText.Substring(0, $mainStart))
function Test-CommandAvailable {
    param([string] `$Name)
    return `$Name -ceq 'dotnet'
}

function Invoke-DotNetOutput {
    param([string[]] `$Arguments, [string] `$WorkingDirectory, [int] `$TimeoutSeconds, [string] `$Name)
    return [pscustomobject]@{ Stdout = '10.0.302 [/fake/sdk]' }
}

if (-not (Test-DotNet10Sdk)) {
    throw 'A single installed .NET 10 SDK must satisfy the bootstrap prerequisite.'
}
"@
    $harnessPath = Join-Path $harnessScripts 'bootstrap-online.ps1'
    [System.IO.File]::WriteAllText($harnessPath, $harness, [System.Text.UTF8Encoding]::new($false))

    $output = & pwsh -NoProfile -File $harnessPath 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Bootstrap single-SDK regression test failed: $($output | Out-String)"
    }
}
finally {
    if (Test-Path -LiteralPath $harnessRoot) {
        Remove-Item -LiteralPath $harnessRoot -Recurse -Force
    }
}

Write-Host 'Bootstrap online script tests passed.'
