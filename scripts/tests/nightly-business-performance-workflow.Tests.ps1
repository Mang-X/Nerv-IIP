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
$ciWorkflowPath = Join-Path $repoRoot '.github/workflows/ci.yml'
$verifierPath = Join-Path $repoRoot 'scripts/verify-business-performance-baseline.ps1'
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')

function Assert-WorkflowContract {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    if (-not $Condition) { throw $Message }
}

function Test-OrdinalStringMember {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Values,
        [Parameter(Mandatory)] [string] $Expected
    )

    return @($Values | Where-Object {
            [string]::Equals($_, $Expected, [StringComparison]::Ordinal)
        }).Count -gt 0
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

    $rubyProgram = @'
path = ARGV.fetch(0)
source = File.read(path)
stream = Psych.parse_stream(source)
raise 'Workflow YAML must contain exactly one document.' unless stream.children.length == 1
root = stream.children.fetch(0).root
raise 'Workflow YAML root must be a mapping.' unless root.is_a?(Psych::Nodes::Mapping)

def converted_key_identity(key)
  converted = Psych::Visitors::ToRuby.create.accept(key)
  "#{converted.class.name}:#{converted.inspect}"
end

def validate_mapping_keys(node, path)
  case node
  when Psych::Nodes::Mapping
    seen = {}
    node.children.each_slice(2) do |key, value|
      raise "Workflow YAML mapping key at #{path} must be a scalar." unless key.is_a?(Psych::Nodes::Scalar)
      identity = converted_key_identity(key)
      if seen.key?(identity)
        raise "Duplicate converted mapping key #{identity} at #{path}: '#{seen.fetch(identity)}' conflicts with '#{key.value}'."
      end
      seen[identity] = key.value.to_s
      validate_mapping_keys(value, "#{path}.#{key.value}")
    end
  when Psych::Nodes::Sequence
    node.children.each_with_index { |child, index| validate_mapping_keys(child, "#{path}[#{index}]") }
  end
end

validate_mapping_keys(root, '$')
root_keys = root.children.each_slice(2).map { |key, _| key.value.to_s }
raise "Workflow YAML root mapping key literal 'on' must appear exactly once." unless root_keys.count('on') == 1
raise "Workflow YAML root mapping key literal 'true' cannot substitute for 'on'." if root_keys.include?('true')

workflow = YAML.safe_load(source, aliases: false)
if workflow.key?('on')
  # A quoted root key remains the string 'on' under YAML 1.1.
elsif workflow.key?(true)
  workflow['on'] = workflow.delete(true)
else
  raise "Workflow YAML trigger key did not survive parsing as literal 'on'."
end
puts JSON.generate(workflow)
'@
    $parsed = Invoke-NativeCommandOutput `
        -Command 'ruby' `
        -Arguments @('-ryaml', '-rjson', '-e', $rubyProgram, $Path) `
        -WorkingDirectory $repoRoot `
        -Name 'parse-nightly-business-performance-workflow'
    return ($parsed.Stdout | ConvertFrom-Json -ErrorAction Stop)
}

function Get-NightlyBusinessPerformanceRunBlock {
    param([Parameter(Mandatory)] [string] $Path)

    $workflow = Get-NightlyBusinessPerformanceWorkflow -Path $Path
    $jobs = Get-WorkflowProperty -Object $workflow -Name 'jobs'
    $job = Get-WorkflowProperty -Object $jobs -Name 'business-performance'
    $steps = @(Get-WorkflowProperty -Object $job -Name 'steps')
    $performanceSteps = @($steps | Where-Object {
            [string]::Equals([string](Get-WorkflowProperty -Object $_ -Name 'name'), 'Run performance baseline', [StringComparison]::Ordinal)
        })
    Assert-WorkflowContract ($performanceSteps.Count -eq 1) 'Nightly business performance workflow must contain exactly one named performance run step.'
    return [string](Get-WorkflowProperty -Object $performanceSteps[0] -Name 'run')
}

