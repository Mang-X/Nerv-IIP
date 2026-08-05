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
  3. A job that publishes evidence (it has at least one step whose `if:` can still run after an
     earlier step failed) must keep the sum of its step budgets strictly below its job budget, so
     some step budget always fires first and the job survives into its `Collect …` / `Upload …`
     steps.
  4. A job with no such step has no evidence to protect: when its job budget fires nothing is lost,
     so the *job* budget is the fail-fast bound and is sized from observed runtime. Its step budgets
     are per-step upper bounds only — with several steps in one job the later ones are not
     individually reachable, and that is accepted. The single rule enforced here is the obviously
     dead case: a step budget at or above the whole job budget can never fire under any schedule.

Tier classification (rule 3 vs rule 4) is the part that must fail *closed*: a job silently demoted
to tier 4 loses exactly the rule this library exists for. `Test-NervCiWorkflowConditionRunsAfterFailure`
therefore treats an `if:` expression as evidence-publishing unless it can prove the opposite, which
follows GitHub's own rule — an expression containing none of the status-check functions is evaluated
as `success() && (expression)` and cannot run after a failure. Anything it cannot tokenize with
confidence (a block scalar continued on later lines, a YAML alias, an unrecognized function call) is
classified as tier 3.

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

    for ($lineIndex = 0; $lineIndex -lt $lines.Length; $lineIndex++) {
        $rawLine = $lines[$lineIndex]
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

        # Job key. Anything else at this indent inside `jobs:` is an unreadable job header (a quoted
        # or otherwise unusual name): silently skipping it would attribute that job's `steps:` and
        # `timeout-minutes` to the *previous* job and quietly merge two budgets into one.
        if ($indent -eq 2) {
            if ($line -notmatch '^\s{2}(?<name>[A-Za-z0-9_.-]+):\s*$') {
                throw "Workflow '$Path' has an unreadable job header at line $($lineIndex + 1); the timeout-budget reader cannot certify it."
            }

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
                Condition = $null
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
    # raw file contains, otherwise a "no violations" result would be meaningless. Indentation alone
    # does not identify a step: `needs:`, `strategy.matrix` and other job-level sequences put their
    # items at the very same column, so each candidate is resolved against its own enclosing
    # job-level key (see Test-NervCiWorkflowLineIsStepEntry). That resolution is deliberately
    # independent of the state machine above — a `steps:` block the state machine never entered
    # still counts here and therefore still trips the mismatch.
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

    $rawStepCount = 0
    for ($index = $jobsLineIndex + 1; $index -lt $lines.Length; $index++) {
        if ($lines[$index] -notmatch '^\s{6}-\s+\S') { continue }
        if (Test-NervCiWorkflowLineIsStepEntry -Lines $lines -Index $index) { $rawStepCount++ }
    }

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

    if ($Text -match '^if:\s*(?<value>.*)$') {
        $Step.Condition = $Matches.value.Trim()
        $Step.AlwaysRuns = Test-NervCiWorkflowConditionRunsAfterFailure -Condition $Matches.value
    }
}

<#
Decides whether a step carrying this `if:` expression can still run after an earlier step in the
same job failed or timed out — i.e. whether it is an evidence step for tier purposes.

Fails closed: returns $true unless the expression is *provably* success-gated. Matching only the
literal `always()` (the first version of this gate) silently demoted a job to the weaker tier for
every other legal spelling — `${{ always() }}`, `always() && …`, `!cancelled()`, a trailing comment
— which switched off the one rule this gate exists to enforce.
#>
function Test-NervCiWorkflowConditionRunsAfterFailure {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Condition
    )

    $expression = Remove-NervCiWorkflowInlineComment -Text $Condition

    # `if: ${{ always() }}` is the same condition as `if: always()`. Unwrap repeatedly so a doubled
    # wrapper cannot hide the status function either.
    while ($expression -match '^\$\{\{(?<inner>.*)\}\}$') {
        $expression = $Matches.inner.Trim()
    }

    # Undecidable from this line alone. A block-scalar header (`>`, `|`, `>-`, …) or a YAML alias
    # continues the value on lines the structural reader does not attribute to the step, and an
    # empty condition is not a shape this reader understands. Classify as evidence-publishing.
    #
    # The header pattern is deliberately wider than the YAML grammar. YAML allows the indentation
    # indicator and the chomping indicator in *either* order (`>2-` and `>-2` are both legal), and
    # an earlier pattern that fixed the order to chomping-then-digits stopped recognizing `>2-` —
    # which silently sent a continued condition to the weaker tier. Over-matching here only ever
    # classifies more strictly, so the loose pattern is the fail-closed choice.
    if ([string]::IsNullOrWhiteSpace($expression)) { return $true }
    if ($expression -match '^[>|][0-9]*[+-]?[0-9]*$') { return $true }
    if ($expression.StartsWith('*') -or $expression.StartsWith('&')) { return $true }

    # GitHub status-check functions. `always()` and `!cancelled()` run after a failure by design,
    # and `failure()` runs *only* after one; all three keep the step reachable when an earlier step
    # has already failed, which is exactly what the evidence rule protects.
    if ($expression -match '(?i)(^|[^A-Za-z0-9_.])(always|failure|cancelled)\s*\(\s*\)') { return $true }

    # No status-check function means GitHub evaluates the expression as `success() && (expression)`,
    # so the step cannot run after a failure — but only if every function call in it is one of the
    # value-shaping functions that leave status gating alone. An unrecognized call is undecidable
    # and falls back to the stricter tier.
    $statusNeutralFunctions = @(
        'success', 'contains', 'startswith', 'endswith', 'format', 'join', 'tojson', 'fromjson', 'hashfiles'
    )
    foreach ($call in [regex]::Matches($expression, '(?i)(?<name>[A-Za-z_][A-Za-z0-9_-]*)\s*\(')) {
        if ($statusNeutralFunctions -notcontains $call.Groups['name'].Value.ToLowerInvariant()) {
            return $true
        }
    }

    return $false
}

