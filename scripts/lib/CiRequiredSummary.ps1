# Script-Governance:
#   Category: library
#   SideEffects:
#     - Parses a caller-provided GitHub Actions workflow through Ruby YAML
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7
#     - Ruby 3.4 with yaml/json standard libraries

function ConvertFrom-NervCiRequiredSummaryWorkflow {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $WorkingDirectory
    )

    $rubyProgram = "require 'yaml'; require 'json'; puts JSON.generate(YAML.safe_load(File.read(ARGV.fetch(0))))"
    $result = Invoke-NativeCommandOutput -Command 'ruby' -Arguments @(
        '-ryaml',
        '-rjson',
        '-e', $rubyProgram,
        $Path
    ) -WorkingDirectory $WorkingDirectory -Name 'parse-ci-required-summary-workflow'

    return ($result.Stdout | ConvertFrom-Json -ErrorAction Stop)
}

function Get-NervCiRequiredSummaryStringValue {
    param(
        [Parameter(Mandatory)] [object] $Object,
        [Parameter(Mandatory)] [string] $PropertyName
    )

    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property) { return '' }
    return [string] $property.Value
}

function Get-NervCiRequiredSummaryFindings {
    param(
        [Parameter(Mandatory)] [string] $WorkflowPath,
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )

    $findings = [Collections.Generic.List[string]]::new()
    $expectedNeeds = @('backend-tests', 'connector-host-tests', 'frontend', 'script-governance')

    try {
        $workflow = ConvertFrom-NervCiRequiredSummaryWorkflow -Path $WorkflowPath -WorkingDirectory $RepositoryRoot
        $jobs = $workflow.PSObject.Properties['jobs'].Value
        if ($null -eq $jobs) {
            $findings.Add('CI workflow must define jobs.')
            return @($findings)
        }

        $summaryProperty = $jobs.PSObject.Properties['ci-summary']
        if ($null -eq $summaryProperty) {
            $findings.Add("CI workflow is missing the stable 'ci-summary' job.")
            return @($findings)
        }

        $summary = $summaryProperty.Value
        $actualNeeds = @($summary.needs | ForEach-Object { [string] $_ })
        $expectedNeedSet = Get-NervStringSet -Values $expectedNeeds -Comparer ([StringComparer]::Ordinal)
        $actualNeedSet = Get-NervStringSet -Values $actualNeeds -Comparer ([StringComparer]::Ordinal)
        $missingNeeds = @($expectedNeeds | Where-Object { -not $actualNeedSet.Contains([string] $_) })
        $unexpectedNeeds = @($actualNeeds | Where-Object { -not $expectedNeedSet.Contains([string] $_) })
        $missingJobs = @($expectedNeeds | Where-Object { $null -eq $jobs.PSObject.Properties[$_] })
        if ($actualNeeds.Count -ne $expectedNeeds.Count -or $missingNeeds.Count -gt 0 -or $unexpectedNeeds.Count -gt 0 -or $missingJobs.Count -gt 0) {
            $findings.Add('CI Summary must need exactly the four current required CI jobs.')
        }

        $name = Get-NervCiRequiredSummaryStringValue -Object $summary -PropertyName 'name'
        $condition = Get-NervCiRequiredSummaryStringValue -Object $summary -PropertyName 'if'
        if (-not [string]::Equals($name, 'CI Summary', [StringComparison]::Ordinal) -or
            -not [string]::Equals($condition, 'always()', [StringComparison]::OrdinalIgnoreCase)) {
            $findings.Add("CI Summary must retain name 'CI Summary' and if: always().")
        }

        if (-not [string]::Equals((Get-NervCiRequiredSummaryStringValue -Object $summary -PropertyName 'runs-on'), 'ubuntu-latest', [StringComparison]::Ordinal) -or
            -not [string]::Equals((Get-NervCiRequiredSummaryStringValue -Object $summary -PropertyName 'timeout-minutes'), '5', [StringComparison]::Ordinal)) {
            $findings.Add('CI Summary must run on ubuntu-latest with a five-minute job timeout.')
        }

        $steps = @($summary.steps)
        $hasContinueOnError = $null -ne $summary.PSObject.Properties['continue-on-error'] -or @(
            $steps | Where-Object { $null -ne $_.PSObject.Properties['continue-on-error'] }
        ).Count -gt 0
        if ($hasContinueOnError) {
            $findings.Add("CI Summary must not set 'continue-on-error' on the job or any step.")
        }

        if ($steps.Count -eq 1 -and $null -ne $steps[0].PSObject.Properties['if']) {
            $findings.Add('CI Summary assertion step must not have a condition.')
        }

        if ($steps.Count -ne 1 -or
            -not [string]::Equals((Get-NervCiRequiredSummaryStringValue -Object $steps[0] -PropertyName 'name'), 'Require all CI lanes', [StringComparison]::Ordinal) -or
            -not [string]::Equals((Get-NervCiRequiredSummaryStringValue -Object $steps[0] -PropertyName 'timeout-minutes'), '3', [StringComparison]::Ordinal)) {
            $findings.Add('CI Summary must contain one three-minute required-lane assertion step.')
        }

        if ($steps.Count -ne 1 -or
            -not [string]::Equals(
                (Get-NervCiRequiredSummaryStringValue -Object $steps[0] -PropertyName 'shell'),
                'bash --noprofile --norc -euo pipefail {0}',
                [StringComparison]::Ordinal)) {
            $findings.Add('CI Summary assertion step must use the governed fail-fast Bash shell.')
        }

        $run = if ($steps.Count -eq 1) { Get-NervCiRequiredSummaryStringValue -Object $steps[0] -PropertyName 'run' } else { '' }
        $commands = @(
            $run -split "`r?`n" |
                ForEach-Object { $_.Trim() } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        )
        $requiredAssertions = @()
        $commandSet = Get-NervStringSet -Values $commands -Comparer ([StringComparer]::Ordinal)
        foreach ($requiredJob in $expectedNeeds) {
            $requiredAssertion = 'test "${{ needs.' + $requiredJob + '.result }}" = "success"'
            $requiredAssertions += $requiredAssertion
            if (-not $commandSet.Contains($requiredAssertion)) {
                $findings.Add("CI Summary must fail when '$requiredJob' is not success.")
            }
        }

        $actualSorted = @(Get-NervStringsSorted -Values $commands -Comparer ([StringComparer]::Ordinal)) -join '|'
        $expectedSorted = @(Get-NervStringsSorted -Values $requiredAssertions -Comparer ([StringComparer]::Ordinal)) -join '|'
        if ($commands.Count -ne $requiredAssertions.Count -or -not [string]::Equals($actualSorted, $expectedSorted, [StringComparison]::Ordinal)) {
            $findings.Add('CI Summary must contain only standalone success assertions for its exact dependencies.')
        }
    }
    catch {
        $findings.Add("CI workflow must be valid structured YAML: $($_.Exception.Message)")
    }

    return @($findings)
}
