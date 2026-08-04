# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Builds each selected backend test project on its first seeded run
#     - Runs four backend test projects in six seeded serial/parallel rounds
#   Writes:
#     - bin/ and obj/ build outputs under the selected .NET test projects
#     - artifacts/script-logs/backend-test-determinism-*/**
#     - artifacts/test-determinism/man-662/**
#   Cleanup:
#     - Restores NERV_IIP_TEST_ORDER_SEED after every round
#     - Leaves immutable local repeatability evidence for diagnosis
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10

[CmdletBinding()]
param(
    [string] $ArtifactRoot = (Join-Path $PSScriptRoot '../artifacts/test-determinism/man-662'),

    [string] $InvocationId = "$(Get-Date -AsUTC -Format 'yyyyMMddTHHmmssfffZ')-$([Guid]::NewGuid().ToString('N'))",

    [ValidateRange(1, 3600)]
    [int] $ProjectTimeoutSeconds = 1200
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $root 'scripts/lib/ScriptAutomation.ps1')

if ($InvocationId -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
    throw '-InvocationId must contain only letters, numbers, dot, underscore, or hyphen.'
}

$effectiveArtifactRoot = if ([System.IO.Path]::IsPathRooted($ArtifactRoot)) {
    $ArtifactRoot
}
else {
    Join-Path $root $ArtifactRoot
}
$invocationRoot = Join-Path $effectiveArtifactRoot $InvocationId
if (Test-Path -LiteralPath $invocationRoot) {
    throw "Evidence invocation '$InvocationId' already exists at $invocationRoot. Use a new invocation ID; reruns never replace prior evidence."
}
[System.IO.Directory]::CreateDirectory($effectiveArtifactRoot) | Out-Null
$claimPath = New-ExclusiveInvocationClaim `
    -ClaimPath (Join-Path $effectiveArtifactRoot ".$InvocationId.claim") `
    -InvocationId $InvocationId
[System.IO.Directory]::CreateDirectory($invocationRoot) | Out-Null

$projects = @(
    'backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj',
    'backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/Nerv.IIP.Business.Maintenance.Web.Tests.csproj',
    'backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj',
    'backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj'
)

function Write-RunSettings {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [ValidateSet(1, 4)]
        [int] $MaxParallelThreads
    )

    $content = @"
<RunSettings>
  <xUnit>
    <ParallelizeTestCollections>true</ParallelizeTestCollections>
    <MaxParallelThreads>$MaxParallelThreads</MaxParallelThreads>
  </xUnit>
</RunSettings>
"@
    [System.IO.File]::WriteAllText($Path, "$content$([Environment]::NewLine)", [System.Text.UTF8Encoding]::new($false))
}

function Get-TestCounts {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $StdoutPath
    )

    # The rounds run with DOTNET_CLI_UI_LANGUAGE=en so this summary line is locale-stable.
    if (-not (Test-Path -LiteralPath $StdoutPath -PathType Leaf)) {
        return $null
    }

    $counts = $null
    foreach ($line in [System.IO.File]::ReadLines($StdoutPath)) {
        if ($line -match 'Failed:\s*(?<failed>\d+),\s*Passed:\s*(?<passed>\d+),\s*Skipped:\s*(?<skipped>\d+),\s*Total:\s*(?<total>\d+)') {
            $counts = [ordered]@{
                failed = [int] $Matches['failed']
                passed = [int] $Matches['passed']
                skipped = [int] $Matches['skipped']
                total = [int] $Matches['total']
            }
        }
    }

    return $counts
}

function Format-TestCounts {
    param($Counts)

    if ($null -eq $Counts) {
        return 'unparsed'
    }

    return "total=$($Counts.total) passed=$($Counts.passed) skipped=$($Counts.skipped) failed=$($Counts.failed)"
}

function Get-ProjectExitCode {
    param(
        [Parameter(Mandatory)]
        [System.Management.Automation.ErrorRecord] $ErrorRecord
    )

    if ($ErrorRecord.Exception.Message -match "exited with (?<exitCode>\d+)") {
        return [int] $Matches['exitCode']
    }

    return 1
}

$summaryRows = [System.Collections.Generic.List[object]]::new()
$hasFailures = $false

