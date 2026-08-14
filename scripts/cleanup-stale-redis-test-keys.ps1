# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Enumerates Redis keys and deletes inactive stale NERV-822 UUIDv7 test namespaces only when -Apply is supplied
#   Writes:
#     - artifacts/script-logs/**
#   Cleanup:
#     - Restores REDISCLI_AUTH and leaves Redis plus all non-candidate keys running
#   Requires:
#     - PowerShell 7
#     - Redis redis-cli client
#     - NERV_IIP_TEST_REDIS targeting a Redis endpoint

[CmdletBinding()]
param(
    [ValidateRange(1, 720)]
    [int] $MinimumAgeHours = 24,

    [switch] $Apply
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'lib/RedisTestNamespaceCleanup.ps1')

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$connectionString = [Environment]::GetEnvironmentVariable('NERV_IIP_TEST_REDIS')
if ([string]::IsNullOrWhiteSpace($connectionString)) {
    throw 'Set NERV_IIP_TEST_REDIS before previewing or applying stale Redis test key cleanup.'
}

$context = ConvertTo-NervRedisCliContext -ConnectionString $connectionString
$scopedEnvironment = @{ REDISCLI_AUTH = $context.Password }
Invoke-WithScopedEnvironment -Variables $scopedEnvironment -ScriptBlock {
    $cleanupOwnerToken = "stale-cleanup-$([Guid]::NewGuid().ToString('N'))"
    function Invoke-NervRedisCleanupCommand {
        param([string]$Name, [string[]]$Arguments)
        return Invoke-NativeCommandOutput `
            -Command 'redis-cli' `
            -Arguments (@($context.Arguments) + $Arguments) `
            -WorkingDirectory $repoRoot `
            -Name $Name
    }

    function Get-NervRedisCleanupKeys {
        param([string]$Namespace)
        $scan = Invoke-NervRedisCleanupCommand -Name "redis-test-key-cleanup-scan-$($Namespace.GetHashCode())" -Arguments @('--scan', '--pattern', "$Namespace*")
        return @($scan.Stdout -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }

    $testOwnerLease = {
        param([string]$Namespace)
        if ($Apply) {
            $claim = Invoke-NervRedisCleanupCommand `
                -Name "redis-test-key-cleanup-claim-$($Namespace.GetHashCode())" `
                -Arguments @('SET', "${Namespace}__owner", $cleanupOwnerToken, 'NX', 'EX', '300')
            return -not [string]::Equals($claim.Stdout.Trim(), 'OK', [StringComparison]::Ordinal)
        }

        $exists = Invoke-NervRedisCleanupCommand `
            -Name "redis-test-key-cleanup-owner-$($Namespace.GetHashCode())" `
            -Arguments @('EXISTS', "${Namespace}__owner")
        return [int]$exists.Stdout.Trim() -ne 0
    }
    $enumerateKeys = { param([string]$Namespace) Get-NervRedisCleanupKeys -Namespace $Namespace }
    $removeKey = {
        param([string]$Key)
        if ($Key.EndsWith(':__owner', [StringComparison]::Ordinal)) {
            $release = Invoke-NervRedisCleanupCommand `
                -Name "redis-test-key-cleanup-owner-release-$($Key.GetHashCode())" `
                -Arguments @(
                    'EVAL',
                    "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) end return 0",
                    '1',
                    $Key,
                    $cleanupOwnerToken)
            if (-not [string]::Equals($release.Stdout.Trim(), '1', [StringComparison]::Ordinal)) {
                throw "Redis stale cleanup lost ownership before releasing '$Key'."
            }
            return
        }

        $removed = Invoke-NervRedisCleanupCommand `
            -Name "redis-test-key-cleanup-unlink-$($Key.GetHashCode())" `
            -Arguments @('UNLINK', $Key)
        if (-not [string]::Equals($removed.Stdout.Trim(), '1', [StringComparison]::Ordinal)) {
            throw "Redis stale cleanup did not remove owned key '$Key'."
        }
    }

    $allKeys = @(Get-NervRedisCleanupKeys -Namespace 'nerv:n822:')
    $mode = if ($Apply) { 'apply' } else { 'preview' }
    Write-Diagnostic "Redis stale test key cleanup mode=$mode host=$($context.Host) port=$($context.Port) minimumAgeHours=$MinimumAgeHours."
    $results = @(Invoke-NervRedisTestNamespaceCleanup `
        -Keys $allKeys `
        -NowUtc ([DateTimeOffset]::UtcNow) `
        -MinimumAge ([TimeSpan]::FromHours($MinimumAgeHours)) `
        -Apply:$Apply `
        -TestOwnerLeaseAction $testOwnerLease `
        -EnumerateKeysAction $enumerateKeys `
        -RemoveKeyAction $removeKey)
    foreach ($result in $results) {
        Write-Diagnostic "Redis test namespace=$($result.Namespace) createdAtUtc=$($result.CreatedAtUtc.ToString('O')) outcome=$($result.Outcome)."
    }
    Write-Diagnostic "Redis stale test key cleanup completed: mode=$mode candidates=$($results.Count)."
}
