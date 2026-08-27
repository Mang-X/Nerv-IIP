# Script-Governance:
#   Category: check
#   SideEffects:
#     - Parses the governed CI workflow and BusinessGateway restore manifest
#     - Executes dotnet --version to verify the current process toolchain
#   Writes:
#     - Temporary command logs under artifacts/script-logs/**
#   Cleanup:
#     - Leaves diagnostic command logs for investigation
#   Requires:
#     - PowerShell 7
#     - Ruby 3.4 with yaml/json standard libraries
#     - .NET SDK selected for the current CI job

[CmdletBinding()]
param(
    [string] $WorkflowPath,
    [string] $ManifestPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/CiRequiredSummary.ps1')

if ([string]::IsNullOrWhiteSpace($WorkflowPath)) {
    $WorkflowPath = Join-Path $repoRoot '.github/workflows/ci.yml'
}
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $repoRoot 'docs/architecture/business-gateway-api-surface-restore.manifest.json'
}

$managedJobNames = @(
    'backend-tests-business-gateway'
    'backend-tests-platform'
    'backend-tests-business-core-a'
    'backend-tests-business-core-b'
    'postgres-provider-tests'
    'redis-cap-transport-tests'
    'acceptance-scenario-matrix-planning'
    'acceptance-scenario-matrix-runtime'
    'business-full-chain-acceptance-v1'
    'connector-host-tests'
    'openapi-client-drift'
    'script-governance'
)
$script:ciDotNetSdkWorkflowEvidenceCache = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)

function Get-ExactJsonProperties {
    param(
        [Parameter(Mandatory)] [System.Text.Json.JsonElement] $Object,
        [Parameter(Mandatory)] [string] $Name
    )

    if ($Object.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) { return @() }
    return @($Object.EnumerateObject() | Where-Object { [string]::Equals($_.Name, $Name, [StringComparison]::Ordinal) })
}

function Get-ManifestSdkAuthority {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [Collections.Generic.List[string]] $Findings
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        $Findings.Add('manifest-missing')
        return ''
    }

    $document = $null
    try {
        $document = [System.Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($Path))
    }
    catch {
        $Findings.Add('manifest-json-invalid')
        return ''
    }

    try {
        $root = $document.RootElement
        if ($root.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
            $Findings.Add('manifest-root-not-object')
            return ''
        }

        $projectProperties = @(Get-ExactJsonProperties -Object $root -Name 'project')
        if ($projectProperties.Count -ne 1 -or
            $projectProperties[0].Value.ValueKind -ne [System.Text.Json.JsonValueKind]::String -or
            -not [string]::Equals(
                $projectProperties[0].Value.GetString(),
                'backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Nerv.IIP.BusinessGateway.Web.csproj',
                [StringComparison]::Ordinal)) {
            $Findings.Add('manifest-project-owner-invalid')
        }

        $toolchainProperties = @(Get-ExactJsonProperties -Object $root -Name 'toolchain')
        if ($toolchainProperties.Count -ne 1) {
            $Findings.Add("manifest-toolchain-count:$($toolchainProperties.Count)")
            return ''
        }

        $sdkProperties = @(Get-ExactJsonProperties -Object $toolchainProperties[0].Value -Name 'sdk')
        if ($sdkProperties.Count -ne 1) {
            $Findings.Add("manifest-toolchain-sdk-count:$($sdkProperties.Count)")
            return ''
        }
        if ($sdkProperties[0].Value.ValueKind -ne [System.Text.Json.JsonValueKind]::String) {
            $Findings.Add('manifest-toolchain-sdk-not-string')
            return ''
        }

        $sdk = [string] $sdkProperties[0].Value.GetString()
        if ($sdk -cnotmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
            $Findings.Add("manifest-toolchain-sdk-not-exact:$sdk")
            return ''
        }
        return $sdk
    }
    finally {
        $document.Dispose()
    }
}

function Get-CiDotNetSdkYamlKeyFindings {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string[]] $ExpectedJobNames
    )

    $rubyProgram = @'
require 'psych'
require 'json'

def mapping_pairs(node)
  return [] unless node.is_a?(Psych::Nodes::Mapping)
  node.children.each_slice(2).to_a
end

def mapping_values(node, name)
  mapping_pairs(node).each_with_object([]) do |(key, value), values|
    values << value if key.is_a?(Psych::Nodes::Scalar) && key.value == name
  end
end

path = ARGV.shift
expected_jobs = ARGV
root = Psych.parse_file(path).root
jobs = mapping_values(root, 'jobs').first
findings = []

