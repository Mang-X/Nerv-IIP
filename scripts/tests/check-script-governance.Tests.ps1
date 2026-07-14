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
Invoke-GovernanceCase -Name 'allowed-multi-category.ps1' -ExpectedExitCode 0
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

$redactionRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-log-redaction-$([System.Guid]::NewGuid().ToString('N'))"
try {
    New-Item -ItemType Directory -Force -Path $redactionRoot | Out-Null
    $redactionLog = Join-Path $redactionRoot 'redaction.log'
    [System.IO.File]::WriteAllLines(
        $redactionLog,
        @(
            'normal line before secret',
            'token=super-secret-token',
            'normal line after secret'
        ),
        [System.Text.UTF8Encoding]::new($false)
    )

    Protect-ScriptAutomationLogFile -Path $redactionLog
    $redactedLines = [System.IO.File]::ReadAllLines($redactionLog)
    $redactedText = [string]::Join("`n", $redactedLines)

    foreach ($expected in @('normal line before secret', 'token=<redacted>', 'normal line after secret')) {
        if (-not $redactedText.Contains($expected)) {
            throw "Expected redacted log to contain '$expected'. Output: $redactedText"
        }
    }

    if ($redactedText.Contains('super-secret-token')) {
        throw "Expected redacted log to remove secret token. Output: $redactedText"
    }

    $helperContent = Get-Content -Path $helper -Raw
    $parseErrors = $null
    $helperAst = [System.Management.Automation.Language.Parser]::ParseInput($helperContent, [ref]$null, [ref]$parseErrors)
    if ($parseErrors -and $parseErrors.Count -gt 0) {
        throw "Failed to parse ScriptAutomation helper: $($parseErrors[0].Message)"
    }

    $protectLogFileAst = $helperAst.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Protect-ScriptAutomationLogFile'
    }, $true)
    if (-not $protectLogFileAst) {
        throw 'Could not find Protect-ScriptAutomationLogFile implementation.'
    }

    if ($protectLogFileAst.Extent.Text -match 'Get-Content\s+\$Path\s+-Raw') {
        throw 'Protect-ScriptAutomationLogFile must stream log redaction instead of using Get-Content -Raw.'
    }
}
finally {
    $resolvedRedactionRoot = Resolve-Path $redactionRoot -ErrorAction SilentlyContinue
    if ($resolvedRedactionRoot) {
        $tempRoot = [System.IO.Path]::GetTempPath()
        if (-not $resolvedRedactionRoot.Path.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove log redaction directory outside temp: $($resolvedRedactionRoot.Path)"
        }

        Remove-Item -LiteralPath $resolvedRedactionRoot.Path -Recurse -Force
    }
}

$idempotentRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-background-idempotent-$([System.Guid]::NewGuid().ToString('N'))"
$idempotentBackground = $null
try {
    $idempotentBackground = Start-ManagedBackgroundProcess -Command 'pwsh' -Arguments @('-NoProfile', '-Command', 'Start-Sleep -Seconds 30') -Name 'background-idempotent-stop-smoke' -LogDirectory (Join-Path $idempotentRoot 'background-idempotent-stop-smoke')

    $firstStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    & $idempotentBackground.Stop 'first'
    $firstStopwatch.Stop()

    $secondStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    & $idempotentBackground.Stop 'second'
    $secondStopwatch.Stop()

    foreach ($measurement in @(
        [pscustomobject]@{ Name = 'first'; Duration = $firstStopwatch.Elapsed },
        [pscustomobject]@{ Name = 'second'; Duration = $secondStopwatch.Elapsed }
    )) {
        if ($measurement.Duration.TotalSeconds -gt 5) {
            throw "Expected $($measurement.Name) background Stop call to return quickly, took $($measurement.Duration)."
        }
    }

    $idempotentBackground = $null
}
finally {
    if ($idempotentBackground) {
        & $idempotentBackground.Stop 'idempotent stop final cleanup'
    }

    $resolvedIdempotentRoot = Resolve-Path $idempotentRoot -ErrorAction SilentlyContinue
    if ($resolvedIdempotentRoot) {
        $tempRoot = [System.IO.Path]::GetTempPath()
        if (-not $resolvedIdempotentRoot.Path.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove idempotent smoke directory outside temp: $($resolvedIdempotentRoot.Path)"
        }

        Remove-Item -LiteralPath $resolvedIdempotentRoot.Path -Recurse -Force
    }
}

$backgroundRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-background-arguments-$([System.Guid]::NewGuid().ToString('N'))"
$backgroundScript = Join-Path $backgroundRoot 'print-arguments.ps1'
$background = $null
try {
    New-Item -ItemType Directory -Force -Path $backgroundRoot | Out-Null
    [System.IO.File]::WriteAllText($backgroundScript, 'Write-Output "count=$($args.Count)"; Write-Output "arg0=$($args[0])"; Write-Output "arg1=$($args[1])"', [System.Text.UTF8Encoding]::new($false))
    $background = Start-ManagedBackgroundProcess -Command 'pwsh' -Arguments @('-NoProfile', '-File', $backgroundScript, 'a b', 'a "quoted" b') -Name 'background-argument-smoke' -LogDirectory (Join-Path $backgroundRoot 'background-argument-smoke')

    if (-not $background.Process.WaitForExit(15000)) {
        throw 'Background argument smoke process did not exit in time.'
    }

    & $background.Stop 'Background argument smoke cleanup'
    $stdout = Get-Content -Path $background.StdoutPath -Raw
    $background = $null

    foreach ($expected in @('count=2', 'arg0=a b', 'arg1=a "quoted" b')) {
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

$detachedRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-detached-$([System.Guid]::NewGuid().ToString('N'))"
try {
    [System.IO.Directory]::CreateDirectory($detachedRoot) | Out-Null
    $detachedChild = Join-Path $detachedRoot 'detached child.ps1'
    $detachedLauncher = Join-Path $detachedRoot 'launcher.ps1'
    $detachedMarker = Join-Path $detachedRoot 'completion marker.txt'
    $detachedIdentity = Join-Path $detachedRoot 'identity.json'
    $detachedStdout = Join-Path $detachedRoot 'stdout.log'
    $detachedStderr = Join-Path $detachedRoot 'stderr.log'
    [System.IO.File]::WriteAllText(
        $detachedChild,
        'param($Marker,$First,$Second); Start-Sleep -Seconds 2; [IO.File]::WriteAllText($Marker,"$First|$Second"); Write-Output completed',
        [System.Text.UTF8Encoding]::new($false)
    )
    $quote = { param($Value) "'$($Value.Replace("'", "''"))'" }
    $launcherText = @"
. $(& $quote $helper)
`$identity = Start-DetachedManagedProcess -Command 'pwsh' -Arguments @('-NoProfile','-File',$(& $quote $detachedChild),$(& $quote $detachedMarker),'a b','a "quoted" b') -WorkingDirectory $(& $quote $detachedRoot) -StdoutPath $(& $quote $detachedStdout) -StderrPath $(& $quote $detachedStderr)
if (`$identity.Pid -le 0 -or [string]::IsNullOrWhiteSpace("`$(`$identity.ProcessStartTimeUtc)")) { throw 'Detached identity missing.' }
[IO.File]::WriteAllText($(& $quote $detachedIdentity), (`$identity | ConvertTo-Json -Compress))
"@
    [System.IO.File]::WriteAllText($detachedLauncher, $launcherText, [System.Text.UTF8Encoding]::new($false))
    & pwsh -NoProfile -File $detachedLauncher
    if ($LASTEXITCODE -ne 0) { throw "Detached launcher failed with exit $LASTEXITCODE." }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    while (-not (Test-Path -LiteralPath $detachedMarker) -and [DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Path -LiteralPath $detachedMarker)) { throw 'Detached child did not survive its launcher process.' }
    $markerText = [System.IO.File]::ReadAllText($detachedMarker)
    if ($markerText -ne 'a b|a "quoted" b') { throw "Detached arguments were corrupted: $markerText" }
    $identity = Get-Content -LiteralPath $detachedIdentity -Raw | ConvertFrom-Json
    Wait-Process -Id ([int] $identity.Pid) -Timeout 10 -ErrorAction SilentlyContinue
}
finally {
    if (Test-Path -LiteralPath $detachedIdentity) {
        $identity = Get-Content -LiteralPath $detachedIdentity -Raw | ConvertFrom-Json
        $process = Get-Process -Id ([int] $identity.Pid) -ErrorAction SilentlyContinue
        if ($process) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    }
    $resolvedDetachedRoot = Resolve-Path $detachedRoot -ErrorAction SilentlyContinue
    if ($resolvedDetachedRoot) {
        $tempRoot = [System.IO.Path]::GetTempPath()
        if (-not $resolvedDetachedRoot.Path.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove detached smoke directory outside temp: $($resolvedDetachedRoot.Path)"
        }
        Remove-Item -LiteralPath $resolvedDetachedRoot.Path -Recurse -Force
    }
}

Write-Host 'Script governance fixture tests passed.'
