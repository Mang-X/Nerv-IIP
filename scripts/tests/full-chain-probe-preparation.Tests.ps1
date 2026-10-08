# Script-Governance:
#   Category: check
#   SideEffects:
#     - Executes FullChain probe preparation and sales-order-demand command boundaries with leaf fixtures
#   Writes:
#     - Owned temporary probe binaries, preparation receipts and TRX fixtures under the operating-system temp directory
#   Cleanup:
#     - Removes owned temporary fixtures in finally
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/FullChainTestLane.ps1')
function Assert-Contract([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
$fixture = Join-Path ([IO.Path]::GetTempPath()) "nerv-probe-preparation-$([Guid]::NewGuid().ToString('N'))"
$receiptPath = Join-Path $fixture 'prepared.json'
$probeProject = Join-Path $fixture 'Probe.csproj'
$binaryPath = Join-Path $fixture 'bin/Release/net10.0/Probe.dll'
$calls = [Collections.Generic.List[object]]::new()
function Invoke-DotNet {
    param($Arguments, $WorkingDirectory, $TimeoutSeconds, $Name)
    $calls.Add([pscustomobject]@{ Arguments = $Arguments; Name = $Name })
    if ([string]::Equals($Arguments[1], $probeProject, [StringComparison]::Ordinal)) {
        $configurationIndex = [Array]::IndexOf($Arguments, '--configuration')
        $configuration = if ($configurationIndex -lt 0) { 'Debug' } else { $Arguments[$configurationIndex + 1] }
        $commandBinary = Join-Path $fixture "bin/$configuration/net10.0/Probe.dll"
        if ([string]::Equals($Arguments[0], 'build', [StringComparison]::Ordinal)) {
            [IO.Directory]::CreateDirectory((Split-Path $commandBinary)) | Out-Null
            [IO.File]::WriteAllText($commandBinary, "built-$configuration")
        }
        if ([string]::Equals($Arguments[0], 'test', [StringComparison]::Ordinal)) {
            if ($script:probeFailure) { throw 'probe-exit-17' }
            if (-not (Test-Path -LiteralPath $commandBinary)) { throw "No $configuration probe binary for test --no-build." }
        }
    }
}
$script:probeFailure = $false
function Invoke-ProbeFixture {
    Invoke-WithScopedEnvironment -Variables @{
        NERV_IIP_FULL_CHAIN_RESULTS_DIRECTORY = $fixture
        NERV_IIP_FULL_CHAIN_RESULT_FILE = 'probe.trx'
    } -ScriptBlock { . ([scriptblock]::Create($probeTest.Extent.Text)) }
}
try {
    [IO.Directory]::CreateDirectory((Split-Path $binaryPath)) | Out-Null
    [IO.File]::WriteAllText($probeProject, '<Project />')
    [IO.File]::WriteAllText($binaryPath, 'fresh-release')
    New-NervFullChainProbePreparation -Project $probeProject -Configuration Release -Path $receiptPath
    $content = [IO.File]::ReadAllText((Join-Path $repoRoot 'scripts/verify-erp-sales-order-demand-planning.ps1'))
    $ast = [Management.Automation.Language.Parser]::ParseInput($content, [ref]$null, [ref]$null)
    $serviceBuild = $ast.Find({ param($n) $n -is [Management.Automation.Language.IfStatementAst] -and [string]::Equals($n.Clauses[0].Item1.Extent.Text, '-not $SkipBuild', [StringComparison]::Ordinal) }, $true)
    $probePreparation = $ast.Find({ param($n) $n -is [Management.Automation.Language.AssignmentStatementAst] -and [string]::Equals($n.Left.Extent.Text, '$probeConfiguration', [StringComparison]::Ordinal) }, $true)
    Assert-Contract ($null -ne $probePreparation) 'Sales adapter must select configuration through preparation before probe execution.'
    # Execute the actual orchestration boundary, including any catch around the
    # test command and the retained TRX verdict; a bare leaf command misses those.
    $probeTest = $ast.Find({ param($n) $n -is [Management.Automation.Language.CommandAst] -and [string]::Equals($n.GetCommandName(), 'Invoke-WithScopedEnvironment', [StringComparison]::Ordinal) -and $n.Extent.Text.Contains("'man517-out-of-order-probe'", [StringComparison]::Ordinal) }, $true)
    $counterFunction = $ast.Find({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and [string]::Equals($n.Name, 'Get-Man517TrxCounter', [StringComparison]::Ordinal) }, $true)
    . ([scriptblock]::Create($counterFunction.Extent.Text))
    $databaseConnectionString = 'fixture-postgres'; $RedisConnectionString = 'fixture-redis'; $capVersion = 'fixture-cap'
    # A pre-existing successful result cannot mask a nonzero probe exit.
    [IO.File]::WriteAllText((Join-Path $fixture 'probe.trx'), '<TestRun><Results><UnitTestResult testName="Nerv.IIP.Business.FullChain.Tests.SalesOrderDemandPlanningPostgresRedisAcceptanceTests.External_process_injects_duplicate_and_out_of_order_sales_order_events" outcome="Passed" /></Results><ResultSummary><Counters total="1" executed="1" passed="1" failed="0" /></ResultSummary></TestRun>')
    $root = $fixture
    $masterDataProject = 'MasterData.csproj'; $erpProject = 'Erp.csproj'; $demandPlanningProject = 'DemandPlanning.csproj'
    $SkipBuild = $false
    $PreparedProbePath = $receiptPath
    $probeResultsDirectory = $fixture; $probeResultsFile = 'probe.trx'
    . ([scriptblock]::Create($serviceBuild.Extent.Text))
    . ([scriptblock]::Create($probePreparation.Extent.Text))
    Invoke-ProbeFixture
    Assert-Contract ($calls.Count -eq 4) 'Prepared path must build three services and execute the probe once.'
    Assert-Contract (@($calls | Where-Object { $_.Arguments[0] -eq 'build' -and $_.Arguments[1] -eq $probeProject }).Count -eq 0) 'Prepared probe must not be rebuilt.'
    $probeCall = $calls[3].Arguments
    Assert-Contract ([string]::Equals(($probeCall[0..5] -join '|'), "test|$probeProject|--configuration|Release|--no-build|--filter", [StringComparison]::Ordinal)) 'Prepared probe must execute Release without rebuilding and retain its filter.'

    $calls.Clear(); $PreparedProbePath = ''
    . ([scriptblock]::Create($probePreparation.Extent.Text))
    Assert-Contract ($calls.Count -eq 1 -and $calls[0].Arguments[0] -eq 'build' -and $calls[0].Arguments[1] -eq $probeProject -and [string]::Equals(($calls[0].Arguments[2..3] -join '|'), '--configuration|Debug', [StringComparison]::Ordinal) -and $probeConfiguration -eq 'Debug') 'Standalone adapter must build its own Debug probe.'
    Invoke-ProbeFixture
    Assert-Contract ($calls.Count -eq 2 -and [string]::Equals(($calls[1].Arguments[0..5] -join '|'), "test|$probeProject|--configuration|Debug|--no-build|--filter", [StringComparison]::Ordinal)) 'Standalone execution must consume the Debug binary it actually built.'

    foreach ($case in @('missing-receipt', 'missing-binary', 'changed-binary', 'wrong-project', 'wrong-configuration', 'expired-owner')) {
        New-NervFullChainProbePreparation -Project $probeProject -Configuration Release -Path $receiptPath
        $receipt = Get-Content $receiptPath -Raw | ConvertFrom-Json
        switch ($case) {
            'missing-receipt' { Remove-Item $receiptPath }
            'missing-binary' { Remove-Item $binaryPath }
            'changed-binary' { [IO.File]::WriteAllText($binaryPath, 'old-or-replaced-release') }
            'wrong-project' { $receipt.project = Join-Path $fixture 'Other.csproj' }
            'wrong-configuration' { $receipt.configuration = 'Debug' }
            'expired-owner' { $receipt.ownerStartedUtcTicks = 0 }
        }
        if (Test-Path $receiptPath) { $receipt | ConvertTo-Json | Set-Content $receiptPath }
        $PreparedProbePath = $receiptPath; $calls.Clear(); $failure = $null
        try { . ([scriptblock]::Create($probePreparation.Extent.Text)) } catch { $failure = $_ }
        Assert-Contract ($null -ne $failure -and $calls.Count -eq 0) "Invalid preparation '$case' must fail before probe build/test."
        [IO.File]::WriteAllText($binaryPath, 'fresh-release')
    }
    New-NervFullChainProbePreparation -Project $probeProject -Configuration Release -Path $receiptPath
    $PreparedProbePath = $receiptPath
    . ([scriptblock]::Create($probePreparation.Extent.Text))
    $script:probeFailure = $true; $failure = $null
    try { Invoke-ProbeFixture } catch { $failure = $_ }
    Assert-Contract ($null -ne $failure -and $failure.Exception.Message.Contains('probe-exit-17', [StringComparison]::Ordinal)) 'Probe failure must propagate out of the adapter command.'
    # Build-bearing discovery failure cannot produce a receipt or admit an adapter.
    Remove-Item $receiptPath
    $runnerSource = [IO.File]::ReadAllText((Join-Path $repoRoot 'scripts/run-full-chain-test-lane.ps1'))
    $runnerAst = [Management.Automation.Language.Parser]::ParseInput($runnerSource, [ref]$null, [ref]$null)
    $discoveryStatement = $runnerAst.Find({ param($n) $n -is [Management.Automation.Language.AssignmentStatementAst] -and [string]::Equals($n.Left.Extent.Text, '$discovery', [StringComparison]::Ordinal) }, $true)
    $receiptStatement = $runnerAst.Find({ param($n) $n -is [Management.Automation.Language.IfStatementAst] -and $n.Extent.Text.Contains('New-NervFullChainProbePreparation', [StringComparison]::Ordinal) }, $true)
    function Invoke-DotNetOutput { param($Name, $WorkingDirectory, $TimeoutSeconds, $Arguments) throw 'preparation-build-exit-19' }
    $fullChainProject = $probeProject; $discoveryTimeoutSeconds = 600; $preparedProbePath = $receiptPath
    $selectedIdSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    [void]$selectedIdSet.Add('sales-order-demand-planning')
    $failure = $null
    try { . ([scriptblock]::Create($discoveryStatement.Extent.Text + "`n" + $receiptStatement.Extent.Text)) } catch { $failure = $_ }
    Assert-Contract ($null -ne $failure -and $failure.Exception.Message.Contains('preparation-build-exit-19', [StringComparison]::Ordinal) -and -not (Test-Path $receiptPath)) 'Failed discovery/build must not publish a successful preparation receipt.'
    New-NervFullChainProbePreparation -Project $probeProject -Configuration Release -Path $receiptPath
    $cleanupStatement = $runnerAst.Find({ param($n) $n -is [Management.Automation.Language.TryStatementAst] -and $n.Body.Statements.Count -eq 1 -and $n.Body.Extent.Text.Contains('Remove-Item -LiteralPath $preparedProbePath', [StringComparison]::Ordinal) }, $true)
    $cleanupFailures = [Collections.Generic.List[string]]::new()
    . ([scriptblock]::Create($cleanupStatement.Extent.Text))
    Assert-Contract (-not (Test-Path $receiptPath) -and $cleanupFailures.Count -eq 0) 'Runner cleanup must expire the exact invocation receipt.'
    Write-Host 'FullChain probe preparation contracts passed.'
}
finally { if (Test-Path $fixture) { Remove-Item $fixture -Recurse -Force } }
