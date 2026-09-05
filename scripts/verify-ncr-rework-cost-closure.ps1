# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Builds and starts seven real business-service Web processes on reserved loopback ports
#     - Creates and drops one randomly named PostgreSQL database
#     - Uses the caller-provided Redis instance for real CAP delivery
#   Writes:
#     - Service/test bin and obj outputs
#     - artifacts/script-logs/**
#     - The caller-injected FullChain TRX and entrypoint evidence files
#   Cleanup:
#     - Stops only this invocation's managed processes
#     - Drops only this invocation's exact random database
#     - Removes only this invocation's unique Redis topic namespace and verifies a foreign sentinel survives
#     - Verifies zero managed process, database, and Redis residue
#   Requires:
#     - PowerShell 7, .NET SDK 10, Docker, redis-cli, PostgreSQL and Redis lane variables

param(
    [string]$PostgresAdminConnectionString = $env:NERV_IIP_TEST_POSTGRES,
    [string]$RedisConnectionString = $env:NERV_IIP_TEST_REDIS,
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root
. (Join-Path $root 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $root 'scripts/lib/RedisTestNamespaceCleanup.ps1')

if ([string]::IsNullOrWhiteSpace($PostgresAdminConnectionString) -or
    [string]::IsNullOrWhiteSpace($RedisConnectionString)) {
    throw 'Set NERV_IIP_TEST_POSTGRES and NERV_IIP_TEST_REDIS for MAN-2813 FullChain verification.'
}

function New-Man2813PortOwner {
    param([Parameter(Mandatory)] [string]$Name)
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    [pscustomobject]@{
        Name = $Name
        Port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
        Reservation = $listener
        Managed = $null
        ProcessId = $null
        ProcessStartTime = $null
    }
}

function Start-Man2813Service {
    param(
        [Parameter(Mandatory)] [object]$Owner,
        [Parameter(Mandatory)] [string]$Dll,
        [Parameter(Mandatory)] [string]$WorkingDirectory,
        [Parameter(Mandatory)] [hashtable]$Environment
    )
    $Owner.Reservation.Stop()
    $Owner.Reservation = $null
    Invoke-WithScopedEnvironment -Variables $Environment -ScriptBlock {
        $managed = Start-ManagedBackgroundProcess -Command 'dotnet' -Arguments @($Dll) -WorkingDirectory $WorkingDirectory -Name "man2813-$($Owner.Name)"
        $Owner.Managed = $managed
        $Owner.ProcessId = $managed.ProcessId
        $Owner.ProcessStartTime = $managed.Process.StartTime
    }
}

function Wait-Man2813Healthy {
    param(
        [Parameter(Mandatory)] [object]$Owner,
        [string]$Path = '/health',
        [hashtable]$Headers = @{}
    )
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(120)
    do {
        if ($null -ne $Owner.Managed -and $Owner.Managed.Process.HasExited) {
            throw "MAN-2813 service '$($Owner.Name)' exited before health readiness. Logs: $($Owner.Managed.LogDirectory)"
        }
        try {
            $response = Invoke-WebRequest -Method Get -Uri "http://127.0.0.1:$($Owner.Port)$Path" -Headers $Headers -TimeoutSec 3
            if ($response.StatusCode -eq 200) { return }
        }
        catch { }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "MAN-2813 service '$($Owner.Name)' did not become healthy on reserved port $($Owner.Port)."
}

function Invoke-Man2813DatabaseSql {
    param([Parameter(Mandatory)] [string]$Sql, [Parameter(Mandatory)] [string]$Name)
    Invoke-NativeCommandOutput -Command 'docker' -Arguments @(
        'compose', '-f', (Join-Path $root 'infra/docker-compose.dev.yml'), 'exec', '-T', 'postgres',
        'psql', '-h', '127.0.0.1', '-U', 'nerv', '-d', 'postgres', '-X', '-tA',
        '-v', 'ON_ERROR_STOP=1', '-c', $Sql
    ) -WorkingDirectory $root -Name $Name
}

function Invoke-Man2813Redis {
    param([Parameter(Mandatory)] [string]$Name, [Parameter(Mandatory)] [string[]]$Arguments)

    Invoke-WithScopedEnvironment -Variables @{ REDISCLI_AUTH = $redisContext.Password } -ScriptBlock {
        Invoke-NativeCommandOutput -Command 'redis-cli' -Arguments (@($redisContext.Arguments) + $Arguments) -WorkingDirectory $root -TimeoutSeconds 30 -Name $Name
    }
}

function Get-Man2813RedisKeys {
    param([Parameter(Mandatory)] [string]$Namespace, [Parameter(Mandatory)] [string]$Name)

    if ($Namespace -cnotmatch '^nerv:n2813:[0-9a-f]{32}:$') { throw "MAN-2813 Redis namespace '$Namespace' is not canonical." }
    $scan = Invoke-Man2813Redis -Name $Name -Arguments @('--scan', '--pattern', "$Namespace*")
    $keys = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    foreach ($key in @($scan.Stdout -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        if (-not $key.StartsWith($Namespace, [StringComparison]::Ordinal)) {
            throw "MAN-2813 Redis namespace scan returned foreign key '$key'."
        }
        [void]$keys.Add($key)
    }
    return @($keys)
}

function Get-Man2813TrxCount {
    param([Parameter(Mandatory)] [xml]$Trx, [Parameter(Mandatory)] [string]$Name)
    $counters = $Trx.SelectSingleNode("//*[local-name()='Counters']")
    if ($null -eq $counters) { throw 'MAN-2813 TRX has no Counters element.' }
    $value = 0
    if (-not [int]::TryParse($counters.GetAttribute($Name), [ref]$value)) {
        throw "MAN-2813 TRX counter '$Name' is not an integer."
    }
    return $value
}

$configuration = if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_FULL_CHAIN_CONFIGURATION)) { 'Release' } else { $env:NERV_IIP_FULL_CHAIN_CONFIGURATION }
$databaseName = "man2813_$([Guid]::NewGuid().ToString('N'))"
$databaseConnectionString = if ($PostgresAdminConnectionString -match '(?i)Database=[^;]*') {
    $PostgresAdminConnectionString -replace '(?i)Database=[^;]*', "Database=$databaseName"
} else {
    "$($PostgresAdminConnectionString.TrimEnd(';'));Database=$databaseName"
}
$capVersion = "man2813-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
$redisNamespace = "nerv:n2813:$([Guid]::NewGuid().ToString('N')):"
$foreignRedisSentinel = "nerv:n2813-control:$([Guid]::NewGuid().ToString('N'))"
$redisContext = ConvertTo-NervRedisCliContext -ConnectionString $RedisConnectionString
$internalToken = "man2813-$([Guid]::NewGuid().ToString('N'))"
$databaseCreated = $false
$redisNamespaceClaimed = $false
$foreignRedisSentinelCreated = $false
$foreignRedisSentinelPreserved = $false
$acceptanceFailure = $null
$cleanupErrors = [Collections.Generic.List[string]]::new()
$remainingProcesses = [Collections.Generic.List[string]]::new()
$remainingDatabase = 0
$remainingRedisKeys = 0
$remainingForeignRedisSentinel = 0

$serviceSpecs = [ordered]@{
    masterData = 'backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Nerv.IIP.Business.MasterData.Web.csproj'
    approval = 'backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Nerv.IIP.Business.Approval.Web.csproj'
    productEngineering = 'backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Nerv.IIP.Business.ProductEngineering.Web.csproj'
    inventory = 'backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Nerv.IIP.Business.Inventory.Web.csproj'
    erp = 'backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Nerv.IIP.Business.Erp.Web.csproj'
    quality = 'backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Nerv.IIP.Business.Quality.Web.csproj'
    mes = 'backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Nerv.IIP.Business.Mes.Web.csproj'
}
$owners = [ordered]@{}
foreach ($name in $serviceSpecs.Keys) { $owners[$name] = New-Man2813PortOwner -Name $name }

$testProject = Join-Path $root 'backend/tests/Nerv.IIP.Business.FullChain.Tests/Nerv.IIP.Business.FullChain.Tests.csproj'
$testIdentity = 'Nerv.IIP.Business.FullChain.Tests.NcrReworkCostClosurePostgresRedisAcceptanceTests.Public_ncr_rework_closes_one_traceable_work_order_and_independent_erp_cost'
$resultsDirectory = if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_FULL_CHAIN_RESULTS_DIRECTORY)) {
    Join-Path $root 'artifacts/acceptance/man2813'
} else { [IO.Path]::GetFullPath($env:NERV_IIP_FULL_CHAIN_RESULTS_DIRECTORY) }
$resultFile = if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_FULL_CHAIN_RESULT_FILE)) {
    'ncr-rework-cost-closure.trx'
} else { $env:NERV_IIP_FULL_CHAIN_RESULT_FILE }
$resultPath = Join-Path $resultsDirectory $resultFile
$evidencePath = if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_FULL_CHAIN_ENTRYPOINT_EVIDENCE_PATH)) {
    Join-Path $resultsDirectory 'entrypoint-evidence.json'
} else { [IO.Path]::GetFullPath($env:NERV_IIP_FULL_CHAIN_ENTRYPOINT_EVIDENCE_PATH) }
[IO.Directory]::CreateDirectory($resultsDirectory) | Out-Null

