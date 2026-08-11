# Script-Governance:
#   Category: check
#   SideEffects:
#     - Parses the nightly business performance workflow
#     - Writes temporary workflow mutations under the operating-system temp directory
#   Writes:
#     - Temporary files under the operating-system temp directory
#   Cleanup:
#     - Removes owned temporary workflow mutation directory in finally
#   Requires:
#     - PowerShell 7
#     - Ruby 3.4 with yaml and json

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$workflowPath = Join-Path $repoRoot '.github/workflows/nightly-business-performance.yml'
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')

function Assert-WorkflowContract {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    if (-not $Condition) { throw $Message }
}

function Get-WorkflowProperty {
    param(
        [Parameter(Mandatory)] [object] $Object,
        [Parameter(Mandatory)] [string] $Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-NightlyBusinessPerformanceWorkflow {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required nightly workflow '.github/workflows/nightly-business-performance.yml' does not exist."
    }

    $rubyProgram = "require 'yaml'; require 'json'; puts JSON.generate(YAML.safe_load(File.read(ARGV.fetch(0))))"
    $parsed = Invoke-NativeCommandOutput `
        -Command 'ruby' `
        -Arguments @('-ryaml', '-rjson', '-e', $rubyProgram, $Path) `
        -WorkingDirectory $repoRoot `
        -Name 'parse-nightly-business-performance-workflow'
    return ($parsed.Stdout | ConvertFrom-Json -ErrorAction Stop)
}

function Assert-NightlyBusinessPerformanceWorkflow {
    param([Parameter(Mandatory)] [string] $Path)

    $workflow = Get-NightlyBusinessPerformanceWorkflow -Path $Path
    $triggers = Get-WorkflowProperty -Object $workflow -Name 'on'
    if ($null -eq $triggers) { $triggers = Get-WorkflowProperty -Object $workflow -Name 'true' }
    Assert-WorkflowContract ($null -ne $triggers) 'Nightly business performance workflow must declare triggers.'
    $triggerNames = @($triggers.PSObject.Properties.Name)
    Assert-WorkflowContract ($triggerNames.Count -eq 2 -and $triggerNames.Contains('schedule') -and $triggerNames.Contains('workflow_dispatch')) 'Nightly business performance workflow must have only schedule and workflow_dispatch triggers.'
    $schedule = @(Get-WorkflowProperty -Object $triggers -Name 'schedule')
    Assert-WorkflowContract ($schedule.Count -eq 1 -and [string]::Equals([string](Get-WorkflowProperty -Object $schedule[0] -Name 'cron'), '0 17 * * *', [StringComparison]::Ordinal)) 'Nightly business performance workflow must schedule exactly 0 17 * * *.'
    $dispatch = Get-WorkflowProperty -Object $triggers -Name 'workflow_dispatch'
    $inputs = Get-WorkflowProperty -Object $dispatch -Name 'inputs'
    $manualThreshold = Get-WorkflowProperty -Object $inputs -Name 'max_elapsed_milliseconds'
    Assert-WorkflowContract ($null -ne $manualThreshold -and [string]::Equals([string](Get-WorkflowProperty -Object $manualThreshold -Name 'type'), 'string', [StringComparison]::Ordinal) -and [string]::Equals([string](Get-WorkflowProperty -Object $manualThreshold -Name 'default'), '0', [StringComparison]::Ordinal)) 'workflow_dispatch must expose max_elapsed_milliseconds as string default 0.'

    $permissions = Get-WorkflowProperty -Object $workflow -Name 'permissions'
    Assert-WorkflowContract ($null -ne $permissions -and $permissions.PSObject.Properties.Count -eq 1 -and [string]::Equals([string](Get-WorkflowProperty -Object $permissions -Name 'contents'), 'read', [StringComparison]::Ordinal)) 'Nightly business performance workflow permissions must be contents: read only.'

    $jobs = Get-WorkflowProperty -Object $workflow -Name 'jobs'
    Assert-WorkflowContract ($null -ne $jobs -and $jobs.PSObject.Properties.Count -eq 1 -and $null -ne $jobs.PSObject.Properties['business-performance']) 'Nightly business performance workflow must contain only the business-performance job.'
    $job = $jobs.PSObject.Properties['business-performance'].Value
    Assert-WorkflowContract ([int](Get-WorkflowProperty -Object $job -Name 'timeout-minutes') -eq 45) 'business-performance job timeout-minutes must be 45.'
    Assert-WorkflowContract ([string]::Equals([string](Get-WorkflowProperty -Object $job -Name 'runs-on'), 'ubuntu-latest', [StringComparison]::Ordinal)) 'business-performance job must run on ubuntu-latest.'
    $connectionString = Get-WorkflowProperty -Object (Get-WorkflowProperty -Object $job -Name 'env') -Name 'NERV_IIP_PERF_POSTGRES'
    Assert-WorkflowContract ($null -ne $connectionString -and [string]$connectionString -match '^Host=127\.0\.0\.1;Port=5432;Database=nerv_iip_performance;Username=nerv;Password=nerv$') 'business-performance job must inject NERV_IIP_PERF_POSTGRES for the PostgreSQL service.'

    $postgres = Get-WorkflowProperty -Object (Get-WorkflowProperty -Object $job -Name 'services') -Name 'postgres'
    Assert-WorkflowContract ($null -ne $postgres -and [string]::Equals([string](Get-WorkflowProperty -Object $postgres -Name 'image'), 'postgres:18', [StringComparison]::Ordinal)) 'business-performance job must use postgres:18 service.'
    $postgresEnvironment = Get-WorkflowProperty -Object $postgres -Name 'env'
    foreach ($expected in @{ POSTGRES_USER = 'nerv'; POSTGRES_PASSWORD = 'nerv'; POSTGRES_DB = 'nerv_iip_performance' }.GetEnumerator()) {
        Assert-WorkflowContract ([string]::Equals([string](Get-WorkflowProperty -Object $postgresEnvironment -Name $expected.Key), $expected.Value, [StringComparison]::Ordinal)) "PostgreSQL service must set $($expected.Key)=$($expected.Value)."
    }
    $postgresPorts = @(Get-WorkflowProperty -Object $postgres -Name 'ports')
    Assert-WorkflowContract ($postgresPorts.Count -eq 1 -and [string]::Equals([string]$postgresPorts[0], '5432:5432', [StringComparison]::Ordinal)) 'PostgreSQL service must expose 5432:5432.'
    Assert-WorkflowContract ([string](Get-WorkflowProperty -Object $postgres -Name 'options') -match 'pg_isready\s+-U\s+nerv\s+-d\s+nerv_iip_performance') 'PostgreSQL service must configure pg_isready health check.'

    $steps = @(Get-WorkflowProperty -Object $job -Name 'steps')
    Assert-WorkflowContract ($steps.Count -eq 5) 'business-performance job must contain exactly five explicit steps.'
    foreach ($step in $steps) {
        Assert-WorkflowContract ($null -ne (Get-WorkflowProperty -Object $step -Name 'timeout-minutes') -and [int](Get-WorkflowProperty -Object $step -Name 'timeout-minutes') -gt 0) 'Every business-performance step must have a positive timeout-minutes.'
    }
    $checkout = @($steps | Where-Object { [string]::Equals([string](Get-WorkflowProperty -Object $_ -Name 'uses'), 'actions/checkout@v4', [StringComparison]::Ordinal) })
    $setupDotnet = @($steps | Where-Object { [string]::Equals([string](Get-WorkflowProperty -Object $_ -Name 'uses'), 'actions/setup-dotnet@v4', [StringComparison]::Ordinal) })
    $cache = @($steps | Where-Object { [string]::Equals([string](Get-WorkflowProperty -Object $_ -Name 'uses'), 'actions/cache@v4', [StringComparison]::Ordinal) })
    $artifact = @($steps | Where-Object { [string]::Equals([string](Get-WorkflowProperty -Object $_ -Name 'uses'), 'actions/upload-artifact@v4', [StringComparison]::Ordinal) })
    Assert-WorkflowContract ($checkout.Count -eq 1 -and $setupDotnet.Count -eq 1 -and $cache.Count -eq 1 -and $artifact.Count -eq 1) 'Nightly business performance workflow must use checkout, setup-dotnet, cache, and upload-artifact at @v4 exactly once each.'
    Assert-WorkflowContract ([int](Get-WorkflowProperty -Object $checkout[0] -Name 'timeout-minutes') -eq 3 -and [int](Get-WorkflowProperty -Object $setupDotnet[0] -Name 'timeout-minutes') -eq 5 -and [int](Get-WorkflowProperty -Object $cache[0] -Name 'timeout-minutes') -eq 8 -and [int](Get-WorkflowProperty -Object $artifact[0] -Name 'timeout-minutes') -eq 5) 'Nightly business performance fixed step timeouts must be 3m, 5m, 8m, and 5m.'
    Assert-WorkflowContract ([string]::Equals([string](Get-WorkflowProperty -Object (Get-WorkflowProperty -Object $setupDotnet[0] -Name 'with') -Name 'dotnet-version'), '10.0.x', [StringComparison]::Ordinal)) 'Nightly business performance workflow must use .NET 10.0.x.'

    $performance = @($steps | Where-Object { [string](Get-WorkflowProperty -Object $_ -Name 'run') -match 'verify-business-performance-baseline\.ps1' })
    Assert-WorkflowContract ($performance.Count -eq 1 -and [int](Get-WorkflowProperty -Object $performance[0] -Name 'timeout-minutes') -eq 20) 'Nightly business performance workflow must have one 20-minute governed performance step.'
    $performanceRun = [string](Get-WorkflowProperty -Object $performance[0] -Name 'run')
    foreach ($required in @('-Scenario all', '-Profile nightly', '-Rows 25', '-MetricsOutputPath artifacts/business-performance/nightly/metrics.jsonl', '-SummaryOutputPath artifacts/business-performance/nightly/summary.json', 'MANUAL_MAX_ELAPSED_MILLISECONDS', '-MaxElapsedMilliseconds', '-InventoryMaxElapsedMilliseconds 600000', '-MesMaxElapsedMilliseconds 600000', '-ErpMaxElapsedMilliseconds 600000')) {
        Assert-WorkflowContract ($performanceRun.Contains($required, [StringComparison]::Ordinal)) "Governed performance step must contain '$required'."
    }
    Assert-WorkflowContract ($performanceRun -match 'MANUAL_MAX_ELAPSED_MILLISECONDS.*-gt\s+0') 'Governed performance step must use the positive manual threshold branch.'
    Assert-WorkflowContract ($performanceRun -notmatch '(^|\s)(dotnet|docker)(\s|$)' -and $performanceRun -notmatch '\|\|\s*true') 'Governed performance step must not bypass the governed script or swallow failure.'

    Assert-WorkflowContract ([string]::Equals([string](Get-WorkflowProperty -Object $artifact[0] -Name 'if'), 'always()', [StringComparison]::Ordinal)) 'Nightly business performance artifact upload must use if: always().'
    $artifactWith = Get-WorkflowProperty -Object $artifact[0] -Name 'with'
    Assert-WorkflowContract ([string]::Equals([string](Get-WorkflowProperty -Object $artifactWith -Name 'name'), 'business-performance-${{ github.run_id }}-${{ github.run_attempt }}', [StringComparison]::Ordinal)) 'Nightly business performance artifact name must include run and attempt.'
    Assert-WorkflowContract ([string]::Equals([string](Get-WorkflowProperty -Object $artifactWith -Name 'if-no-files-found'), 'error', [StringComparison]::Ordinal)) 'Nightly business performance artifact upload must fail on missing files.'
    Assert-WorkflowContract ([int](Get-WorkflowProperty -Object $artifactWith -Name 'retention-days') -eq 30) 'Nightly business performance artifact retention must be 30 days.'
    $artifactPaths = @([string](Get-WorkflowProperty -Object $artifactWith -Name 'path') -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 })
    Assert-WorkflowContract ($artifactPaths.Count -eq 2 -and $artifactPaths.Contains('artifacts/business-performance/nightly/metrics.jsonl') -and $artifactPaths.Contains('artifacts/business-performance/nightly/summary.json')) 'Nightly business performance artifact must allowlist only metrics.jsonl and summary.json.'

    $serializedWorkflow = $workflow | ConvertTo-Json -Depth 100 -Compress
    Assert-WorkflowContract ($serializedWorkflow -notmatch 'continue-on-error') 'Nightly business performance workflow must not use continue-on-error.'
    Assert-WorkflowContract ($serializedWorkflow -notmatch '\|\|\s*true') 'Nightly business performance workflow must not use || true.'
}

