# Script-Governance:
#   Category: library, check
#   SideEffects:
#     - None
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest

function New-NervScriptCompatibilityPwshArguments {
  param(
    [Parameter(Mandatory)]
    [string]$ScriptPath,

    [System.Collections.IDictionary]$NamedArguments = @{},

    [object[]]$PositionalArguments = @()
  )

  $payload = [pscustomobject]@{
    ScriptPath = $ScriptPath
    NamedArguments = $NamedArguments
    PositionalArguments = @($PositionalArguments)
  }
  $serializedPayload = [Management.Automation.PSSerializer]::Serialize($payload, 10)
  $encodedPayload = [Convert]::ToBase64String([Text.UTF8Encoding]::new($false).GetBytes($serializedPayload))
  $commandText = @"
`$serializedPayload = [Text.UTF8Encoding]::new(`$false).GetString([Convert]::FromBase64String('$encodedPayload'))
`$invocation = [Management.Automation.PSSerializer]::Deserialize(`$serializedPayload)
`$targetScript = [string]`$invocation.ScriptPath
`$namedArguments = @{}
foreach (`$entry in `$invocation.NamedArguments.GetEnumerator()) {
  `$namedArguments[[string]`$entry.Key] = `$entry.Value
}
`$positionalArguments = @(`$invocation.PositionalArguments)
& `$targetScript @namedArguments @positionalArguments
`$targetSucceeded = `$?
`$targetExitCode = `$LASTEXITCODE
if (-not `$targetSucceeded) {
  if (`$null -ne `$targetExitCode) { exit `$targetExitCode }
  exit 1
}
"@

  return [string[]]@('-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $commandText)
}
