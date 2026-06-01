# Script-Governance:
#   Category: check
#   SideEffects:
#     - Parses the Business IIoT runtime facts APS/MES verify script
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$verifyScript = Join-Path $repoRoot 'scripts/verify-business-iiot-runtime-facts-aps-mes.ps1'

if (-not (Test-Path $verifyScript)) {
    throw 'Business IIoT runtime facts verify script must exist.'
}

$content = Get-Content -Path $verifyScript -Raw
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($verifyScript, [ref] $tokens, [ref] $parseErrors)

if ($parseErrors.Count -gt 0) {
    throw "Verify script must parse cleanly: $($parseErrors[0].Message)"
}

foreach ($requiredText in @(
    '# Script-Governance:',
    'Category: verify',
    '[switch] $SkipRestore',
    '[switch] $SkipFrontend',
    'scripts/lib/ScriptAutomation.ps1',
    'Invoke-DotNet',
    'Invoke-Pnpm',
    'backend/tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests.csproj',
    'backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj',
    'backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/Nerv.IIP.Business.Maintenance.Web.Tests.csproj',
    'backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj',
    'backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj',
    'backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj',
    'generate:api',
    '@nerv-iip/business-console',
    'typecheck',
    'test',
    'build',
    'Business IIoT runtime facts APS/MES verified.'
)) {
    if (-not $content.Contains($requiredText)) {
        throw "Verify script must contain required text: $requiredText"
    }
}

$commands = $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true)
foreach ($command in $commands) {
    $commandName = $command.GetCommandName()
    if ([string]::IsNullOrWhiteSpace($commandName)) {
        continue
    }

    if (@('dotnet', 'pnpm', 'pwsh', 'powershell') -contains $commandName.ToLowerInvariant()) {
        throw "Verify script must not call native '$commandName' directly."
    }
}

Write-Host 'Business IIoT runtime facts verify script coverage tests passed.'
