# Script-Governance:
#   Category: check
#   SideEffects:
#     - Parses the Business IndustrialTelemetry MVP verify script
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$verifyScript = Join-Path $repoRoot 'scripts/verify-business-industrial-telemetry-mvp.ps1'

if (-not (Test-Path $verifyScript)) {
    throw 'Business IndustrialTelemetry MVP verify script must exist.'
}

$content = Get-Content -Path $verifyScript -Raw
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($verifyScript, [ref] $tokens, [ref] $parseErrors)

if ($parseErrors.Count -gt 0) {
    throw "Verify script must parse cleanly: $($parseErrors[0].Message)"
}

$forbiddenCommandNames = [Collections.Generic.HashSet[string]]::new(
    [string[]]@('dotnet', 'docker', 'pnpm', 'pwsh', 'powershell'),
    [StringComparer]::OrdinalIgnoreCase)
$softHyphenCommandName = "dotnet$([char]0x00AD)"
if ($forbiddenCommandNames.Contains($softHyphenCommandName)) {
    throw 'Native command identity matching must not fold U+00AD into dotnet.'
}

foreach ($requiredText in @(
    '[string] $PostgresConnectionString',
    'NERV_IIP_TEST_POSTGRES',
    'Use-ScopedEnvironmentVariable',
    'business-industrial-telemetry-postgres-tests',
    'FullyQualifiedName~Postgres_ | FullyQualifiedName~on_postgres',
    'Business IndustrialTelemetry PostgreSQL regressions verified.'
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

    if ($forbiddenCommandNames.Contains($commandName)) {
        throw "Verify script must not call native '$commandName' directly."
    }
}

Write-Host 'Business IndustrialTelemetry verify script coverage tests passed.'
