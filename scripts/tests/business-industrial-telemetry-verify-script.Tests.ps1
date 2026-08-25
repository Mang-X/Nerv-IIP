# Script-Governance:
#   Category: check
#   SideEffects:
#     - Validates the IndustrialTelemetry PostgreSQL lane manifest and runner contract
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$runnerPath = Join-Path $repoRoot 'scripts/run-postgres-test-lane.ps1'
$manifestPath = Join-Path $repoRoot 'scripts/postgres-test-lane.json'

function Assert-Contract([bool] $Condition, [string] $Message) {
    if (-not $Condition) {
        throw $Message
    }
}

Assert-Contract (Test-Path -LiteralPath $runnerPath -PathType Leaf) 'The governed PostgreSQL lane runner must exist.'
Assert-Contract (Test-Path -LiteralPath $manifestPath -PathType Leaf) 'The governed PostgreSQL lane manifest must exist.'

$runnerContent = Get-Content -LiteralPath $runnerPath -Raw
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($runnerPath, [ref] $tokens, [ref] $parseErrors)
Assert-Contract ($parseErrors.Count -eq 0) "The PostgreSQL lane runner must parse cleanly: $($parseErrors[0].Message)"

foreach ($requiredText in @(
    '# Script-Governance:',
    'NERV_IIP_TEST_POSTGRES',
    'Invoke-DotNetOutput',
    '--no-restore',
    'Get-NervPostgresTrxResult',
    'Assert-NervPostgresTestLaneSummary',
    'CREATE DATABASE',
    'DROP DATABASE',
    'PostgreSQL lane members'
)) {
    Assert-Contract ($runnerContent.Contains($requiredText, [StringComparison]::Ordinal)) "The PostgreSQL lane runner must retain required behavior: $requiredText"
}

$forbiddenCommandNames = [Collections.Generic.HashSet[string]]::new(
    [string[]]@('dotnet', 'docker', 'pnpm', 'pwsh', 'powershell'),
    [StringComparer]::OrdinalIgnoreCase)
$commands = $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true)
foreach ($command in $commands) {
    $commandName = $command.GetCommandName()
    if (-not [string]::IsNullOrWhiteSpace($commandName)) {
        Assert-Contract (-not $forbiddenCommandNames.Contains($commandName)) "The PostgreSQL lane runner must not call native '$commandName' directly."
    }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -Depth 20
$telemetryMembers = @($manifest.members | Where-Object {
        [string]::Equals([string]$_.id, 'industrialtelemetry-postgres-profile', [StringComparison]::Ordinal)
    })
Assert-Contract ($telemetryMembers.Count -eq 1) 'The PostgreSQL lane must define exactly one IndustrialTelemetry member.'
$telemetryMember = $telemetryMembers[0]

Assert-Contract ([string]::Equals([string]$telemetryMember.service, 'IndustrialTelemetry', [StringComparison]::Ordinal)) 'The IndustrialTelemetry lane member must retain its service identity.'
Assert-Contract ([string]::Equals([string]$telemetryMember.tier, 'core', [StringComparison]::Ordinal)) 'The IndustrialTelemetry lane member must remain a core lane member.'
Assert-Contract ([string]::Equals([string]$telemetryMember.status, 'active', [StringComparison]::Ordinal)) 'The IndustrialTelemetry lane member must remain active.'
Assert-Contract ([string]::Equals([string]$telemetryMember.databaseOwnership, 'runner', [StringComparison]::Ordinal)) 'The IndustrialTelemetry lane must use runner-owned databases for failure diagnostics and cleanup.'
Assert-Contract ([string]::Equals([string]$telemetryMember.project, 'backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj', [StringComparison]::Ordinal)) 'The IndustrialTelemetry lane must target its Web test project.'
Assert-Contract ((@($telemetryMember.diagnosticSchemas).Count -eq 1) -and [string]::Equals([string]$telemetryMember.diagnosticSchemas[0], 'industrial_telemetry', [StringComparison]::Ordinal)) 'The IndustrialTelemetry lane must retain its restricted business and CAP schema diagnosis.'

$expectedTestIdentities = @(
    'Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.IndustrialTelemetryDeviceControlReadFaceTests.History_filters_by_device_status_and_time_window_on_postgres',
    'Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.IndustrialTelemetryHistorianTests.Postgres_downsampling_executes_pending_window_antijoin_queries',
    'Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.IndustrialTelemetryIdempotentConcurrencyTests.Concurrent_shelve_with_same_key_replays_the_loser_on_postgres',
    'Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.IndustrialTelemetryIdempotentConcurrencyTests.Exact_ordering_migration_backfills_existing_microsecond_timestamps_on_postgres',
    'Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.IndustrialTelemetryIdempotentConcurrencyTests.Manifest_and_activation_submicrosecond_races_keep_exact_order_on_postgres',
    'Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.IndustrialTelemetryIdempotentConcurrencyTests.Rule_alarm_race_with_changed_alarm_code_keeps_single_active_alarm_on_postgres',
    'Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.IndustrialTelemetryOeePostgresQueryTests.Oee_query_filters_production_facts_by_datetimeoffset_window_on_postgres'
)
$actualTestIdentities = @($telemetryMember.expectedTestIdentities)
Assert-Contract (($actualTestIdentities -join "`n") -ceq ($expectedTestIdentities -join "`n")) 'The IndustrialTelemetry lane must freeze exactly the seven governed PostgreSQL test identities.'

$filterSegments = @([string]$telemetryMember.filter -split '\|')
$expectedFilterSegments = @($expectedTestIdentities | ForEach-Object { "FullyQualifiedName~$_" })
Assert-Contract (($filterSegments -join "`n") -ceq ($expectedFilterSegments -join "`n")) 'The IndustrialTelemetry lane filter must select each frozen identity exactly once, at method scope.'

$testSourceRoot = Join-Path $repoRoot 'backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests'
foreach ($sourceName in @(
    'IndustrialTelemetryDeviceControlReadFaceTests.cs',
    'IndustrialTelemetryHistorianTests.cs',
    'IndustrialTelemetryIdempotentConcurrencyTests.cs',
    'IndustrialTelemetryOeePostgresQueryTests.cs'
)) {
    Assert-Contract (Test-Path -LiteralPath (Join-Path $testSourceRoot $sourceName) -PathType Leaf) "The IndustrialTelemetry PostgreSQL lane source '$sourceName' must exist."
}

Write-Host 'IndustrialTelemetry PostgreSQL lane contract coverage tests passed.'
