# Script-Governance:
#   Category: check
#   SideEffects:
#     - Validates FullChain lane manifest, TRX and summary contracts with temporary fixtures
#   Writes:
#     - Temporary TRX fixtures under the operating-system temp directory
#   Cleanup:
#     - Removes owned temporary fixtures in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/FullChainTestLane.ps1')

$manifestPath = Join-Path $repoRoot 'scripts/full-chain-test-lane.json'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-full-chain-lane-$([Guid]::NewGuid().ToString('N'))"

function Assert-Contract([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function New-FullChainTrx {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string[]] $Identities,
        [string] $Outcome = 'Passed'
    )

    $definitions = [Collections.Generic.List[string]]::new()
    $results = [Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $Identities.Count; $index++) {
        $identity = $Identities[$index]
        $separatorIndex = $identity.LastIndexOf('.', [StringComparison]::Ordinal)
        $class = $identity.Substring(0, $separatorIndex)
        $method = $identity.Substring($separatorIndex + 1)
        $id = "test-$index"
        $definitions.Add("<UnitTest id=`"$id`"><TestMethod className=`"$class`" name=`"$method`" /></UnitTest>")
        $results.Add("<UnitTestResult testId=`"$id`" testName=`"$method`" outcome=`"$Outcome`" />")
    }
    $trx = "<?xml version=`"1.0`"?><TestRun xmlns=`"http://microsoft.com/schemas/VisualStudio/TeamTest/2010`"><Results>$($results -join '')</Results><TestDefinitions>$($definitions -join '')</TestDefinitions></TestRun>"
    [IO.File]::WriteAllText($Path, $trx, [Text.UTF8Encoding]::new($false))
}

