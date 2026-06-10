# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Exports PlatformGateway and BusinessGateway OpenAPI snapshots
#     - Regenerates the frontend api-client package
#     - Checks tracked and untracked OpenAPI/api-client drift with git
#   Writes:
#     - artifacts/script-logs/**
#     - artifacts/openapi-export/**
#     - frontend/node_modules/**
#     - frontend/packages/api-client/openapi/*.v1.json
#     - frontend/packages/api-client/src/generated/**
#   Cleanup:
#     - Stops managed nested script or pnpm process trees when they time out through ScriptAutomation.ps1
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - Node.js 22.22.3
#     - pnpm 11.1.2

[CmdletBinding()]
param(
    [switch] $SkipRegenerate,

    [switch] $SkipFrontendInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root
. (Join-Path $root 'scripts/lib/ScriptAutomation.ps1')

$diffPathspecs = @(
    'frontend/packages/api-client/openapi/*.v1.json',
    'frontend/packages/api-client/src/generated'
)

$statusPathspecs = @(
    'frontend/packages/api-client/openapi',
    'frontend/packages/api-client/src/generated'
)

function Get-GitOutputText {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [Parameter(Mandatory)]
        [string] $Name
    )

    $result = Invoke-NativeCommandOutput `
        -Command 'git' `
        -Arguments $Arguments `
        -WorkingDirectory $root `
        -TimeoutSeconds 120 `
        -Name $Name

    return $result.Stdout
}

if (-not $SkipRegenerate) {
    Invoke-PwshScript `
        -ScriptPath (Join-Path $root 'scripts/export-gateway-openapi.ps1') `
        -WorkingDirectory $root `
        -TimeoutSeconds 900 `
        -Name 'openapi-drift-export-gateway-openapi' | Out-Null

    if (-not $SkipFrontendInstall) {
        Invoke-Pnpm `
            -Arguments @('-C', 'frontend', 'install', '--frozen-lockfile', '--config.confirmModulesPurge=false') `
            -WorkingDirectory $root `
            -TimeoutSeconds 900 `
            -Name 'openapi-drift-frontend-install' | Out-Null
    }

    Invoke-Pnpm `
        -Arguments @('-C', 'frontend', 'generate:api') `
        -WorkingDirectory $root `
        -TimeoutSeconds 600 `
        -Name 'openapi-drift-frontend-generate-api' | Out-Null
}
else {
    Write-Diagnostic 'Skipping OpenAPI export and api-client generation; checking current working tree only.'
}

$status = Get-GitOutputText `
    -Arguments (@('status', '--short', '--untracked-files=all', '--') + $statusPathspecs) `
    -Name 'openapi-drift-git-status'

if ([string]::IsNullOrWhiteSpace($status)) {
    Invoke-NativeCommandWithTimeout `
        -Command 'git' `
        -Arguments (@('diff', '--exit-code', '--') + $diffPathspecs) `
        -WorkingDirectory $root `
        -TimeoutSeconds 120 `
        -Name 'openapi-drift-git-diff-exit-code' | Out-Null

    Invoke-NativeCommandWithTimeout `
        -Command 'git' `
        -Arguments (@('diff', '--cached', '--exit-code', '--') + $diffPathspecs) `
        -WorkingDirectory $root `
        -TimeoutSeconds 120 `
        -Name 'openapi-drift-git-diff-cached-exit-code' | Out-Null

    Write-Host 'OpenAPI/api-client drift check passed.'
    exit 0
}

Write-Host 'OpenAPI/api-client drift detected.'
Write-Host ''
Write-Host 'Changed files:'
Write-Host $status.TrimEnd()

$unstagedDiff = Get-GitOutputText `
    -Arguments (@('diff', '--') + $diffPathspecs) `
    -Name 'openapi-drift-git-diff'

if (-not [string]::IsNullOrWhiteSpace($unstagedDiff)) {
    Write-Host ''
    Write-Host 'Unstaged diff:'
    Write-Host $unstagedDiff.TrimEnd()
}

$stagedDiff = Get-GitOutputText `
    -Arguments (@('diff', '--cached', '--') + $diffPathspecs) `
    -Name 'openapi-drift-git-diff-cached'

if (-not [string]::IsNullOrWhiteSpace($stagedDiff)) {
    Write-Host ''
    Write-Host 'Staged diff:'
    Write-Host $stagedDiff.TrimEnd()
}

try {
    Invoke-NativeCommandWithTimeout `
        -Command 'git' `
        -Arguments (@('diff', '--exit-code', '--') + $diffPathspecs) `
        -WorkingDirectory $root `
        -TimeoutSeconds 120 `
        -Name 'openapi-drift-git-diff-exit-code' | Out-Null
}
catch {
    Write-Diagnostic -Level 'WARN' -Message $_.Exception.Message
}

try {
    Invoke-NativeCommandWithTimeout `
        -Command 'git' `
        -Arguments (@('diff', '--cached', '--exit-code', '--') + $diffPathspecs) `
        -WorkingDirectory $root `
        -TimeoutSeconds 120 `
        -Name 'openapi-drift-git-diff-cached-exit-code' | Out-Null
}
catch {
    Write-Diagnostic -Level 'WARN' -Message $_.Exception.Message
}

throw 'OpenAPI/api-client drift check failed. Run scripts/export-gateway-openapi.ps1 and pnpm -C frontend generate:api, then commit the resulting snapshot and generated client changes.'
