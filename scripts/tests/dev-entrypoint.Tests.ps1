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

$appHostText = Get-Content -LiteralPath (Join-Path $repoRoot 'infra/aspire/Nerv.IIP.AppHost/AppHostGatewaysAndFrontend.cs') -Raw
if ($appHostText.Contains('env: fullStackEphemeral ? "PORT" : null', [StringComparison]::Ordinal) -or -not $appHostText.Contains('env: fullStackEphemeral ? "NERV_IIP_VITE_PORT" : null', [StringComparison]::Ordinal)) {
    throw 'Ephemeral Vite endpoints must use the dedicated NERV_IIP_VITE_PORT environment variable.'
}
foreach ($relativePath in @(
    'frontend/apps/console/vite.config.ts',
    'frontend/apps/business-console/vite.config.ts',
    'frontend/apps/screen/vite.config.ts'
)) {
    $viteText = Get-Content -LiteralPath (Join-Path $repoRoot $relativePath) -Raw
    if ($viteText.Contains('process.env.PORT', [StringComparison]::Ordinal) -or -not $viteText.Contains('process.env.NERV_IIP_VITE_PORT', [StringComparison]::Ordinal)) {
        throw "$relativePath must not let a generic PORT variable change persistent development ports."
    }
}
foreach ($relativePath in @(
    'frontend/apps/console/playwright.config.ts',
    'frontend/apps/business-console/playwright.config.ts'
)) {
    $playwrightText = Get-Content -LiteralPath (Join-Path $repoRoot $relativePath) -Raw
    if ($playwrightText.Contains('process.env.PLAYWRIGHT_BASE_URL', [StringComparison]::Ordinal) -or -not $playwrightText.Contains('process.env.NERV_IIP_PLAYWRIGHT_BASE_URL', [StringComparison]::Ordinal)) {
        throw "$relativePath must use the dedicated NERV_IIP_PLAYWRIGHT_BASE_URL override."
    }
}

$launchPath = Join-Path $repoRoot 'scripts/dev/start-platform.ps1'
$launchTokens = $null
$launchParseErrors = $null
$launchAst = [System.Management.Automation.Language.Parser]::ParseFile(
    $launchPath, [ref] $launchTokens, [ref] $launchParseErrors)
if ($launchParseErrors.Count -gt 0) {
    throw "Local launcher has parse errors: $launchParseErrors"
}
$environmentAssignment = $launchAst.Find({
    param($node)
    $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
        $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
        $node.Left.VariablePath.UserPath -eq 'appHostEnvironment'
}, $true)
$readLaunchEnvironment = [scriptblock]::Create($environmentAssignment.Right.Extent.Text)
$savedProvider = $env:Messaging__Provider
try {
    foreach ($case in @(
        @{ Override = $null; Expected = 'Redis' },
        @{ Override = 'RabbitMQ'; Expected = 'RabbitMQ' }
    )) {
        $env:Messaging__Provider = $case.Override
        $localEnvironment = & $readLaunchEnvironment
        if ($localEnvironment.Messaging__Provider -ne $case.Expected) {
            throw "Expected local Aspire messaging provider '$($case.Expected)'."
        }
    }
}
finally {
    $env:Messaging__Provider = $savedProvider
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
    '.\nerv.ps1 fullstack run -Scenario smoke -EnableWmsDemoWorker',
    '.\nerv.ps1 fullstack start -EnableWmsDemoWorker',
    '.\nerv.ps1 publish-compose',
    '.\nerv.ps1 ports',
    '.\nerv.ps1 help'
)) {
    if (-not $help.Output.Contains($expected)) {
        throw "Help output did not contain '$expected'. Output: $($help.Output)"
    }
}

foreach ($expected in @(
    'fullstack run is one-shot and automatically stops and cleans up its session when complete',
    'Use fullstack start for interactive diagnostic walkthroughs; run fullstack stop before handoff or ending the task'
)) {
    if (-not $help.Output.Contains($expected, [StringComparison]::Ordinal)) {
        throw "Root help output did not explain full-stack session '$expected'. Output: $($help.Output)"
    }
}

$fullStackHelp = Invoke-Nerv -Arguments @('fullstack', 'help')
if ($fullStackHelp.ExitCode -ne 0) {
    throw "Expected fullstack help to exit 0, got $($fullStackHelp.ExitCode). Output: $($fullStackHelp.Output)"
}

