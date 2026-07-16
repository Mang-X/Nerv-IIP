# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs coding engine unit tests
#     - Builds backend solution
#     - Runs frontend typecheck
#   Writes:
#     - artifacts/script-logs/**
#     - backend/**/bin/**
#     - backend/**/obj/**
#     - frontend/**/.vite/**
#   Cleanup:
#     - Stops managed child process trees when helper timeouts occur
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - Node.js 22.18.0 or newer
#     - pnpm 11.13.1

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root
. (Join-Path $root 'scripts/lib/ScriptAutomation.ps1')

Invoke-DotNet `
  -Arguments @('test', 'backend/tests/Nerv.IIP.Coding.Tests/Nerv.IIP.Coding.Tests.csproj', '-v', 'minimal') `
  -WorkingDirectory $root `
  -TimeoutSeconds 300 `
  -Name 'coding-rule-engine-tests' | Out-Null

Invoke-DotNet `
  -Arguments @('build', 'backend/Nerv.IIP.sln', '--no-restore', '-v', 'minimal') `
  -WorkingDirectory $root `
  -TimeoutSeconds 900 `
  -Name 'coding-rule-engine-backend-build' | Out-Null

Invoke-Pnpm `
  -Arguments @('-C', 'frontend', 'typecheck') `
  -WorkingDirectory $root `
  -TimeoutSeconds 600 `
  -Name 'coding-rule-engine-frontend-typecheck' | Out-Null

Write-Host 'Coding rule engine verification passed.'