<#
Removes a YAML inline comment from a plain scalar without cutting a `#` that belongs to the value.
Without this, `if: always() # keep evidence` was not recognized as `always()` at all.
#>
function Remove-NervCiWorkflowInlineComment {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Text
    )

    $inSingleQuote = $false
    $inDoubleQuote = $false
    for ($index = 0; $index -lt $Text.Length; $index++) {
        $character = $Text[$index]

        # A `\"` inside a double-quoted run is a literal quote, not the end of the run. Treating it
        # as the end reopened the rest of the value to comment stripping, so `"a \" # b" && always()`
        # lost its `always()` and the step was demoted to the weaker tier. Cutting less is the
        # fail-closed direction here, so the escape is consumed whole.
        if ($inDoubleQuote -and $character -eq '\' -and $index + 1 -lt $Text.Length) {
            $index++
            continue
        }

        if ($character -eq "'" -and -not $inDoubleQuote) {
            # YAML's single-quote escape is a doubled quote; consuming the pair keeps the run open
            # explicitly instead of relying on two toggles happening to cancel out.
            if ($inSingleQuote -and $index + 1 -lt $Text.Length -and $Text[$index + 1] -eq "'") {
                $index++
                continue
            }

            $inSingleQuote = -not $inSingleQuote
            continue
        }

        if ($character -eq '"' -and -not $inSingleQuote) { $inDoubleQuote = -not $inDoubleQuote; continue }
        if ($character -ne '#' -or $inSingleQuote -or $inDoubleQuote) { continue }
        if ($index -eq 0 -or [char]::IsWhiteSpace($Text[$index - 1])) {
            return $Text.Substring(0, $index).Trim()
        }
    }

    return $Text.Trim()
}

<#
True when the six-space-indented sequence item on line <Index> belongs to a `steps:` block rather
than to another job-level sequence (`needs:`, `strategy.matrix` shorthand, …). The first version of
this cross-check counted every such line and made the reader throw `step parse mismatch` on any
workflow that used `needs:` — a hard red pointing at a parse error instead of at the real cause.
#>
function Test-NervCiWorkflowLineIsStepEntry {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowEmptyString()] [string[]] $Lines,
        [Parameter(Mandatory)] [int] $Index
    )

    for ($cursor = $Index - 1; $cursor -ge 0; $cursor--) {
        $line = $Lines[$cursor]
        if ($line -match '^\s*$' -or $line -match '^\s*#') { continue }
        if (($line.Length - $line.TrimStart(' ').Length) -gt 4) { continue }
        return [bool] ($line -match '^\s{4}steps:\s*$')
    }

    return $false
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
            # The job budget is this tier's fail-fast bound; step budgets are per-step upper bounds
            # and later steps are not individually reachable. A step budget at or above the whole
            # job budget is the case that is dead under every schedule, so that is what is rejected.
            $violations.Add([pscustomobject]@{
                code = 'job-budget-not-above-largest-step'
                job = $job.Name
                message = "Job '$($job.Name)' has a job budget of $($job.TimeoutMinutes) which is not above its largest step budget $largestStep; that step budget can never fire under any schedule."
            })
        }
    }

    # Comma-wrapped so a clean workflow still yields an array whose Count is 0, not $null.
    return ,$violations.ToArray()
}
