# Script-Governance:
#   Category: check
#   SideEffects:
#     - Starts and stops bounded test-owned PowerShell process trees
#     - Creates and removes isolated OS temporary fixtures
#   Writes:
#     - Isolated directories under the OS temporary directory
#   Cleanup:
#     - Stops exact test-owned process identities and removes temporary fixtures
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
. (Join-Path $repoRoot 'scripts/lib/FullStackProcessRuntime.ps1')

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-OrdinalEqual([AllowNull()] $Actual, [AllowNull()] $Expected, [string] $Message) {
    Assert-True ([string]::Equals([string] $Actual, [string] $Expected, [StringComparison]::Ordinal)) `
        "$Message Expected '$Expected', actual '$Actual'."
}

function Assert-IntSequence([int[]] $Actual, [int[]] $Expected, [string] $Message) {
    Assert-OrdinalEqual (@($Actual) -join ',') (@($Expected) -join ',') $Message
}

function New-TestProcessRecord {
    param(
        [int] $ProcessId,
        [int] $ParentPid,
        [string] $StartTimeUtc,
        [string] $Name = "fixture-$ProcessId",
        [long] $PrecisionTicks = 10
    )

    return [pscustomobject][ordered]@{
        pid = $ProcessId
        ppid = $ParentPid
        processStartTimeUtc = $StartTimeUtc
        processStartTimePrecisionTicks = $PrecisionTicks
        processName = $Name
        provenance = 'synthetic-contract-fixture'
    }
}

function New-TestInventory {
    param(
        [object[]] $Records,
        [bool] $Complete = $true,
        [string[]] $Diagnostics = @()
    )

    return [pscustomobject][ordered]@{
        complete = $Complete
        records = @($Records)
        platform = 'synthetic'
        provenance = 'synthetic-contract-fixture'
        diagnostics = @($Diagnostics)
    }
}

function Copy-TestRecords([object[]] $Records) {
    return @($Records | ForEach-Object {
        New-TestProcessRecord `
            -ProcessId ([int] $_.pid) `
            -ParentPid ([int] $_.ppid) `
            -StartTimeUtc ([string] $_.processStartTimeUtc) `
            -Name ([string] $_.processName) `
            -PrecisionTicks ([long] $_.processStartTimePrecisionTicks)
    })
}

function Test-ExactFixtureIdentity([object] $Identity) {
    try {
        $expected = [DateTimeOffset]::ParseExact(
            [string] $Identity.processStartTimeUtc,
            'O',
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind).UtcDateTime
        $actual = (Get-Process -Id ([int] $Identity.pid) -ErrorAction Stop).StartTime.ToUniversalTime()
        return [Math]::Abs(($actual - $expected).TotalMilliseconds) -lt 1
    }
    catch {
        return $false
    }
}

function Stop-ExactFixtureIdentity([object] $Identity) {
    if (Test-ExactFixtureIdentity -Identity $Identity) {
        Stop-Process -Id ([int] $Identity.pid) -Force -ErrorAction SilentlyContinue
    }
}

function New-EncodedPowerShellCommand([string] $Text) {
    return [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Text))
}

function Start-TestOwnedProcess([string] $EncodedCommand) {
    $processPath = (Get-Process -Id $PID -ErrorAction Stop).Path
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $processPath
    $startInfo.UseShellExecute = $false
    [void] $startInfo.ArgumentList.Add('-NoProfile')
    [void] $startInfo.ArgumentList.Add('-NonInteractive')
    [void] $startInfo.ArgumentList.Add('-EncodedCommand')
    [void] $startInfo.ArgumentList.Add($EncodedCommand)
    return [Diagnostics.Process]::Start($startInfo)
}

