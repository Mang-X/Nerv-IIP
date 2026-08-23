# Script-Governance:
#   Category: library
#   SideEffects:
#     - Enumerates operating-system processes when requested
#     - Stops only caller-authorized exact PID/start-time process trees when requested
#   Writes:
#     - None
#   Cleanup:
#     - Performs bounded child-first cleanup of exact fullstack-owned process identities
#   Requires:
#     - PowerShell 7
#     - scripts/lib/ScriptAutomation.ps1

Set-StrictMode -Version Latest

if ($null -eq (Get-Command Invoke-NativeCommandOutput -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'ScriptAutomation.ps1')
}

# These script-block seams are deliberately not parameters on the production entrypoints. They let
# the contract suite inject an inventory and count destructive actions without exposing a TestOnly
# authority replacement in the public API.
$script:NervFullStackProcessRuntimeInventoryAction = $null
$script:NervFullStackProcessRuntimeStopAction = $null

if (-not ('Nerv.IIP.FullStackProcessRuntime.NativeMethods' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace Nerv.IIP.FullStackProcessRuntime
{
    public static class NativeMethods
    {
        [DllImport("libc", SetLastError = true)]
        public static extern long sysconf(int name);
    }
}
'@
}

function Get-NervFullStackProcessRuntimeProperty {
    param(
        [AllowNull()]
        [object] $InputObject,

        [Parameter(Mandatory)]
        [string] $Name
    )

    if ($null -eq $InputObject) { return $null }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function ConvertTo-NervFullStackProcessUtcTime {
    param(
        [AllowNull()]
        [object] $Value
    )

    if ($null -eq $Value) { return $null }
    if ($Value -is [DateTimeOffset]) { return ([DateTimeOffset] $Value).UtcDateTime }
    if ($Value -is [DateTime]) { return ([DateTime] $Value).ToUniversalTime() }

    $parsed = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParseExact(
        [string] $Value,
        'O',
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind,
        [ref] $parsed)) {
        return $null
    }

    return $parsed.UtcDateTime
}

function ConvertTo-NervFullStackNormalizedProcessTicks {
    param(
        [Parameter(Mandatory)]
        [DateTime] $UtcTime,

        [Parameter(Mandatory)]
        [long] $PrecisionTicks
    )

    if ($PrecisionTicks -lt 1) { return $null }
    $ticks = $UtcTime.ToUniversalTime().Ticks
    return $ticks - ($ticks % $PrecisionTicks)
}

function New-NervFullStackProcessInventoryResult {
    param(
        [Parameter(Mandatory)]
        [bool] $Complete,

        [object[]] $Records = @(),

        [Parameter(Mandatory)]
        [string] $Platform,

        [Parameter(Mandatory)]
        [string] $Provenance,

        [string[]] $Diagnostics = @()
    )

    return [pscustomobject][ordered]@{
        complete = $Complete
        records = @($Records)
        platform = $Platform
        provenance = $Provenance
        diagnostics = @($Diagnostics)
        observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    }
}

function Get-NervFullStackWindowsProcessInventory {
    $records = [Collections.Generic.List[object]]::new()
    $diagnostics = [Collections.Generic.List[string]]::new()

    try {
        $processes = @(Get-CimInstance -ClassName Win32_Process -ErrorAction Stop)
    }
    catch {
        return New-NervFullStackProcessInventoryResult `
            -Complete $false `
            -Platform 'Windows' `
            -Provenance 'windows-cim:Win32_Process' `
            -Diagnostics @("Windows CIM inventory failed: $($_.Exception.Message)")
    }

    foreach ($process in $processes) {
        $pidValue = 0
        $parentValue = 0
        $startTime = ConvertTo-NervFullStackProcessUtcTime -Value $process.CreationDate
        if (
            -not [int]::TryParse([string] $process.ProcessId, [ref] $pidValue) -or
            -not [int]::TryParse([string] $process.ParentProcessId, [ref] $parentValue) -or
            $null -eq $startTime
        ) {
            $diagnostics.Add("Windows CIM returned an incomplete process record for PID '$($process.ProcessId)'.")
            continue
        }

        $records.Add([pscustomobject][ordered]@{
            pid = $pidValue
            ppid = $parentValue
            processStartTimeUtc = $startTime.ToString('O')
            processStartTimePrecisionTicks = 10L
            processName = [string] $process.Name
            provenance = 'windows-cim:Win32_Process.CreationDate'
        })
    }

    return New-NervFullStackProcessInventoryResult `
        -Complete ($diagnostics.Count -eq 0) `
        -Records $records.ToArray() `
        -Platform 'Windows' `
        -Provenance 'windows-cim:Win32_Process' `
        -Diagnostics $diagnostics.ToArray()
}

function Get-NervFullStackLinuxProcessInventory {
    $records = [Collections.Generic.List[object]]::new()
    $diagnostics = [Collections.Generic.List[string]]::new()
    $provenance = 'linux-procfs:/proc/<pid>/stat+btime+sysconf'

    try {
        $bootLine = @([IO.File]::ReadAllLines('/proc/stat') | Where-Object {
            $_.StartsWith('btime ', [StringComparison]::Ordinal)
        })
        $bootSeconds = 0L
        if ($bootLine.Count -ne 1 -or -not [long]::TryParse($bootLine[0].Substring(6), [ref] $bootSeconds)) {
            throw 'Linux /proc/stat did not expose one parseable btime record.'
        }

        # Linux glibc/musl use 2 for _SC_CLK_TCK.
        $clockTicksPerSecond = [Nerv.IIP.FullStackProcessRuntime.NativeMethods]::sysconf(2)
        if ($clockTicksPerSecond -le 0) { throw 'Linux sysconf(_SC_CLK_TCK) failed.' }
        $precisionTicks = [long] [Math]::Ceiling([TimeSpan]::TicksPerSecond / [double] $clockTicksPerSecond)
        $bootUtc = [DateTimeOffset]::FromUnixTimeSeconds($bootSeconds).UtcDateTime
        $entries = @([IO.Directory]::EnumerateDirectories('/proc'))
    }
    catch {
        return New-NervFullStackProcessInventoryResult `
            -Complete $false `
            -Platform 'Linux' `
            -Provenance $provenance `
            -Diagnostics @("Linux process inventory initialization failed: $($_.Exception.Message)")
    }

    foreach ($entry in $entries) {
        $pidValue = 0
        if (-not [int]::TryParse([IO.Path]::GetFileName($entry), [ref] $pidValue)) { continue }

        $statPath = Join-Path $entry 'stat'
        try {
            $stat = [IO.File]::ReadAllText($statPath)
            $commandEnd = $stat.LastIndexOf([string] ')', [StringComparison]::Ordinal)
            if ($commandEnd -lt 0 -or ($commandEnd + 2) -ge $stat.Length) {
                throw 'Malformed /proc stat command boundary.'
            }
            $fields = @($stat.Substring($commandEnd + 2).Split(' ', [StringSplitOptions]::RemoveEmptyEntries))
            if ($fields.Count -lt 20) { throw 'Malformed /proc stat field count.' }

            $parentValue = 0
            $startClockTicks = 0L
            if (
                -not [int]::TryParse($fields[1], [ref] $parentValue) -or
                -not [long]::TryParse($fields[19], [ref] $startClockTicks)
            ) {
                throw 'Malformed /proc parent or start-time field.'
            }

            $startTicks = [long] [Math]::Round(
                $startClockTicks * [TimeSpan]::TicksPerSecond / [double] $clockTicksPerSecond,
                [MidpointRounding]::ToEven)
            $startUtc = $bootUtc.AddTicks($startTicks)
            $records.Add([pscustomobject][ordered]@{
                pid = $pidValue
                ppid = $parentValue
                processStartTimeUtc = $startUtc.ToString('O')
                processStartTimePrecisionTicks = $precisionTicks
                processName = ''
                provenance = $provenance
            })
        }
        catch [IO.FileNotFoundException] {
            # A PID that disappeared after directory enumeration is absent from this frozen snapshot.
        }
        catch [IO.DirectoryNotFoundException] {
            # A PID that disappeared after directory enumeration is absent from this frozen snapshot.
        }
        catch {
            $diagnostics.Add("Linux process record $pidValue was unreadable: $($_.Exception.Message)")
        }
    }

    return New-NervFullStackProcessInventoryResult `
        -Complete ($diagnostics.Count -eq 0) `
        -Records $records.ToArray() `
        -Platform 'Linux' `
        -Provenance $provenance `
        -Diagnostics $diagnostics.ToArray()
}

function Get-NervFullStackMacOSProcessInventory {
    $records = [Collections.Generic.List[object]]::new()
    $diagnostics = [Collections.Generic.List[string]]::new()
    $provenance = 'macos-governed-ps:pid,ppid,lstart'

    try {
        $result = Invoke-NativeCommandOutput `
            -Command '/bin/ps' `
            -Arguments @('-axo', 'pid=,ppid=,lstart=') `
            -WorkingDirectory $PSScriptRoot `
            -TimeoutSeconds 10 `
            -Name 'fullstack-process-inventory-macos'
    }
    catch {
        return New-NervFullStackProcessInventoryResult `
            -Complete $false `
            -Platform 'macOS' `
            -Provenance $provenance `
            -Diagnostics @("macOS process enumeration failed: $($_.Exception.Message)")
    }

    foreach ($line in @(([string] $result.Stdout) -split "`r?`n")) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $match = [regex]::Match($line, '^\s*(\d+)\s+(\d+)\s+(.+?)\s*$')
        if (-not $match.Success) {
            $diagnostics.Add('macOS ps returned an unparseable process row.')
            continue
        }

        $pidValue = 0
        $parentPid = 0
        $normalizedStart = [regex]::Replace($match.Groups[3].Value.Trim(), '\s+', ' ')
        $startTime = [DateTime]::MinValue
        if (
            -not [int]::TryParse($match.Groups[1].Value, [ref] $pidValue) -or
            -not [int]::TryParse($match.Groups[2].Value, [ref] $parentPid) -or
            -not [DateTime]::TryParseExact(
                $normalizedStart,
                'ddd MMM d HH:mm:ss yyyy',
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::AssumeLocal,
                [ref] $startTime)
        ) {
            $diagnostics.Add("macOS ps returned an invalid process record for PID '$($match.Groups[1].Value)'.")
            continue
        }

        $records.Add([pscustomobject][ordered]@{
            pid = $pidValue
            ppid = $parentPid
            processStartTimeUtc = $startTime.ToUniversalTime().ToString('O')
            processStartTimePrecisionTicks = [TimeSpan]::TicksPerSecond
            processName = ''
            provenance = $provenance
        })
    }

    return New-NervFullStackProcessInventoryResult `
        -Complete ($diagnostics.Count -eq 0) `
        -Records $records.ToArray() `
        -Platform 'macOS' `
        -Provenance $provenance `
        -Diagnostics $diagnostics.ToArray()
}

function Get-NervFullStackProcessInventory {
    [OutputType([pscustomobject])]
    param()

    if ($IsWindows) { return Get-NervFullStackWindowsProcessInventory }
    if ($IsLinux) { return Get-NervFullStackLinuxProcessInventory }
    if ($IsMacOS) { return Get-NervFullStackMacOSProcessInventory }

    return New-NervFullStackProcessInventoryResult `
        -Complete $false `
        -Platform 'Unknown' `
        -Provenance 'unsupported-platform' `
        -Diagnostics @('The current platform has no governed fullstack process inventory provider.')
}

function Get-NervFullStackProcessIdentityState {
    [OutputType([string])]
    param(
        [Parameter(Mandatory)]
        [object] $Identity,

        [Parameter(Mandatory)]
        [object] $InventorySnapshot
    )

    if (-not [bool] (Get-NervFullStackProcessRuntimeProperty -InputObject $InventorySnapshot -Name 'complete')) {
        return 'Unknown'
    }

    $pidValue = 0
    $identityPid = Get-NervFullStackProcessRuntimeProperty -InputObject $Identity -Name 'pid'
    $identityStartValue = Get-NervFullStackProcessRuntimeProperty -InputObject $Identity -Name 'processStartTimeUtc'
    $expectedStart = ConvertTo-NervFullStackProcessUtcTime -Value $identityStartValue
    if (-not [int]::TryParse([string] $identityPid, [ref] $pidValue) -or $pidValue -le 0 -or $null -eq $expectedStart) {
        return 'Unknown'
    }

    $records = @(Get-NervFullStackProcessRuntimeProperty -InputObject $InventorySnapshot -Name 'records')
    $pidMatches = @($records | Where-Object {
        $recordPid = 0
        [int]::TryParse([string] (Get-NervFullStackProcessRuntimeProperty -InputObject $_ -Name 'pid'), [ref] $recordPid) -and
            $recordPid -eq $pidValue
    })
    if ($pidMatches.Count -eq 0) { return 'Absent' }
    if ($pidMatches.Count -ne 1) { return 'Unknown' }

    $actualStart = ConvertTo-NervFullStackProcessUtcTime -Value (
        Get-NervFullStackProcessRuntimeProperty -InputObject $pidMatches[0] -Name 'processStartTimeUtc')
    $precisionTicks = 0L
    if (
        $null -eq $actualStart -or
        -not [long]::TryParse(
            [string] (Get-NervFullStackProcessRuntimeProperty -InputObject $pidMatches[0] -Name 'processStartTimePrecisionTicks'),
            [ref] $precisionTicks) -or
        $precisionTicks -lt 1
    ) {
        return 'Unknown'
    }

    $expectedTicks = ConvertTo-NervFullStackNormalizedProcessTicks -UtcTime $expectedStart -PrecisionTicks $precisionTicks
    $actualTicks = ConvertTo-NervFullStackNormalizedProcessTicks -UtcTime $actualStart -PrecisionTicks $precisionTicks
    if ($null -eq $expectedTicks -or $null -eq $actualTicks) { return 'Unknown' }
    if ($expectedTicks -eq $actualTicks) { return 'Active' }
    return 'Mismatched'
}

function Invoke-NervFullStackProcessRuntimeAction {
    param(
        [Parameter(Mandatory)]
        [scriptblock] $Action,

        [object[]] $ArgumentList = @()
    )

    & $Action @ArgumentList
}

function Invoke-NervFullStackProcessRuntimeInventory {
    if ($null -ne $script:NervFullStackProcessRuntimeInventoryAction) {
        return Invoke-NervFullStackProcessRuntimeAction -Action $script:NervFullStackProcessRuntimeInventoryAction
    }
    return Get-NervFullStackProcessInventory
}

function Invoke-NervFullStackProcessRuntimeStop {
    param([Parameter(Mandatory)] [int] $ProcessId)

    if ($null -ne $script:NervFullStackProcessRuntimeStopAction) {
        $null = Invoke-NervFullStackProcessRuntimeAction `
            -Action $script:NervFullStackProcessRuntimeStopAction `
            -ArgumentList @($ProcessId)
        return
    }
    Stop-Process -Id $ProcessId -Force -ErrorAction Stop
}

function Test-NervFullStackProcessExactExclusion {
    param(
        [Parameter(Mandatory)] [object] $Record,
        [object[]] $ExcludedIdentities = @()
    )

    $recordPid = [int] (Get-NervFullStackProcessRuntimeProperty -InputObject $Record -Name 'pid')
    $recordStart = ConvertTo-NervFullStackProcessUtcTime -Value (
        Get-NervFullStackProcessRuntimeProperty -InputObject $Record -Name 'processStartTimeUtc')
    $precisionTicks = [long] (Get-NervFullStackProcessRuntimeProperty -InputObject $Record -Name 'processStartTimePrecisionTicks')
    if ($null -eq $recordStart -or $precisionTicks -lt 1) { return $false }
    $recordTicks = ConvertTo-NervFullStackNormalizedProcessTicks -UtcTime $recordStart -PrecisionTicks $precisionTicks

    foreach ($excluded in @($ExcludedIdentities)) {
        $excludedPid = 0
        $excludedStart = ConvertTo-NervFullStackProcessUtcTime -Value (
            Get-NervFullStackProcessRuntimeProperty -InputObject $excluded -Name 'processStartTimeUtc')
        if (
            [int]::TryParse(
                [string] (Get-NervFullStackProcessRuntimeProperty -InputObject $excluded -Name 'pid'),
                [ref] $excludedPid) -and
            $excludedPid -eq $recordPid -and
            $null -ne $excludedStart -and
            (ConvertTo-NervFullStackNormalizedProcessTicks -UtcTime $excludedStart -PrecisionTicks $precisionTicks) -eq $recordTicks
        ) {
            return $true
        }
    }
    return $false
}

function Get-NervFullStackFrozenProcessTree {
    param(
        [Parameter(Mandatory)] [object] $InventorySnapshot,
        [Parameter(Mandatory)] [int] $RootProcessId
    )

    $records = @(Get-NervFullStackProcessRuntimeProperty -InputObject $InventorySnapshot -Name 'records')
    $byParent = [Collections.Generic.Dictionary[int, Collections.Generic.List[object]]]::new()
    foreach ($record in $records) {
        $parentPid = 0
        if (-not [int]::TryParse(
            [string] (Get-NervFullStackProcessRuntimeProperty -InputObject $record -Name 'ppid'),
            [ref] $parentPid)) {
            continue
        }
        if (-not $byParent.ContainsKey($parentPid)) {
            $byParent.Add($parentPid, [Collections.Generic.List[object]]::new())
        }
        $byParent[$parentPid].Add($record)
    }

    $frozen = [Collections.Generic.List[object]]::new()
    $visited = [Collections.Generic.HashSet[int]]::new()
    [void] $visited.Add($RootProcessId)
    $queue = [Collections.Generic.Queue[object]]::new()
    $queue.Enqueue([pscustomobject]@{ pid = $RootProcessId; depth = 0 })
    while ($queue.Count -gt 0) {
        $parent = $queue.Dequeue()
        if (-not $byParent.ContainsKey([int] $parent.pid)) { continue }
        foreach ($child in $byParent[[int] $parent.pid]) {
            $childPid = 0
            if (-not [int]::TryParse(
                [string] (Get-NervFullStackProcessRuntimeProperty -InputObject $child -Name 'pid'),
                [ref] $childPid)) {
                continue
            }
            if (-not $visited.Add($childPid)) { continue }
            $depth = [int] $parent.depth + 1
            $frozen.Add([pscustomobject]@{ record = $child; depth = $depth })
            $queue.Enqueue([pscustomobject]@{ pid = $childPid; depth = $depth })
        }
    }

    $frozen.Sort([Comparison[object]] {
        param($left, $right)
        if ([int] $left.depth -gt [int] $right.depth) { return -1 }
        if ([int] $left.depth -lt [int] $right.depth) { return 1 }
        if ([int] $left.record.pid -lt [int] $right.record.pid) { return -1 }
        if ([int] $left.record.pid -gt [int] $right.record.pid) { return 1 }
        return 0
    })
    return $frozen.ToArray()
}

function Add-NervFullStackUniqueProcessId {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [Collections.Generic.List[int]] $List,
        [Parameter(Mandatory)] [int] $ProcessId
    )
    if (-not $List.Contains($ProcessId)) { $List.Add($ProcessId) }
}

function New-NervFullStackProcessStopResult {
    param(
        [Parameter(Mandatory)] [bool] $Complete,
        [Parameter(Mandatory)] [string] $Disposition,
        [Parameter(Mandatory)] [int] $Passes,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [Collections.Generic.List[int]] $Stopped,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [Collections.Generic.List[int]] $Mismatched,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [Collections.Generic.List[int]] $Failed,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [Collections.Generic.List[int]] $Surviving,
        [string[]] $Diagnostics = @()
    )

    return [pscustomobject][ordered]@{
        complete = $Complete
        disposition = $Disposition
        passes = $Passes
        StoppedProcessIds = @($Stopped)
        IdentityMismatchProcessIds = @($Mismatched)
        FailedProcessIds = @($Failed)
        SurvivingProcessIds = @($Surviving)
        diagnostics = @($Diagnostics)
    }
}

function Stop-NervFullStackOwnedProcessTree {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [object] $RootIdentity,

        [object[]] $ExcludedIdentities = @(),

        [Parameter(Mandatory)]
        [ValidateRange(1, 100)]
        [int] $MaxPasses,

        [Parameter(Mandatory)]
        [timespan] $Timeout
    )

    if ($Timeout -le [timespan]::Zero) { throw 'Timeout must be greater than zero.' }

    $rootPid = 0
    if (-not [int]::TryParse(
        [string] (Get-NervFullStackProcessRuntimeProperty -InputObject $RootIdentity -Name 'pid'),
        [ref] $rootPid) -or $rootPid -le 0) {
        throw 'RootIdentity must contain a positive pid.'
    }

    $stopped = [Collections.Generic.List[int]]::new()
    $mismatched = [Collections.Generic.List[int]]::new()
    $failed = [Collections.Generic.List[int]]::new()
    $surviving = [Collections.Generic.List[int]]::new()
    $diagnostics = [Collections.Generic.List[string]]::new()
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $passes = 0

    for ($pass = 1; $pass -le $MaxPasses -and $watch.Elapsed -lt $Timeout; $pass++) {
        $passes = $pass
        $frozenInventory = Invoke-NervFullStackProcessRuntimeInventory
        if (-not [bool] (Get-NervFullStackProcessRuntimeProperty -InputObject $frozenInventory -Name 'complete')) {
            $diagnostics.Add('process:inventory-incomplete')
            return New-NervFullStackProcessStopResult -Complete $false -Disposition 'Blocked' -Passes $passes -Stopped $stopped -Mismatched $mismatched -Failed $failed -Surviving $surviving -Diagnostics $diagnostics.ToArray()
        }

        $rootState = Get-NervFullStackProcessIdentityState -Identity $RootIdentity -InventorySnapshot $frozenInventory
        if ([string]::Equals($rootState, 'Absent', [StringComparison]::Ordinal)) {
            $complete = $failed.Count -eq 0 -and $mismatched.Count -eq 0 -and $surviving.Count -eq 0
            return New-NervFullStackProcessStopResult -Complete $complete -Disposition $(if ($complete) { 'Complete' } else { 'Failed' }) -Passes $passes -Stopped $stopped -Mismatched $mismatched -Failed $failed -Surviving $surviving -Diagnostics $diagnostics.ToArray()
        }
        if ([string]::Equals($rootState, 'Mismatched', [StringComparison]::Ordinal)) {
            Add-NervFullStackUniqueProcessId -List $mismatched -ProcessId $rootPid
            $diagnostics.Add('process:root-identity-mismatched')
            return New-NervFullStackProcessStopResult -Complete $false -Disposition 'Blocked' -Passes $passes -Stopped $stopped -Mismatched $mismatched -Failed $failed -Surviving $surviving -Diagnostics $diagnostics.ToArray()
        }
        if (-not [string]::Equals($rootState, 'Active', [StringComparison]::Ordinal)) {
            $diagnostics.Add('process:root-identity-unknown')
            return New-NervFullStackProcessStopResult -Complete $false -Disposition 'Blocked' -Passes $passes -Stopped $stopped -Mismatched $mismatched -Failed $failed -Surviving $surviving -Diagnostics $diagnostics.ToArray()
        }

        $frozenTree = @(Get-NervFullStackFrozenProcessTree -InventorySnapshot $frozenInventory -RootProcessId $rootPid)
        foreach ($candidate in $frozenTree) {
            if ($watch.Elapsed -ge $Timeout) { break }
            $record = $candidate.record
            $candidatePid = [int] (Get-NervFullStackProcessRuntimeProperty -InputObject $record -Name 'pid')
            if (Test-NervFullStackProcessExactExclusion -Record $record -ExcludedIdentities $ExcludedIdentities) { continue }

            # Every destructive action gets a fresh complete inventory and exact root/candidate check.
            $revalidation = Invoke-NervFullStackProcessRuntimeInventory
            if (-not [bool] (Get-NervFullStackProcessRuntimeProperty -InputObject $revalidation -Name 'complete')) {
                $diagnostics.Add('process:revalidation-inventory-incomplete')
                return New-NervFullStackProcessStopResult -Complete $false -Disposition 'Blocked' -Passes $passes -Stopped $stopped -Mismatched $mismatched -Failed $failed -Surviving $surviving -Diagnostics $diagnostics.ToArray()
            }
            $freshRootState = Get-NervFullStackProcessIdentityState -Identity $RootIdentity -InventorySnapshot $revalidation
            if (-not [string]::Equals($freshRootState, 'Active', [StringComparison]::Ordinal)) {
                if ([string]::Equals($freshRootState, 'Mismatched', [StringComparison]::Ordinal)) {
                    Add-NervFullStackUniqueProcessId -List $mismatched -ProcessId $rootPid
                }
                $diagnostics.Add("process:root-revalidation-$($freshRootState.ToLowerInvariant())")
                return New-NervFullStackProcessStopResult -Complete $false -Disposition 'Blocked' -Passes $passes -Stopped $stopped -Mismatched $mismatched -Failed $failed -Surviving $surviving -Diagnostics $diagnostics.ToArray()
            }

            $candidateState = Get-NervFullStackProcessIdentityState -Identity $record -InventorySnapshot $revalidation
            if ([string]::Equals($candidateState, 'Absent', [StringComparison]::Ordinal)) { continue }
            if ([string]::Equals($candidateState, 'Mismatched', [StringComparison]::Ordinal)) {
                Add-NervFullStackUniqueProcessId -List $mismatched -ProcessId $candidatePid
                continue
            }
            if (-not [string]::Equals($candidateState, 'Active', [StringComparison]::Ordinal)) {
                Add-NervFullStackUniqueProcessId -List $failed -ProcessId $candidatePid
                $diagnostics.Add("process:identity-unknown:$candidatePid")
                continue
            }

            try {
                Invoke-NervFullStackProcessRuntimeStop -ProcessId $candidatePid
            }
            catch {
                Add-NervFullStackUniqueProcessId -List $failed -ProcessId $candidatePid
                $diagnostics.Add("process:stop-failed:$candidatePid")
                continue
            }

            $exited = $false
            while ($watch.Elapsed -lt $Timeout) {
                $exitInventory = Invoke-NervFullStackProcessRuntimeInventory
                if (-not [bool] (Get-NervFullStackProcessRuntimeProperty -InputObject $exitInventory -Name 'complete')) {
                    $diagnostics.Add('process:exit-readback-inventory-incomplete')
                    return New-NervFullStackProcessStopResult -Complete $false -Disposition 'Blocked' -Passes $passes -Stopped $stopped -Mismatched $mismatched -Failed $failed -Surviving $surviving -Diagnostics $diagnostics.ToArray()
                }
                $exitState = Get-NervFullStackProcessIdentityState -Identity $record -InventorySnapshot $exitInventory
                if ([string]::Equals($exitState, 'Absent', [StringComparison]::Ordinal)) {
                    $exited = $true
                    Add-NervFullStackUniqueProcessId -List $stopped -ProcessId $candidatePid
                    break
                }
                if ([string]::Equals($exitState, 'Mismatched', [StringComparison]::Ordinal)) {
                    Add-NervFullStackUniqueProcessId -List $mismatched -ProcessId $candidatePid
                    break
                }
                if ([string]::Equals($exitState, 'Unknown', [StringComparison]::Ordinal)) {
                    Add-NervFullStackUniqueProcessId -List $failed -ProcessId $candidatePid
                    break
                }
                [Threading.Thread]::Sleep(10)
            }
            if (-not $exited -and -not $mismatched.Contains($candidatePid) -and -not $failed.Contains($candidatePid)) {
                Add-NervFullStackUniqueProcessId -List $surviving -ProcessId $candidatePid
            }
        }

        if ($watch.Elapsed -ge $Timeout) { break }

        # A fresh inventory after descendants detects late arrivals. Root is stopped only after a
        # complete quiescent observation with no non-excluded descendant.
        $postDescendantInventory = Invoke-NervFullStackProcessRuntimeInventory
        if (-not [bool] (Get-NervFullStackProcessRuntimeProperty -InputObject $postDescendantInventory -Name 'complete')) {
            $diagnostics.Add('process:post-descendant-inventory-incomplete')
            return New-NervFullStackProcessStopResult -Complete $false -Disposition 'Blocked' -Passes $passes -Stopped $stopped -Mismatched $mismatched -Failed $failed -Surviving $surviving -Diagnostics $diagnostics.ToArray()
        }
        $postRootState = Get-NervFullStackProcessIdentityState -Identity $RootIdentity -InventorySnapshot $postDescendantInventory
        if (-not [string]::Equals($postRootState, 'Active', [StringComparison]::Ordinal)) { continue }
        $remainingDescendants = @(Get-NervFullStackFrozenProcessTree -InventorySnapshot $postDescendantInventory -RootProcessId $rootPid | Where-Object {
            -not (Test-NervFullStackProcessExactExclusion -Record $_.record -ExcludedIdentities $ExcludedIdentities)
        })
        if ($remainingDescendants.Count -gt 0) { continue }

        $rootRecord = @((Get-NervFullStackProcessRuntimeProperty -InputObject $postDescendantInventory -Name 'records') | Where-Object {
            [int] (Get-NervFullStackProcessRuntimeProperty -InputObject $_ -Name 'pid') -eq $rootPid
        })
        if ($rootRecord.Count -ne 1) {
            $diagnostics.Add('process:root-record-not-unique')
            break
        }
        if (Test-NervFullStackProcessExactExclusion -Record $rootRecord[0] -ExcludedIdentities $ExcludedIdentities) {
            Add-NervFullStackUniqueProcessId -List $surviving -ProcessId $rootPid
            $diagnostics.Add('process:root-explicitly-excluded')
            break
        }

        # The post-descendant snapshot is the exact revalidation immediately before root stop.
        try {
            Invoke-NervFullStackProcessRuntimeStop -ProcessId $rootPid
        }
        catch {
            Add-NervFullStackUniqueProcessId -List $failed -ProcessId $rootPid
            $diagnostics.Add("process:stop-failed:$rootPid")
            break
        }

        while ($watch.Elapsed -lt $Timeout) {
            $rootExitInventory = Invoke-NervFullStackProcessRuntimeInventory
            if (-not [bool] (Get-NervFullStackProcessRuntimeProperty -InputObject $rootExitInventory -Name 'complete')) {
                $diagnostics.Add('process:root-exit-readback-inventory-incomplete')
                return New-NervFullStackProcessStopResult -Complete $false -Disposition 'Blocked' -Passes $passes -Stopped $stopped -Mismatched $mismatched -Failed $failed -Surviving $surviving -Diagnostics $diagnostics.ToArray()
            }
            $rootExitState = Get-NervFullStackProcessIdentityState -Identity $RootIdentity -InventorySnapshot $rootExitInventory
            if ([string]::Equals($rootExitState, 'Absent', [StringComparison]::Ordinal)) {
                Add-NervFullStackUniqueProcessId -List $stopped -ProcessId $rootPid
                break
            }
            if ([string]::Equals($rootExitState, 'Mismatched', [StringComparison]::Ordinal)) {
                Add-NervFullStackUniqueProcessId -List $mismatched -ProcessId $rootPid
                break
            }
            if ([string]::Equals($rootExitState, 'Unknown', [StringComparison]::Ordinal)) {
                Add-NervFullStackUniqueProcessId -List $failed -ProcessId $rootPid
                break
            }
            [Threading.Thread]::Sleep(10)
        }
    }

    $finalInventory = Invoke-NervFullStackProcessRuntimeInventory
    if ([bool] (Get-NervFullStackProcessRuntimeProperty -InputObject $finalInventory -Name 'complete')) {
        $finalRootState = Get-NervFullStackProcessIdentityState -Identity $RootIdentity -InventorySnapshot $finalInventory
        if ([string]::Equals($finalRootState, 'Active', [StringComparison]::Ordinal)) {
            Add-NervFullStackUniqueProcessId -List $surviving -ProcessId $rootPid
        }
        elseif ([string]::Equals($finalRootState, 'Mismatched', [StringComparison]::Ordinal)) {
            Add-NervFullStackUniqueProcessId -List $mismatched -ProcessId $rootPid
        }
        elseif ([string]::Equals($finalRootState, 'Unknown', [StringComparison]::Ordinal)) {
            Add-NervFullStackUniqueProcessId -List $failed -ProcessId $rootPid
        }
        foreach ($candidate in @(Get-NervFullStackFrozenProcessTree -InventorySnapshot $finalInventory -RootProcessId $rootPid)) {
            if (-not (Test-NervFullStackProcessExactExclusion -Record $candidate.record -ExcludedIdentities $ExcludedIdentities)) {
                Add-NervFullStackUniqueProcessId -List $surviving -ProcessId ([int] $candidate.record.pid)
            }
        }
    }
    else {
        $diagnostics.Add('process:final-inventory-incomplete')
    }

    $complete = $stopped.Contains($rootPid) -and $mismatched.Count -eq 0 -and $failed.Count -eq 0 -and $surviving.Count -eq 0
    $disposition = if ($complete) { 'Complete' } elseif ($watch.Elapsed -ge $Timeout) { 'Timeout' } else { 'Failed' }
    return New-NervFullStackProcessStopResult -Complete $complete -Disposition $disposition -Passes $passes -Stopped $stopped -Mismatched $mismatched -Failed $failed -Surviving $surviving -Diagnostics $diagnostics.ToArray()
}

