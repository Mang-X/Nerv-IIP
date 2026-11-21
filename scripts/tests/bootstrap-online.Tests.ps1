# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads the bootstrap SDK prerequisite and captures generated user-secret writes in memory
#   Writes:
#     - Temporary AppHost project outside the repository
#   Cleanup:
#     - Removes the owned AppHost fixture directory
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$bootstrapScript = Join-Path $repoRoot 'scripts/dev/bootstrap-online-environment.ps1'
# Load the production functions through the side-effect-free help entrypoint.
. $bootstrapScript -Help | Out-Null

function Test-CommandAvailable {
    param([string]$Name)
    return [string]::Equals($Name, 'dotnet', [StringComparison]::Ordinal)
}

function Invoke-DotNetOutput {
    param([string[]]$Arguments, [string]$WorkingDirectory, [int]$TimeoutSeconds, [string]$Name)
    return [pscustomobject]@{ Stdout = '10.0.302 [/fake/sdk]' }
}

if (-not (Test-DotNet10Sdk)) {
    throw 'A single installed .NET 10 SDK must satisfy the bootstrap prerequisite.'
}

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-bootstrap-online-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    $project = Join-Path $fixtureRoot 'Fake.AppHost.csproj'
    [IO.File]::WriteAllText($project, '<Project Sdk="Microsoft.NET.Sdk" />')
    [IO.File]::WriteAllText(
        (Join-Path $fixtureRoot 'Program.cs'),
        'var proof = builder.AddParameter("template-asset-retirement-proof-secret-base64", secret: true);')

    $capturedSecrets = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    function Get-AppHostUserSecrets { param([string]$AppHostProject) return @{} }
    function Invoke-DotNet {
        param([string[]]$Arguments, [string]$WorkingDirectory, [int]$TimeoutSeconds, [string]$Name, [int[]]$SensitiveArgumentIndexes)
        $capturedSecrets.Add($Arguments[2], $Arguments[3])
    }

    Initialize-LocalAppHostSecrets -AppHostProject $project
    $proofKey = 'Parameters:template-asset-retirement-proof-secret-base64'
    if (-not $capturedSecrets.ContainsKey($proofKey) -or [Convert]::FromBase64String($capturedSecrets[$proofKey]).Length -lt 32) {
        throw 'Bootstrap must initialize the discovered AppHost proof secret with at least 32 bytes.'
    }
    if (-not $capturedSecrets.ContainsKey('Parameters:postgres-password')) {
        throw 'Bootstrap must initialize the implicit PostgreSQL AppHost secret.'
    }
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}

Write-Host 'Bootstrap online script tests passed.'