try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    $manifest = Import-NervFullChainTestLaneManifest -ManifestPath $manifestPath -RepositoryRoot $repoRoot
    $expectedIds = @(
        'maintenance-runtime-hours',
        'mes-inventory-produced-lot',
        'erp-wms-delivery-completion',
        'sales-order-demand-planning',
        'erp-return-closure'
    )
    Assert-Contract ([string]::Equals((@($manifest.members.id) -join '|'), ($expectedIds -join '|'), [StringComparison]::Ordinal)) 'FullChain manifest must freeze exactly the five approved scenarios in execution order.'
    Assert-Contract (@($manifest.members | Where-Object { $_.tier -ne 'core' -or $_.status -ne 'active' }).Count -eq 0) 'All five NERV-767 scenarios must be active/core.'
    Assert-Contract ((@($manifest.members.expectedTestIdentities) | ForEach-Object { @($_).Count } | Measure-Object -Sum).Sum -eq 5) 'Each FullChain scenario must freeze exactly one identity.'
    Assert-Contract (@($manifest.members | Where-Object { $_.project -ne 'backend/tests/Nerv.IIP.Business.FullChain.Tests/Nerv.IIP.Business.FullChain.Tests.csproj' }).Count -eq 0) 'All scenarios must target the FullChain test project.'
    Assert-Contract (@($manifest.members | Where-Object { @($_.diagnosticSchemas).Count -eq 0 }).Count -eq 0) 'Every scenario must declare restricted PostgreSQL diagnostic schemas.'
    Assert-Contract (@($manifest.members | Where-Object { [string]::IsNullOrWhiteSpace([string]$_.entrypoint.kind) }).Count -eq 0) 'Every scenario must declare an entrypoint kind.'

    $runnerPath = Join-Path $repoRoot 'scripts/run-full-chain-test-lane.ps1'
    Assert-Contract (Test-Path -LiteralPath $runnerPath -PathType Leaf) 'The governed FullChain runner must exist.'
    $runnerContent = [IO.File]::ReadAllText($runnerPath)
    foreach ($requiredFragment in @(
        'Import-NervFullChainTestLaneManifest',
        'Get-NervFullChainTrxResult',
        'Assert-NervFullChainTestLaneSummary',
        'Wait-NervFullChainComposeProbe',
        'MaximumAttempts = 30',
        "'fullstack'",
        "'script'",
        "'dotnet'",
        'NERV_IIP_FULL_CHAIN_RESULTS_DIRECTORY',
        'NERV_IIP_FULL_CHAIN_RESULT_FILE',
        'NERV_IIP_FULLSTACK_STATE_ROOT',
        'NERV_IIP_FULL_CHAIN_ENTRYPOINT_EVIDENCE_PATH',
        "SetEnvironmentVariable('Messaging__Provider', 'Redis')",
        "SetEnvironmentVariable('Persistence__Provider', 'PostgreSQL')",
        "dependencyEvidence = 'passed'",
        'Assert-NervFullChainMemberEvidence'
    )) {
        Assert-Contract ($runnerContent.Contains($requiredFragment, [StringComparison]::Ordinal)) "FullChain runner is missing required contract fragment '$requiredFragment'."
    }
    Assert-Contract (-not $runnerContent.Contains('continue-on-error', [StringComparison]::OrdinalIgnoreCase)) 'FullChain runner must preserve natural failures.'
    Assert-Contract (-not $runnerContent.Contains('FLUSHALL', [StringComparison]::OrdinalIgnoreCase)) 'FullChain runner must never use broad Redis cleanup.'

    $workflowContent = [IO.File]::ReadAllText((Join-Path $repoRoot '.github/workflows/ci.yml'))
    foreach ($requiredWorkflowFragment in @(
        'business-full-chain-acceptance:',
        'name: Business FullChain Acceptance',
        "needs.impact-plan.outputs.full_chain != 'false'",
        'bash "${RUNNER_TEMP}/aspire-install.sh" --version 13.4.6',
        'pnpm -C frontend install --frozen-lockfile',
        'pnpm -C frontend exec playwright install --with-deps chromium',
        '-Lane full-chain',
        '-SelectedLanes full-chain',
        'full-chain-dependency-summary-${{ github.run_id }}-${{ github.run_attempt }}',
        'full-chain-failure-diagnostics-${{ github.run_id }}-${{ github.run_attempt }}',
        'retention-days: 14'
    )) {
        Assert-Contract ($workflowContent.Contains($requiredWorkflowFragment, [StringComparison]::Ordinal)) "FullChain workflow is missing required contract fragment '$requiredWorkflowFragment'."
    }
    Assert-Contract ($workflowContent.Contains('if-no-files-found: error', [StringComparison]::Ordinal)) 'FullChain evidence uploads must fail when required artifacts are missing.'

    $manifestObject = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -Depth 20
    $manifestObject.members = @($manifestObject.members | Select-Object -First 4)
    $missingMemberPath = Join-Path $fixtureRoot 'missing-member.json'
    [IO.File]::WriteAllText($missingMemberPath, (($manifestObject | ConvertTo-Json -Depth 20) + "`n"), [Text.UTF8Encoding]::new($false))
    $missingRejected = $false
    try { Import-NervFullChainTestLaneManifest -ManifestPath $missingMemberPath -RepositoryRoot $repoRoot | Out-Null }
    catch { $missingRejected = $_.Exception.Message.Contains('exactly 5 active/core members', [StringComparison]::Ordinal) }
    Assert-Contract $missingRejected 'Removing a FullChain scenario must fail the manifest contract.'

    $identity = [string]$manifest.members[0].expectedTestIdentities[0]
    $trxPath = Join-Path $fixtureRoot 'full-chain.trx'
    New-FullChainTrx -Path $trxPath -Identities @($identity)
    $trxResult = Get-NervFullChainTrxResult -ResultsDirectory $fixtureRoot -ExpectedTestIdentities @($identity)
    Assert-Contract ($trxResult.passed -eq 1 -and $trxResult.failed -eq 0 -and $trxResult.skipped -eq 0) 'One passed frozen identity must satisfy the FullChain TRX contract.'

    New-FullChainTrx -Path $trxPath -Identities @($identity) -Outcome 'NotExecuted'
    $skip = Get-NervFullChainTrxResult -ResultsDirectory $fixtureRoot -ExpectedTestIdentities @($identity) -AllowInvalid
    Assert-Contract (-not $skip.valid -and $skip.skipped -eq 1) 'A skipped FullChain identity must remain visible and invalid.'

    New-FullChainTrx -Path $trxPath -Identities @($identity, 'Nerv.IIP.Business.FullChain.Tests.UnexpectedTests.Unexpected')
    $extra = Get-NervFullChainTrxResult -ResultsDirectory $fixtureRoot -ExpectedTestIdentities @($identity) -AllowInvalid
    Assert-Contract (-not $extra.identitiesMatch) 'An extra FullChain identity must fail the frozen set contract.'

    $memberEvidenceRoot = Join-Path $fixtureRoot 'member-evidence'
    [IO.Directory]::CreateDirectory($memberEvidenceRoot) | Out-Null
    $memberEvidencePath = Join-Path $memberEvidenceRoot 'entrypoint-evidence.json'
    $cleanupFixture = [ordered]@{
        managedProcesses = [ordered]@{ remaining = 0 }
        disposableDatabase = [ordered]@{ remaining = 0 }
        composeServices = [ordered]@{ remaining = 0 }
        cleanupFailures = @()
    }
    [IO.File]::WriteAllText($memberEvidencePath, (($cleanupFixture | ConvertTo-Json -Depth 10) + "`n"), [Text.UTF8Encoding]::new($false))
    $verifiedMemberEvidence = Assert-NervFullChainMemberEvidence -Member $manifest.members[3] -MemberResultsDirectory $memberEvidenceRoot -RepositoryRoot $repoRoot
    Assert-Contract ($verifiedMemberEvidence.cleanup -eq 'passed' -and $verifiedMemberEvidence.diagnosticEvidence -eq 'entrypoint-evidence-verified') 'A complete entrypoint-owned cleanup artifact must satisfy the member evidence contract.'
    Remove-Item -LiteralPath $memberEvidencePath -Force
    $missingEvidenceRejected = $false
    try { Assert-NervFullChainMemberEvidence -Member $manifest.members[3] -MemberResultsDirectory $memberEvidenceRoot -RepositoryRoot $repoRoot | Out-Null }
    catch { $missingEvidenceRejected = $_.Exception.Message.Contains('cleanup evidence is missing', [StringComparison]::Ordinal) }
    Assert-Contract $missingEvidenceRejected 'Removing entrypoint cleanup evidence must fail the FullChain member contract.'

    $summaries = @($manifest.members | ForEach-Object {
        [pscustomobject]@{ memberId = $_.id; outcome = 'passed'; cleanup = 'passed'; expected = 1; discovered = 1; passed = 1; failed = 0; skipped = 0; dependencyEvidence = 'passed'; diagnosticEvidence = 'fixture-verified' }
    })
    Assert-NervFullChainTestLaneSummary -SelectedMemberIds $expectedIds -MemberSummaries $summaries
    $summaries[2].cleanup = 'failed'
    $cleanupRejected = $false
    try { Assert-NervFullChainTestLaneSummary -SelectedMemberIds $expectedIds -MemberSummaries $summaries }
    catch { $cleanupRejected = $_.Exception.Message.Contains("cleanup 'failed'", [StringComparison]::Ordinal) }
    Assert-Contract $cleanupRejected 'A FullChain cleanup failure must fail the lane summary.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}

Write-Output 'FullChain test lane contract tests passed.'