try {
    $redisProbe = Invoke-Man2813Redis -Name 'man2813-redis-readiness' -Arguments @('PING')
    if (-not [string]::Equals($redisProbe.Stdout.Trim(), 'PONG', [StringComparison]::Ordinal)) {
        throw 'MAN-2813 Redis readiness probe did not return PONG.'
    }
    if (@(Get-Man2813RedisKeys -Namespace $redisNamespace -Name 'man2813-redis-claim').Count -ne 0) {
        throw "MAN-2813 Redis namespace '$redisNamespace' was not empty before claim."
    }
    $redisNamespaceClaimed = $true
    $sentinelCreated = Invoke-Man2813Redis -Name 'man2813-foreign-redis-sentinel-create' -Arguments @('SET', $foreignRedisSentinel, $capVersion, 'NX')
    if (-not [string]::Equals($sentinelCreated.Stdout.Trim(), 'OK', [StringComparison]::Ordinal)) {
        throw 'MAN-2813 could not create its foreign Redis sentinel.'
    }
    $foreignRedisSentinelCreated = $true

    Invoke-Man2813DatabaseSql -Sql "CREATE DATABASE $databaseName;" -Name 'man2813-create-database' | Out-Null
    $databaseCreated = $true

    if (-not $SkipBuild) {
        foreach ($relativeProject in @($serviceSpecs.Values) + @('backend/tests/Nerv.IIP.Business.FullChain.Tests/Nerv.IIP.Business.FullChain.Tests.csproj')) {
            Invoke-DotNet -Arguments @('build', (Join-Path $root $relativeProject), '--configuration', $configuration, '-m:1', '-nr:false', '/p:UseSharedCompilation=false') -WorkingDirectory $root -TimeoutSeconds 600 -Name 'man2813-build' | Out-Null
        }
    }

    $urls = @{}
    foreach ($name in $owners.Keys) { $urls[$name] = "http://127.0.0.1:$($owners[$name].Port)" }
    $commonEnvironment = @{
        ASPNETCORE_ENVIRONMENT = 'Development'
        Persistence__Provider = 'PostgreSQL'
        Persistence__AutoMigrate = 'true'
        ConnectionStrings__PostgreSQL = $databaseConnectionString
        Messaging__Provider = 'Redis'
        Messaging__Redis__ConnectionString = $RedisConnectionString
        ConnectionStrings__Redis = $RedisConnectionString
        Cap__Version = $capVersion
        Cap__TopicNamePrefix = $redisNamespace
        Cap__FailedRetryInterval = '2'
        Cap__FallbackWindowLookbackSeconds = '30'
        InternalService__BearerToken = $internalToken
    }

    foreach ($name in $serviceSpecs.Keys) {
        $project = Join-Path $root $serviceSpecs[$name]
        $projectDirectory = Split-Path -Parent $project
        $assemblyName = [IO.Path]::GetFileNameWithoutExtension($project)
        $dll = Join-Path $projectDirectory "bin/$configuration/net10.0/$assemblyName.dll"
        $specific = @{
            ASPNETCORE_URLS = $urls[$name]
            MasterData__BaseUrl = $urls.masterData
            Approval__BaseUrl = $urls.approval
            ProductEngineering__BaseUrl = $urls.productEngineering
            Inventory__BaseUrl = $urls.inventory
            Inventory__SiteCode = 'production'
            Erp__BaseUrl = $urls.erp
            Quality__BaseUrl = $urls.quality
        }
        if ([string]::Equals($name, 'masterData', [StringComparison]::Ordinal) -or
            [string]::Equals($name, 'productEngineering', [StringComparison]::Ordinal)) {
            $specific.LeaderDemo__Seed__Enabled = 'true'
        }
        Start-Man2813Service -Owner $owners[$name] -Dll $dll -WorkingDirectory $projectDirectory -Environment ($commonEnvironment + $specific)
        if ([string]::Equals($name, 'mes', [StringComparison]::Ordinal)) {
            Wait-Man2813Healthy -Owner $owners[$name] `
                -Path '/api/business/v1/mes/work-orders?organizationId=org-001&environmentId=env-dev&take=1' `
                -Headers @{ Authorization = "Bearer $internalToken" }
        }
        else { Wait-Man2813Healthy -Owner $owners[$name] }
    }

    Invoke-WithScopedEnvironment -Variables @{
        NERV_IIP_TEST_POSTGRES = $databaseConnectionString
        NERV_IIP_TEST_REDIS = $RedisConnectionString
        NERV_IIP_TEST_MASTER_DATA_URL = $urls.masterData
        NERV_IIP_TEST_PRODUCT_ENGINEERING_URL = $urls.productEngineering
        NERV_IIP_TEST_INVENTORY_URL = $urls.inventory
        NERV_IIP_TEST_APPROVAL_URL = $urls.approval
        NERV_IIP_TEST_QUALITY_URL = $urls.quality
        NERV_IIP_TEST_MES_URL = $urls.mes
        NERV_IIP_TEST_ERP_URL = $urls.erp
        NERV_IIP_TEST_INTERNAL_TOKEN = $internalToken
    } -ScriptBlock {
        Invoke-DotNet -Arguments @(
            'test', $testProject, '--configuration', $configuration, '--no-build', '--no-restore',
            '--filter', "FullyQualifiedName=$testIdentity", '--results-directory', $resultsDirectory,
            '--logger', "trx;LogFileName=$resultFile", '-m:1', '-nr:false'
        ) -WorkingDirectory $root -TimeoutSeconds 300 -Name 'man2813-full-chain-test' | Out-Null
    }

    if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) { throw 'MAN-2813 produced no TRX.' }
    [xml]$trx = Get-Content -LiteralPath $resultPath -Raw
    $executions = @($trx.SelectNodes("//*[local-name()='UnitTestResult']") | Where-Object {
        [string]::Equals([string]$_.GetAttribute('testName'), $testIdentity, [StringComparison]::Ordinal)
    })
    $total = Get-Man2813TrxCount -Trx $trx -Name 'total'
    $executed = Get-Man2813TrxCount -Trx $trx -Name 'executed'
    $passed = Get-Man2813TrxCount -Trx $trx -Name 'passed'
    $failed = Get-Man2813TrxCount -Trx $trx -Name 'failed'
    if ($executions.Count -ne 1 -or
        -not [string]::Equals($executions[0].GetAttribute('outcome'), 'Passed', [StringComparison]::Ordinal) -or
        $total -ne 1 -or $executed -ne 1 -or $passed -ne 1 -or $failed -ne 0) {
        throw "MAN-2813 requires exact TRX counts total=executed=passed=1, failed=skipped=0; actual total=$total executed=$executed passed=$passed failed=$failed."
    }
}
catch { $acceptanceFailure = $_ }
finally {
    foreach ($name in @($owners.Keys)[(@($owners.Keys).Count - 1)..0]) {
        $owner = $owners[$name]
        try {
            if ($null -ne $owner.Reservation) { $owner.Reservation.Stop(); $owner.Reservation = $null }
            if ($null -ne $owner.Managed) { $owner.Managed.Stop.Invoke('MAN-2813 verification cleanup') | Out-Null }
        }
        catch { $cleanupErrors.Add("process $name`: $($_.Exception.Message)") }
    }
    foreach ($name in $owners.Keys) {
        $owner = $owners[$name]
        if ($null -ne $owner.ProcessId -and $null -ne (Get-Process -Id $owner.ProcessId -ErrorAction SilentlyContinue)) {
            $remainingProcesses.Add("$name`:$($owner.ProcessId)")
        }
    }
    if ($remainingProcesses.Count -gt 0) { $cleanupErrors.Add("managed processes remain: $($remainingProcesses -join ', ')") }
    if ($databaseCreated) {
        try { Invoke-Man2813DatabaseSql -Sql "DROP DATABASE IF EXISTS $databaseName WITH (FORCE);" -Name 'man2813-drop-database' | Out-Null }
        catch { $cleanupErrors.Add("database drop: $($_.Exception.Message)") }
        try {
            $readback = Invoke-Man2813DatabaseSql -Sql "SELECT count(*) FROM pg_database WHERE datname = '$databaseName';" -Name 'man2813-database-readback'
            if (-not [int]::TryParse("$($readback.Stdout)".Trim(), [ref]$remainingDatabase)) {
                $cleanupErrors.Add('database cleanup readback was not countable.')
            }
        }
        catch { $cleanupErrors.Add("database readback: $($_.Exception.Message)") }
    }
    if ($redisNamespaceClaimed) {
        try {
            foreach ($key in @(Get-Man2813RedisKeys -Namespace $redisNamespace -Name 'man2813-redis-owned-scan')) {
                $removed = Invoke-Man2813Redis -Name 'man2813-redis-owned-remove' -Arguments @('UNLINK', $key)
                if (-not [string]::Equals($removed.Stdout.Trim(), '1', [StringComparison]::Ordinal)) {
                    throw "MAN-2813 Redis cleanup did not remove owned key '$key'."
                }
            }
            $remainingRedisKeys = @(Get-Man2813RedisKeys -Namespace $redisNamespace -Name 'man2813-redis-owned-readback').Count
            if ($remainingRedisKeys -ne 0) { $cleanupErrors.Add("owned Redis keys remain: $remainingRedisKeys") }
        }
        catch { $cleanupErrors.Add("Redis namespace cleanup: $($_.Exception.Message)") }
    }
    if ($foreignRedisSentinelCreated) {
        try {
            $sentinelReadback = Invoke-Man2813Redis -Name 'man2813-foreign-redis-sentinel-readback' -Arguments @('GET', $foreignRedisSentinel)
            $foreignRedisSentinelPreserved = [string]::Equals($sentinelReadback.Stdout.Trim(), $capVersion, [StringComparison]::Ordinal)
            if (-not $foreignRedisSentinelPreserved) { $cleanupErrors.Add('foreign Redis sentinel was not preserved by namespace cleanup.') }
        }
        catch { $cleanupErrors.Add("foreign Redis sentinel readback: $($_.Exception.Message)") }
        try {
            $sentinelRemoved = Invoke-Man2813Redis -Name 'man2813-foreign-redis-sentinel-remove' -Arguments @('UNLINK', $foreignRedisSentinel)
            if (-not [string]::Equals($sentinelRemoved.Stdout.Trim(), '1', [StringComparison]::Ordinal)) {
                $cleanupErrors.Add('foreign Redis sentinel exact cleanup did not remove one key.')
            }
            $sentinelRemaining = Invoke-Man2813Redis -Name 'man2813-foreign-redis-sentinel-final-readback' -Arguments @('EXISTS', $foreignRedisSentinel)
            if (-not [int]::TryParse($sentinelRemaining.Stdout.Trim(), [ref]$remainingForeignRedisSentinel)) {
                $cleanupErrors.Add('foreign Redis sentinel final readback was not countable.')
            }
        }
        catch { $cleanupErrors.Add("foreign Redis sentinel cleanup: $($_.Exception.Message)") }
    }
    try {
        [IO.Directory]::CreateDirectory((Split-Path -Parent $evidencePath)) | Out-Null
        [ordered]@{
            scenario = 'MAN-2813 NCR rework to ERP cost closure'
            completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
            requiredServices = @($serviceSpecs.Keys)
            testIdentity = $testIdentity
            cleanup = [ordered]@{
                managedProcessRemaining = $remainingProcesses.Count
                exactDatabaseRemaining = $remainingDatabase
                ownedRedisKeyRemaining = $remainingRedisKeys
                ownedComposeServiceRemaining = 0
                foreignRedisSentinelPreserved = $foreignRedisSentinelPreserved
                foreignRedisSentinelRemaining = $remainingForeignRedisSentinel
                errors = @($cleanupErrors | ForEach-Object { Protect-ScriptAutomationText -Text $_ })
            }
        } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $evidencePath -Encoding utf8
    }
    catch { $cleanupErrors.Add("evidence: $($_.Exception.Message)") }
}

if ($cleanupErrors.Count -gt 0) {
    $cleanupSummary = @($cleanupErrors | ForEach-Object { Protect-ScriptAutomationText -Text $_ }) -join '; '
    if ($null -ne $acceptanceFailure) { Write-Diagnostic -Level 'WARN' -Message "MAN-2813 acceptance failed and cleanup also failed: $cleanupSummary" }
    else { throw "MAN-2813 cleanup failed: $cleanupSummary" }
}
if ($null -ne $acceptanceFailure) { throw $acceptanceFailure }
Write-Host "MAN-2813 real public-entry FullChain acceptance passed: $resultPath"