function Wait-TestFixtureFile([string] $Path, [timespan] $Timeout) {
    $deadline = [DateTimeOffset]::UtcNow.Add($Timeout)
    while (-not [IO.File]::Exists($Path) -and [DateTimeOffset]::UtcNow -lt $deadline) {
        [Threading.Thread]::Sleep(20)
    }
    Assert-True ([IO.File]::Exists($Path)) "Fixture identity '$Path' was not published within the bound."
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

# F1a frozen member: inventory-tree-and-drain.
$member = 'inventory-tree-and-drain'
Write-Host "Running $member"

$baseTime = '2026-08-23T00:00:00.1234560Z'
$nextTime = '2026-08-23T00:00:01.1234560Z'
$root = New-TestProcessRecord -ProcessId 41001 -ParentPid 1 -StartTimeUtc $baseTime -Name 'fixture-root'
$child = New-TestProcessRecord -ProcessId 41002 -ParentPid 41001 -StartTimeUtc $baseTime -Name 'fixture-child'
$grandchild = New-TestProcessRecord -ProcessId 41003 -ParentPid 41002 -StartTimeUtc $baseTime -Name 'fixture-grandchild'
$treeInventory = New-TestInventory -Records @($root, $child, $grandchild)

Assert-OrdinalEqual `
    (Get-NervFullStackProcessIdentityState -Identity $root -InventorySnapshot $treeInventory) `
    'Active' `
    'Exact PID/start-time must be Active.'
Assert-OrdinalEqual `
    (Get-NervFullStackProcessIdentityState -Identity ([pscustomobject]@{ pid = $root.pid }) -InventorySnapshot $treeInventory) `
    'Unknown' `
    'PID-only identity must be Unknown.'
Assert-OrdinalEqual `
    (Get-NervFullStackProcessIdentityState -Identity ([pscustomobject]@{ pid = $root.pid; processStartTimeUtc = $nextTime }) -InventorySnapshot $treeInventory) `
    'Mismatched' `
    'A reused PID with another start-time must be Mismatched.'
Assert-OrdinalEqual `
    (Get-NervFullStackProcessIdentityState -Identity ([pscustomobject]@{ pid = 41999; processStartTimeUtc = $baseTime }) -InventorySnapshot $treeInventory) `
    'Absent' `
    'An absent PID in a complete inventory must be Absent.'
Assert-OrdinalEqual `
    (Get-NervFullStackProcessIdentityState -Identity $root -InventorySnapshot (New-TestInventory -Records @($root) -Complete $false)) `
    'Unknown' `
    'An incomplete inventory must yield Unknown.'

$script:NervFullStackProcessRuntimeInventoryAction = $null
$script:NervFullStackProcessRuntimeStopAction = $null
try {
    # The private test seam must invoke the supplied script blocks and preserve their arguments/results.
    $script:seamInventoryCalls = 0
    $script:seamStopPid = 0
    $script:NervFullStackProcessRuntimeInventoryAction = {
        $script:seamInventoryCalls++
        New-TestInventory -Records @($root)
    }
    $script:NervFullStackProcessRuntimeStopAction = {
        param([int] $ExactPid)
        $script:seamStopPid = $ExactPid
    }
    $seamInventory = Invoke-NervFullStackProcessRuntimeInventory
    Invoke-NervFullStackProcessRuntimeStop -ProcessId 41997
    Assert-True ($script:seamInventoryCalls -eq 1) 'The inventory seam must invoke its supplied script block exactly once.'
    Assert-True ([bool] $seamInventory.complete) 'The inventory seam must preserve the supplied action result.'
    Assert-True ($script:seamStopPid -eq 41997) 'The stop seam must pass the exact process ID to its supplied script block.'

    # Synthetic behavior fixture: exact root/child/grandchild, child-first order, and complete exit readback.
    $script:testRecords = Copy-TestRecords @($root, $child, $grandchild)
    $script:testStopOrder = [Collections.Generic.List[int]]::new()
    $script:NervFullStackProcessRuntimeInventoryAction = {
        New-TestInventory -Records (Copy-TestRecords $script:testRecords)
    }
    $script:NervFullStackProcessRuntimeStopAction = {
        param([int] $ExactPid)
        $script:testStopOrder.Add($ExactPid)
        $script:testRecords = @($script:testRecords | Where-Object { [int] $_.pid -ne $ExactPid })
    }

    $treeResult = Stop-NervFullStackOwnedProcessTree `
        -RootIdentity $root `
        -ExcludedIdentities @() `
        -MaxPasses 4 `
        -Timeout ([timespan]::FromSeconds(1))
    Assert-True $treeResult.complete 'A fully stopped exact tree must be complete.'
    Assert-IntSequence $script:testStopOrder.ToArray() @(41003, 41002, 41001) 'Tree stop order must be child-first.'
    Assert-IntSequence $treeResult.StoppedProcessIds @(41003, 41002, 41001) 'Stopped IDs must preserve destructive child-first order.'
    Assert-True (@($treeResult.FailedProcessIds).Count -eq 0) 'A complete tree must have no failed PIDs.'
    Assert-True (@($treeResult.SurvivingProcessIds).Count -eq 0) 'A complete tree must have no survivors.'

    # Exact exclusions only: a matching identity is skipped; a same-name foreign identity cannot exclude a child.
    $script:testRecords = Copy-TestRecords @($root, $child)
    $script:testStopOrder.Clear()
    $excluded = New-TestProcessRecord -ProcessId 41002 -ParentPid 41001 -StartTimeUtc $baseTime -Name 'fixture-child'
    $excludedResult = Stop-NervFullStackOwnedProcessTree `
        -RootIdentity $root `
        -ExcludedIdentities @($excluded) `
        -MaxPasses 3 `
        -Timeout ([timespan]::FromSeconds(1))
    Assert-True $excludedResult.complete 'An exact excluded identity must not become a survivor.'
    Assert-IntSequence $script:testStopOrder.ToArray() @(41001) 'The exact excluded identity must receive no stop call.'

    $script:testRecords = Copy-TestRecords @($root, $child)
    $script:testStopOrder.Clear()
    $sameNameForeignIdentity = New-TestProcessRecord -ProcessId 41998 -ParentPid 1 -StartTimeUtc $baseTime -Name 'fixture-child'
    $nameResult = Stop-NervFullStackOwnedProcessTree `
        -RootIdentity $root `
        -ExcludedIdentities @($sameNameForeignIdentity) `
        -MaxPasses 3 `
        -Timeout ([timespan]::FromSeconds(1))
    Assert-True $nameResult.complete 'A same-name foreign exclusion must not affect the owned tree.'
    Assert-IntSequence $script:testStopOrder.ToArray() @(41002, 41001) 'Exclusions must not use process names.'

    # A late descendant appears after the first child is stopped and must be caught before root stop.
    $late = New-TestProcessRecord -ProcessId 41004 -ParentPid 41001 -StartTimeUtc $baseTime -Name 'fixture-late'
    $script:testRecords = Copy-TestRecords @($root, $child)
    $script:testStopOrder.Clear()
    $script:lateAdded = $false
    $script:NervFullStackProcessRuntimeStopAction = {
        param([int] $ExactPid)
        $script:testStopOrder.Add($ExactPid)
        $script:testRecords = @($script:testRecords | Where-Object { [int] $_.pid -ne $ExactPid })
        if ($ExactPid -eq 41002 -and -not $script:lateAdded) {
            $script:testRecords = @($script:testRecords) + (Copy-TestRecords @($late))
            $script:lateAdded = $true
        }
    }
    $lateResult = Stop-NervFullStackOwnedProcessTree `
        -RootIdentity $root `
        -ExcludedIdentities @() `
        -MaxPasses 4 `
        -Timeout ([timespan]::FromSeconds(1))
    Assert-True $lateResult.complete 'A bounded later pass must stop a late descendant.'
    Assert-IntSequence $script:testStopOrder.ToArray() @(41002, 41004, 41001) 'A late descendant must be stopped before the root.'

    # Candidate PID reuse after the tree freeze is caught by the exact pre-stop revalidation.
    $reusedChild = New-TestProcessRecord -ProcessId 41002 -ParentPid 41001 -StartTimeUtc $nextTime -Name 'fixture-child'
    $script:testStopOrder.Clear()
    $script:inventoryCallCount = 0
    $script:NervFullStackProcessRuntimeInventoryAction = {
        $script:inventoryCallCount++
        if ($script:inventoryCallCount -eq 1) { return New-TestInventory -Records @($root, $child) }
        return New-TestInventory -Records @($root, $reusedChild)
    }
    $candidateReuseResult = Stop-NervFullStackOwnedProcessTree `
        -RootIdentity $root `
        -ExcludedIdentities @() `
        -MaxPasses 1 `
        -Timeout ([timespan]::FromMilliseconds(200))
    Assert-True (-not $candidateReuseResult.complete) 'Candidate PID reuse must be non-green.'
    Assert-True ($script:testStopOrder.Count -eq 0) 'Candidate PID reuse immediately before stop must cause zero destructive calls.'
    Assert-IntSequence $candidateReuseResult.IdentityMismatchProcessIds @(41002) 'Candidate PID reuse must be reported.'

    # Root mismatch and incomplete inventory must fail closed with zero destructive calls.
    $script:testStopOrder.Clear()
    $script:NervFullStackProcessRuntimeInventoryAction = { New-TestInventory -Records @($root) -Complete $false -Diagnostics @('fixture-incomplete') }
    $incompleteResult = Stop-NervFullStackOwnedProcessTree `
        -RootIdentity $root `
        -ExcludedIdentities @() `
        -MaxPasses 2 `
        -Timeout ([timespan]::FromMilliseconds(100))
    Assert-True (-not $incompleteResult.complete) 'Incomplete inventory must be non-green.'
    Assert-OrdinalEqual $incompleteResult.disposition 'Blocked' 'Incomplete pass inventory must be blocked.'
    Assert-True (@($incompleteResult.diagnostics).Contains('process:inventory-incomplete')) 'Incomplete pass inventory must retain its pass-level diagnostic.'
    Assert-True ($script:testStopOrder.Count -eq 0) 'Incomplete inventory must cause zero destructive calls.'

    $script:inventoryCallCount = 0
    $script:NervFullStackProcessRuntimeInventoryAction = {
        $script:inventoryCallCount++
        if ($script:inventoryCallCount -eq 1) { return New-TestInventory -Records @($root, $child) }
        return New-TestInventory -Records @($root, $child) -Complete $false -Diagnostics @('fixture-revalidation-incomplete')
    }
    $revalidationIncompleteResult = Stop-NervFullStackOwnedProcessTree `
        -RootIdentity $root `
        -ExcludedIdentities @() `
        -MaxPasses 2 `
        -Timeout ([timespan]::FromMilliseconds(100))
    Assert-True (-not $revalidationIncompleteResult.complete) 'Incomplete revalidation inventory must be non-green.'
    Assert-OrdinalEqual $revalidationIncompleteResult.disposition 'Blocked' 'Incomplete revalidation inventory must be blocked.'
    Assert-True (@($revalidationIncompleteResult.diagnostics).Contains('process:revalidation-inventory-incomplete')) 'Incomplete revalidation must retain its phase diagnostic.'
    Assert-True ($script:testStopOrder.Count -eq 0) 'Incomplete revalidation inventory must cause zero destructive calls.'

    $script:NervFullStackProcessRuntimeInventoryAction = { New-TestInventory -Records @((New-TestProcessRecord -ProcessId 41001 -ParentPid 1 -StartTimeUtc $nextTime)) }
    $mismatchResult = Stop-NervFullStackOwnedProcessTree `
        -RootIdentity $root `
        -ExcludedIdentities @() `
        -MaxPasses 2 `
        -Timeout ([timespan]::FromMilliseconds(100))
    Assert-True (-not $mismatchResult.complete) 'Root PID reuse must be non-green.'
    Assert-True ($script:testStopOrder.Count -eq 0) 'Root PID reuse must cause zero destructive calls.'
    Assert-IntSequence $mismatchResult.IdentityMismatchProcessIds @(41001) 'Root PID reuse must be reported.'

    $script:NervFullStackProcessRuntimeInventoryAction = { New-TestInventory -Records @($root) }
    $unknownRoot = [pscustomobject]@{ pid = 41001; processStartTimeUtc = 'not-a-time' }
    $unknownResult = Stop-NervFullStackOwnedProcessTree `
        -RootIdentity $unknownRoot `
        -ExcludedIdentities @() `
        -MaxPasses 2 `
        -Timeout ([timespan]::FromMilliseconds(100))
    Assert-True (-not $unknownResult.complete) 'Unknown root identity must be non-green.'
    Assert-True ($script:testStopOrder.Count -eq 0) 'Unknown root identity must cause zero destructive calls.'

    # A no-op destructive action produces survivors and exits within the caller bound.
    $script:testRecords = Copy-TestRecords @($root, $child)
    $script:testStopOrder.Clear()
    $script:NervFullStackProcessRuntimeInventoryAction = { New-TestInventory -Records (Copy-TestRecords $script:testRecords) }
    $script:NervFullStackProcessRuntimeStopAction = {
        param([int] $ExactPid)
        $script:testStopOrder.Add($ExactPid)
    }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $survivorResult = Stop-NervFullStackOwnedProcessTree `
        -RootIdentity $root `
        -ExcludedIdentities @() `
        -MaxPasses 2 `
        -Timeout ([timespan]::FromMilliseconds(120))
    $watch.Stop()
    Assert-True (-not $survivorResult.complete) 'A surviving process must make the result non-green.'
    Assert-True ($watch.Elapsed -lt [timespan]::FromSeconds(1)) 'Exit wait must remain bounded by the caller timeout.'
    Assert-True (@($survivorResult.SurvivingProcessIds).Count -gt 0) 'Surviving PIDs must be reported.'
}
finally {
    $script:NervFullStackProcessRuntimeInventoryAction = $null
    $script:NervFullStackProcessRuntimeStopAction = $null
}

# Caller-owned stream handles: complete, timeout with redacted partial output, fault, and not-applicable.
$completeDrain = Wait-NervFullStackProcessOutputDrain `
    -StreamHandles @([pscustomobject]@{ name = 'stdout'; completion = [Threading.Tasks.Task]::FromResult[string]('ready token=plain-secret') }) `
    -Timeout ([timespan]::FromSeconds(1))
Assert-True $completeDrain.complete 'A completed readable stream must be green.'
Assert-OrdinalEqual $completeDrain.disposition 'Complete' 'Completed stream disposition must be Complete.'
Assert-True (-not $completeDrain.partialOutput) 'A completed drain must not report partial output.'
Assert-True $completeDrain.output[0].text.Contains('<redacted>', [StringComparison]::Ordinal) 'Completed output must be redacted.'
Assert-True (-not $completeDrain.output[0].text.Contains('plain-secret', [StringComparison]::Ordinal)) 'Completed output must not retain a token value.'

$sensitiveCompletedText = @'
safe-prefix {"token":"quoted-json-secret","authorization":"Bearer quoted-bearer-secret"}
-----BEGIN PRIVATE KEY-----
pem-secret-material
-----END PRIVATE KEY-----
https://url-user:url-password@example.test/path safe-suffix
'@
$sensitiveCompletedDrain = Wait-NervFullStackProcessOutputDrain `
    -StreamHandles @([pscustomobject]@{ name = 'stdout'; completion = [Threading.Tasks.Task]::FromResult[string]($sensitiveCompletedText) }) `
    -Timeout ([timespan]::FromSeconds(1))
$sensitiveCompletedOutput = [string] $sensitiveCompletedDrain.output[0].text
Assert-True $sensitiveCompletedDrain.complete 'A completed sensitive stream must remain green after redaction.'
Assert-True $sensitiveCompletedOutput.Contains('safe-prefix', [StringComparison]::Ordinal) 'Redaction must preserve non-sensitive completed output.'
Assert-True $sensitiveCompletedOutput.Contains('safe-suffix', [StringComparison]::Ordinal) 'Redaction must preserve trailing non-sensitive completed output.'
foreach ($secretValue in @('quoted-json-secret', 'quoted-bearer-secret', 'pem-secret-material', 'url-user', 'url-password')) {
    Assert-True (-not $sensitiveCompletedOutput.Contains($secretValue, [StringComparison]::Ordinal)) "Completed output must redact '$secretValue'."
}

$pending = [Threading.Tasks.TaskCompletionSource[string]]::new()
$script:partialDrainText = 'prefix password=hunter2'
$partialDrain = Wait-NervFullStackProcessOutputDrain `
    -StreamHandles @([pscustomobject]@{ name = 'stderr'; completion = $pending.Task; snapshotAction = { $script:partialDrainText } }) `
    -Timeout ([timespan]::FromMilliseconds(60))
Assert-True (-not $partialDrain.complete) 'A drain timeout must be non-green.'
Assert-OrdinalEqual $partialDrain.disposition 'Timeout' 'Pending stream disposition must be Timeout.'
Assert-True $partialDrain.partialOutput 'A timeout with buffered text must report partial output.'
Assert-True $partialDrain.output[0].text.Contains('<redacted>', [StringComparison]::Ordinal) 'Partial output must be retained in redacted form.'
Assert-True (-not $partialDrain.output[0].text.Contains('hunter2', [StringComparison]::Ordinal)) 'Partial output must not retain a password value.'

$pendingSensitive = [Threading.Tasks.TaskCompletionSource[string]]::new()
$script:sensitivePartialDrainText = 'partial-safe {"client_secret":"partial-json-secret"} https://partial-user:partial-password@example.test/waiting'
$sensitivePartialDrain = Wait-NervFullStackProcessOutputDrain `
    -StreamHandles @([pscustomobject]@{ name = 'stderr'; completion = $pendingSensitive.Task; snapshotAction = { $script:sensitivePartialDrainText } }) `
    -Timeout ([timespan]::FromMilliseconds(60))
$sensitivePartialOutput = [string] $sensitivePartialDrain.output[0].text
Assert-True (-not $sensitivePartialDrain.complete) 'A sensitive drain timeout must remain non-green.'
Assert-OrdinalEqual $sensitivePartialDrain.disposition 'Timeout' 'A sensitive partial stream must retain timeout disposition.'
Assert-True $sensitivePartialDrain.partialOutput 'A sensitive timeout must retain redacted partial output.'
Assert-True $sensitivePartialOutput.Contains('partial-safe', [StringComparison]::Ordinal) 'Redaction must preserve non-sensitive partial output.'
foreach ($secretValue in @('partial-json-secret', 'partial-user', 'partial-password')) {
    Assert-True (-not $sensitivePartialOutput.Contains($secretValue, [StringComparison]::Ordinal)) "Partial output must redact '$secretValue'."
}

$faultedDrain = Wait-NervFullStackProcessOutputDrain `
    -StreamHandles @([pscustomobject]@{ name = 'stdout'; completion = [Threading.Tasks.Task]::FromException[string]([InvalidOperationException]::new('token=unsafe')) }) `
    -Timeout ([timespan]::FromSeconds(1))
Assert-True (-not $faultedDrain.complete) 'A faulted drain must be non-green.'
Assert-OrdinalEqual $faultedDrain.disposition 'Failed' 'A faulted stream disposition must be Failed.'
Assert-True (-not ([string]::Join(' ', @($faultedDrain.diagnostics))).Contains('unsafe', [StringComparison]::Ordinal)) 'Drain diagnostics must be redacted.'

$notApplicableDrain = Wait-NervFullStackProcessOutputDrain -StreamHandles @() -Timeout ([timespan]::FromMilliseconds(20))
Assert-True (-not $notApplicableDrain.complete) 'No readable stream must not fabricate a green drain.'
Assert-OrdinalEqual $notApplicableDrain.disposition 'NotApplicable' 'No readable stream must be explicit not-applicable.'

# Real primitive fixture. It is test-owned and bounded; it does not represent an E2-activated platform checkpoint.
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-fullstack-process-runtime-$([Guid]::NewGuid().ToString('N'))"
[IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
$rootIdentity = $null
$childIdentity = $null
$grandchildIdentity = $null
try {
    $rootPath = Join-Path $fixtureRoot 'root.json'
    $childPath = Join-Path $fixtureRoot 'child.json'
    $grandchildPath = Join-Path $fixtureRoot 'grandchild.json'

    $grandchildCommand = @"
`$p = Get-Process -Id `$PID
[IO.File]::WriteAllText('$($grandchildPath.Replace("'", "''"))', ([pscustomobject]@{ pid = `$PID; processStartTimeUtc = `$p.StartTime.ToUniversalTime().ToString('O') } | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new(`$false))
while (`$true) { [Threading.Thread]::Sleep(100) }
"@
    $grandchildEncoded = New-EncodedPowerShellCommand $grandchildCommand
    $processPathLiteral = ((Get-Process -Id $PID).Path).Replace("'", "''")
    $childCommand = @"
`$p = Get-Process -Id `$PID
[IO.File]::WriteAllText('$($childPath.Replace("'", "''"))', ([pscustomobject]@{ pid = `$PID; processStartTimeUtc = `$p.StartTime.ToUniversalTime().ToString('O') } | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new(`$false))
`$si = [Diagnostics.ProcessStartInfo]::new(); `$si.FileName = '$processPathLiteral'; `$si.UseShellExecute = `$false
[void] `$si.ArgumentList.Add('-NoProfile'); [void] `$si.ArgumentList.Add('-NonInteractive'); [void] `$si.ArgumentList.Add('-EncodedCommand'); [void] `$si.ArgumentList.Add('$grandchildEncoded')
[void] [Diagnostics.Process]::Start(`$si)
while (`$true) { [Threading.Thread]::Sleep(100) }
"@
    $childEncoded = New-EncodedPowerShellCommand $childCommand
    $rootCommand = @"