function Invoke-NightlyBusinessPerformanceRunFixture {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $ManualThreshold
    )

    $runBlock = Get-NightlyBusinessPerformanceRunBlock -Path $Path
    $verifierTarget = './scripts/verify-business-performance-baseline.ps1'
    Assert-WorkflowContract ([regex]::Matches($runBlock, [regex]::Escape($verifierTarget)).Count -eq 1) 'Performance run fixture must replace exactly one governed verifier target.'

    $tokens = $null
    $parseErrors = $null
    $verifierAst = [Management.Automation.Language.Parser]::ParseFile($verifierPath, [ref]$tokens, [ref]$parseErrors)
    Assert-WorkflowContract ($parseErrors.Count -eq 0 -and $null -ne $verifierAst.ParamBlock) 'The production business performance verifier must expose one parseable parameter block.'

    $fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-nightly-verifier-binding-$([Guid]::NewGuid().ToString('N'))"
    $fakeVerifierPath = Join-Path $fixtureRoot 'fake-business-performance-verifier.ps1'
    $fakeVerifier = @"
[CmdletBinding()]
$($verifierAst.ParamBlock.Extent.Text)

[pscustomobject] ([hashtable] `$PSBoundParameters)
"@
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    [IO.File]::WriteAllText($fakeVerifierPath, $fakeVerifier, [Text.UTF8Encoding]::new($false))

    $instrumentedRunBlock = $runBlock.Replace($verifierTarget, '$nightlyFakeVerifierPath')
    $nightlyFakeVerifierPath = $fakeVerifierPath
    $invocations = @()
    $failure = $null

    try {
        try {
            $invocations = @(Invoke-WithScopedEnvironment -Variables @{
                    MANUAL_MAX_ELAPSED_MILLISECONDS = $ManualThreshold
                } -ScriptBlock {
                    & ([scriptblock]::Create($instrumentedRunBlock))
                })
        }
        catch {
            $failure = $_
        }
    }
    finally {
        if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
    }

    return [pscustomobject]@{
        Failure = $failure
        InvocationCount = $invocations.Count
        BoundParameters = if ($invocations.Count -eq 1) { $invocations[0] } else { $null }
    }
}

function Assert-NightlyBoundParameter {
    param(
        [Parameter(Mandatory)] [object] $BoundParameters,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [object] $ExpectedValue,
        [Parameter(Mandatory)] [type] $ExpectedType
    )

    $property = $BoundParameters.PSObject.Properties[$Name]
    Assert-WorkflowContract ($null -ne $property) "The fake verifier binder must receive named parameter '$Name'."
    Assert-WorkflowContract ($property.Value.GetType() -eq $ExpectedType) "Bound parameter '$Name' must have runtime type '$($ExpectedType.FullName)'. Actual: $($property.Value.GetType().FullName)"
    Assert-WorkflowContract ([object]::Equals($property.Value, $ExpectedValue)) "Bound parameter '$Name' must equal '$ExpectedValue'. Actual: $($property.Value)"
}

function Test-NightlyBusinessPerformanceInvalidManualThresholds {
    foreach ($invalidThreshold in @('-1', 'abc')) {
        $result = Invoke-NightlyBusinessPerformanceRunFixture -Path $workflowPath -ManualThreshold $invalidThreshold
        Assert-WorkflowContract ($null -ne $result.Failure) "Manual threshold '$invalidThreshold' must fail before invoking the verifier."
        Assert-WorkflowContract ($result.Failure.Exception.Message.Contains('must be an invariant integer greater than or equal to 0', [StringComparison]::Ordinal)) "Manual threshold '$invalidThreshold' must produce the governed deterministic diagnostic. Actual: $($result.Failure.Exception.Message)"
        Assert-WorkflowContract ($result.InvocationCount -eq 0) "Manual threshold '$invalidThreshold' must not invoke the verifier."
    }

    Write-Host 'Invalid manual threshold behavior tests passed.'
}

function Test-NightlyBusinessPerformanceManualThresholdModes {
    foreach ($testCase in @(
            @{
                Input = '0'
                ExpectedThresholds = @{
                    InventoryMaxElapsedMilliseconds = 30000
                    MesMaxElapsedMilliseconds = 30000
                    ErpMaxElapsedMilliseconds = 30000
                }
                AbsentThresholds = @('MaxElapsedMilliseconds')
            },
            @{
                Input = '1'
                ExpectedThresholds = @{ MaxElapsedMilliseconds = 1 }
                AbsentThresholds = @('InventoryMaxElapsedMilliseconds', 'MesMaxElapsedMilliseconds', 'ErpMaxElapsedMilliseconds')
            }
        )) {
        $result = Invoke-NightlyBusinessPerformanceRunFixture -Path $workflowPath -ManualThreshold $testCase.Input
        Assert-WorkflowContract ($null -eq $result.Failure) "Manual threshold '$($testCase.Input)' must execute its governed threshold mode. Actual failure: $($result.Failure)"
        Assert-WorkflowContract ($result.InvocationCount -eq 1) "Manual threshold '$($testCase.Input)' must invoke the fake verifier exactly once through the real PowerShell binder."

        Assert-NightlyBoundParameter -BoundParameters $result.BoundParameters -Name 'Scenario' -ExpectedValue 'all' -ExpectedType ([string])
        Assert-NightlyBoundParameter -BoundParameters $result.BoundParameters -Name 'Profile' -ExpectedValue 'nightly' -ExpectedType ([string])
        Assert-NightlyBoundParameter -BoundParameters $result.BoundParameters -Name 'Rows' -ExpectedValue 25 -ExpectedType ([int])
        Assert-NightlyBoundParameter -BoundParameters $result.BoundParameters -Name 'MetricsOutputPath' -ExpectedValue 'artifacts/business-performance/nightly/metrics.jsonl' -ExpectedType ([string])
        Assert-NightlyBoundParameter -BoundParameters $result.BoundParameters -Name 'SummaryOutputPath' -ExpectedValue 'artifacts/business-performance/nightly/summary.json' -ExpectedType ([string])

        foreach ($expectedThreshold in $testCase.ExpectedThresholds.GetEnumerator()) {
            Assert-NightlyBoundParameter -BoundParameters $result.BoundParameters -Name $expectedThreshold.Key -ExpectedValue $expectedThreshold.Value -ExpectedType ([int])
        }
        foreach ($absentThreshold in $testCase.AbsentThresholds) {
            Assert-WorkflowContract ($null -eq $result.BoundParameters.PSObject.Properties[$absentThreshold]) "Manual threshold '$($testCase.Input)' must not bind '$absentThreshold'."
        }
        foreach ($absentCommonParameter in @('ConnectionString', 'SkipRestore')) {
            Assert-WorkflowContract ($null -eq $result.BoundParameters.PSObject.Properties[$absentCommonParameter]) "Nightly workflow must not bind optional verifier parameter '$absentCommonParameter'."
        }
        $expectedBindingCount = 5 + $testCase.ExpectedThresholds.Count
        Assert-WorkflowContract (@($result.BoundParameters.PSObject.Properties).Count -eq $expectedBindingCount) "Manual threshold '$($testCase.Input)' must bind exactly $expectedBindingCount governed parameters."
    }

    Write-Host 'Manual threshold mode real-binder behavior tests passed.'
}

function Test-NightlyBusinessPerformancePositionalArrayRegression {
    $fixturePath = Join-Path ([IO.Path]::GetTempPath()) "nerv-nightly-business-performance-positional-$([Guid]::NewGuid().ToString('N')).yml"
    $workflowText = [IO.File]::ReadAllText($workflowPath)
    $namedCommonArguments = @'
          $commonArguments = @{
              Scenario = 'all'
              Profile = 'nightly'
              Rows = 25
              MetricsOutputPath = 'artifacts/business-performance/nightly/metrics.jsonl'
              SummaryOutputPath = 'artifacts/business-performance/nightly/summary.json'
          }
'@
    $positionalCommonArguments = @'
          $commonArguments = @(
              '-Scenario', 'all',
              '-Profile', 'nightly',
              '-Rows', 25,
              '-MetricsOutputPath', 'artifacts/business-performance/nightly/metrics.jsonl',
              '-SummaryOutputPath', 'artifacts/business-performance/nightly/summary.json'
          )
'@
    Assert-WorkflowContract ($workflowText.Contains($namedCommonArguments, [StringComparison]::Ordinal)) 'Positional-array regression fixture must match the canonical named common arguments.'

    try {
        [IO.File]::WriteAllText($fixturePath, $workflowText.Replace($namedCommonArguments, $positionalCommonArguments), [Text.UTF8Encoding]::new($false))
        $result = Invoke-NightlyBusinessPerformanceRunFixture -Path $fixturePath -ManualThreshold '0'
        Assert-WorkflowContract ($null -ne $result.Failure) 'Restoring positional array splatting must fail through the fake verifier real parameter binder.'
        Assert-WorkflowContract ($result.Failure.Exception.Message.Contains("parameter 'Rows'", [StringComparison]::Ordinal) -and $result.Failure.Exception.Message.Contains('nightly', [StringComparison]::Ordinal)) "Positional array splatting must reproduce the hosted Rows/nightly binding failure. Actual: $($result.Failure.Exception.Message)"
        Assert-WorkflowContract ($result.InvocationCount -eq 0) 'A binder failure must prevent the fake verifier body from running.'
        Write-Host 'Historical positional-array binder regression test passed.'
    }
    finally {
        if (Test-Path -LiteralPath $fixturePath) { Remove-Item -LiteralPath $fixturePath -Force }
    }
}

function Assert-NightlyBusinessPerformanceWorkflow {
    param([Parameter(Mandatory)] [string] $Path)

    $workflow = Get-NightlyBusinessPerformanceWorkflow -Path $Path
    $triggers = Get-WorkflowProperty -Object $workflow -Name 'on'
    Assert-WorkflowContract ($null -ne $triggers) 'Nightly business performance workflow must declare triggers.'
    $triggerNames = @($triggers.PSObject.Properties.Name)
    Assert-WorkflowContract ($triggerNames.Count -eq 2 -and (Test-OrdinalStringMember -Values $triggerNames -Expected 'schedule') -and (Test-OrdinalStringMember -Values $triggerNames -Expected 'workflow_dispatch')) 'Nightly business performance workflow must have only schedule and workflow_dispatch triggers.'
    $schedule = @(Get-WorkflowProperty -Object $triggers -Name 'schedule')
    Assert-WorkflowContract ($schedule.Count -eq 1 -and [string]::Equals([string](Get-WorkflowProperty -Object $schedule[0] -Name 'cron'), '0 17 * * *', [StringComparison]::Ordinal)) 'Nightly business performance workflow must schedule exactly 0 17 * * *.'
    $dispatch = Get-WorkflowProperty -Object $triggers -Name 'workflow_dispatch'
    $inputs = Get-WorkflowProperty -Object $dispatch -Name 'inputs'
    $manualThreshold = Get-WorkflowProperty -Object $inputs -Name 'max_elapsed_milliseconds'
    Assert-WorkflowContract ($null -ne $manualThreshold -and [string]::Equals([string](Get-WorkflowProperty -Object $manualThreshold -Name 'type'), 'string', [StringComparison]::Ordinal) -and [string]::Equals([string](Get-WorkflowProperty -Object $manualThreshold -Name 'default'), '0', [StringComparison]::Ordinal)) 'workflow_dispatch must expose max_elapsed_milliseconds as string default 0.'

    $permissions = Get-WorkflowProperty -Object $workflow -Name 'permissions'
    Assert-WorkflowContract ($null -ne $permissions -and @($permissions.PSObject.Properties).Count -eq 1 -and [string]::Equals([string](Get-WorkflowProperty -Object $permissions -Name 'contents'), 'read', [StringComparison]::Ordinal)) 'Nightly business performance workflow permissions must be contents: read only.'

    $jobs = Get-WorkflowProperty -Object $workflow -Name 'jobs'
    Assert-WorkflowContract ($null -ne $jobs -and @($jobs.PSObject.Properties).Count -eq 1 -and $null -ne $jobs.PSObject.Properties['business-performance']) 'Nightly business performance workflow must contain only the business-performance job.'
    $job = $jobs.PSObject.Properties['business-performance'].Value
    Assert-WorkflowContract ([int](Get-WorkflowProperty -Object $job -Name 'timeout-minutes') -eq 45) 'business-performance job timeout-minutes must be 45.'
    Assert-WorkflowContract ([string]::Equals([string](Get-WorkflowProperty -Object $job -Name 'runs-on'), 'ubuntu-latest', [StringComparison]::Ordinal)) 'business-performance job must run on ubuntu-latest.'
    $jobEnvironment = Get-WorkflowProperty -Object $job -Name 'env'
    $connectionString = Get-WorkflowProperty -Object $jobEnvironment -Name 'NERV_IIP_PERF_POSTGRES'
    Assert-WorkflowContract ($null -ne $connectionString -and [string]$connectionString -match '^Host=127\.0\.0\.1;Port=5432;Database=nerv_iip_performance;Username=nerv;Password=nerv$') 'business-performance job must inject NERV_IIP_PERF_POSTGRES for the PostgreSQL service.'
    $manualThresholdBinding = [string](Get-WorkflowProperty -Object $jobEnvironment -Name 'MANUAL_MAX_ELAPSED_MILLISECONDS')
    Assert-WorkflowContract ([string]::Equals($manualThresholdBinding, '${{ github.event.inputs.max_elapsed_milliseconds || ''0'' }}', [StringComparison]::Ordinal)) 'business-performance job must bind MANUAL_MAX_ELAPSED_MILLISECONDS exactly to github.event.inputs.max_elapsed_milliseconds with fallback 0.'

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
    $performanceCode = @($performanceRun -split "`r?`n" | ForEach-Object { $_.TrimEnd() } | Where-Object { $_ -notmatch '^\s*(#|$)' }) -join "`n"
    $expectedPerformanceCode = @'
$commonArguments = @{
    Scenario = 'all'
    Profile = 'nightly'
    Rows = 25
    MetricsOutputPath = 'artifacts/business-performance/nightly/metrics.jsonl'
    SummaryOutputPath = 'artifacts/business-performance/nightly/summary.json'
}
$manualMaxElapsedMilliseconds = 0
$manualInputParsed = [int]::TryParse(
    [string]$env:MANUAL_MAX_ELAPSED_MILLISECONDS,
    [Globalization.NumberStyles]::Integer,
    [Globalization.CultureInfo]::InvariantCulture,
    [ref]$manualMaxElapsedMilliseconds
)
if (-not $manualInputParsed -or $manualMaxElapsedMilliseconds -lt 0) {
    throw 'workflow_dispatch max_elapsed_milliseconds must be an invariant integer greater than or equal to 0.'
}
if ($manualMaxElapsedMilliseconds -gt 0) {
    $thresholdArguments = @{
        MaxElapsedMilliseconds = $manualMaxElapsedMilliseconds
    }
}
else {
    $thresholdArguments = @{
        InventoryMaxElapsedMilliseconds = 30000
        MesMaxElapsedMilliseconds = 30000
        ErpMaxElapsedMilliseconds = 30000
    }
}
& ./scripts/verify-business-performance-baseline.ps1 @commonArguments @thresholdArguments
'@.Trim()
    Assert-WorkflowContract ([string]::Equals($performanceCode.Trim(), $expectedPerformanceCode, [StringComparison]::Ordinal)) 'Governed performance step must use the canonical comment-free PowerShell threshold split: manual runs pass only -MaxElapsedMilliseconds; scheduled runs pass only the three nonzero per-scenario thresholds.'
    Assert-WorkflowContract ($performanceCode -notmatch '(^|\s)(dotnet|docker)(\s|$)' -and $performanceCode -notmatch '\|\|\s*true') 'Governed performance step must not bypass the governed script or swallow failure.'

    Assert-WorkflowContract ([string]::Equals([string](Get-WorkflowProperty -Object $artifact[0] -Name 'if'), 'always()', [StringComparison]::Ordinal)) 'Nightly business performance artifact upload must use if: always().'
    $artifactWith = Get-WorkflowProperty -Object $artifact[0] -Name 'with'
    Assert-WorkflowContract ([string]::Equals([string](Get-WorkflowProperty -Object $artifactWith -Name 'name'), 'business-performance-${{ github.run_id }}-${{ github.run_attempt }}', [StringComparison]::Ordinal)) 'Nightly business performance artifact name must include run and attempt.'
    Assert-WorkflowContract ([string]::Equals([string](Get-WorkflowProperty -Object $artifactWith -Name 'if-no-files-found'), 'error', [StringComparison]::Ordinal)) 'Nightly business performance artifact upload must fail on missing files.'
    Assert-WorkflowContract ([int](Get-WorkflowProperty -Object $artifactWith -Name 'retention-days') -eq 30) 'Nightly business performance artifact retention must be 30 days.'
    $artifactPaths = @([string](Get-WorkflowProperty -Object $artifactWith -Name 'path') -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 })
    Assert-WorkflowContract ($artifactPaths.Count -eq 2 -and (Test-OrdinalStringMember -Values $artifactPaths -Expected 'artifacts/business-performance/nightly/metrics.jsonl') -and (Test-OrdinalStringMember -Values $artifactPaths -Expected 'artifacts/business-performance/nightly/summary.json')) 'Nightly business performance artifact must allowlist only metrics.jsonl and summary.json.'

    $serializedWorkflow = $workflow | ConvertTo-Json -Depth 100 -Compress
    Assert-WorkflowContract ($serializedWorkflow -notmatch 'continue-on-error') 'Nightly business performance workflow must not use continue-on-error.'
    Assert-WorkflowContract ($serializedWorkflow -notmatch '\|\|\s*true') 'Nightly business performance workflow must not use || true.'
}

