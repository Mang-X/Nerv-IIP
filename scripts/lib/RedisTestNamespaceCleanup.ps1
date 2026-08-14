# Script-Governance:
#   Category: library
#   SideEffects:
#     - Provides pure candidate selection and injected Redis namespace cleanup orchestration
#   Writes:
#     - None
#   Cleanup:
#     - Does not connect to Redis unless caller-provided actions do so
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest

function ConvertTo-NervRedisCliContext {
    param([Parameter(Mandatory)] [string] $ConnectionString)

    $segments = @($ConnectionString.Split(',', [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { $_.Trim() })
    if ($segments.Count -eq 0 -or $segments[0] -notmatch '^(?<host>\[[^\]]+\]|[^:]+):(?<port>[0-9]+)$') {
        throw 'NERV_IIP_TEST_REDIS must begin with host:port; credentials redacted.'
    }

    $options = @{}
    foreach ($segment in @($segments | Select-Object -Skip 1)) {
        $parts = $segment.Split('=', 2)
        if ($parts.Count -eq 2) { $options[$parts[0].Trim().ToLowerInvariant()] = $parts[1].Trim() }
    }

    $arguments = @('--raw', '-h', $Matches.host.Trim('[', ']'), '-p', $Matches.port)
    if ($options.ContainsKey('ssl') -and [string]::Equals([string]$options.ssl, 'true', [StringComparison]::OrdinalIgnoreCase)) {
        $arguments += '--tls'
    }

    return [pscustomobject]@{
        Arguments = $arguments
        Host = $Matches.host.Trim('[', ']')
        Port = [int]$Matches.port
        Password = if ($options.ContainsKey('password')) { [string]$options.password } else { $null }
    }
}

function ConvertFrom-NervRedisTestNamespaceKey {
    param([Parameter(Mandatory)] [string] $Key)

    $match = [regex]::Match(
        $Key,
        '^(?<namespace>nerv:n822:(?<timestamp>[0-9a-f]{12})7[0-9a-f]{3}[89ab][0-9a-f]{15}:).+$',
        [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $match.Success) { return $null }

    try {
        $milliseconds = [Convert]::ToInt64([string]$match.Groups['timestamp'].Value, 16)
        $createdAtUtc = [DateTimeOffset]::FromUnixTimeMilliseconds($milliseconds)
    }
    catch { return $null }

    return [pscustomobject]@{
        Namespace = [string]$match.Groups['namespace'].Value
        CreatedAtUtc = $createdAtUtc
    }
}

function Get-NervStaleRedisTestNamespaceCandidate {
    param(
        [string[]] $Keys = @(),
        [Parameter(Mandatory)] [DateTimeOffset] $NowUtc,
        [Parameter(Mandatory)] [TimeSpan] $MinimumAge
    )

    if ($MinimumAge -le [TimeSpan]::Zero) { throw 'MinimumAge must be greater than zero.' }
    $threshold = $NowUtc - $MinimumAge
    $byNamespace = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($key in $Keys) {
        if ([string]::IsNullOrWhiteSpace($key)) { continue }
        $parsed = ConvertFrom-NervRedisTestNamespaceKey -Key $key
        if ($null -ne $parsed -and $parsed.CreatedAtUtc -le $threshold) {
            $byNamespace[$parsed.Namespace] = $parsed
        }
    }

    $candidates = @($byNamespace.Values)
    [Array]::Sort($candidates, [Comparison[object]] {
        param($left, $right)
        $leftTicks = ([DateTimeOffset]$left.CreatedAtUtc).UtcTicks
        $rightTicks = ([DateTimeOffset]$right.CreatedAtUtc).UtcTicks
        if ($leftTicks -lt $rightTicks) { return -1 }
        if ($leftTicks -gt $rightTicks) { return 1 }
        return [StringComparer]::Ordinal.Compare([string]$left.Namespace, [string]$right.Namespace)
    })
    return $candidates
}

function Invoke-NervRedisTestNamespaceCleanup {
    param(
        [string[]] $Keys = @(),
        [Parameter(Mandatory)] [DateTimeOffset] $NowUtc,
        [Parameter(Mandatory)] [TimeSpan] $MinimumAge,
        [switch] $Apply,
        [Parameter(Mandatory)] [scriptblock] $TestOwnerLeaseAction,
        [Parameter(Mandatory)] [scriptblock] $EnumerateKeysAction,
        [Parameter(Mandatory)] [scriptblock] $RemoveKeyAction
    )

    $candidates = @(Get-NervStaleRedisTestNamespaceCandidate -Keys $Keys -NowUtc $NowUtc -MinimumAge $MinimumAge)
    foreach ($candidate in $candidates) {
        $namespace = [string]$candidate.Namespace
        if ([bool](& $TestOwnerLeaseAction $namespace)) {
            [pscustomobject]@{ Namespace = $namespace; CreatedAtUtc = $candidate.CreatedAtUtc; Outcome = 'skipped-active' }
            continue
        }

        if (-not $Apply) {
            [pscustomobject]@{ Namespace = $namespace; CreatedAtUtc = $candidate.CreatedAtUtc; Outcome = 'preview' }
            continue
        }

        $ownedKeys = @(& $EnumerateKeysAction $namespace | ForEach-Object { [string]$_ })
        foreach ($key in $ownedKeys) {
            if (-not $key.StartsWith($namespace, [StringComparison]::Ordinal)) {
                throw "Redis namespace enumeration returned foreign key '$key' for '$namespace'."
            }
        }

        $ownerKey = "${namespace}__owner"
        $nonOwnerKeys = [string[]]@($ownedKeys | Where-Object { -not [string]::Equals($_, $ownerKey, [StringComparison]::Ordinal) })
        [Array]::Sort($nonOwnerKeys, [StringComparer]::Ordinal)
        foreach ($key in $nonOwnerKeys) {
            & $RemoveKeyAction $key
        }
        if (@($ownedKeys | Where-Object { [string]::Equals($_, $ownerKey, [StringComparison]::Ordinal) }).Count -ne 0) {
            & $RemoveKeyAction $ownerKey
        }

        $remaining = @(& $EnumerateKeysAction $namespace)
        if ($remaining.Count -ne 0) {
            throw "Redis test namespace '$namespace' cleanup left $($remaining.Count) key(s)."
        }

        [pscustomobject]@{ Namespace = $namespace; CreatedAtUtc = $candidate.CreatedAtUtc; Outcome = 'deleted' }
    }
}
