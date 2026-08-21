# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs CI-equivalent PowerShell child-process argument fixtures
#   Writes:
#     - Temporary fixture scripts and command logs under the operating-system temp directory
#   Cleanup:
#     - Removes the owned temporary fixture root in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/ScriptCompatibility.ps1')

function Assert-Contract([bool]$Condition, [string]$Message) {
  if (-not $Condition) { throw $Message }
}

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-script-compatibility-arguments-$([Guid]::NewGuid().ToString('N'))"
$fixturePath = Join-Path $fixtureRoot 'argument fixture.ps1'
$injectionMarkerPath = Join-Path $fixtureRoot 'injection-marker.txt'

try {
  [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
  $fixtureText = @'
[CmdletBinding(PositionalBinding = $false)]
param(
  [switch]$Flag,
  [string]$NamedValue,
  [Parameter(Position = 0)][string]$PlainValue,
  [Parameter(Position = 1)][AllowEmptyString()][string]$EmptyValue,
  [Parameter(Position = 2)][string]$SpaceValue,
  [Parameter(Position = 3)][string]$QuoteValue,
  [Parameter(Position = 4)][string]$LeadingHyphenValue,
  [Parameter(Position = 5)][string]$InjectionValue,
  [int]$ExitCode = 0,
  [int]$SleepMilliseconds = 0,
  [Parameter(ValueFromRemainingArguments = $true)][string[]]$Remaining
)

if ($SleepMilliseconds -gt 0) { Start-Sleep -Milliseconds $SleepMilliseconds }
$result = [ordered]@{
  flag = $Flag.IsPresent
  namedValue = $NamedValue
  plainValue = $PlainValue
  emptyValue = $EmptyValue
  spaceValue = $SpaceValue
  quoteValue = $QuoteValue
  leadingHyphenValue = $LeadingHyphenValue
  injectionValue = $InjectionValue
  remaining = @($Remaining)
}
$result | ConvertTo-Json -Compress
if ($ExitCode -ne 0) { exit $ExitCode }
'@
  [IO.File]::WriteAllText($fixturePath, $fixtureText, [Text.UTF8Encoding]::new($false))

  $injectionValue = "'; [IO.File]::WriteAllText('$injectionMarkerPath', 'injected'); #"
  $arguments = New-NervScriptCompatibilityPwshArguments `
    -ScriptPath $fixturePath `
    -NamedArguments ([ordered]@{ Flag = $true; NamedValue = 'named value' }) `
    -PositionalArguments @('plain', '', 'space value', "it's", '-leading', $injectionValue)
  $result = Invoke-NativeCommandWithTimeout -Command 'pwsh' -Arguments $arguments -WorkingDirectory $repoRoot -TimeoutSeconds 15 -Name 'script-compatibility-argument-contract' -LogDirectory (Join-Path $fixtureRoot 'success')
  $stdout = [IO.File]::ReadAllText($result.StdoutPath).Trim()
  $observed = $stdout | ConvertFrom-Json

  Assert-Contract ($observed.flag -eq $true) "CI-equivalent invocation must bind Flag as a switch; observed flag=$($observed.flag), plainValue='$($observed.plainValue)'."
  Assert-Contract ([string]::Equals([string]$observed.namedValue, 'named value', [StringComparison]::Ordinal)) 'CI-equivalent invocation must preserve a named parameter value.'
  Assert-Contract ([string]::Equals([string]$observed.plainValue, 'plain', [StringComparison]::Ordinal)) 'CI-equivalent invocation must preserve a plain positional value.'
  Assert-Contract ([string]::Equals([string]$observed.emptyValue, '', [StringComparison]::Ordinal)) 'CI-equivalent invocation must preserve an empty string.'
  Assert-Contract ([string]::Equals([string]$observed.spaceValue, 'space value', [StringComparison]::Ordinal)) 'CI-equivalent invocation must preserve a value containing spaces.'
  Assert-Contract ([string]::Equals([string]$observed.quoteValue, "it's", [StringComparison]::Ordinal)) 'CI-equivalent invocation must preserve a value containing a single quote.'
  Assert-Contract ([string]::Equals([string]$observed.leadingHyphenValue, '-leading', [StringComparison]::Ordinal)) 'CI-equivalent invocation must preserve a leading-hyphen positional value.'
  Assert-Contract ([string]::Equals([string]$observed.injectionValue, $injectionValue, [StringComparison]::Ordinal)) 'CI-equivalent invocation must preserve an injection-shaped value as inert data.'
  $remainingJson = $observed.remaining | ConvertTo-Json -Compress
  Assert-Contract ([string]::Equals([string]$remainingJson, 'null', [StringComparison]::Ordinal)) "CI-equivalent invocation must not shift values into remaining arguments; observed $remainingJson."
  Assert-Contract (-not (Test-Path -LiteralPath $injectionMarkerPath)) 'CI-equivalent invocation must not execute injection-shaped argument data.'

  $nonzeroArguments = New-NervScriptCompatibilityPwshArguments -ScriptPath $fixturePath -NamedArguments @{ ExitCode = 23 }
  $nonzeroMessage = '<no exception>'
  try {
    Invoke-NativeCommandWithTimeout -Command 'pwsh' -Arguments $nonzeroArguments -WorkingDirectory $repoRoot -TimeoutSeconds 15 -Name 'script-compatibility-nonzero-contract' -LogDirectory (Join-Path $fixtureRoot 'nonzero') | Out-Null
  }
  catch { $nonzeroMessage = $_.Exception.Message }
  Assert-Contract ($nonzeroMessage.Contains("Command 'pwsh' exited with 23", [StringComparison]::Ordinal)) "CI-equivalent invocation must preserve the target script exit code; observed '$nonzeroMessage'."

  $timeoutArguments = New-NervScriptCompatibilityPwshArguments -ScriptPath $fixturePath -NamedArguments @{ SleepMilliseconds = 5000 }
  $timeoutMessage = '<no exception>'
  try {
    Invoke-NativeCommandWithTimeout -Command 'pwsh' -Arguments $timeoutArguments -WorkingDirectory $repoRoot -TimeoutSeconds 1 -Name 'script-compatibility-timeout-contract' -LogDirectory (Join-Path $fixtureRoot 'timeout') | Out-Null
  }
  catch { $timeoutMessage = $_.Exception.Message }
  Assert-Contract ($timeoutMessage.Contains('timed out', [StringComparison]::OrdinalIgnoreCase)) "CI-equivalent invocation must preserve timeout failure; observed '$timeoutMessage'."
}
finally {
  if (Test-Path -LiteralPath $fixtureRoot) {
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
  }
}

Write-Host 'Script compatibility argument contracts passed.'
