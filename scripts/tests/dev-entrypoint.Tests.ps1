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

$appHostText = Get-Content -LiteralPath (Join-Path $repoRoot 'infra/aspire/Nerv.IIP.AppHost/Program.cs') -Raw
if ($appHostText.Contains('env: fullStackEphemeral ? "PORT" : null') -or -not $appHostText.Contains('env: fullStackEphemeral ? "NERV_IIP_VITE_PORT" : null')) {
    throw 'Ephemeral Vite endpoints must use the dedicated NERV_IIP_VITE_PORT environment variable.'
}
foreach ($relativePath in @(
    'frontend/apps/console/vite.config.ts',
    'frontend/apps/business-console/vite.config.ts',
    'frontend/apps/screen/vite.config.ts'
)) {
    $viteText = Get-Content -LiteralPath (Join-Path $repoRoot $relativePath) -Raw
    if ($viteText.Contains('process.env.PORT') -or -not $viteText.Contains('process.env.NERV_IIP_VITE_PORT')) {
        throw "$relativePath must not let a generic PORT variable change persistent development ports."
    }
}
foreach ($relativePath in @(
    'frontend/apps/console/playwright.config.ts',
    'frontend/apps/business-console/playwright.config.ts'
)) {
    $playwrightText = Get-Content -LiteralPath (Join-Path $repoRoot $relativePath) -Raw
    if ($playwrightText.Contains('process.env.PLAYWRIGHT_BASE_URL') -or -not $playwrightText.Contains('process.env.NERV_IIP_PLAYWRIGHT_BASE_URL')) {
        throw "$relativePath must use the dedicated NERV_IIP_PLAYWRIGHT_BASE_URL override."
    }
}

function Invoke-Nerv {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments,
        [string] $ScriptPath = $nerv
    )

    $output = & pwsh -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @Arguments 2>&1
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = ($output | Out-String)
    }
}

$help = Invoke-Nerv -Arguments @('help')
if ($help.ExitCode -ne 0) {
    throw "Expected help to exit 0, got $($help.ExitCode). Output: $($help.Output)"
}

foreach ($expected in @(
    '.\nerv.ps1 bootstrap',
    '.\nerv.ps1 dev',
    '.\nerv.ps1 stop',
    '.\nerv.ps1 status',
    '.\nerv.ps1 wait',
    '.\nerv.ps1 logs',
    '.\nerv.ps1 describe',
    '.\nerv.ps1 fullstack run -Scenario smoke',
    '.\nerv.ps1 demo start',
    '.\nerv.ps1 demo reset',
    '.\nerv.ps1 demo seed',
    '.\nerv.ps1 demo health-check',
    '.\nerv.ps1 demo stop',
    '.\nerv.ps1 publish-compose',
    '.\nerv.ps1 ports',
    '.\nerv.ps1 help'
)) {
    if (-not $help.Output.Contains($expected)) {
        throw "Help output did not contain '$expected'. Output: $($help.Output)"
    }
}

$fullStackHelp = Invoke-Nerv -Arguments @('fullstack', 'help')
if ($fullStackHelp.ExitCode -ne 0) {
    throw "Expected fullstack help to exit 0, got $($fullStackHelp.ExitCode). Output: $($fullStackHelp.Output)"
}
foreach ($expected in @('run', 'start', 'url', 'status', 'logs', 'stop', 'list', 'gc')) {
    if (-not $fullStackHelp.Output.Contains($expected)) {
        throw "Full-stack help did not contain '$expected'. Output: $($fullStackHelp.Output)"
    }
}

$dispatchRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-entrypoint-dispatch-$([guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory((Join-Path $dispatchRoot 'scripts')) | Out-Null
    $dispatchNerv = Join-Path $dispatchRoot 'nerv.ps1'
    Copy-Item -LiteralPath $nerv -Destination $dispatchNerv
    $captureScript = @'
param(
    [Parameter(Position=0)][string]$Action,
    [Parameter(Position=1)][string]$Target,
    [string]$Scenario,
    [string]$SessionId,
    [switch]$NoBuild,
    [int]$Tail,
    [switch]$Follow
)
[pscustomobject]@{ action=$Action; target=$Target; scenario=$Scenario; sessionId=$SessionId; noBuild=[bool]$NoBuild; tail=$Tail; follow=[bool]$Follow } | ConvertTo-Json -Compress
'@
    [IO.File]::WriteAllText((Join-Path $dispatchRoot 'scripts/fullstack-session.ps1'), $captureScript, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        (Join-Path $dispatchRoot 'scripts/leader-demo.ps1'),
        '[CmdletBinding()] param([Parameter(Position=0)][string]$Action) [pscustomobject]@{ action=$Action } | ConvertTo-Json -Compress',
        [Text.UTF8Encoding]::new($false))

    $fullStackRun = Invoke-Nerv -ScriptPath $dispatchNerv -Arguments @('fullstack', 'run', '-Scenario', 'smoke', '-NoBuild')
    $runCapture = $fullStackRun.Output | ConvertFrom-Json
    if ($fullStackRun.ExitCode -ne 0 -or $runCapture.action -ne 'run' -or $runCapture.scenario -ne 'smoke' -or -not $runCapture.noBuild) {
        throw "Named full-stack run options were not forwarded. Output: $($fullStackRun.Output)"
    }

    $forwardedSessionId = 'nerv-abcd-123456'
    $fullStackStop = Invoke-Nerv -ScriptPath $dispatchNerv -Arguments @('fullstack', 'stop', '-SessionId', $forwardedSessionId)
    $stopCapture = $fullStackStop.Output | ConvertFrom-Json
    if ($fullStackStop.ExitCode -ne 0 -or $stopCapture.action -ne 'stop' -or $stopCapture.sessionId -ne $forwardedSessionId) {
        throw "Named full-stack session ID was not forwarded. Output: $($fullStackStop.Output)"
    }

    $fullStackUrl = Invoke-Nerv -ScriptPath $dispatchNerv -Arguments @('fullstack', 'url', 'business-console')
    $urlCapture = $fullStackUrl.Output | ConvertFrom-Json
    if ($fullStackUrl.ExitCode -ne 0 -or $urlCapture.action -ne 'url' -or $urlCapture.target -ne 'business-console') {
        throw "Positional full-stack URL target was not forwarded. Output: $($fullStackUrl.Output)"
    }

    foreach ($demoAction in @('start', 'reset', 'seed', 'health-check', 'stop')) {
        $demoResult = Invoke-Nerv -ScriptPath $dispatchNerv -Arguments @('demo', $demoAction)
        $demoCapture = $demoResult.Output | ConvertFrom-Json
        if ($demoResult.ExitCode -ne 0 -or $demoCapture.action -ne $demoAction) {
            throw "Demo action '$demoAction' was not forwarded. Output: $($demoResult.Output)"
        }
    }
}
finally {
    Remove-Item -LiteralPath $dispatchRoot -Recurse -Force -ErrorAction SilentlyContinue
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
    '5120 BusinessScheduling',
    '5125 BusinessConsole',
    '5126 BusinessPDA',
    '5180 DesignSystem',
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

$devHelp = Invoke-Nerv -Arguments @('dev', '-Help')
if ($devHelp.ExitCode -ne 0) {
    throw "Expected dev -Help to exit 0, got $($devHelp.ExitCode). Output: $($devHelp.Output)"
}

foreach ($expected in @('-NoBuild', '-InfraOnly', '-OpenDashboard', 'Aspire AppHost')) {
    if (-not $devHelp.Output.Contains($expected)) {
        throw "dev -Help output did not contain '$expected'. Output: $($devHelp.Output)"
    }
}

$bootstrapHelp = Invoke-Nerv -Arguments @('bootstrap', '-Help')
if ($bootstrapHelp.ExitCode -ne 0) {
    throw "Expected bootstrap -Help to exit 0, got $($bootstrapHelp.ExitCode). Output: $($bootstrapHelp.Output)"
}

foreach ($expected in @('-InstallMissing', '-SkipRestore', '-SkipLocalSecrets', '-Start')) {
    if (-not $bootstrapHelp.Output.Contains($expected)) {
        throw "bootstrap -Help output did not contain '$expected'. Output: $($bootstrapHelp.Output)"
    }
}

Write-Host 'Development entrypoint smoke tests passed.'