expected_jobs.each do |job_name|
  job = mapping_values(jobs, job_name).first
  steps = mapping_values(job, 'steps').first
  next unless steps.is_a?(Psych::Nodes::Sequence)

  steps.children.each do |step|
    uses_setup_dotnet = mapping_values(step, 'uses').any? do |uses|
      uses.is_a?(Psych::Nodes::Scalar) && uses.value.start_with?('actions/setup-dotnet@')
    end
    next unless uses_setup_dotnet

    key_count = mapping_values(step, 'with').sum do |with_node|
      mapping_pairs(with_node).count do |key, _value|
        key.is_a?(Psych::Nodes::Scalar) && key.value == 'dotnet-version'
      end
    end
    findings << "ci-dotnet-sdk-key-count:#{job_name}:#{key_count}" unless key_count == 1
  end
end

puts JSON.generate(findings)
'@
    $arguments = @('-rpsych', '-rjson', '-e', $rubyProgram, $Path) + $ExpectedJobNames
    $result = Invoke-NativeCommandOutput `
        -Command 'ruby' `
        -Arguments $arguments `
        -WorkingDirectory $repoRoot `
        -Name 'verify-ci-dotnet-sdk-authority-yaml-keys'
    return @($result.Stdout | ConvertFrom-Json -ErrorAction Stop)
}

function Get-CiDotNetSdkWorkflowContentIdentity {
    param(
        [Parameter(Mandatory)] [string] $Path
    )

    $resolvedPath = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    $contentHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($resolvedPath))).ToLowerInvariant()
    return [pscustomobject]@{
        ResolvedPath = $resolvedPath
        ContentSha256 = $contentHash
        CacheKey = "$resolvedPath|$contentHash"
    }
}

function Get-CiDotNetSdkWorkflowEvidence {
    param(
        [Parameter(Mandatory)] [string] $Path
    )

    $identityBeforeParse = Get-CiDotNetSdkWorkflowContentIdentity -Path $Path
    $cachedEvidence = $null
    if ($script:ciDotNetSdkWorkflowEvidenceCache.TryGetValue($identityBeforeParse.CacheKey, [ref] $cachedEvidence)) {
        return $cachedEvidence
    }

    $yamlKeyFindings = @(Get-CiDotNetSdkYamlKeyFindings -Path $identityBeforeParse.ResolvedPath -ExpectedJobNames $managedJobNames)
    $workflow = ConvertFrom-NervCiRequiredSummaryWorkflow -Path $identityBeforeParse.ResolvedPath -WorkingDirectory $repoRoot
    $identityAfterParse = Get-CiDotNetSdkWorkflowContentIdentity -Path $identityBeforeParse.ResolvedPath
    if (-not [string]::Equals($identityAfterParse.CacheKey, $identityBeforeParse.CacheKey, [StringComparison]::Ordinal)) {
        throw "CI workflow changed while authority evidence was being collected: $($identityBeforeParse.ResolvedPath)"
    }

    $evidence = [pscustomobject]@{
        SourcePath = $identityBeforeParse.ResolvedPath
        ContentSha256 = $identityBeforeParse.ContentSha256
        YamlKeyFindings = $yamlKeyFindings
        Workflow = $workflow
    }
    $script:ciDotNetSdkWorkflowEvidenceCache[$identityBeforeParse.CacheKey] = $evidence
    return $evidence
}

function New-CiDotNetSdkAuthorityResult {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [Collections.Generic.List[string]] $Findings,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $ManifestSdk,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $ActualSdk
    )

    $findingValues = @($Findings)
    $exitCode = if ($findingValues.Count -eq 0) { 0 } else { 1 }
    $outputLines = if ($findingValues.Count -eq 0) {
        @("CI .NET SDK authority verified: manifestSdk=$ManifestSdk actualSdk=$ActualSdk managedJobs=$($managedJobNames.Count)")
    }
    else {
        @($findingValues | ForEach-Object { "CI .NET SDK authority violation: $_" })
    }
    return [pscustomobject]@{
        Findings = $findingValues
        ManifestSdk = $ManifestSdk
        ActualSdk = $ActualSdk
        ExitCode = $exitCode
        OutputLines = $outputLines
    }
}

