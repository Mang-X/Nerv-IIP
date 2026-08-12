# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs the CI required-summary verifier against temporary workflow mutations
#   Writes:
#     - Temporary workflow fixtures under the operating-system temp directory
#     - artifacts/script-logs/**
#   Cleanup:
#     - Removes owned temporary fixtures in finally
#   Requires:
#     - PowerShell 7
#     - Ruby 3.4 with yaml/json standard libraries

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')

$verifierPath = Join-Path $repoRoot 'scripts/verify-ci-required-summary.ps1'
$workflowPath = Join-Path $repoRoot '.github/workflows/ci.yml'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-ci-required-summary-$([Guid]::NewGuid().ToString('N'))"
$fixturePath = Join-Path $fixtureRoot 'ci.yml'

function Assert-Contract {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    if (-not $Condition) { throw $Message }
}

function Invoke-SummaryVerifier {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [string] $Path = $workflowPath
    )

    try {
        $result = Invoke-NativeCommandOutput `
            -Command 'pwsh' `
            -Arguments @('-NoProfile', '-File', $verifierPath, '-WorkflowPath', $Path) `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 120 `
            -Name $Name
        return [pscustomobject]@{ Passed = $true; Message = ("$($result.Stdout)" -replace '\s+', ' ') }
    }
    catch {
        return [pscustomobject]@{ Passed = $false; Message = ("$($_.Exception.Message)" -replace '\s+', ' ') }
    }
}

function Invoke-Mutation {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Original,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Replacement,
        [Parameter(Mandatory)] [string] $ExpectedDiagnostic,
        [Parameter(Mandatory)] [string] $Workflow
    )

    $mutated = $Workflow.Replace($Original, $Replacement)
    Assert-Contract (-not [string]::Equals($mutated, $Workflow, [StringComparison]::Ordinal)) "Mutation '$Name' did not match the workflow."
    [IO.File]::WriteAllText($fixturePath, $mutated, [Text.UTF8Encoding]::new($false))
    $result = Invoke-SummaryVerifier -Name $Name -Path $fixturePath
    Assert-Contract (-not $result.Passed) "Mutation '$Name' must fail required-summary governance."
    Assert-Contract ($result.Message.Contains($ExpectedDiagnostic, [StringComparison]::Ordinal)) "Mutation '$Name' returned the wrong diagnostic: $($result.Message)"
}

try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    Assert-Contract (Test-Path -LiteralPath $verifierPath -PathType Leaf) 'The CI required-summary verifier is missing.'

    $workflow = [IO.File]::ReadAllText($workflowPath)
    $baseline = Invoke-SummaryVerifier -Name 'ci-required-summary-baseline'
    Assert-Contract $baseline.Passed "The repository workflow must satisfy required-summary governance: $($baseline.Message)"

    $needLine = '      - connector-host-tests'
    Invoke-Mutation -Name 'ci-summary-missing-need' -Workflow $workflow `
        -Original "$needLine$([Environment]::NewLine)" -Replacement '' `
        -ExpectedDiagnostic 'CI Summary must need exactly the five current required CI jobs.'

    Invoke-Mutation -Name 'ci-summary-not-always' -Workflow $workflow `
        -Original "  ci-summary:$([Environment]::NewLine)    name: CI Summary$([Environment]::NewLine)    timeout-minutes: 5$([Environment]::NewLine)    runs-on: ubuntu-latest$([Environment]::NewLine)    if: always()" `
        -Replacement "  ci-summary:$([Environment]::NewLine)    name: CI Summary$([Environment]::NewLine)    timeout-minutes: 5$([Environment]::NewLine)    runs-on: ubuntu-latest$([Environment]::NewLine)    if: success()" `
        -ExpectedDiagnostic "CI Summary must retain name 'CI Summary' and if: always()."

    Invoke-Mutation -Name 'ci-summary-missing-job' -Workflow $workflow `
        -Original "  connector-host-tests:$([Environment]::NewLine)" `
        -Replacement "  connector-host-tests-missing:$([Environment]::NewLine)" `
        -ExpectedDiagnostic 'CI Summary must need exactly the five current required CI jobs.'

    $strictAssertion = '          test "${{ needs.connector-host-tests.result }}" = "success"'
    Invoke-Mutation -Name 'ci-summary-noop' -Workflow $workflow `
        -Original $strictAssertion -Replacement '          echo "${{ needs.connector-host-tests.result }}"' `
        -ExpectedDiagnostic "CI Summary must fail when 'connector-host-tests' is not success."

    Invoke-Mutation -Name 'ci-summary-masked-failure' -Workflow $workflow `
        -Original $strictAssertion -Replacement "$strictAssertion || true" `
        -ExpectedDiagnostic "CI Summary must fail when 'connector-host-tests' is not success."

    Invoke-Mutation -Name 'ci-summary-step-continue-on-error' -Workflow $workflow `
        -Original "      - name: Require all CI lanes$([Environment]::NewLine)" `
        -Replacement "      - name: Require all CI lanes$([Environment]::NewLine)        continue-on-error: true$([Environment]::NewLine)" `
        -ExpectedDiagnostic "CI Summary must not set 'continue-on-error' on the job or any step."

    Invoke-Mutation -Name 'ci-summary-skipped-assertion-step' -Workflow $workflow `
        -Original "      - name: Require all CI lanes$([Environment]::NewLine)" `
        -Replacement "      - name: Require all CI lanes$([Environment]::NewLine)        if: false$([Environment]::NewLine)" `
        -ExpectedDiagnostic 'CI Summary assertion step must not have a condition.'

    Invoke-Mutation -Name 'ci-summary-non-fail-fast-shell' -Workflow $workflow `
        -Original '        shell: bash --noprofile --norc -euo pipefail {0}' `
        -Replacement '        shell: bash {0}' `
        -ExpectedDiagnostic 'CI Summary assertion step must use the governed fail-fast Bash shell.'

    Invoke-Mutation -Name 'ci-summary-job-continue-on-error' -Workflow $workflow `
        -Original "  ci-summary:$([Environment]::NewLine)    name: CI Summary$([Environment]::NewLine)" `
        -Replacement "  ci-summary:$([Environment]::NewLine)    name: CI Summary$([Environment]::NewLine)    continue-on-error: true$([Environment]::NewLine)" `
        -ExpectedDiagnostic "CI Summary must not set 'continue-on-error' on the job or any step."

    foreach ($nonSuccessResult in @('failure', 'cancelled', 'skipped')) {
        $nonSuccessAssertion = '          test "${{ needs.connector-host-tests.result }}" = "' + $nonSuccessResult + '"'
        Invoke-Mutation -Name "ci-summary-allows-$nonSuccessResult" -Workflow $workflow `
            -Original $strictAssertion -Replacement $nonSuccessAssertion `
            -ExpectedDiagnostic "CI Summary must fail when 'connector-host-tests' is not success."
    }
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

Write-Output 'CI required-summary contract tests passed.'