function Protect-NervFullStackProcessOutputText {
    param([AllowNull()] [string] $Text)
    if ($null -eq $Text) { return '' }
    $safe = [regex]::Replace(
        $Text,
        '(?is)-----BEGIN [^-\r\n]+-----.*?-----END [^-\r\n]+-----',
        '<redacted-pem>')
    $safe = [regex]::Replace($safe, '(?i)(https?://)[^/@\s]+@', '$1<redacted>@')
    $safe = [regex]::Replace(
        $safe,
        '(?i)(["''](?:authorization|password|pwd|token|secret|client_secret)["'']\s*:\s*["''])[^"'']*(["''])',
        '$1<redacted>$2')
    $safe = [regex]::Replace($safe, '(?i)(authorization\s*[:=]\s*bearer\s+)[^\s''"]+', '$1<redacted>')
    $safe = [regex]::Replace($safe, '(?i)((?:password|pwd|token|secret|client_secret)\s*[:=]\s*)[^;\s''"]+', '$1<redacted>')
    $safe = [regex]::Replace($safe, '(?i)(Host=[^;]+;Port=[^;]+;Database=[^;]+;Username=[^;]+;Password=)[^;\s]+', '$1<redacted>')
    return $safe
}

function Get-NervFullStackStreamSnapshot {
    param([Parameter(Mandatory)] [object] $Handle)

    $snapshotAction = Get-NervFullStackProcessRuntimeProperty -InputObject $Handle -Name 'snapshotAction'
    if ($snapshotAction -is [scriptblock]) {
        return [string] (Invoke-NervFullStackProcessRuntimeAction -Action $snapshotAction)
    }

    $snapshotMethod = $Handle.PSObject.Methods['Snapshot']
    if ($null -ne $snapshotMethod) {
        return [string] $snapshotMethod.Invoke()
    }

    $partial = Get-NervFullStackProcessRuntimeProperty -InputObject $Handle -Name 'partialOutput'
    if ($null -ne $partial) { return [string] $partial }
    return ''
}

