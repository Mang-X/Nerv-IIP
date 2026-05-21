# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs root development entrypoint smoke tests
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$nerv = Join-Path $repoRoot 'nerv.ps1'

function Invoke-Nerv {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    $output = & pwsh -NoProfile -ExecutionPolicy Bypass -File $nerv @Arguments 2>&1
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = ($output | Out-String)
    }
}

$help = Invoke-Nerv -Arguments @('help')
if ($help.ExitCode -ne 0) {
    throw "Expected help to exit 0, got $($help.ExitCode). Output: $($help.Output)"
}

foreach ($expected in @('.\nerv.ps1 dev', '.\nerv.ps1 ports', '.\nerv.ps1 help')) {
    if (-not $help.Output.Contains($expected)) {
        throw "Help output did not contain '$expected'. Output: $($help.Output)"
    }
}

$ports = Invoke-Nerv -Arguments @('ports')
if ($ports.ExitCode -ne 0) {
    throw "Expected ports to exit 0, got $($ports.ExitCode). Output: $($ports.Output)"
}

foreach ($expected in @(
    '5100 PlatformGateway',
    '5101 AppHub',
    '5102 IAM',
    '5103 Ops',
    '5104 FileStorage',
    '5105 Console',
    '15432 PostgreSQL',
    '9000 MinIO API',
    '9001 MinIO Console'
)) {
    if (-not $ports.Output.Contains($expected)) {
        throw "Ports output did not contain '$expected'. Output: $($ports.Output)"
    }
}

$unknown = Invoke-Nerv -Arguments @('unknown-command')
if ($unknown.ExitCode -eq 0) {
    throw "Expected unknown command to fail. Output: $($unknown.Output)"
}

if (-not $unknown.Output.Contains("Unknown command 'unknown-command'")) {
    throw "Unknown command output was not helpful. Output: $($unknown.Output)"
}

Write-Host 'Development entrypoint smoke tests passed.'
