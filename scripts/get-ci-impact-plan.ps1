# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads a caller-provided Git diff when explicit changed paths are not supplied
#     - Writes the impact-plan JSON artifact and optional GitHub output/summary files
#   Writes:
#     - artifacts/ci-impact-plan/**
#     - Caller-provided GitHub output and step-summary files
#   Cleanup:
#     - None required; outputs are bounded files owned by the current CI run
#   Requires:
#     - PowerShell 7
#     - Git

[CmdletBinding(DefaultParameterSetName = 'Paths')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Paths')] [AllowEmptyCollection()] [string[]] $ChangedPaths,
    [Parameter(Mandatory, ParameterSetName = 'Diff')] [ValidatePattern('^[0-9a-fA-F]{40}$')] [string] $BaseSha,
    [Parameter(Mandatory, ParameterSetName = 'Diff')] [ValidatePattern('^[0-9a-fA-F]{40}$')] [string] $HeadSha,
    [Parameter(ParameterSetName = 'Diff')] [ValidateSet('MergeBase', 'Range')] [string] $DiffMode = 'MergeBase',
    [string] $OutputPath = 'artifacts/ci-impact-plan/impact-plan.json',
    [string] $GitHubOutputPath = $env:GITHUB_OUTPUT,
    [string] $StepSummaryPath = $env:GITHUB_STEP_SUMMARY
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/CiImpactPlan.ps1')

if ([string]::Equals($PSCmdlet.ParameterSetName, 'Diff', [StringComparison]::Ordinal)) {
    $range = if ([string]::Equals($DiffMode, 'MergeBase', [StringComparison]::Ordinal)) { "$BaseSha...$HeadSha" } else { "$BaseSha..$HeadSha" }
    $diff = Invoke-NativeCommandOutput `
        -Command 'git' `
        -Arguments @('diff', '--name-only', '--no-renames', '--diff-filter=ACMRD', $range, '--') `
        -WorkingDirectory $repoRoot `
        -Name 'resolve-ci-impact-changed-paths'
    $ChangedPaths = @($diff.Stdout -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

$plan = Get-NervCiImpactPlan -ChangedPaths $ChangedPaths
$resolvedOutputPath = if ([IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $repoRoot $OutputPath }
$outputDirectory = Split-Path -Parent $resolvedOutputPath
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$json = $plan | ConvertTo-Json -Depth 20
[IO.File]::WriteAllText($resolvedOutputPath, "$json`n", [Text.UTF8Encoding]::new($false))

if (-not [string]::IsNullOrWhiteSpace($GitHubOutputPath)) {
    $outputLines = [Collections.Generic.List[string]]::new()
    foreach ($property in @($plan.PSObject.Properties | Where-Object { $_.Value -is [bool] })) {
        $outputLines.Add("$($property.Name)=$(([string]$property.Value).ToLowerInvariant())")
    }
    $outputLines.Add("business_services=$(ConvertTo-Json -InputObject @($plan.business_services) -Compress)")
    $outputLines.Add("plan_path=$OutputPath")
    [IO.File]::AppendAllLines($GitHubOutputPath, $outputLines, [Text.UTF8Encoding]::new($false))
}

if (-not [string]::IsNullOrWhiteSpace($StepSummaryPath)) {
    $summary = [Text.StringBuilder]::new()
    [void]$summary.AppendLine('## CI impact plan')
    [void]$summary.AppendLine()
    [void]$summary.AppendLine('NERV-668 routes Script Governance and OpenAPI/api-client Drift; NERV-685 derives governed frontend workspace shards. Other jobs remain unrouted.')
    [void]$summary.AppendLine()
    [void]$summary.AppendLine('| Signal | Selected | Reason |')
    [void]$summary.AppendLine('| --- | --- | --- |')
    foreach ($property in @($plan.PSObject.Properties | Where-Object { $_.Value -is [bool] })) {
        $reason = if ($property.Value) { @($plan.reasons.PSObject.Properties[$property.Name].Value) -join '<br>' } else { 'no matching changed path' }
        [void]$summary.AppendLine("| ``$($property.Name)`` | ``$(([string]$property.Value).ToLowerInvariant())`` | $reason |")
    }
    [void]$summary.AppendLine()
    [void]$summary.AppendLine("Business services: ``$(@($plan.business_services) -join ', ')``")
    [IO.File]::AppendAllText($StepSummaryPath, $summary.ToString(), [Text.UTF8Encoding]::new($false))
}

Write-Output "CI impact plan written to $resolvedOutputPath"