`$p = Get-Process -Id `$PID
[IO.File]::WriteAllText('$($rootPath.Replace("'", "''"))', ([pscustomobject]@{ pid = `$PID; processStartTimeUtc = `$p.StartTime.ToUniversalTime().ToString('O') } | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new(`$false))
`$si = [Diagnostics.ProcessStartInfo]::new(); `$si.FileName = '$processPathLiteral'; `$si.UseShellExecute = `$false
[void] `$si.ArgumentList.Add('-NoProfile'); [void] `$si.ArgumentList.Add('-NonInteractive'); [void] `$si.ArgumentList.Add('-EncodedCommand'); [void] `$si.ArgumentList.Add('$childEncoded')
[void] [Diagnostics.Process]::Start(`$si)
while (`$true) { [Threading.Thread]::Sleep(100) }
"@

    $rootProcess = Start-TestOwnedProcess -EncodedCommand (New-EncodedPowerShellCommand $rootCommand)
    $rootIdentity = Wait-TestFixtureFile -Path $rootPath -Timeout ([timespan]::FromSeconds(10))
    $childIdentity = Wait-TestFixtureFile -Path $childPath -Timeout ([timespan]::FromSeconds(10))
    $grandchildIdentity = Wait-TestFixtureFile -Path $grandchildPath -Timeout ([timespan]::FromSeconds(10))

    $realInventory = Get-NervFullStackProcessInventory
    Assert-True $realInventory.complete 'The current platform inventory must be complete for the test-owned fixture.'
    Assert-OrdinalEqual `
        (Get-NervFullStackProcessIdentityState -Identity $rootIdentity -InventorySnapshot $realInventory) `
        'Active' `
        'The real test-owned root PID/start-time must be Active.'

    $realTreeResult = Stop-NervFullStackOwnedProcessTree `
        -RootIdentity $rootIdentity `
        -ExcludedIdentities @() `
        -MaxPasses 5 `
        -Timeout ([timespan]::FromSeconds(5))
    Assert-True $realTreeResult.complete 'The real test-owned root/child/grandchild tree must stop completely.'
    Assert-True (-not (Test-ExactFixtureIdentity $rootIdentity)) 'The real root must be absent after cleanup.'
    Assert-True (-not (Test-ExactFixtureIdentity $childIdentity)) 'The real child must be absent after cleanup.'
    Assert-True (-not (Test-ExactFixtureIdentity $grandchildIdentity)) 'The real grandchild must be absent after cleanup.'
}
finally {
    foreach ($identity in @($grandchildIdentity, $childIdentity, $rootIdentity)) {
        if ($null -ne $identity) { Stop-ExactFixtureIdentity -Identity $identity }
    }
    if ([IO.Directory]::Exists($fixtureRoot)) {
        [IO.Directory]::Delete($fixtureRoot, $true)
    }
}

Write-Host "Full-stack process runtime tests passed: $member"