function Test-NightlyBusinessPerformanceManualThresholdExclusivity {
    $fixturePath = Join-Path ([IO.Path]::GetTempPath()) "nerv-nightly-business-performance-fixture-$([Guid]::NewGuid().ToString('N')).yml"
    $fixture = @'
name: Nightly Business Performance
on:
  schedule:
    - cron: '0 17 * * *'
  workflow_dispatch:
    inputs:
      max_elapsed_milliseconds:
        type: string
        default: '0'
permissions:
  contents: read
jobs:
  business-performance:
    timeout-minutes: 45
    runs-on: ubuntu-latest
    env:
      NERV_IIP_PERF_POSTGRES: Host=127.0.0.1;Port=5432;Database=nerv_iip_performance;Username=nerv;Password=nerv
      MANUAL_MAX_ELAPSED_MILLISECONDS: ${{ github.event.inputs.max_elapsed_milliseconds || '0' }}
    services:
      postgres:
        image: postgres:18
        env:
          POSTGRES_USER: nerv
          POSTGRES_PASSWORD: nerv
          POSTGRES_DB: nerv_iip_performance
        ports:
          - 5432:5432
        options: >-
          --health-cmd "pg_isready -U nerv -d nerv_iip_performance"
    steps:
      - uses: actions/checkout@v4
        timeout-minutes: 3
      - uses: actions/setup-dotnet@v4
        timeout-minutes: 5
        with:
          dotnet-version: 10.0.x
      - uses: actions/cache@v4
        timeout-minutes: 8
      - name: Run performance baseline
        timeout-minutes: 20
        shell: pwsh
        run: |
          $commonArguments = @{
              Scenario = 'all'
              Profile = 'nightly'
              Rows = 25
              MetricsOutputPath = 'artifacts/business-performance/nightly/metrics.jsonl'
              SummaryOutputPath = 'artifacts/business-performance/nightly/summary.json'
          }
          $manualMaxElapsedMilliseconds = 0
          $manualInputParsed = [int]::TryParse(
              [string]$env:MANUAL_MAX_ELAPSED_MILLISECONDS,
              [Globalization.NumberStyles]::Integer,
              [Globalization.CultureInfo]::InvariantCulture,
              [ref]$manualMaxElapsedMilliseconds
          )
          if (-not $manualInputParsed -or $manualMaxElapsedMilliseconds -lt 0) {
              throw 'workflow_dispatch max_elapsed_milliseconds must be an invariant integer greater than or equal to 0.'
          }
          if ($manualMaxElapsedMilliseconds -gt 0) {
              $thresholdArguments = @{
                  MaxElapsedMilliseconds = $manualMaxElapsedMilliseconds
              }
          }
          else {
              $thresholdArguments = @{
                  InventoryMaxElapsedMilliseconds = 30000
                  MesMaxElapsedMilliseconds = 30000
                  ErpMaxElapsedMilliseconds = 30000
              }
          }
          & ./scripts/verify-business-performance-baseline.ps1 @commonArguments @thresholdArguments
      - uses: actions/upload-artifact@v4
        timeout-minutes: 5
        if: always()
        with:
          name: business-performance-${{ github.run_id }}-${{ github.run_attempt }}
          path: |
            artifacts/business-performance/nightly/metrics.jsonl
            artifacts/business-performance/nightly/summary.json
          if-no-files-found: error
          retention-days: 30
'@
    try {
        [IO.File]::WriteAllText($fixturePath, $fixture, [Text.UTF8Encoding]::new($false))
        Assert-NightlyBusinessPerformanceWorkflow -Path $fixturePath
        $manualOnly = '                  MaxElapsedMilliseconds = $manualMaxElapsedMilliseconds'
        $manualWithScheduledThreshold = "                  MaxElapsedMilliseconds = `$manualMaxElapsedMilliseconds`n                  InventoryMaxElapsedMilliseconds = 30000"
        Assert-WorkflowContract ($fixture.Contains($manualOnly, [StringComparison]::Ordinal)) 'Manual-threshold exclusivity fixture mutation must match the canonical manual branch.'
        [IO.File]::WriteAllText($fixturePath, $fixture.Replace($manualOnly, $manualWithScheduledThreshold), [Text.UTF8Encoding]::new($false))
        $failure = $null
        try { Assert-NightlyBusinessPerformanceWorkflow -Path $fixturePath }
        catch { $failure = $_ }
        Assert-WorkflowContract ($null -ne $failure -and $failure.Exception.Message.Contains('manual runs pass only -MaxElapsedMilliseconds', [StringComparison]::Ordinal)) 'A manual threshold branch that also passes a scheduled threshold must fail the canonical branch contract.'
        Write-Host 'Manual threshold exclusivity synthetic fixture mutation passed.'
    }
    finally {
        if (Test-Path -LiteralPath $fixturePath) { Remove-Item -LiteralPath $fixturePath -Force }
    }
}