function Wait-NervFullStackProcessOutputDrain {
    [OutputType([pscustomobject])]
    param(
        [object[]] $StreamHandles = @(),

        [Parameter(Mandatory)]
        [timespan] $Timeout
    )

    if ($Timeout -le [timespan]::Zero) { throw 'Timeout must be greater than zero.' }

    $readable = [Collections.Generic.List[object]]::new()
    $diagnostics = [Collections.Generic.List[string]]::new()
    foreach ($handle in @($StreamHandles)) {
        if ($null -eq $handle) { continue }
        $name = [string] (Get-NervFullStackProcessRuntimeProperty -InputObject $handle -Name 'name')
        $completion = Get-NervFullStackProcessRuntimeProperty -InputObject $handle -Name 'completion'
        if ([string]::IsNullOrWhiteSpace($name) -or $completion -isnot [Threading.Tasks.Task]) {
            $diagnostics.Add('drain:unreadable-handle')
            continue
        }
        $readable.Add([pscustomobject]@{ name = $name; completion = $completion; handle = $handle })
    }

    if ($readable.Count -eq 0) {
        if ($diagnostics.Count -eq 0) { $diagnostics.Add('drain:no-readable-handles') }
        return [pscustomobject][ordered]@{
            complete = $false
            disposition = 'NotApplicable'
            timedOut = $false
            partialOutput = $false
            output = @()
            unfinishedStreams = @()
            diagnostics = @($diagnostics)
        }
    }

    $watch = [Diagnostics.Stopwatch]::StartNew()
    while (@($readable | Where-Object { -not $_.completion.IsCompleted }).Count -gt 0 -and $watch.Elapsed -lt $Timeout) {
        [Threading.Thread]::Sleep(10)
    }

    $outputs = [Collections.Generic.List[object]]::new()
    $unfinished = [Collections.Generic.List[string]]::new()
    $failed = $false
    $hasPartial = $false
    foreach ($stream in $readable) {
        $task = [Threading.Tasks.Task] $stream.completion
        $text = ''
        if (-not $task.IsCompleted) {
            $unfinished.Add([string] $stream.name)
            $text = Get-NervFullStackStreamSnapshot -Handle $stream.handle
            if (-not [string]::IsNullOrEmpty($text)) { $hasPartial = $true }
        }
        elseif ($task.IsFaulted) {
            $failed = $true
            $failure = $task.Exception.GetBaseException().Message
            $diagnostics.Add("drain:$($stream.name):$(Protect-NervFullStackProcessOutputText $failure)")
            $text = Get-NervFullStackStreamSnapshot -Handle $stream.handle
            if (-not [string]::IsNullOrEmpty($text)) { $hasPartial = $true }
        }
        elseif ($task.IsCanceled) {
            $failed = $true
            $diagnostics.Add("drain:$($stream.name):canceled")
            $text = Get-NervFullStackStreamSnapshot -Handle $stream.handle
            if (-not [string]::IsNullOrEmpty($text)) { $hasPartial = $true }
        }
        else {
            $resultProperty = $task.GetType().GetProperty('Result')
            if ($null -ne $resultProperty) { $text = [string] $resultProperty.GetValue($task) }
            else { $text = Get-NervFullStackStreamSnapshot -Handle $stream.handle }
        }

        $outputs.Add([pscustomobject][ordered]@{
            name = [string] $stream.name
            text = Protect-NervFullStackProcessOutputText $text
        })
    }

    $timedOut = $unfinished.Count -gt 0
    if ($timedOut) { $diagnostics.Add("drain:timeout:$([int] $Timeout.TotalMilliseconds)ms") }
    $complete = -not $timedOut -and -not $failed
    $disposition = if ($timedOut) { 'Timeout' } elseif ($failed) { 'Failed' } else { 'Complete' }
    return [pscustomobject][ordered]@{
        complete = $complete
        disposition = $disposition
        timedOut = $timedOut
        partialOutput = $hasPartial
        output = @($outputs)
        unfinishedStreams = @($unfinished)
        diagnostics = @($diagnostics)
    }
}
