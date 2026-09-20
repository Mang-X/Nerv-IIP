# Script-Governance:
#   Category: library
#   SideEffects:
#     - Projects Redis/CAP observer process and Redis metadata
#   Writes:
#     - Caller-owned bounded phase observation file
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

function ConvertFrom-RedisCapInfo {
    param([string]$Info)
    $values = [ordered]@{}
    foreach ($line in $Info -split "`r?`n") {
        if ($line -match '^(used_cpu_sys|used_cpu_user|connected_clients|blocked_clients):([0-9.]+)$') { $values[$Matches[1]] = [double]::Parse($Matches[2], [Globalization.CultureInfo]::InvariantCulture) }
        elseif ($line -match '^(cmdstat|latency_percentiles_usec)_(exists|xgroup\|create|xreadgroup|xadd|xack|xpending|xclaim|xautoclaim|eval|evalsha|info):([a-z0-9_=.,]+)$') { $values[$Matches[1] + '_' + $Matches[2]] = $Matches[3] }
    }
    return $values
}

function Write-RedisCapObservationRecord {
    param([string]$Path, [long]$MaxBytes, [object]$Record)
    $line = ($Record | ConvertTo-Json -Depth 10 -Compress) + "`n"
    $length = if (Test-Path -LiteralPath $Path) { (Get-Item -LiteralPath $Path).Length } else { 0 }
    if ($length + [Text.Encoding]::UTF8.GetByteCount($line) -gt $MaxBytes) { return $false }
    [IO.File]::AppendAllText($Path, $line, [Text.UTF8Encoding]::new($false))
    return $true
}

function Resolve-RedisCapObservedTesthost {
    param([int]$ProcessId, [hashtable]$Processes, [int]$LaneProcessId, [object[]]$Members, [string]$ResultsDirectory)
    $candidate = $Processes[$ProcessId]
    if ($null -eq $candidate -or -not @($candidate.arguments | Where-Object { [string]::Equals([IO.Path]::GetFileName($_), 'testhost.dll', [StringComparison]::Ordinal) }).Count) { return $null }
    $ancestor = [int]$candidate.parent
    $execution = $null
    $visited = [Collections.Generic.HashSet[int]]::new()
    while ($ancestor -ge 1 -and $visited.Add($ancestor)) {
        if ($ancestor -eq $LaneProcessId) { return $execution }
        $process = $Processes[$ancestor]
        if ($null -eq $process) { return $null }
        $arguments = [string[]]$process.arguments
        if ([Linq.Enumerable]::Contains[string]([string[]]($arguments), [string]('--list-tests'), [StringComparer]::Ordinal)) { return $null }
        $resultIndex = [Array]::IndexOf($arguments, '--results-directory')
        $filterIndex = [Array]::IndexOf($arguments, '--filter')
        if ([Linq.Enumerable]::Contains[string]([string[]]($arguments), [string]('test'), [StringComparer]::Ordinal) -and $resultIndex -ge 0 -and $resultIndex + 1 -lt $arguments.Length -and $filterIndex -ge 0 -and $filterIndex + 1 -lt $arguments.Length) {
            foreach ($member in $Members) {
                if ([Linq.Enumerable]::Contains[string]([string[]]($arguments), [string]([string]$member.project), [StringComparer]::Ordinal) -and
                    [string]::Equals($arguments[$filterIndex + 1], [string]$member.filter, [StringComparison]::Ordinal) -and
                    [string]::Equals($arguments[$resultIndex + 1], (Join-Path $ResultsDirectory ([string]$member.id)), [StringComparison]::Ordinal)) {
                    $execution = [pscustomobject]@{ memberId = [string]$member.id; executionProcessId = $ancestor; processId = $ProcessId }
                }
            }
        }
        $ancestor = [int]$process.parent
    }
    return $null
}

function Export-RedisCapFixturePhases {
    param([string]$ResultsDirectory, [string]$OutputPath)
    $phases = [Collections.Generic.List[string]]::new()
    $allowed = [Collections.Generic.HashSet[string]]::new([string[]]@('entered', 'release_completed', 'release_failed', 'inner_subscribe_started', 'inner_subscribe_succeeded', 'inner_subscribe_failed', 'listening_entered', 'observation_limit_reached'), [StringComparer]::Ordinal)
    foreach ($trx in Get-ChildItem -LiteralPath $ResultsDirectory -Recurse -Filter '*.trx' -ErrorAction SilentlyContinue) {
        $document = [xml](Get-Content -LiteralPath $trx.FullName -Raw)
        foreach ($node in $document.SelectNodes('//*[local-name()="StdErr" or local-name()="StdOut"]')) {
            foreach ($line in $node.InnerText -split "`r?`n") {
                if (-not $line.StartsWith('{"source":"mes_asset_unavailable_subscription",', [StringComparison]::Ordinal)) { continue }
                try {
                    $value = $line | ConvertFrom-Json -DateKind String
                    if (-not $allowed.Contains([string]$value.phase) -or [string]$value.fixtureId -cnotmatch '^[0-9a-f]{32}$' -or [string]$value.runtime -cnotmatch '^\.NET [0-9.]+$' -or [string]$value.architecture -cnotmatch '^(X64|Arm64|X86|Arm)$') { continue }
                    $utc = [DateTimeOffset]::Parse([string]$value.utc, [Globalization.CultureInfo]::InvariantCulture).ToUniversalTime().ToString('O')
                    $safe = [ordered]@{ source = 'mes_asset_unavailable_subscription'; fixtureId = $value.fixtureId; consumerId = [int]$value.consumerId; sequence = [int]$value.sequence; phase = $value.phase; timestamp = [long]$value.timestamp; timestampFrequency = [long]$value.timestampFrequency; utc = $utc; processId = [int]$value.processId; runtime = $value.runtime; architecture = $value.architecture; processorCount = [int]$value.processorCount }
                    if ($phases.Count -lt 128) { $phases.Add(($safe | ConvertTo-Json -Compress)) }
                }
                catch { } # Unrecognized output is not a trusted phase record.
            }
        }
    }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OutputPath))) | Out-Null
    [IO.File]::WriteAllLines($OutputPath, $phases, [Text.UTF8Encoding]::new($false))
    return $phases.Count
}
