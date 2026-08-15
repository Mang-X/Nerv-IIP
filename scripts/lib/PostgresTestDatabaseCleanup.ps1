# Script-Governance:
#   Category: library
#   SideEffects:
#     - Provides pure candidate selection and injected PostgreSQL cleanup orchestration
#   Writes:
#     - None
#   Cleanup:
#     - Does not connect to PostgreSQL unless caller-provided actions do so
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest

function ConvertTo-NervPostgresAdminEnvironment {
    param([Parameter(Mandatory)] [string] $ConnectionString)

    try {
        $builder = [Data.Common.DbConnectionStringBuilder]::new()
        $builder.set_ConnectionString($ConnectionString)
    }
    catch {
        throw 'NERV_IIP_TEST_POSTGRES is not a valid connection string; credentials redacted.'
    }

    $aliases = [ordered]@{
        PGHOST = @('Host', 'Server')
        PGPORT = @('Port')
        PGDATABASE = @('Database', 'Initial Catalog')
        PGUSER = @('Username', 'User ID', 'UserId')
        PGPASSWORD = @('Password', 'Pwd')
    }
    $environment = @{}
    foreach ($target in $aliases.Keys) {
        $value = $null
        foreach ($key in $builder.Keys) {
            foreach ($alias in $aliases[$target]) {
                if ([string]::Equals([string] $key, $alias, [StringComparison]::OrdinalIgnoreCase)) {
                    $value = [string] $builder[[string] $key]
                }
            }
        }
        if ([string]::IsNullOrWhiteSpace($value)) {
            throw "NERV_IIP_TEST_POSTGRES is missing '$target'; credentials redacted."
        }
        $environment[$target] = $value
    }
    if (-not [string]::Equals([string] $environment.PGDATABASE, 'postgres', [StringComparison]::Ordinal)) {
        throw 'NERV_IIP_TEST_POSTGRES must explicitly use Database=postgres; credentials redacted.'
    }
    return $environment
}

function ConvertFrom-NervPostgresTestDatabaseName {
    param([Parameter(Mandatory)] [string] $DatabaseName)

    $match = [regex]::Match(
        $DatabaseName,
        '^(?<prefix>nerv(?:_[a-z0-9]+)+)_(?<timestamp>[0-9a-f]{12})7[0-9a-f]{3}[89ab][0-9a-f]{15}$',
        [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if ($DatabaseName.Length -gt 63 -or -not $match.Success) {
        return $null
    }

    $prefix = [string] $match.Groups['prefix'].Value
    if ($prefix.Length -gt 30) {
        return $null
    }

    try {
        $milliseconds = [Convert]::ToInt64([string] $match.Groups['timestamp'].Value, 16)
        $createdAtUtc = [DateTimeOffset]::FromUnixTimeMilliseconds($milliseconds)
    }
    catch {
        return $null
    }

    return [pscustomobject]@{
        DatabaseName = $DatabaseName
        Prefix = $prefix
        CreatedAtUtc = $createdAtUtc
    }
}

function Get-NervStalePostgresTestDatabaseCandidate {
    param(
        [string[]] $DatabaseNames = @(),
        [Parameter(Mandatory)] [DateTimeOffset] $NowUtc,
        [Parameter(Mandatory)] [TimeSpan] $MinimumAge
    )

    if ($MinimumAge -le [TimeSpan]::Zero) {
        throw 'MinimumAge must be greater than zero.'
    }

    $threshold = $NowUtc - $MinimumAge
    $candidates = [Collections.Generic.List[object]]::new()
    foreach ($databaseName in $DatabaseNames) {
        if ([string]::IsNullOrWhiteSpace($databaseName)) {
            continue
        }

        $parsed = ConvertFrom-NervPostgresTestDatabaseName -DatabaseName $databaseName
        if ($null -ne $parsed -and $parsed.CreatedAtUtc -le $threshold) {
            $candidates.Add($parsed)
        }
    }

    $candidates.Sort([Comparison[object]] {
        param($left, $right)
        $leftTicks = ([DateTimeOffset] $left.CreatedAtUtc).UtcTicks
        $rightTicks = ([DateTimeOffset] $right.CreatedAtUtc).UtcTicks
        if ($leftTicks -lt $rightTicks) { return -1 }
        if ($leftTicks -gt $rightTicks) { return 1 }
        return [StringComparer]::Ordinal.Compare([string] $left.DatabaseName, [string] $right.DatabaseName)
    })
    return $candidates.ToArray()
}

function Invoke-NervPostgresTestDatabaseCleanup {
    param(
        [string[]] $DatabaseNames = @(),
        [Parameter(Mandatory)] [DateTimeOffset] $NowUtc,
        [Parameter(Mandatory)] [TimeSpan] $MinimumAge,
        [switch] $Apply,
        [Parameter(Mandatory)] [scriptblock] $GetActiveSessionCountAction,
        [Parameter(Mandatory)] [scriptblock] $DropDatabaseAction,
        [Parameter(Mandatory)] [scriptblock] $DatabaseExistsAction
    )

    $candidates = @(Get-NervStalePostgresTestDatabaseCandidate -DatabaseNames $DatabaseNames -NowUtc $NowUtc -MinimumAge $MinimumAge)
    foreach ($candidate in $candidates) {
        $databaseName = [string] $candidate.DatabaseName
        $activeSessionCount = [int] (& $GetActiveSessionCountAction $databaseName)
        if ($activeSessionCount -ne 0) {
            [pscustomobject]@{ DatabaseName = $databaseName; CreatedAtUtc = $candidate.CreatedAtUtc; Outcome = 'skipped-active' }
            continue
        }

        if (-not $Apply) {
            [pscustomobject]@{ DatabaseName = $databaseName; CreatedAtUtc = $candidate.CreatedAtUtc; Outcome = 'preview' }
            continue
        }

        & $DropDatabaseAction $databaseName
        if ([bool] (& $DatabaseExistsAction $databaseName)) {
            throw "PostgreSQL test database '$databaseName' still exists after DROP."
        }

        [pscustomobject]@{ DatabaseName = $databaseName; CreatedAtUtc = $candidate.CreatedAtUtc; Outcome = 'deleted' }
    }
}