$fullStackSessionText = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/fullstack/manage-fullstack-session.ps1') -Raw
$tokens = $null
$parseErrors = $null
$fullStackSessionAst = [System.Management.Automation.Language.Parser]::ParseInput($fullStackSessionText, [ref] $tokens, [ref] $parseErrors)
$startDispatch = @($fullStackSessionAst.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.CommandAst] -and
        [string]::Equals($node.GetCommandName(), 'Start-NervFullStackSession', [StringComparison]::Ordinal) -and
        $node.Parent -is [System.Management.Automation.Language.PipelineAst] -and
        $node.Parent.Parent -is [System.Management.Automation.Language.StatementBlockAst] -and
        $node.Parent.Parent.Parent -is [System.Management.Automation.Language.IfStatementAst]
}, $true))
if ($startDispatch.Count -ne 1) {
    throw "Expected one direct full-stack start dispatch, found $($startDispatch.Count)."
}
$startDispatchBlock = [scriptblock]::Create($startDispatch[0].Parent.Parent.Parent.Extent.Text)
function Start-NervFullStackSession {
    param([switch] $EnableWmsDemoWorker)
    $script:capturedWmsDemoWorker = [bool] $EnableWmsDemoWorker
}
foreach ($workerOptIn in @($true, $false)) {
    $script:capturedWmsDemoWorker = $null
    $Action = 'start'
    $EnableWmsDemoWorker = $workerOptIn
    & $startDispatchBlock
    if ($null -eq $script:capturedWmsDemoWorker -or $script:capturedWmsDemoWorker -ne $workerOptIn) {
        throw "Full-stack start bound EnableWmsDemoWorker=$script:capturedWmsDemoWorker; expected $workerOptIn."
    }
}
foreach ($expected in @('run', 'start', 'url', 'status', 'logs', 'stop', 'list', 'gc', '-EnableWmsDemoWorker')) {
    if (-not $fullStackHelp.Output.Contains($expected)) {
        throw "Full-stack help did not contain '$expected'. Output: $($fullStackHelp.Output)"
    }
}
foreach ($expected in @(
    'fullstack run is one-shot and automatically stops and cleans up its session when complete',
    'Use fullstack start for interactive diagnostic walkthroughs; run fullstack stop before handoff or ending the task'
)) {
    if (-not $fullStackHelp.Output.Contains($expected, [StringComparison]::Ordinal)) {
        throw "Full-stack help output did not explain session '$expected'. Output: $($fullStackHelp.Output)"
    }
}

$dispatchRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-entrypoint-dispatch-$([guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory((Join-Path $dispatchRoot 'scripts/fullstack')) | Out-Null
    $dispatchNerv = Join-Path $dispatchRoot 'nerv.ps1'
    Copy-Item -LiteralPath $nerv -Destination $dispatchNerv
    $captureScript = @'
param(
    [Parameter(Position=0)][string]$Action,
    [Parameter(Position=1)][string]$Target,
    [string]$Scenario,
    [string]$SessionId,
    [switch]$NoBuild,
    [switch]$EnableWmsDemoWorker,
    [int]$Tail,
    [switch]$Follow
)
[pscustomobject]@{ action=$Action; target=$Target; scenario=$Scenario; sessionId=$SessionId; noBuild=[bool]$NoBuild; enableWmsDemoWorker=[bool]$EnableWmsDemoWorker; tail=$Tail; follow=[bool]$Follow } | ConvertTo-Json -Compress
'@
    [IO.File]::WriteAllText((Join-Path $dispatchRoot 'scripts/fullstack/manage-fullstack-session.ps1'), $captureScript, [Text.UTF8Encoding]::new($false))

    # 已加载旧版代码的进程按固定路径启动 guardian 或按精确 SessionId 请求回收。
    $legacyStop = 'scripts/fullstack/fullstack-session.ps1'
    Copy-Item -LiteralPath (Join-Path $repoRoot $legacyStop) -Destination (Join-Path $dispatchRoot $legacyStop)
    [IO.Directory]::CreateDirectory((Join-Path $dispatchRoot 'scripts/lib')) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1') -Destination (Join-Path $dispatchRoot 'scripts/lib')
    $legacySessionId = 'nerv-abcd-123456'
    $stop = Invoke-Nerv -ScriptPath (Join-Path $dispatchRoot $legacyStop) -Arguments @('stop', '-SessionId', $legacySessionId)
    $stopped = $stop.Output | ConvertFrom-Json
    if ($stop.ExitCode -ne 0 -or -not [string]::Equals($stopped.action, 'stop', [StringComparison]::Ordinal) -or -not [string]::Equals($stopped.sessionId, $legacySessionId, [StringComparison]::Ordinal)) {
        throw "Legacy guardian cleanup lost its action or session identity: $($stop.Output)"
    }

    $stateDirectory = [IO.Directory]::CreateDirectory((Join-Path $dispatchRoot 'fullstack-sessions'))
    [IO.File]::WriteAllText((Join-Path $stateDirectory.FullName "$legacySessionId.json"), '{"state":"Stopped"}')
    $guardian = Invoke-Nerv -ScriptPath (Join-Path $repoRoot 'scripts/fullstack/fullstack-guardian.ps1') -Arguments @(
        '-SessionId', $legacySessionId, '-Mode', 'Automated', '-StateRoot', $dispatchRoot,
        '-IntervalSeconds', '1', '-CoordinatorPid', "$PID", '-CoordinatorStartTimeUtc', ([DateTime]::UtcNow.ToString('O'))
    )
    if ($guardian.ExitCode -ne 0 -or -not $guardian.Output.Contains("Guardian started for '$legacySessionId' in Automated mode.", [StringComparison]::Ordinal)) {
        throw "Legacy coordinator failed to start the guardian for its session: $($guardian.Output)"
    }

    $fullStackRun = Invoke-Nerv -ScriptPath $dispatchNerv -Arguments @('fullstack', 'run', '-Scenario', 'smoke', '-NoBuild')
    $runCapture = $fullStackRun.Output | ConvertFrom-Json
    if ($fullStackRun.ExitCode -ne 0 -or (-not [string]::Equals([string]($runCapture.action), [string]('run'), [StringComparison]::OrdinalIgnoreCase)) -or (-not [string]::Equals([string]($runCapture.scenario), [string]('smoke'), [StringComparison]::OrdinalIgnoreCase)) -or -not $runCapture.noBuild) {
        throw "Named full-stack run options were not forwarded. Output: $($fullStackRun.Output)"
    }

    $fullStackWmsRun = Invoke-Nerv -ScriptPath $dispatchNerv -Arguments @('fullstack', 'run', '-Scenario', 'smoke', '-EnableWmsDemoWorker')
    $wmsRunCapture = $fullStackWmsRun.Output | ConvertFrom-Json
    if ($fullStackWmsRun.ExitCode -ne 0 -or -not $wmsRunCapture.enableWmsDemoWorker) {
        throw "WMS demo worker opt-in was not forwarded. Output: $($fullStackWmsRun.Output)"
    }

    $fullStackWmsDisabled = Invoke-Nerv -ScriptPath $dispatchNerv -Arguments @('fullstack', 'run', '-Scenario', 'smoke', '-EnableWmsDemoWorker:$false')
    $wmsDisabledCapture = $fullStackWmsDisabled.Output | ConvertFrom-Json
    if ($fullStackWmsDisabled.ExitCode -ne 0 -or $wmsDisabledCapture.enableWmsDemoWorker) {
        throw "Explicitly disabled WMS demo worker opt-in was not preserved. Output: $($fullStackWmsDisabled.Output)"
    }

    $forwardedSessionId = 'nerv-abcd-123456'
    $fullStackStop = Invoke-Nerv -ScriptPath $dispatchNerv -Arguments @('fullstack', 'stop', '-SessionId', $forwardedSessionId)
    $stopCapture = $fullStackStop.Output | ConvertFrom-Json
    if ($fullStackStop.ExitCode -ne 0 -or (-not [string]::Equals([string]($stopCapture.action), [string]('stop'), [StringComparison]::OrdinalIgnoreCase)) -or -not [string]::Equals([string]$stopCapture.sessionId, $forwardedSessionId, [StringComparison]::Ordinal)) {
        throw "Named full-stack session ID was not forwarded. Output: $($fullStackStop.Output)"
    }

    $fullStackUrl = Invoke-Nerv -ScriptPath $dispatchNerv -Arguments @('fullstack', 'url', 'business-console')
    $urlCapture = $fullStackUrl.Output | ConvertFrom-Json
    if ($fullStackUrl.ExitCode -ne 0 -or (-not [string]::Equals([string]($urlCapture.action), [string]('url'), [StringComparison]::OrdinalIgnoreCase)) -or (-not [string]::Equals([string]($urlCapture.target), [string]('business-console'), [StringComparison]::OrdinalIgnoreCase))) {
        throw "Positional full-stack URL target was not forwarded. Output: $($fullStackUrl.Output)"
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

if (-not $unknown.Output.Contains("Unknown command 'unknown-command'", [StringComparison]::Ordinal)) {
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