function Invoke-CiDotNetSdkAuthorityCheck {
    param(
        [Parameter(Mandatory)] [string] $CiWorkflowPath,
        [Parameter(Mandatory)] [string] $RestoreManifestPath,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $ActualSdk
    )

    $findings = [Collections.Generic.List[string]]::new()
    $manifestSdk = Get-ManifestSdkAuthority -Path $RestoreManifestPath -Findings $findings
    if ([string]::IsNullOrWhiteSpace($manifestSdk)) {
        return New-CiDotNetSdkAuthorityResult -Findings $findings -ManifestSdk '' -ActualSdk $ActualSdk
    }

    if ($ActualSdk -cnotmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
        $findings.Add("actual-dotnet-sdk-version-invalid:$ActualSdk")
    }
    elseif (-not [string]::Equals($ActualSdk, $manifestSdk, [StringComparison]::Ordinal)) {
        $findings.Add("actual-dotnet-sdk-version:${ActualSdk}:expected=$manifestSdk")
    }

    if (-not (Test-Path -LiteralPath $CiWorkflowPath -PathType Leaf)) {
        $findings.Add('ci-workflow-missing')
        return New-CiDotNetSdkAuthorityResult -Findings $findings -ManifestSdk $manifestSdk -ActualSdk $ActualSdk
    }

    $workflowEvidence = Get-CiDotNetSdkWorkflowEvidence -Path $CiWorkflowPath
    foreach ($yamlKeyFinding in @($workflowEvidence.YamlKeyFindings)) {
        $findings.Add([string] $yamlKeyFinding)
    }

    $workflow = $workflowEvidence.Workflow
    $jobsProperty = $workflow.PSObject.Properties['jobs']
    if ($null -eq $jobsProperty) {
        $findings.Add('ci-jobs-missing')
        return New-CiDotNetSdkAuthorityResult -Findings $findings -ManifestSdk $manifestSdk -ActualSdk $ActualSdk
    }

    $expectedJobs = [Collections.Generic.HashSet[string]]::new([string[]] $managedJobNames, [StringComparer]::Ordinal)
    foreach ($jobName in $managedJobNames) {
        $jobProperty = $jobsProperty.Value.PSObject.Properties[$jobName]
        if ($null -eq $jobProperty) {
            $findings.Add("ci-dotnet-job-missing:$jobName")
            continue
        }

        $setupSteps = @($jobProperty.Value.steps | Where-Object {
                $usesProperty = $_.PSObject.Properties['uses']
                $null -ne $usesProperty -and ([string] $usesProperty.Value).StartsWith('actions/setup-dotnet@', [StringComparison]::Ordinal)
            })
        if ($setupSteps.Count -ne 1) {
            $findings.Add("ci-setup-dotnet-count:${jobName}:$($setupSteps.Count)")
        }
        foreach ($setupStep in $setupSteps) {
            $withProperty = $setupStep.PSObject.Properties['with']
            $versionProperty = if ($null -ne $withProperty) { $withProperty.Value.PSObject.Properties['dotnet-version'] } else { $null }
            $actualVersion = if ($null -ne $versionProperty) { [string] $versionProperty.Value } else { '' }
            if (-not [string]::Equals($actualVersion, $manifestSdk, [StringComparison]::Ordinal)) {
                $findings.Add("ci-dotnet-sdk-version:${jobName}:${actualVersion}:expected=$manifestSdk")
            }
        }
    }

    foreach ($jobProperty in $jobsProperty.Value.PSObject.Properties) {
        if ($expectedJobs.Contains([string] $jobProperty.Name)) { continue }
        $unexpectedSetupSteps = @($jobProperty.Value.steps | Where-Object {
                $usesProperty = $_.PSObject.Properties['uses']
                $null -ne $usesProperty -and ([string] $usesProperty.Value).StartsWith('actions/setup-dotnet@', [StringComparison]::Ordinal)
            })
        if ($unexpectedSetupSteps.Count -gt 0) {
            $findings.Add("ci-setup-dotnet-unexpected-job:$($jobProperty.Name):$($unexpectedSetupSteps.Count)")
        }
    }

    return New-CiDotNetSdkAuthorityResult -Findings $findings -ManifestSdk $manifestSdk -ActualSdk $ActualSdk
}

function Get-CiDotNetSdkActualVersion {
    $dotnetVersionResult = Invoke-NativeCommandOutput `
        -Command 'dotnet' `
        -Arguments @('--version') `
        -WorkingDirectory $repoRoot `
        -Name 'verify-ci-dotnet-sdk-authority-version'
    return ([string] $dotnetVersionResult.Stdout).Trim()
}

if ([string]::Equals($MyInvocation.InvocationName, '.', [StringComparison]::Ordinal)) {
    return
}

try {
    $actualSdk = Get-CiDotNetSdkActualVersion
    $result = Invoke-CiDotNetSdkAuthorityCheck `
        -CiWorkflowPath $WorkflowPath `
        -RestoreManifestPath $ManifestPath `
        -ActualSdk $actualSdk
}
catch {
    Write-Output "CI .NET SDK authority violation: checker-error:$($_.Exception.Message)"
    exit 1
}

foreach ($outputLine in $result.OutputLines) {
    Write-Output $outputLine
}
if ($result.ExitCode -ne 0) { exit $result.ExitCode }
