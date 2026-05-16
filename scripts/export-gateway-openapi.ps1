Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

function Wait-Healthy {
  param([string]$Uri)
  $deadline = (Get-Date).AddSeconds(30)
  do {
    try {
      $result = Invoke-RestMethod -Method Get -Uri $Uri
      if ($result -eq "Healthy") { return }
    }
    catch {
      Start-Sleep -Milliseconds 500
    }
  } while ((Get-Date) -lt $deadline)
  throw "Service did not become healthy at $Uri"
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$gatewayUrl = "http://127.0.0.1:58204"
$gatewayProject = Join-Path $root "backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj"
$output = Join-Path $root "frontend/packages/api-client/openapi/platform-gateway.v1.json"
$outputDirectory = Split-Path -Parent $output

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$gatewayJob = $null
try {
  dotnet build $gatewayProject

  $gatewayJob = Start-Job -ScriptBlock {
    param($project, $url)
    $env:ASPNETCORE_URLS = $url
    dotnet run --project $project --no-build --no-launch-profile
  } -ArgumentList $gatewayProject, $gatewayUrl

  Wait-Healthy "$gatewayUrl/health"
  Invoke-WebRequest -Method Get -Uri "$gatewayUrl/swagger/v1/swagger.json" -OutFile $output
  Write-Host "Gateway OpenAPI exported to $output"
}
finally {
  if ($gatewayJob) {
    Stop-Job $gatewayJob -ErrorAction SilentlyContinue
    Remove-Job $gatewayJob -Force -ErrorAction SilentlyContinue
  }
}
