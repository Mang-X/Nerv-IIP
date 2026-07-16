# Script-Governance:
#   Category: release-install
#   SideEffects:
#     - Checks connected-machine prerequisites for local Aspire startup
#     - Optionally installs missing Windows developer prerequisites through winget
#     - Checks/trusts local HTTPS developer certificates when -InstallMissing is used
#     - Initializes missing local AppHost user secrets for Development startup
#     - Optionally starts the local platform through scripts/dev.ps1
#   Writes:
#     - artifacts/script-logs/**
#     - artifacts/bootstrap-online/**
#     - User-scoped AppHost user secrets when -SkipLocalSecrets is not specified
#   Cleanup:
#     - Stops managed child process trees when helper commands time out
#     - Leaves installed tools, restored packages, Docker resources, and Aspire resources in place
#   Requires:
#     - PowerShell 7
#     - Network access
#     - winget on Windows when -InstallMissing is used

[CmdletBinding()]
param(
    [switch] $InstallMissing,
    [switch] $SkipRestore,
    [switch] $SkipLocalSecrets,
    [switch] $Start,
    [switch] $NoBuild,
    [switch] $Help,
    [string] $LocalAdminPassword
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
. (Join-Path $root 'scripts/lib/ScriptAutomation.ps1')

function Write-BootstrapHelp {
    Write-Host @'
Nerv-IIP connected-machine bootstrap

Usage:
  .\nerv.ps1 bootstrap [-InstallMissing] [-SkipRestore] [-SkipLocalSecrets] [-Start] [-NoBuild]

Options:
  -InstallMissing    On Windows, install missing .NET SDK, Node.js, Docker Desktop, and Aspire CLI.
                    Also trust local HTTPS developer certificates when they are missing.
  -SkipRestore       Skip dotnet tool restore, dotnet restore, and pnpm install.
  -SkipLocalSecrets  Do not initialize missing local AppHost user secrets.
  -Start             Start the platform through Aspire after bootstrap.
  -NoBuild           Forward -NoBuild when -Start is used.
  -LocalAdminPassword
                    Optional known local IAM seed admin password. If omitted, a random
                    Development-only value is written to user-secrets.
  -Help              Print this help.

Default behavior:
  Preflight prerequisites, initialize missing local Development secrets, and restore packages.
'@
}

function Test-CommandAvailable {
    param(
        [Parameter(Mandatory)]
        [string] $Name
    )

    return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Update-ProcessPath {
    $segments = New-Object System.Collections.Generic.List[string]

    foreach ($scope in @('Machine', 'User')) {
        $path = [Environment]::GetEnvironmentVariable('Path', $scope)
        if (-not [string]::IsNullOrWhiteSpace($path)) {
            foreach ($segment in ($path -split [System.IO.Path]::PathSeparator)) {
                if (-not [string]::IsNullOrWhiteSpace($segment) -and -not $segments.Contains($segment)) {
                    $segments.Add($segment)
                }
            }
        }
    }

    foreach ($segment in @(
        (Join-Path $HOME '.dotnet'),
        (Join-Path $HOME '.dotnet/tools'),
        (Join-Path $HOME '.aspire/bin'),
        'C:\Program Files\nodejs',
        'C:\Program Files\Docker\Docker\resources\bin'
    )) {
        if ((Test-Path -LiteralPath $segment) -and -not $segments.Contains($segment)) {
            $segments.Add($segment)
        }
    }

    $env:Path = ($segments -join [System.IO.Path]::PathSeparator)
}

function Invoke-WingetInstall {
    param(
        [Parameter(Mandatory)]
        [string] $Id,

        [Parameter(Mandatory)]
        [string] $Name
    )

    if (-not $IsWindows) {
        throw "$Name is missing. Automatic prerequisite installation is currently only implemented for Windows connected bootstrap."
    }

    if (-not (Test-CommandAvailable -Name 'winget')) {
        throw "winget is required to install $Name automatically. Install App Installer from Microsoft Store, or install $Name manually."
    }

    Invoke-NativeCommandWithTimeout `
        -Command 'winget' `
        -Arguments @('install', '--id', $Id, '--exact', '--accept-package-agreements', '--accept-source-agreements', '--silent') `
        -WorkingDirectory $root `
        -TimeoutSeconds 1800 `
        -Name "bootstrap-install-$Name" | Out-Null

    Update-ProcessPath
}

function Test-DotNet10Sdk {
    if (-not (Test-CommandAvailable -Name 'dotnet')) {
        return $false
    }

    try {
        $result = Invoke-DotNetOutput -Arguments @('--list-sdks') -WorkingDirectory $root -Name 'bootstrap-dotnet-sdks'
        return ($result.Stdout -split '\r?\n' | Where-Object { $_ -match '^10\.' }).Count -gt 0
    }
    catch {
        return $false
    }
}

function Get-NodeMajorVersion {
    if (-not (Test-CommandAvailable -Name 'node')) {
        return $null
    }

    try {
        $command = (Get-Command 'node' -ErrorAction Stop).Source
        $result = Invoke-NativeCommandOutput -Command $command -Arguments @('--version') -WorkingDirectory $root -Name 'bootstrap-node-version'
        if ($result.Stdout -match 'v(?<major>\d+)\.') {
            return [int] $Matches['major']
        }
    }
    catch {
        return $null
    }

    return $null
}

function Test-DockerDaemon {
    if (-not (Test-CommandAvailable -Name 'docker')) {
        return $false
    }

    try {
        $command = (Get-Command 'docker' -ErrorAction Stop).Source
        Invoke-NativeCommandOutput -Command $command -Arguments @('version', '--format', '{{.Server.Version}}') -WorkingDirectory $root -TimeoutSeconds 30 -Name 'bootstrap-docker-daemon' | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Get-CurrentPwshCommand {
    if ($IsWindows) {
        return (Join-Path $PSHOME 'pwsh.exe')
    }

    return (Join-Path $PSHOME 'pwsh')
}

function Install-AspireCli {
    $outputDirectory = Join-Path $root 'artifacts/bootstrap-online'
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

    $installer = Join-Path $outputDirectory 'aspire-install.ps1'
    Invoke-WebRequest -Uri 'https://aspire.dev/install.ps1' -OutFile $installer

    Invoke-NativeCommandWithTimeout `
        -Command (Get-CurrentPwshCommand) `
        -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $installer) `
        -WorkingDirectory $root `
        -TimeoutSeconds 600 `
        -Name 'bootstrap-install-aspire' | Out-Null

    Update-ProcessPath
}

function Ensure-Pnpm {
    if (Test-CommandAvailable -Name 'pnpm') {
        return
    }

    if (Test-CommandAvailable -Name 'corepack') {
        $corepack = (Get-Command 'corepack' -ErrorAction Stop).Source
        Invoke-NativeCommandWithTimeout -Command $corepack -Arguments @('enable') -WorkingDirectory $root -TimeoutSeconds 120 -Name 'bootstrap-corepack-enable' | Out-Null
        Invoke-NativeCommandWithTimeout -Command $corepack -Arguments @('prepare', 'pnpm@11.13.1', '--activate') -WorkingDirectory $root -TimeoutSeconds 300 -Name 'bootstrap-corepack-pnpm' | Out-Null
        Update-ProcessPath
        return
    }

    if (Test-CommandAvailable -Name 'npm') {
        $npm = (Get-Command 'npm' -ErrorAction Stop).Source
        Invoke-NativeCommandWithTimeout -Command $npm -Arguments @('install', '-g', 'pnpm@11.13.1') -WorkingDirectory $root -TimeoutSeconds 300 -Name 'bootstrap-npm-pnpm' | Out-Null
        Update-ProcessPath
        return
    }

    throw 'pnpm is missing and neither corepack nor npm is available.'
}

function New-SecretValue {
    param(
        [int] $Bytes = 32
    )

    $buffer = [byte[]]::new($Bytes)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($buffer)
    return [Convert]::ToBase64String($buffer)
}

function ConvertTo-Base64Url {
    param(
        [Parameter(Mandatory)]
        [byte[]] $Bytes
    )

    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function New-IamJwtSigningMaterial {
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    try {
        $parameters = $rsa.ExportParameters($false)
        $privateKeyPem = $rsa.ExportPkcs8PrivateKeyPem()

        $kid = "local-dev-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmss'))"
        $jwk = [ordered]@{
            kty = 'RSA'
            use = 'sig'
            kid = $kid
            alg = 'RS256'
            n = ConvertTo-Base64Url -Bytes $parameters.Modulus
            e = ConvertTo-Base64Url -Bytes $parameters.Exponent
        }

        $jwks = [ordered]@{
            keys = @($jwk)
        } | ConvertTo-Json -Compress -Depth 5

        return [pscustomobject]@{
            Kid = $kid
            PrivateKeyPem = [string] $privateKeyPem
            JwksJson = $jwks
        }
    }
    finally {
        $rsa.Dispose()
    }
}

function Get-AppHostUserSecrets {
    param(
        [Parameter(Mandatory)]
        [string] $AppHostProject
    )

    try {
        $result = Invoke-DotNetOutput -Arguments @('user-secrets', 'list', '--project', $AppHostProject) -WorkingDirectory $root -Name 'bootstrap-user-secrets-list'
    }
    catch {
        $message = "$($_.Exception.Message)"
        if ($message.Contains("Could not find the global property 'UserSecretsId'") -or $message.Contains('No UserSecretsId')) {
            return @{}
        }

        throw
    }

    $secrets = @{}
    foreach ($line in ($result.Stdout -split '\r?\n')) {
        $parts = "$line" -split ' = ', 2
        if ($parts.Count -eq 2) {
            $secrets[$parts[0]] = $parts[1]
        }
    }

    return $secrets
}

function Set-AppHostUserSecret {
    param(
        [Parameter(Mandatory)]
        [hashtable] $ExistingSecrets,

        [Parameter(Mandatory)]
        [string] $AppHostProject,

        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $Value
    )

    Invoke-DotNet `
        -Arguments @('user-secrets', 'set', $Name, $Value, '--project', $AppHostProject) `
        -WorkingDirectory $root `
        -TimeoutSeconds 120 `
        -Name "bootstrap-secret-$($Name.Replace(':', '-'))" `
        -SensitiveArgumentIndexes @(3) | Out-Null
    $ExistingSecrets[$Name] = '<set>'
}

function Set-AppHostUserSecretIfMissing {
    param(
        [Parameter(Mandatory)]
        [hashtable] $ExistingSecrets,

        [Parameter(Mandatory)]
        [string] $AppHostProject,

        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $Value
    )

    if ($ExistingSecrets.ContainsKey($Name) -and -not [string]::IsNullOrWhiteSpace($ExistingSecrets[$Name])) {
        return $false
    }

    Set-AppHostUserSecret -ExistingSecrets $ExistingSecrets -AppHostProject $AppHostProject -Name $Name -Value $Value
    $ExistingSecrets[$Name] = '<set>'
    return $true
}

function Initialize-LocalAppHostSecrets {
    param(
        [Parameter(Mandatory)]
        [string] $AppHostProject
    )

    $existing = Get-AppHostUserSecrets -AppHostProject $AppHostProject
    $created = New-Object System.Collections.Generic.List[string]

    $adminPasswordWasGenerated = [string]::IsNullOrWhiteSpace($LocalAdminPassword)
    $adminPassword = if ($adminPasswordWasGenerated) {
        New-SecretValue -Bytes 24
    }
    else {
        $LocalAdminPassword
    }

    $secretMap = [ordered]@{
        'Parameters:internal-service-bearer-token' = New-SecretValue -Bytes 48
        'Parameters:postgres-password' = New-SecretValue -Bytes 24
        'Parameters:redis-password' = New-SecretValue -Bytes 24
        'Parameters:minio-root-user' = 'nerv-local-minio'
        'Parameters:minio-root-password' = New-SecretValue -Bytes 24
        'Parameters:iam-seed-admin-password' = $adminPassword
        'Parameters:iam-seed-connector-host-secret' = New-SecretValue -Bytes 32
        'Parameters:connector-ingestion-token-signing-key' = New-SecretValue -Bytes 48
    }

    $iamJwtSecrets = @(
        'Parameters:iam-jwt-signing-key-id',
        'Parameters:iam-jwt-private-key-pem',
        'Parameters:iam-jwt-jwks-json'
    )
    $missingIamJwtSecrets = @($iamJwtSecrets | Where-Object {
        -not $existing.ContainsKey($_) -or [string]::IsNullOrWhiteSpace($existing[$_])
    })
    if ($missingIamJwtSecrets.Count -gt 0) {
        $jwtMaterial = New-IamJwtSigningMaterial
        Set-AppHostUserSecret -ExistingSecrets $existing -AppHostProject $AppHostProject -Name 'Parameters:iam-jwt-signing-key-id' -Value $jwtMaterial.Kid
        Set-AppHostUserSecret -ExistingSecrets $existing -AppHostProject $AppHostProject -Name 'Parameters:iam-jwt-private-key-pem' -Value $jwtMaterial.PrivateKeyPem
        Set-AppHostUserSecret -ExistingSecrets $existing -AppHostProject $AppHostProject -Name 'Parameters:iam-jwt-jwks-json' -Value $jwtMaterial.JwksJson
        foreach ($name in $iamJwtSecrets) {
            $created.Add($name)
        }
    }

    if (Set-AppHostUserSecretIfMissing -ExistingSecrets $existing -AppHostProject $AppHostProject -Name 'Parameters:iam-secrets-pepper' -Value (New-SecretValue -Bytes 48)) {
        $created.Add('Parameters:iam-secrets-pepper')
    }

    foreach ($entry in $secretMap.GetEnumerator()) {
        if (Set-AppHostUserSecretIfMissing -ExistingSecrets $existing -AppHostProject $AppHostProject -Name $entry.Key -Value $entry.Value) {
            $created.Add($entry.Key)
        }
    }

    if ($created.Count -gt 0) {
        Write-Diagnostic "Initialized missing local AppHost user secrets: $($created -join ', ')"
        if ($adminPasswordWasGenerated -and $created.Contains('Parameters:iam-seed-admin-password')) {
            Write-Diagnostic 'Generated a random local IAM seed admin password in user-secrets. Retrieve or override it with dotnet user-secrets before the first database seed if you need a known local login password.'
        }
    }
    else {
        Write-Diagnostic 'All required local AppHost user secrets were already present.'
    }
}

function Ensure-ConnectedPrerequisites {
    if (-not (Test-DotNet10Sdk)) {
        if (-not $InstallMissing) {
            throw '.NET SDK 10 is required. Re-run with -InstallMissing on Windows or install .NET SDK 10 manually.'
        }

        Invoke-WingetInstall -Id 'Microsoft.DotNet.SDK.10' -Name 'dotnet-sdk-10'
    }

    $nodeMajor = Get-NodeMajorVersion
    if ($null -eq $nodeMajor -or $nodeMajor -lt 22) {
        if (-not $InstallMissing) {
            throw 'Node.js >= 22 is required. Re-run with -InstallMissing on Windows or install Node.js manually.'
        }

        Invoke-WingetInstall -Id 'OpenJS.NodeJS' -Name 'nodejs'
    }

    Ensure-Pnpm

    if (-not (Test-CommandAvailable -Name 'docker')) {
        if (-not $InstallMissing) {
            throw 'Docker CLI/Desktop is required. Re-run with -InstallMissing on Windows or install Docker manually.'
        }

        Invoke-WingetInstall -Id 'Docker.DockerDesktop' -Name 'docker-desktop'
    }

    if (-not (Test-CommandAvailable -Name 'aspire')) {
        if (-not $InstallMissing) {
            throw 'Aspire CLI is required. Re-run with -InstallMissing or install it from https://aspire.dev.'
        }

        Install-AspireCli
    }

    if (-not (Test-DockerDaemon)) {
        throw 'Docker is installed but its daemon is not reachable. Start Docker Desktop or the Docker service, then rerun bootstrap.'
    }

    Get-AspireCliCommand | Out-Null
    Write-Diagnostic 'Connected-machine prerequisite preflight passed.'
}

function Ensure-DevelopmentHttpsCertificate {
    Write-Diagnostic 'Checking local HTTPS developer certificate trust.'

    try {
        Invoke-DotNetOutput -Arguments @('dev-certs', 'https', '--check', '--trust') -WorkingDirectory $root -TimeoutSeconds 60 -Name 'bootstrap-dev-cert-check' | Out-Null
        Write-Diagnostic 'Local HTTPS developer certificate is already trusted.'
        return
    }
    catch {
        if (-not $InstallMissing) {
            throw @"
Local HTTPS developer certificate is missing or not trusted.

Run .\nerv.ps1 bootstrap -InstallMissing, or run these commands manually:
  aspire certs trust
  dotnet dev-certs https --trust

If Aspire/DCP still reports certificate name mismatch:
  aspire certs clean
  aspire certs trust
  dotnet dev-certs https --trust

Details:
$($_.Exception.Message)
"@
        }

        Write-Diagnostic -Level 'WARN' -Message "Local HTTPS developer certificate check failed; attempting to trust certificates because -InstallMissing was supplied."
    }

    Invoke-Aspire -Arguments @('certs', 'trust', '--non-interactive') -WorkingDirectory $root -TimeoutSeconds 120 -Name 'bootstrap-aspire-certs-trust' | Out-Null
    Invoke-DotNet -Arguments @('dev-certs', 'https', '--trust') -WorkingDirectory $root -TimeoutSeconds 120 -Name 'bootstrap-dotnet-dev-certs-trust' | Out-Null
    Invoke-DotNetOutput -Arguments @('dev-certs', 'https', '--check', '--trust') -WorkingDirectory $root -TimeoutSeconds 60 -Name 'bootstrap-dev-cert-verify' | Out-Null
    Write-Diagnostic 'Local HTTPS developer certificate trust was verified.'
}

function Restore-WorkspaceDependencies {
    Invoke-DotNet -Arguments @('tool', 'restore') -WorkingDirectory $root -TimeoutSeconds 300 -Name 'bootstrap-dotnet-tool-restore' | Out-Null
    Invoke-DotNet -Arguments @('restore', 'backend/Nerv.IIP.sln') -WorkingDirectory $root -TimeoutSeconds 900 -Name 'bootstrap-backend-restore' | Out-Null
    Invoke-DotNet -Arguments @('restore', 'connector-hosts/Nerv.IIP.ConnectorHost.sln') -WorkingDirectory $root -TimeoutSeconds 600 -Name 'bootstrap-connector-restore' | Out-Null
    Invoke-Pnpm -Arguments @('install', '--frozen-lockfile') -WorkingDirectory (Join-Path $root 'frontend') -TimeoutSeconds 900 -Name 'bootstrap-frontend-install' | Out-Null
    Invoke-DotNet -Arguments @('build', 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj', '--no-restore') -WorkingDirectory $root -TimeoutSeconds 600 -Name 'bootstrap-apphost-build' | Out-Null
    Write-Diagnostic 'Workspace restore and AppHost build passed.'
}

if ($Help) {
    Write-BootstrapHelp
    exit 0
}

Set-Location $root
Update-ProcessPath

Ensure-ConnectedPrerequisites
Ensure-DevelopmentHttpsCertificate

$appHostProject = Join-Path $root 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj'
if (-not $SkipLocalSecrets) {
    Initialize-LocalAppHostSecrets -AppHostProject $appHostProject
}

if (-not $SkipRestore) {
    Restore-WorkspaceDependencies
}

if ($Start) {
    $devArguments = @()
    if ($NoBuild) {
        $devArguments += '-NoBuild'
    }

    Invoke-PwshScript -ScriptPath (Join-Path $root 'scripts/dev.ps1') -Arguments $devArguments -WorkingDirectory $root -TimeoutSeconds 1800 -Name 'bootstrap-start-dev' | Out-Null
}

Write-Host 'Connected-machine bootstrap completed.'
exit 0