for ($roundIndex = 0; $roundIndex -lt 6; $roundIndex++) {
    $run = $roundIndex + 1
    $seed = 'man662-{0:d2}' -f $run
    $parallelProfile = if ($run % 2 -eq 1) { 'serial' } else { 'parallel' }
    $maxParallelThreads = if ($parallelProfile -ceq 'serial') { 1 } else { 4 }
    $projectOrder = @(
        for ($offset = 0; $offset -lt $projects.Count; $offset++) {
            $projects[($roundIndex + $offset) % $projects.Count]
        }
    )

    $roundRoot = Join-Path $invocationRoot ('run-{0:d2}' -f $run)
    [System.IO.Directory]::CreateDirectory($roundRoot) | Out-Null
    $runSettingsPath = Join-Path $roundRoot "$parallelProfile.runsettings"
    Write-RunSettings -Path $runSettingsPath -MaxParallelThreads $maxParallelThreads

    Write-Diagnostic "Starting backend determinism run=$run seed=$seed profile=$parallelProfile maxParallelThreads=$maxParallelThreads."
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $projectResults = @(
        Invoke-WithScopedEnvironment -Variables @{ NERV_IIP_TEST_ORDER_SEED = $seed; DOTNET_CLI_UI_LANGUAGE = 'en' } -ScriptBlock {
            foreach ($project in $projectOrder) {
                $arguments = @(
                    'test',
                    $project,
                    '--configuration',
                    'Release',
                    '--settings',
                    $runSettingsPath
                )
                if ($run -gt 1) {
                    $arguments += @('--no-build', '--no-restore')
                }

                try {
                    $invocation = Invoke-DotNet `
                        -Arguments $arguments `
                        -WorkingDirectory $root `
                        -TimeoutSeconds $ProjectTimeoutSeconds `
                        -Name "backend-test-determinism-$InvocationId-run-$run-$([System.IO.Path]::GetFileNameWithoutExtension($project))"
                    [ordered]@{
                        project = $project
                        exitCode = 0
                        counts = Get-TestCounts -StdoutPath $invocation.StdoutPath
                    }
                }
                catch {
                    $exitCode = Get-ProjectExitCode -ErrorRecord $_
                    Write-Diagnostic -Level 'ERROR' -Message "Backend determinism project failed: run=$run seed=$seed profile=$parallelProfile project=$project exitCode=$exitCode."
                    [ordered]@{
                        project = $project
                        exitCode = $exitCode
                        counts = $null
                    }
                }
            }
        }
    )
    $projectExitCodes = @($projectResults | ForEach-Object { $_.exitCode })
    $stopwatch.Stop()

    $roundFailure = @($projectExitCodes | Where-Object { [int] $_ -ne 0 } | Select-Object -First 1)
    $roundExitCode = if ($roundFailure.Count -eq 0) { 0 } else { [int] $roundFailure[0] }
    if ($roundExitCode -ne 0) {
        $hasFailures = $true
    }

    $summaryRows.Add([ordered]@{
        run = $run
        seed = $seed
        profile = $parallelProfile
        projectOrder = @($projectOrder)
        elapsedMs = [long] $stopwatch.ElapsedMilliseconds
        exitCode = $roundExitCode
        projectResults = @($projectResults)
    })
}

# Exit code equality alone would call a round that silently skipped tests "consistent". Every project
# must report the same total/passed/skipped/failed counts in every round it completed.
$countMismatches = [System.Collections.Generic.List[string]]::new()
foreach ($project in $projects) {
    $observed = @(
        foreach ($row in $summaryRows) {
            $match = @($row.projectResults | Where-Object { $_.project -ceq $project })
            if ($match.Count -eq 1 -and $match[0].exitCode -eq 0) {
                [pscustomobject]@{ Run = $row.run; Counts = $match[0].counts }
            }
        }
    )
    if ($observed.Count -lt 2) {
        continue
    }

    $baseline = $observed[0]
    if ($null -eq $baseline.Counts) {
        $countMismatches.Add("$project run=$($baseline.Run) produced no parsable test summary.")
        continue
    }

    foreach ($entry in $observed | Select-Object -Skip 1) {
        if ($null -eq $entry.Counts) {
            $countMismatches.Add("$project run=$($entry.Run) produced no parsable test summary.")
            continue
        }

        if ($entry.Counts.total -ne $baseline.Counts.total -or
            $entry.Counts.passed -ne $baseline.Counts.passed -or
            $entry.Counts.skipped -ne $baseline.Counts.skipped -or
            $entry.Counts.failed -ne $baseline.Counts.failed) {
            $countMismatches.Add(
                "$project run=$($entry.Run) reported $(Format-TestCounts $entry.Counts) but run=$($baseline.Run) reported $(Format-TestCounts $baseline.Counts).")
        }
    }
}

$summaryPath = Join-Path $invocationRoot 'summary.json'
$summaryJson = @($summaryRows) | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($summaryPath, "$summaryJson$([Environment]::NewLine)", [System.Text.UTF8Encoding]::new($false))
Write-Host "Backend test determinism summary: $summaryPath"

if ($hasFailures) {
    throw "One or more backend test determinism rounds failed. Summary: $summaryPath"
}

if ($countMismatches.Count -gt 0) {
    throw "Backend test determinism rounds disagreed on test results:$([Environment]::NewLine)$($countMismatches -join [Environment]::NewLine)$([Environment]::NewLine)Summary: $summaryPath"
}

Write-Host 'Backend test determinism repeatability verified for six seeded rounds with identical per-project test results.'
