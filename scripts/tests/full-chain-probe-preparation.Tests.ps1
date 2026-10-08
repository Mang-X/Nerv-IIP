# Script-Governance:
#   Category: check
#   SideEffects:
#     - Executes FullChain probe preparation and both script-kind adapter command boundaries with leaf fixtures
#   Writes:
#     - Owned temporary probe binaries, preparation receipts, TRX fixtures and a child consumer under the operating-system temp directory
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
    $originalReceipt = [IO.File]::ReadAllText($receiptPath)
    $childScript = Join-Path $fixture 'consume-prepared-probe.ps1'
    [IO.File]::WriteAllText($childScript, @'
param($LibraryPath, $Project, $Receipt)
$ErrorActionPreference = 'Stop'
. $LibraryPath
Invoke-NervFullChainProbePreparation -Project $Project -WorkingDirectory (Split-Path $Project) -PreparedProbePath $Receipt
'@)
    $child = Invoke-NativeCommandOutput -Command 'pwsh' -Arguments @('-NoProfile', '-File', $childScript, (Join-Path $repoRoot 'scripts/lib/FullChainTestLane.ps1'), $probeProject, $receiptPath) -WorkingDirectory $fixture -Name 'full-chain-prepared-probe-child'
    Assert-Contract ([string]::Equals($child.Stdout.Trim(), 'Release', [StringComparison]::Ordinal)) 'A child adapter must recognize the live preparation owner without clock conversion drift.'
    foreach ($adapter in @(
        @{ Path = 'scripts/verify-erp-sales-order-demand-planning.ps1'; Services = @('MasterData.csproj', 'Erp.csproj', 'DemandPlanning.csproj'); Probe = 'man517-out-of-order-probe'; Counter = 'Get-Man517TrxCounter'; Identity = 'Nerv.IIP.Business.FullChain.Tests.SalesOrderDemandPlanningPostgresRedisAcceptanceTests.External_process_injects_duplicate_and_out_of_order_sales_order_events' },
        @{ Path = 'scripts/verify-erp-wms-delivery-completion.ps1'; Services = @('Erp.csproj', 'Wms.csproj', 'Inventory.csproj'); Probe = 'man527-replay-probe'; Counter = 'Get-Man527TrxCounter'; Identity = 'Nerv.IIP.Business.FullChain.Tests.ErpWmsDeliveryCompletionPostgresRedisAcceptanceTests.External_process_replays_completed_wms_event_without_duplicate_delivery_or_receivable_facts' }
    )) {
        $calls.Clear(); $script:probeFailure = $false
        [IO.File]::WriteAllText($receiptPath, $originalReceipt)
        $content = [IO.File]::ReadAllText((Join-Path $repoRoot $adapter.Path))
        $ast = [Management.Automation.Language.Parser]::ParseInput($content, [ref]$null, [ref]$null)
        $serviceBuild = $ast.Find({ param($n) $n -is [Management.Automation.Language.IfStatementAst] -and [string]::Equals($n.Clauses[0].Item1.Extent.Text, '-not $SkipBuild', [StringComparison]::Ordinal) }, $true)
        $probePreparation = $ast.Find({ param($n) $n -is [Management.Automation.Language.AssignmentStatementAst] -and [string]::Equals($n.Left.Extent.Text, '$probeConfiguration', [StringComparison]::Ordinal) }, $true)
        Assert-Contract ($null -ne $probePreparation) "$($adapter.Path) must select configuration through preparation before probe execution."
        # Execute the actual orchestration boundary, including any catch around the
        # test command and the retained TRX verdict; a bare leaf command misses those.
        $probeTest = $ast.Find({ param($n) $n -is [Management.Automation.Language.CommandAst] -and [string]::Equals($n.GetCommandName(), 'Invoke-WithScopedEnvironment', [StringComparison]::Ordinal) -and $n.Extent.Text.Contains($adapter.Probe, [StringComparison]::Ordinal) }, $true)
        $counterFunction = $ast.Find({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and [string]::Equals($n.Name, $adapter.Counter, [StringComparison]::Ordinal) }, $true)
        . ([scriptblock]::Create($counterFunction.Extent.Text))
        $databaseConnectionString = 'fixture-postgres'; $RedisConnectionString = 'fixture-redis'; $capVersion = 'fixture-cap'; $deliveryOrderNo = 'fixture-delivery'
        # A pre-existing successful result cannot mask a nonzero probe exit.
        $successfulTrx = '<TestRun><Results><UnitTestResult testName="' + $adapter.Identity + '" outcome="Passed" /></Results><ResultSummary><Counters total="1" executed="1" passed="1" failed="0" /></ResultSummary></TestRun>'
        [IO.File]::WriteAllText((Join-Path $fixture 'probe.trx'), $successfulTrx)
        $root = $fixture
        $masterDataProject = 'MasterData.csproj'; $erpProject = 'Erp.csproj'; $demandPlanningProject = 'DemandPlanning.csproj'; $wmsProject = 'Wms.csproj'; $inventoryProject = 'Inventory.csproj'
        $SkipBuild = $false
        $PreparedProbePath = $receiptPath
        $probeResultsDirectory = $fixture; $probeResultsFile = 'probe.trx'
        . ([scriptblock]::Create($serviceBuild.Extent.Text))
        . ([scriptblock]::Create($probePreparation.Extent.Text))
        Invoke-ProbeFixture
        Assert-Contract ($calls.Count -eq 4) 'Prepared path must build three services and execute the probe once.'
        $serviceProjects = @($calls | Where-Object { [string]::Equals($_.Arguments[0], 'build', [StringComparison]::Ordinal) } | ForEach-Object { $_.Arguments[1] })
        Assert-Contract ([string]::Equals(($serviceProjects -join '|'), ($adapter.Services -join '|'), [StringComparison]::Ordinal)) 'Probe reuse must preserve each distinct service build.'
        Assert-Contract (@($calls | Where-Object { [string]::Equals($_.Arguments[0], 'build', [StringComparison]::Ordinal) -and [string]::Equals($_.Arguments[1], $probeProject, [StringComparison]::Ordinal) }).Count -eq 0) 'Prepared probe must not be rebuilt.'
        $probeCall = $calls[3].Arguments
        Assert-Contract ([string]::Equals(($probeCall[0..5] -join '|'), "test|$probeProject|--configuration|Release|--no-build|--filter", [StringComparison]::Ordinal)) 'Prepared probe must execute Release without rebuilding and retain its filter.'

        $calls.Clear(); $PreparedProbePath = ''
        . ([scriptblock]::Create($serviceBuild.Extent.Text))
        . ([scriptblock]::Create($probePreparation.Extent.Text))
        Assert-Contract ($calls.Count -eq 4 -and [string]::Equals($calls[3].Arguments[0], 'build', [StringComparison]::Ordinal) -and [string]::Equals($calls[3].Arguments[1], $probeProject, [StringComparison]::Ordinal) -and [string]::Equals(($calls[3].Arguments[2..3] -join '|'), '--configuration|Debug', [StringComparison]::Ordinal) -and [string]::Equals($probeConfiguration, 'Debug', [StringComparison]::Ordinal)) 'Standalone adapter must build its own Debug probe.'
        Invoke-ProbeFixture
        Assert-Contract ($calls.Count -eq 5 -and [string]::Equals(($calls[4].Arguments[0..5] -join '|'), "test|$probeProject|--configuration|Debug|--no-build|--filter", [StringComparison]::Ordinal)) 'Standalone execution must consume the Debug binary it actually built.'

        foreach ($case in @('missing-receipt', 'missing-binary', 'changed-binary', 'wrong-project', 'wrong-configuration', 'expired-owner')) {
            [IO.File]::WriteAllText($receiptPath, $originalReceipt)
            $receipt = Get-Content $receiptPath -Raw | ConvertFrom-Json
            if ([string]::Equals($case, 'missing-receipt', [StringComparison]::Ordinal)) { Remove-Item $receiptPath }
            elseif ([string]::Equals($case, 'missing-binary', [StringComparison]::Ordinal)) { Remove-Item $binaryPath }
            elseif ([string]::Equals($case, 'changed-binary', [StringComparison]::Ordinal)) { [IO.File]::WriteAllText($binaryPath, 'old-or-replaced-release') }
            elseif ([string]::Equals($case, 'wrong-project', [StringComparison]::Ordinal)) { $receipt.project = Join-Path $fixture 'Other.csproj' }
            elseif ([string]::Equals($case, 'wrong-configuration', [StringComparison]::Ordinal)) { $receipt.configuration = 'Debug' }
            elseif ([string]::Equals($case, 'expired-owner', [StringComparison]::Ordinal)) { $receipt.ownerStartIdentity = 'expired-owner' }

            if (Test-Path $receiptPath) { $receipt | ConvertTo-Json | Set-Content $receiptPath }
            $PreparedProbePath = $receiptPath; $calls.Clear(); $failure = $null
            try { . ([scriptblock]::Create($probePreparation.Extent.Text)) } catch { $failure = $_ }
            Assert-Contract ($null -ne $failure -and $calls.Count -eq 0) "Invalid preparation '$case' must fail before probe build/test."
            [IO.File]::WriteAllText($binaryPath, 'fresh-release')
        }
        [IO.File]::WriteAllText($receiptPath, $originalReceipt)
        $PreparedProbePath = $receiptPath
        . ([scriptblock]::Create($probePreparation.Extent.Text))
        $script:probeFailure = $true; $failure = $null
        try { Invoke-ProbeFixture } catch { $failure = $_ }
        Assert-Contract ($null -ne $failure -and $failure.Exception.Message.Contains('probe-exit-17', [StringComparison]::Ordinal)) 'Probe failure must propagate out of the adapter command.'
        $script:probeFailure = $false
        foreach ($case in @('missing-trx', 'wrong-test', 'failed-counters')) {
            if ([string]::Equals($case, 'missing-trx', [StringComparison]::Ordinal)) { Remove-Item (Join-Path $fixture 'probe.trx') }
            elseif ([string]::Equals($case, 'wrong-test', [StringComparison]::Ordinal)) { [IO.File]::WriteAllText((Join-Path $fixture 'probe.trx'), $successfulTrx.Replace($adapter.Identity, 'Another.Test')) }
            else { [IO.File]::WriteAllText((Join-Path $fixture 'probe.trx'), $successfulTrx.Replace('passed="1" failed="0"', 'passed="0" failed="1"')) }
            $failure = $null
            try { Invoke-ProbeFixture } catch { $failure = $_ }
            Assert-Contract ($null -ne $failure) "$($adapter.Path) must reject $case after probe execution."
            [IO.File]::WriteAllText((Join-Path $fixture 'probe.trx'), $successfulTrx)
        }
    }
    # Build-bearing discovery failure cannot produce a receipt or admit an adapter.
    Remove-Item $receiptPath
    $runnerSource = [IO.File]::ReadAllText((Join-Path $repoRoot 'scripts/run-full-chain-test-lane.ps1'))
    $runnerAst = [Management.Automation.Language.Parser]::ParseInput($runnerSource, [ref]$null, [ref]$null)
    $discoveryStatement = $runnerAst.Find({ param($n) $n -is [Management.Automation.Language.AssignmentStatementAst] -and [string]::Equals($n.Left.Extent.Text, '$discovery', [StringComparison]::Ordinal) }, $true)
    $receiptStatement = $runnerAst.Find({ param($n) $n -is [Management.Automation.Language.IfStatementAst] -and $n.Extent.Text.Contains('New-NervFullChainProbePreparation', [StringComparison]::Ordinal) }, $true)
    function Invoke-DotNetOutput { param($Name, $WorkingDirectory, $TimeoutSeconds, $Arguments) throw 'preparation-build-exit-19' }
    $probeReuseMemberId = 'sales-order-demand-planning'; $deliveryProbeReuseMemberId = 'erp-wms-delivery-completion'
    $fullChainProject = $probeProject; $discoveryTimeoutSeconds = 600; $preparedProbePath = $receiptPath
    $selectedIdSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    [void]$selectedIdSet.Add('sales-order-demand-planning')
    $failure = $null
    try { . ([scriptblock]::Create($discoveryStatement.Extent.Text + "`n" + $receiptStatement.Extent.Text)) } catch { $failure = $_ }
    Assert-Contract ($null -ne $failure -and $failure.Exception.Message.Contains('preparation-build-exit-19', [StringComparison]::Ordinal) -and -not (Test-Path $receiptPath)) 'Failed discovery/build must not publish a successful preparation receipt.'
    $repoRoot = $fixture; $fullChainProject = 'Probe.csproj'
    foreach ($selection in @(@('erp-wms-delivery-completion'), @('sales-order-demand-planning', 'erp-wms-delivery-completion'))) {
        $selectedIdSet.Clear()
        foreach ($id in $selection) { [void]$selectedIdSet.Add($id) }
        . ([scriptblock]::Create($receiptStatement.Extent.Text))
        Assert-Contract (Test-Path -LiteralPath $receiptPath) 'ERP/WMS selection must receive preparation even without the sales-order member.'
        Remove-Item $receiptPath
    }
    New-NervFullChainProbePreparation -Project $probeProject -Configuration Release -Path $receiptPath
    $cleanupStatement = $runnerAst.Find({ param($n) $n -is [Management.Automation.Language.TryStatementAst] -and $n.Body.Statements.Count -eq 1 -and $n.Body.Extent.Text.Contains('Remove-Item -LiteralPath $preparedProbePath', [StringComparison]::Ordinal) }, $true)
    $cleanupFailures = [Collections.Generic.List[string]]::new()
    . ([scriptblock]::Create($cleanupStatement.Extent.Text))
    Assert-Contract (-not (Test-Path $receiptPath) -and $cleanupFailures.Count -eq 0) 'Runner cleanup must expire the exact invocation receipt.'
    Write-Host 'FullChain probe preparation contracts passed.'
}
finally { if (Test-Path $fixture) { Remove-Item $fixture -Recurse -Force } }
