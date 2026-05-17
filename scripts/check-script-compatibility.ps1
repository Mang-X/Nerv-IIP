# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs script governance and compatibility verification commands
#     - Optionally runs the IAM persistent auth verification script
#   Writes:
#     - artifacts/script-logs/**
#     - artifacts/script-logs/script-compatibility/**/evidence.json
#   Cleanup:
#     - Stops managed child process trees through ScriptAutomation.ps1 when commands time out
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - Docker Compose v2 when running without -FastOnly

[CmdletBinding()]
param(
  [switch]$FastOnly,
  [switch]$AllowWindows,
  [string]$EvidencePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

if ($IsWindows -and -not $AllowWindows) {
  throw "Script compatibility gate must run on macOS or Linux. Use -AllowWindows only for a local smoke run."
}

if ([string]::IsNullOrWhiteSpace($EvidencePath)) {
  $evidenceDirectory = New-ScriptAutomationLogDirectory -Name "script-compatibility"
  $EvidencePath = Join-Path $evidenceDirectory "evidence.json"
}
else {
  $evidenceDirectory = Split-Path -Parent $EvidencePath
  if (-not [string]::IsNullOrWhiteSpace($evidenceDirectory)) {
    New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
  }
}

$commandRecords = New-Object System.Collections.Generic.List[object]

function Invoke-RecordedNativeCommand {
  param(
    [Parameter(Mandatory)]
    [string]$Command,

    [string[]]$Arguments = @(),

    [Parameter(Mandatory)]
    [string]$Name,

    [int]$TimeoutSeconds = 120
  )

  $startedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
  try {
    $result = Invoke-NativeCommandWithTimeout -Command $Command -Arguments $Arguments -WorkingDirectory $root -TimeoutSeconds $TimeoutSeconds -Name $Name
    $stdout = ""
    if (Test-Path $result.StdoutPath) {
      $stdoutContent = Get-Content $result.StdoutPath -Raw
      if ($null -ne $stdoutContent) {
        $stdout = $stdoutContent.Trim()
      }
    }
    $commandRecords.Add([pscustomobject]@{
      name = $Name
      command = $Command
      arguments = $Arguments
      exitCode = $result.ExitCode
      startedAtUtc = $startedAtUtc
      durationMs = $result.Duration.TotalMilliseconds
      stdout = $stdout
      logDirectory = $result.LogDirectory
    })
    return $result
  }
  catch {
    $commandRecords.Add([pscustomobject]@{
      name = $Name
      command = $Command
      arguments = $Arguments
      exitCode = -1
      startedAtUtc = $startedAtUtc
      durationMs = 0
      stdout = ""
      logDirectory = ""
      error = $_.Exception.Message
    })
    throw
  }
}

function Invoke-RecordedPwshScript {
  param(
    [Parameter(Mandatory)]
    [string]$ScriptPath,

    [string[]]$Arguments = @(),

    [Parameter(Mandatory)]
    [string]$Name,

    [int]$TimeoutSeconds = 300
  )

  Invoke-RecordedNativeCommand -Command "pwsh" -Arguments (@("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $ScriptPath) + $Arguments) -Name $Name -TimeoutSeconds $TimeoutSeconds | Out-Null
}

try {
  Invoke-RecordedNativeCommand -Command "dotnet" -Arguments @("--version") -Name "compat-dotnet-version" -TimeoutSeconds 60 | Out-Null
  Invoke-RecordedPwshScript -ScriptPath (Join-Path $root "scripts/check-script-governance.ps1") -Name "compat-script-governance" -TimeoutSeconds 120
  Invoke-RecordedPwshScript -ScriptPath (Join-Path $root "scripts/tests/check-script-governance.Tests.ps1") -Name "compat-script-governance-tests" -TimeoutSeconds 180
  Invoke-RecordedNativeCommand -Command "git" -Arguments @("diff", "--check") -Name "compat-git-diff-check" -TimeoutSeconds 120 | Out-Null

  if (-not $FastOnly) {
    Invoke-RecordedNativeCommand -Command "docker" -Arguments @("compose", "version", "--short") -Name "compat-docker-compose-version" -TimeoutSeconds 60 | Out-Null
    Invoke-RecordedPwshScript -ScriptPath (Join-Path $root "scripts/verify-iam-persistent-auth-foundation.ps1") -Name "compat-iam-persistent-auth-verify" -TimeoutSeconds 1200
  }
}
finally {
  $evidence = [ordered]@{
    schema = 1
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    osDescription = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    osArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    isWindows = $IsWindows
    isLinux = $IsLinux
    isMacOS = $IsMacOS
    powerShellVersion = $PSVersionTable.PSVersion.ToString()
    fastOnly = $FastOnly.IsPresent
    commands = $commandRecords.ToArray()
  }

  $json = ($evidence | ConvertTo-Json -Depth 20) + [Environment]::NewLine
  [System.IO.File]::WriteAllText($EvidencePath, $json, [System.Text.UTF8Encoding]::new($false))
  Write-Host "Script compatibility evidence written to $EvidencePath"
}

Write-Host "Script compatibility gate verified."
