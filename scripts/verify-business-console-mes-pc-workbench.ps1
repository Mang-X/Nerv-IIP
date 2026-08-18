# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs focused backend, API client, and Business Console verification for MES PC workbench
#     - Regenerates frontend API client from existing OpenAPI snapshots
#     - Optionally runs Business Console Playwright smoke tests when -E2E is supplied
#   Writes:
#     - frontend/packages/api-client/src/generated/**
#     - frontend/apps/business-console/dist/**
#     - frontend/apps/business-console/test-results/**
#     - artifacts/script-logs/**
#   Cleanup:
#     - Child process cleanup is managed by ScriptAutomation.ps1
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - Node.js >=22.18.0
#     - pnpm 11.22.0
#     - Chrome/Chromium executable when -E2E is supplied

param(
    [switch] $E2E,
    [string] $ChromiumExecutablePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

function Invoke-PnpmInteractive {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    # corepack 按“进程 cwd 就近 package.json 的 packageManager 字段”解析 pnpm 版本，
    # 统一经 Resolve-PnpmInvocation 把 -C/--dir 对齐为进程 cwd，避免根目录 cwd 拉取
    # 最新 pnpm 与 frontend/ 锁定版本冲突。
    $invocation = Resolve-PnpmInvocation -Arguments $Arguments -WorkingDirectory $root

    $result = Invoke-NativeCommandInteractive -Command "cmd" -Name $Name -WorkingDirectory $invocation.WorkingDirectory -Arguments (@(
        "/d",
        "/s",
        "/c",
        "pnpm"
    ) + $invocation.Arguments)

    if ($result.ExitCode -ne 0) {
        throw "$Name failed with exit code $($result.ExitCode)."
    }
}

function Invoke-DotNetTest {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $Project
    )

    $result = Invoke-DotNetInteractive -Name $Name -WorkingDirectory $root -Arguments @(
        "test",
        $Project,
        "--no-restore"
    )

    if ($result.ExitCode -ne 0) {
        throw "$Name failed with exit code $($result.ExitCode)."
    }
}

Invoke-DotNetTest -Name "mes-pc-mes-web-tests" -Project "backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj"

Invoke-DotNetTest -Name "mes-pc-business-gateway-tests" -Project "backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj"

Invoke-PnpmInteractive -Name "mes-pc-generate-api" -Arguments @(
    "-C",
    "frontend",
    "generate:api"
) | Out-Null

Invoke-PnpmInteractive -Name "mes-pc-api-client-typecheck" -Arguments @(
    "-C",
    "frontend",
    "--filter",
    "@nerv-iip/api-client",
    "typecheck"
) | Out-Null

Invoke-PnpmInteractive -Name "mes-pc-api-client-test" -Arguments @(
    "-C",
    "frontend",
    "--filter",
    "@nerv-iip/api-client",
    "test"
) | Out-Null

Invoke-PnpmInteractive -Name "mes-pc-business-console-typecheck" -Arguments @(
    "-C",
    "frontend",
    "--filter",
    "@nerv-iip/business-console",
    "typecheck"
) | Out-Null

Invoke-PnpmInteractive -Name "mes-pc-business-console-test" -Arguments @(
    "-C",
    "frontend",
    "--filter",
    "@nerv-iip/business-console",
    "test"
) | Out-Null

Invoke-PnpmInteractive -Name "mes-pc-business-console-build" -Arguments @(
    "-C",
    "frontend",
    "--filter",
    "@nerv-iip/business-console",
    "build"
) | Out-Null

if ($E2E) {
    if ([string]::IsNullOrWhiteSpace($ChromiumExecutablePath)) {
        Invoke-PnpmInteractive -Name "mes-pc-business-console-e2e" -Arguments @(
            "-C",
            "frontend",
            "--filter",
            "@nerv-iip/business-console",
            "e2e",
            "--",
            "business-console.spec.ts"
        ) | Out-Null
    }
    else {
        Invoke-WithScopedEnvironment -Variables @{
            PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH = $ChromiumExecutablePath
        } -ScriptBlock {
            Invoke-PnpmInteractive -Name "mes-pc-business-console-e2e" -Arguments @(
                "-C",
                "frontend",
                "--filter",
                "@nerv-iip/business-console",
                "e2e",
                "--",
                "business-console.spec.ts"
            ) | Out-Null
        }
    }
}

Write-Host "Business Console MES PC workbench verified."
