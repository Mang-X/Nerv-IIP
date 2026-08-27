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

function Get-CiDotNetSdkAuthorityFindings {
    param(
        [Parameter(Mandatory)] [string] $CiWorkflowPath,
        [Parameter(Mandatory)] [string] $RestoreManifestPath,
        [Parameter(Mandatory)] [string[]] $ExpectedJobNames
    )

    $findings = [Collections.Generic.List[string]]::new()
    $manifestSdk = Get-ManifestSdkAuthority -Path $RestoreManifestPath -Findings $findings
    if ([string]::IsNullOrWhiteSpace($manifestSdk)) {
        return [pscustomobject]@{ Findings = @($findings); ManifestSdk = ''; ActualSdk = '' }
    }

    $dotnetVersionResult = Invoke-NativeCommandOutput `
        -Command 'dotnet' `
        -Arguments @('--version') `
        -WorkingDirectory $repoRoot `
        -Name 'verify-ci-dotnet-sdk-authority-version'
    $actualSdk = ([string] $dotnetVersionResult.Stdout).Trim()
    if ($actualSdk -cnotmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
        $findings.Add("actual-dotnet-sdk-version-invalid:$actualSdk")
    }
    elseif (-not [string]::Equals($actualSdk, $manifestSdk, [StringComparison]::Ordinal)) {
        $findings.Add("actual-dotnet-sdk-version:${actualSdk}:expected=$manifestSdk")
    }

    if (-not (Test-Path -LiteralPath $CiWorkflowPath -PathType Leaf)) {
        $findings.Add('ci-workflow-missing')
        return [pscustomobject]@{ Findings = @($findings); ManifestSdk = $manifestSdk; ActualSdk = $actualSdk }
    }

    $workflow = ConvertFrom-NervCiRequiredSummaryWorkflow -Path $CiWorkflowPath -WorkingDirectory $repoRoot
    $jobsProperty = $workflow.PSObject.Properties['jobs']
    if ($null -eq $jobsProperty) {
        $findings.Add('ci-jobs-missing')
        return [pscustomobject]@{ Findings = @($findings); ManifestSdk = $manifestSdk; ActualSdk = $actualSdk }
    }

    $expectedJobs = [Collections.Generic.HashSet[string]]::new($ExpectedJobNames, [StringComparer]::Ordinal)
    foreach ($jobName in $ExpectedJobNames) {
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

    return [pscustomobject]@{ Findings = @($findings); ManifestSdk = $manifestSdk; ActualSdk = $actualSdk }
}

try {
    $result = Get-CiDotNetSdkAuthorityFindings `
        -CiWorkflowPath $WorkflowPath `
        -RestoreManifestPath $ManifestPath `
        -ExpectedJobNames $managedJobNames
}
catch {
    Write-Output "CI .NET SDK authority violation: checker-error:$($_.Exception.Message)"
    exit 1
}

if ($result.Findings.Count -gt 0) {
    foreach ($finding in $result.Findings) {
        Write-Output "CI .NET SDK authority violation: $finding"
    }
    exit 1
}

Write-Output "CI .NET SDK authority verified: manifestSdk=$($result.ManifestSdk) actualSdk=$($result.ActualSdk) managedJobs=$($managedJobNames.Count)"