Assert-NightlyBusinessPerformanceWorkflow -Path $workflowPath

$workflowText = Get-Content -LiteralPath $workflowPath -Raw
$mutationRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-nightly-business-performance-workflow-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($mutationRoot) | Out-Null
    foreach ($mutation in @(
            @{ Name = 'connection-string'; Original = 'NERV_IIP_PERF_POSTGRES:'; Replacement = 'NERV_IIP_PERF_POSTGRES_REMOVED:' },
            @{ Name = 'scheduled-threshold'; Original = '-InventoryMaxElapsedMilliseconds 600000'; Replacement = '-InventoryMaxElapsedMilliseconds 0' },
            @{ Name = 'artifact-always'; Original = 'if: always()'; Replacement = 'if: success()' },
            @{ Name = 'artifact-missing-files'; Original = 'if-no-files-found: error'; Replacement = 'if-no-files-found: warn' },
            @{ Name = 'continue-on-error'; Original = 'timeout-minutes: 20'; Replacement = "timeout-minutes: 20$([Environment]::NewLine)        continue-on-error: true" },
            @{ Name = 'masked-performance-failure'; Original = '-ErpMaxElapsedMilliseconds 600000'; Replacement = '-ErpMaxElapsedMilliseconds 600000 || true' }
        )) {
        Assert-WorkflowContract ($workflowText.Contains($mutation.Original, [StringComparison]::Ordinal)) "Mutation '$($mutation.Name)' must match its canonical workflow text."
        $mutatedPath = Join-Path $mutationRoot "$($mutation.Name).yml"
        [IO.File]::WriteAllText($mutatedPath, $workflowText.Replace($mutation.Original, $mutation.Replacement), [Text.UTF8Encoding]::new($false))
        $failure = $null
        try { Assert-NightlyBusinessPerformanceWorkflow -Path $mutatedPath }
        catch { $failure = $_ }
        Assert-WorkflowContract ($null -ne $failure) "Mutation '$($mutation.Name)' must fail the nightly business performance workflow contract."
    }
}
finally {
    if (Test-Path -LiteralPath $mutationRoot) { Remove-Item -LiteralPath $mutationRoot -Recurse -Force }
}

Write-Host 'Nightly business performance workflow contract tests passed.'
