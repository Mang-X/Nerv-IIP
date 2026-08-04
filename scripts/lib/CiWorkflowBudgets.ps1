# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads GitHub Actions workflow files
#   Writes:
#     - None
#   Cleanup:
#     - No process or external resource ownership
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest

<#
MAN-799 CI timeout-budget invariants.

The rules this library enforces (narrative: docs/architecture/test-evidence-governance.md):

  1. Every job declares `timeout-minutes`. Without one a job inherits GitHub's 360-minute default
     and a deadlock burns a full runner hour-block.
  2. Every explicit step declares `timeout-minutes`. GitHub's implicit `Set up job` and post steps
     cannot carry one; they are covered by the margin left inside the job budget, not by this rule.
  3. A job that publishes evidence (it has at least one `if: always()` step) must keep the sum of
     its step budgets strictly below its job budget, so some step budget always fires first and the
     job survives into its `Collect …` / `Upload …` steps.
  4. A job with no `if: always()` step has no evidence to protect. Its budget is sized from observed
     runtime instead of from the step sum, and only has to stay strictly above its largest single
     step budget so step budgets remain reachable.

The parser is intentionally structural rather than a general YAML implementation: it reads the
two-space-indented shape GitHub Actions workflows are written in, and `Get-NervCiWorkflowBudgets`
fails closed when the file does not have that shape (see the step-count cross-check), so a
misparse can never silently report "no violations".
#>

function Get-NervCiWorkflowBudgets {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path
    )

    $lines = [System.IO.File]::ReadAllLines($Path)
    $jobs = New-Object System.Collections.Generic.List[object]

    $inJobs = $false
    $currentJob = $null
    $inSteps = $false
    $currentStep = $null

    foreach ($rawLine in $lines) {
        if ($rawLine -match '^\s*$' -or $rawLine -match '^\s*#') { continue }

        $indent = $rawLine.Length - $rawLine.TrimStart(' ').Length
        $line = $rawLine.TrimEnd()

        if ($indent -eq 0) {
            $inJobs = ($line -match '^jobs:\s*$')
            $currentJob = $null
            $inSteps = $false
            $currentStep = $null
            continue
        }

        if (-not $inJobs) { continue }

        # Job key.
        if ($indent -eq 2 -and $line -match '^\s{2}(?<name>[A-Za-z0-9_.-]+):\s*$') {
            $currentJob = [pscustomobject]@{
                Name = $Matches.name
                TimeoutMinutes = $null
                Steps = New-Object System.Collections.Generic.List[object]
            }
            $jobs.Add($currentJob)
            $inSteps = $false
            $currentStep = $null
            continue
        }

        if ($null -eq $currentJob) { continue }

        # Job-level mapping keys.
        if ($indent -eq 4 -and $line -notmatch '^\s{4}-\s') {
            $inSteps = ($line -match '^\s{4}steps:\s*$')
            $currentStep = $null
            if ($line -match '^\s{4}timeout-minutes:\s*(?<value>\d+)\s*$') {
                $currentJob.TimeoutMinutes = [int] $Matches.value
            }

            continue
        }

        if (-not $inSteps) { continue }

        # Step entry: `      - <key>: <value>`.
        if ($indent -eq 6 -and $line -match '^\s{6}-\s+(?<rest>.*)$') {
            $currentStep = [pscustomobject]@{
                Name = $null
                TimeoutMinutes = $null
                AlwaysRuns = $false
            }
            $currentJob.Steps.Add($currentStep)
            $rest = $Matches.rest
            Set-NervCiWorkflowStepProperty -Step $currentStep -Text $rest
            continue
        }

        # Step mapping keys sit at exactly 8 spaces; anything deeper belongs to a nested mapping
        # (`with:`) or a block scalar (`run: |`) and is not a step property.
        if ($indent -eq 8 -and $null -ne $currentStep) {
            Set-NervCiWorkflowStepProperty -Step $currentStep -Text $line.Trim()
        }
    }

    # Fail closed on a misparse: the structural reader must have found exactly the step entries the
    # raw file contains, otherwise a "no violations" result would be meaningless. The raw count is
    # taken over the `jobs:` section only — top-level sequences such as `on.push.branches` sit at
    # the same indentation but are not steps.
    $jobsLineIndex = -1
    for ($index = 0; $index -lt $lines.Length; $index++) {
        if ($lines[$index] -match '^jobs:\s*$') {
            $jobsLineIndex = $index
            break
        }
    }

    if ($jobsLineIndex -lt 0) {
        throw "Workflow '$Path' has no top-level 'jobs:' mapping."
    }

    $rawStepCount = @($lines[$jobsLineIndex..($lines.Length - 1)] | Where-Object { $_ -match '^\s{6}-\s+\S' }).Count
    $parsedStepCount = ($jobs | ForEach-Object { $_.Steps.Count } | Measure-Object -Sum).Sum
    if ($null -eq $parsedStepCount) { $parsedStepCount = 0 }
    if ($jobs.Count -eq 0) {
        throw "Workflow '$Path' produced no jobs; the timeout-budget reader cannot certify it."
    }

    if ($rawStepCount -ne $parsedStepCount) {
        throw "Workflow '$Path' step parse mismatch: raw=$rawStepCount parsed=$parsedStepCount."
    }

    # Comma-wrapped so an empty or single-element result still reaches the caller as an array.
    return ,$jobs.ToArray()
}