Test-NightlyBusinessPerformanceInvalidManualThresholds
Test-NightlyBusinessPerformanceManualThresholdModes
Test-NightlyBusinessPerformancePositionalArrayRegression
Test-NightlyBusinessPerformanceManualThresholdExclusivity
Assert-NightlyBusinessPerformanceWorkflow -Path $workflowPath

$workflowText = Get-Content -LiteralPath $workflowPath -Raw
$workflowNewline = if ($workflowText.Contains("`r`n", [StringComparison]::Ordinal)) { "`r`n" } else { "`n" }
$mutationRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-nightly-business-performance-workflow-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($mutationRoot) | Out-Null
    $quotedTriggerPath = Join-Path $mutationRoot 'quoted-root-trigger.yml'
    $quotedTriggerText = $workflowText.Replace("on:$workflowNewline", "`"on`":$workflowNewline")
    Assert-WorkflowContract (-not [string]::Equals($quotedTriggerText, $workflowText, [StringComparison]::Ordinal)) 'Quoted root trigger fixture must replace the canonical literal on key.'
    [IO.File]::WriteAllText($quotedTriggerPath, $quotedTriggerText, [Text.UTF8Encoding]::new($false))
    Assert-NightlyBusinessPerformanceWorkflow -Path $quotedTriggerPath

    foreach ($mutation in @(
            @{ Name = 'trigger-true-substitute'; Original = "on:$workflowNewline  schedule:"; Replacement = "true:$workflowNewline  schedule:"; ExpectedMessage = "root mapping key literal 'on' must appear exactly once" },
            @{ Name = 'trigger-yaml11-alias-collision'; Original = "on:$workflowNewline  schedule:"; Replacement = "on: {}$workflowNewline$workflowNewline" + "yes:$workflowNewline  schedule:"; ExpectedMessage = "Duplicate converted mapping key" },
            @{ Name = 'duplicate-root-trigger'; Original = "on:$workflowNewline  schedule:"; Replacement = "on: {}$workflowNewline$workflowNewline" + "on:$workflowNewline  schedule:"; ExpectedMessage = "Duplicate converted mapping key TrueClass:true at `$" },
            @{ Name = 'duplicate-job-timeout'; Original = "    timeout-minutes: 45$workflowNewline    runs-on:"; Replacement = "    timeout-minutes: 45$workflowNewline    timeout-minutes: 45$workflowNewline    runs-on:"; ExpectedMessage = "Duplicate converted mapping key String:`"timeout-minutes`" at `$.jobs.business-performance" },
            @{ Name = 'connection-string'; Original = 'NERV_IIP_PERF_POSTGRES:'; Replacement = 'NERV_IIP_PERF_POSTGRES_REMOVED:' },
            @{ Name = 'manual-threshold-binding-deleted'; Original = "      MANUAL_MAX_ELAPSED_MILLISECONDS: `${{ github.event.inputs.max_elapsed_milliseconds || '0' }}$workflowNewline"; Replacement = '' },
            @{ Name = 'manual-threshold-binding-fixed-zero'; Original = "MANUAL_MAX_ELAPSED_MILLISECONDS: `${{ github.event.inputs.max_elapsed_milliseconds || '0' }}"; Replacement = "MANUAL_MAX_ELAPSED_MILLISECONDS: '0'" },
            @{ Name = 'manual-threshold-binding-wrong-input'; Original = 'github.event.inputs.max_elapsed_milliseconds'; Replacement = 'github.event.inputs.wrong_max_elapsed_milliseconds' },
            @{ Name = 'scheduled-threshold'; Original = 'InventoryMaxElapsedMilliseconds = 30000'; Replacement = 'InventoryMaxElapsedMilliseconds = 0' },
            @{ Name = 'artifact-always'; Original = 'if: always()'; Replacement = 'if: success()' },
            @{ Name = 'artifact-missing-files'; Original = 'if-no-files-found: error'; Replacement = 'if-no-files-found: warn' },
            @{ Name = 'continue-on-error'; Original = 'timeout-minutes: 20'; Replacement = "timeout-minutes: 20$([Environment]::NewLine)        continue-on-error: true" },
            @{ Name = 'masked-performance-failure'; Original = '& ./scripts/verify-business-performance-baseline.ps1 @commonArguments @thresholdArguments'; Replacement = '& ./scripts/verify-business-performance-baseline.ps1 @commonArguments @thresholdArguments || true' }
        )) {
        Assert-WorkflowContract ($workflowText.Contains($mutation.Original, [StringComparison]::Ordinal)) "Mutation '$($mutation.Name)' must match its canonical workflow text."
        $mutatedPath = Join-Path $mutationRoot "$($mutation.Name).yml"
        [IO.File]::WriteAllText($mutatedPath, $workflowText.Replace($mutation.Original, $mutation.Replacement), [Text.UTF8Encoding]::new($false))
        $failure = $null
        try { Assert-NightlyBusinessPerformanceWorkflow -Path $mutatedPath }
        catch { $failure = $_ }
        Assert-WorkflowContract ($null -ne $failure) "Mutation '$($mutation.Name)' must fail the nightly business performance workflow contract."
        if ($mutation.ContainsKey('ExpectedMessage')) {
            Assert-WorkflowContract ($failure.Exception.Message.Contains([string]$mutation.ExpectedMessage, [StringComparison]::Ordinal)) "Mutation '$($mutation.Name)' must produce deterministic diagnostic '$($mutation.ExpectedMessage)'. Actual: $($failure.Exception.Message)"
        }
    }
}
finally {
    if (Test-Path -LiteralPath $mutationRoot) { Remove-Item -LiteralPath $mutationRoot -Recurse -Force }
}

$ciWorkflowText = [IO.File]::ReadAllText($ciWorkflowPath)
$completenessTestInvocation = 'run: ./scripts/tests/business-performance-metrics-completeness.Tests.ps1'
Assert-WorkflowContract ([regex]::Matches($ciWorkflowText, [regex]::Escape($completenessTestInvocation)).Count -eq 1) 'Script Governance CI must execute the business performance metric completeness contract exactly once.'

Write-Host 'Nightly business performance workflow contract tests passed.'
