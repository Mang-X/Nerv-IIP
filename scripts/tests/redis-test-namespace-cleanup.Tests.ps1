# Script-Governance:
#   Category: check
#   SideEffects:
#     - Validates stale Redis test namespace cleanup through injected actions
#   Writes:
#     - None
#   Cleanup:
#     - Does not connect to Redis or delete keys
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/RedisTestNamespaceCleanup.ps1')
function Assert-Contract([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function New-TestNamespace([DateTimeOffset]$CreatedAt) {
    $milliseconds = $CreatedAt.ToUnixTimeMilliseconds()
    return 'nerv:n822:{0:x12}7abc8def0123456789ab:' -f $milliseconds
}

$now = [DateTimeOffset]::Parse('2026-08-14T12:00:00Z', [Globalization.CultureInfo]::InvariantCulture)
$minimumAge = [TimeSpan]::FromHours(24)
$older = New-TestNamespace ($now - [TimeSpan]::FromHours(48))
$exactlyOld = New-TestNamespace ($now - $minimumAge)
$young = New-TestNamespace ($now - $minimumAge + [TimeSpan]::FromMilliseconds(1))
$future = New-TestNamespace ($now + [TimeSpan]::FromMinutes(1))

$context = ConvertTo-NervRedisCliContext '127.0.0.1:6379,password=local-secret,abortConnect=false'
Assert-Contract ([string]::Equals([string]$context.Host, '127.0.0.1', [StringComparison]::Ordinal) -and $context.Port -eq 6379) 'Redis CLI host and port must parse from the established endpoint format.'
Assert-Contract ([string]::Equals([string]$context.Password, 'local-secret', [StringComparison]::Ordinal)) 'Redis password must be passed through scoped REDISCLI_AUTH rather than command arguments.'
Assert-Contract (-not (($context.Arguments -join ' ').Contains('local-secret', [StringComparison]::Ordinal))) 'Redis CLI arguments must not expose the password.'

$parsed = ConvertFrom-NervRedisTestNamespaceKey "${older}stream"
Assert-Contract ($null -ne $parsed -and [string]::Equals([string]$parsed.Namespace, $older, [StringComparison]::Ordinal)) 'A canonical NERV-822 UUIDv7 namespace key must parse.'
Assert-Contract ($parsed.CreatedAtUtc -eq ($now - [TimeSpan]::FromHours(48))) 'Redis namespace UUIDv7 timestamp must round-trip exactly.'
foreach ($invalid in @(
    'business:key',
    'nerv:n688:0123456789abcdef:stream',
    'nerv:n822:0198abcdefab4abc8def0123456789ab:stream',
    'nerv:n822:0198abcdefab7abc8def0123456789ab:',
    'nerv:n822:0198abcdefab7abc8def0123456789ab'
)) {
    Assert-Contract ($null -eq (ConvertFrom-NervRedisTestNamespaceKey -Key $invalid)) "Invalid key '$invalid' must fail closed."
}

$keys = @("${older}stream", "${older}__owner", "${exactlyOld}stream", "${young}stream", "${future}stream", 'shared:key')
$candidates = @(Get-NervStaleRedisTestNamespaceCandidate -Keys $keys -NowUtc $now -MinimumAge $minimumAge)
Assert-Contract ($candidates.Count -eq 2) 'Only canonical namespaces at or older than the exact threshold may become candidates.'
Assert-Contract ([string]::Equals([string]$candidates[0].Namespace, $older, [StringComparison]::Ordinal) -and [string]::Equals([string]$candidates[1].Namespace, $exactlyOld, [StringComparison]::Ordinal)) 'Candidates must be unique and deterministic oldest-first.'

$removed = [Collections.Generic.List[string]]::new()
$keyState = [Collections.Generic.HashSet[string]]::new([string[]]@("${older}stream", "${older}__owner", 'shared:key'), [StringComparer]::Ordinal)
$enumerate = { param([string]$Namespace) @($keyState | Where-Object { $_.StartsWith($Namespace, [StringComparison]::Ordinal) }) }.GetNewClosure()
$remove = { param([string]$Key) [void]$keyState.Remove($Key); $removed.Add($Key) }.GetNewClosure()
$preview = @(Invoke-NervRedisTestNamespaceCleanup -Keys @("${older}stream") -NowUtc $now -MinimumAge $minimumAge -TestOwnerLeaseAction { param($namespace) $false } -EnumerateKeysAction $enumerate -RemoveKeyAction $remove)
Assert-Contract ($removed.Count -eq 0 -and [string]::Equals([string]$preview[0].Outcome, 'preview', [StringComparison]::Ordinal)) 'Default cleanup must only preview stale namespaces.'

$active = @(Invoke-NervRedisTestNamespaceCleanup -Keys @("${older}stream") -NowUtc $now -MinimumAge $minimumAge -Apply -TestOwnerLeaseAction { param($namespace) $true } -EnumerateKeysAction { param($namespace) throw 'must not enumerate active namespace' } -RemoveKeyAction { param($key) throw 'must not remove active namespace' })
Assert-Contract ([string]::Equals([string]$active[0].Outcome, 'skipped-active', [StringComparison]::Ordinal)) 'An unexpired owner lease must prevent cleanup.'

$deleted = @(Invoke-NervRedisTestNamespaceCleanup -Keys @("${older}stream") -NowUtc $now -MinimumAge $minimumAge -Apply -TestOwnerLeaseAction { param($namespace) $false } -EnumerateKeysAction $enumerate -RemoveKeyAction $remove)
Assert-Contract (
    $removed.Count -eq 2 -and
    [string]::Equals([string]$removed[0], "${older}stream", [StringComparison]::Ordinal) -and
    [string]::Equals([string]$removed[1], "${older}__owner", [StringComparison]::Ordinal)) 'Apply must remove exact namespace keys deterministically and keep its owner claim until last.'
Assert-Contract (
    [string]::Equals([string]$deleted[0].Outcome, 'deleted', [StringComparison]::Ordinal) -and
    [Linq.Enumerable]::Contains($keyState, 'shared:key', [StringComparer]::Ordinal)) 'Apply must verify cleanup and preserve foreign keys.'

$foreignRejected = $false
try {
    Invoke-NervRedisTestNamespaceCleanup -Keys @("${exactlyOld}stream") -NowUtc $now -MinimumAge $minimumAge -Apply -TestOwnerLeaseAction { param($namespace) $false } -EnumerateKeysAction { param($namespace) @('shared:key') } -RemoveKeyAction { param($key) } | Out-Null
}
catch { $foreignRejected = $_.Exception.Message.Contains('foreign key', [StringComparison]::Ordinal) }
Assert-Contract $foreignRejected 'Namespace enumeration must fail closed when it returns a foreign key.'

$readbackRejected = $false
try {
    Invoke-NervRedisTestNamespaceCleanup -Keys @("${exactlyOld}stream") -NowUtc $now -MinimumAge $minimumAge -Apply -TestOwnerLeaseAction { param($namespace) $false } -EnumerateKeysAction { param($namespace) @("${exactlyOld}stream") } -RemoveKeyAction { param($key) } | Out-Null
}
catch { $readbackRejected = $_.Exception.Message.Contains('cleanup left', [StringComparison]::Ordinal) }
Assert-Contract $readbackRejected 'A key that survives UNLINK must fail cleanup.'

$entryPath = Join-Path $repoRoot 'scripts/cleanup-stale-redis-test-keys.ps1'
Assert-Contract (Test-Path -LiteralPath $entryPath -PathType Leaf) 'The governed Redis cleanup entrypoint must exist.'
$entry = [IO.File]::ReadAllText($entryPath)
Assert-Contract ($entry.Contains('lib/ScriptAutomation.ps1', [StringComparison]::Ordinal)) 'The entrypoint must dot-source ScriptAutomation.ps1.'
Assert-Contract ($entry.Contains('NERV_IIP_TEST_REDIS', [StringComparison]::Ordinal)) 'The entrypoint must consume the established base variable.'
Assert-Contract (-not $entry.Contains('FLUSHALL', [StringComparison]::OrdinalIgnoreCase)) 'Stale cleanup must never flush shared Redis.'
Assert-Contract ($entry.Contains("'UNLINK'", [StringComparison]::Ordinal)) 'Apply must use exact per-key UNLINK.'
Assert-Contract ($entry.Contains("'NX', 'EX', '300'", [StringComparison]::Ordinal)) 'Apply must atomically claim the stale namespace before enumerating keys.'
Assert-Contract ($entry.Contains("redis.call('get', KEYS[1]) == ARGV[1]", [StringComparison]::Ordinal)) 'Apply must token-check its owner claim before releasing it last.'

Write-Output 'Redis test namespace cleanup contract tests passed.'