function Set-NervCiWorkflowStepProperty {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object] $Step,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Text
    )

    if ($Text -match '^name:\s*(?<value>.+?)\s*$') {
        $Step.Name = $Matches.value
        return
    }

    if ($Text -match '^timeout-minutes:\s*(?<value>\d+)\s*$') {
        $Step.TimeoutMinutes = [int] $Matches.value
        return
    }

    if ($Text -match '^if:\s*always\(\)\s*$') {
        $Step.AlwaysRuns = $true
    }
}

function Test-NervCiWorkflowBudgets {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Jobs
    )

    $violations = New-Object System.Collections.Generic.List[object]

    foreach ($job in $Jobs) {
        if ($null -eq $job.TimeoutMinutes) {
            $violations.Add([pscustomobject]@{
                code = 'missing-job-timeout'
                job = $job.Name
                message = "Job '$($job.Name)' has no timeout-minutes; it would inherit GitHub's 360-minute default."
            })
        }

        foreach ($step in $job.Steps) {
            if ($null -eq $step.TimeoutMinutes) {
                $label = if ($step.Name) { $step.Name } else { '<unnamed>' }
                $violations.Add([pscustomobject]@{
                    code = 'missing-step-timeout'
                    job = $job.Name
                    step = $label
                    message = "Step '$label' in job '$($job.Name)' has no timeout-minutes."
                })
            }
        }

        if ($null -eq $job.TimeoutMinutes) { continue }
        if (@($job.Steps | Where-Object { $null -eq $_.TimeoutMinutes }).Count -gt 0) { continue }

        $stepSum = (@($job.Steps | ForEach-Object { $_.TimeoutMinutes }) | Measure-Object -Sum).Sum
        if ($null -eq $stepSum) { $stepSum = 0 }
        $largestStep = (@($job.Steps | ForEach-Object { $_.TimeoutMinutes }) | Measure-Object -Maximum).Maximum
        if ($null -eq $largestStep) { $largestStep = 0 }
        $publishesEvidence = @($job.Steps | Where-Object { $_.AlwaysRuns }).Count -gt 0

        if ($publishesEvidence) {
            if ($stepSum -ge $job.TimeoutMinutes) {
                $violations.Add([pscustomobject]@{
                    code = 'evidence-job-budget-not-above-step-sum'
                    job = $job.Name
                    message = "Job '$($job.Name)' publishes evidence but its step budgets sum to $stepSum, which is not below its job budget $($job.TimeoutMinutes); the job timeout could fire first and cancel the if: always() evidence steps."
                })
            }
        }
        elseif ($largestStep -ge $job.TimeoutMinutes) {
            $violations.Add([pscustomobject]@{
                code = 'job-budget-not-above-largest-step'
                job = $job.Name
                message = "Job '$($job.Name)' has a job budget of $($job.TimeoutMinutes) which is not above its largest step budget $largestStep; that step budget can never fire."
            })
        }
    }

    # Comma-wrapped so a clean workflow still yields an array whose Count is 0, not $null.
    return ,$violations.ToArray()
}
