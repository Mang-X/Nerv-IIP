# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Starts, pauses, resumes, inspects, and stops one session-owned managed FullStack run
#     - Sends HTTP requests only to endpoints owned by that FullStack session
#   Writes:
#     - artifacts/fullstack/<session-id>/nerv-1860/**
#     - artifacts/script-logs/nerv-1860-managed-fullstack/**
#     - Local full-stack session manifests and artifacts
#   Cleanup:
#     - Resumes the owned coordinator and delegates exact session cleanup to the managed FullStack lifecycle
#     - Falls back to the exact SessionId stop command if the owned coordinator does not finish
#   Requires:
#     - PowerShell 7 on macOS
#     - Aspire CLI 13.4.x
#     - Docker
#     - git

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^nerv-[a-z0-9]+-[a-z0-9]+$')]
    [string] $SessionId,

    [int] $StartupTimeoutSeconds = 300,

    [int] $RunTimeoutSeconds = 900
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'lib/FullStackSessionState.ps1')

if (-not $IsMacOS) {
    throw 'NERV-1860 process-environment evidence currently requires macOS ps semantics.'
}

function Get-Nerv1860ProcessEnvironmentValue {
    param(
        [Parameter(Mandatory)] [int] $ProcessId,
        [Parameter(Mandatory)] [string] $Name
    )

    $command = Invoke-NativeCommandOutput `
        -Command '/bin/ps' `
        -Arguments @('eww', '-p', "$ProcessId", '-o', 'command=') `
        -WorkingDirectory $repoRoot `
        -TimeoutSeconds 30 `
        -Name "nerv-1860-process-$ProcessId-environment"
    $match = [regex]::Match(
        $command,
        "(?:^| )$([regex]::Escape($Name))=([^ ]+)",
        [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $match.Success) {
        throw "Process $ProcessId does not expose required environment key '$Name'."
    }

    return $match.Groups[1].Value
}

function Get-Nerv1860ExactProcess {
    param([Parameter(Mandatory)] [string] $ExecutablePath)

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    do {
        $matches = @(Get-Process | Where-Object {
            try {
                [string]::Equals(
                    [System.IO.Path]::GetFullPath($_.Path),
                    [System.IO.Path]::GetFullPath($ExecutablePath),
                    [StringComparison]::Ordinal)
            }
            catch {
                $false
            }
        })
        if ($matches.Count -eq 1) {
            return $matches[0]
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "Expected exactly one process for '$ExecutablePath', found $($matches.Count)."
}

function Get-Nerv1860Fingerprint {
    param([Parameter(Mandatory)] [string] $Value)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
    try {
        return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant().Substring(0, 16)
    }
    finally {
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function Invoke-Nerv1860Http {
    param(
        [Parameter(Mandatory)] [string] $Method,
        [Parameter(Mandatory)] [string] $Uri,
        [hashtable] $Headers = @{},
        [AllowNull()] [string] $Body
    )

    $parameters = @{
        Method = $Method
        Uri = $Uri
        Headers = $Headers
        SkipHttpErrorCheck = $true
        TimeoutSec = 30
    }
    if ($null -ne $Body) {
        $parameters['Body'] = $Body
        $parameters['ContentType'] = 'application/json'
    }
    $response = Invoke-WebRequest @parameters
    $json = if ([string]::IsNullOrWhiteSpace($response.Content)) {
        $null
    }
    else {
        $response.Content | ConvertFrom-Json
    }

    return [pscustomobject]@{
        StatusCode = [int] $response.StatusCode
        Content = [string] $response.Content
        Json = $json
    }
}

function Write-Nerv1860ResponseSource {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [object] $Response
    )

    $Response.Json | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $Path -Encoding utf8
}

function Get-Nerv1860LogCount {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [scriptblock] $Predicate
    )

    $count = 0
    foreach ($line in Get-Content -LiteralPath $Path) {
        $record = $line | ConvertFrom-Json
        if ($Predicate.Invoke($record)) {
            $count++
        }
    }
    return $count
}

function Stop-Nerv1860OwnedSession {
    param([Parameter(Mandatory)] [string] $OwnedSessionId)

    Invoke-NativeCommandWithTimeout `
        -Command 'pwsh' `
        -Arguments @('-NoLogo', '-NoProfile', '-File', (Join-Path $repoRoot 'nerv.ps1'), 'fullstack', 'stop', '-SessionId', $OwnedSessionId) `
        -WorkingDirectory $repoRoot `
        -TimeoutSeconds 180 `
        -Name "nerv-1860-stop-$OwnedSessionId" | Out-Null
}

$manifestPath = Get-NervFullStackManifestPath -SessionId $SessionId
$managedLogDirectory = Join-Path $repoRoot "artifacts/script-logs/nerv-1860-managed-fullstack/$SessionId"
$managed = $null
$coordinatorPaused = $false
$acceptanceFailure = $null
$runExitCode = $null
$artifactPath = $null
$runtimeEvidence = $null
$adminPassword = $null
$userAccessToken = $null
$internalToken = $null

try {
    if (Test-Path -LiteralPath $manifestPath) {
        throw "FullStack session '$SessionId' already has a manifest; use a fresh SessionId."
    }

    $managed = Start-ManagedBackgroundProcess `
        -Command 'pwsh' `
        -Arguments @('-NoLogo', '-NoProfile', '-File', (Join-Path $repoRoot 'nerv.ps1'), 'fullstack', 'run', '-SessionId', $SessionId, '-Scenario', 'smoke') `
        -WorkingDirectory $repoRoot `
        -Name "nerv-1860-managed-fullstack-$SessionId" `
        -LogDirectory $managedLogDirectory

    $startupDeadline = [DateTimeOffset]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    $manifest = $null
    do {
        if (Test-Path -LiteralPath $manifestPath) {
            $manifest = Read-NervFullStackManifest -SessionId $SessionId
            if ([string]::Equals([string] $manifest.state, 'Running', [StringComparison]::OrdinalIgnoreCase)) {
                break
            }
            if ([string]::Equals([string] $manifest.state, 'Failed', [StringComparison]::OrdinalIgnoreCase) -or
                [string]::Equals([string] $manifest.state, 'Stopped', [StringComparison]::OrdinalIgnoreCase) -or
                [string]::Equals([string] $manifest.state, 'CleanupFailed', [StringComparison]::OrdinalIgnoreCase)) {
                throw "Managed FullStack reached terminal state '$($manifest.state)' before acceptance."
            }
        }
        if ($managed.Process.HasExited) {
            throw "Managed FullStack coordinator exited before session '$SessionId' reached Running."
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $startupDeadline)

    if ($null -eq $manifest -or -not [string]::Equals([string] $manifest.state, 'Running', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Managed FullStack session '$SessionId' did not reach Running within $StartupTimeoutSeconds seconds."
    }

    Invoke-NativeCommandWithTimeout `
        -Command '/bin/kill' `
        -Arguments @('-STOP', "$($managed.ProcessId)") `
        -WorkingDirectory $repoRoot `
        -TimeoutSeconds 30 `
        -Name "nerv-1860-pause-$SessionId" | Out-Null
    $coordinatorPaused = $true

    $artifactPath = [System.IO.Path]::GetFullPath([string] $manifest.artifactPath)
    $evidenceDirectory = Join-Path $artifactPath 'nerv-1860'
    $httpSourceDirectory = Join-Path $evidenceDirectory 'http'
    [System.IO.Directory]::CreateDirectory($httpSourceDirectory) | Out-Null

    $appHostProcessId = [int] $manifest.aspire.appHostPid
    $iamExecutable = Join-Path $repoRoot 'backend/services/Iam/src/Nerv.IIP.Iam.Web/bin/Debug/net10.0/Nerv.IIP.Iam.Web'
    $businessGatewayExecutable = Join-Path $repoRoot 'backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/bin/Debug/net10.0/Nerv.IIP.BusinessGateway.Web'
    $productEngineeringExecutable = Join-Path $repoRoot 'backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/bin/Debug/net10.0/Nerv.IIP.Business.ProductEngineering.Web'
    $iamProcess = Get-Nerv1860ExactProcess -ExecutablePath $iamExecutable
    $businessGatewayProcess = Get-Nerv1860ExactProcess -ExecutablePath $businessGatewayExecutable
    $productEngineeringProcess = Get-Nerv1860ExactProcess -ExecutablePath $productEngineeringExecutable

    $appHostToken = Get-Nerv1860ProcessEnvironmentValue -ProcessId $appHostProcessId -Name 'Parameters__internal-service-bearer-token'
    $iamToken = Get-Nerv1860ProcessEnvironmentValue -ProcessId $iamProcess.Id -Name 'InternalService__BearerToken'
    $businessGatewayToken = Get-Nerv1860ProcessEnvironmentValue -ProcessId $businessGatewayProcess.Id -Name 'InternalService__BearerToken'
    $productEngineeringToken = Get-Nerv1860ProcessEnvironmentValue -ProcessId $productEngineeringProcess.Id -Name 'InternalService__BearerToken'
    $adminPassword = Get-Nerv1860ProcessEnvironmentValue -ProcessId $iamProcess.Id -Name 'Iam__Seed__AdminPassword'
    $productEngineeringBaseUrl = Get-Nerv1860ProcessEnvironmentValue -ProcessId $businessGatewayProcess.Id -Name 'ProductEngineering__BaseUrl'
    $gatewayBaseUrl = [string] $manifest.endpoints.gateway
    $businessGatewayBaseUrl = [string] $manifest.endpoints.'business-gateway'

    $participants = @(
        [ordered]@{ name = 'apphost'; pid = $appHostProcessId; executable = 'backend/apphost'; environmentKey = 'Parameters__internal-service-bearer-token'; fingerprint = Get-Nerv1860Fingerprint $appHostToken; tokenLength = $appHostToken.Length },
        [ordered]@{ name = 'iam'; pid = $iamProcess.Id; executable = $iamExecutable.Substring($repoRoot.Length + 1); environmentKey = 'InternalService__BearerToken'; fingerprint = Get-Nerv1860Fingerprint $iamToken; tokenLength = $iamToken.Length },
        [ordered]@{ name = 'business-gateway'; pid = $businessGatewayProcess.Id; executable = $businessGatewayExecutable.Substring($repoRoot.Length + 1); environmentKey = 'InternalService__BearerToken'; fingerprint = Get-Nerv1860Fingerprint $businessGatewayToken; tokenLength = $businessGatewayToken.Length },
        [ordered]@{ name = 'product-engineering'; pid = $productEngineeringProcess.Id; executable = $productEngineeringExecutable.Substring($repoRoot.Length + 1); environmentKey = 'InternalService__BearerToken'; fingerprint = Get-Nerv1860Fingerprint $productEngineeringToken; tokenLength = $productEngineeringToken.Length }
    )
    $fingerprints = @($participants | ForEach-Object { [string] $_.fingerprint } | Select-Object -Unique)
    $tokenLengths = @($participants | ForEach-Object { [int] $_.tokenLength } | Select-Object -Unique)
    if ($fingerprints.Count -ne 1 -or $tokenLengths.Count -ne 1) {
        throw 'AppHost, IAM, BusinessGateway, and ProductEngineering did not receive one credential snapshot.'
    }
    $internalToken = $businessGatewayToken

    $organizationId = 'org-001'
    $environmentId = 'env-dev'
    $siteCode = 'SITE-001'
    $finishedGoodSku = 'FG-QJ-P1-L'
    $bomCode = 'MBOM-FG-QJ-P1-L'
    $bomRevision = '2'

    $loginBody = @{ loginName = 'admin'; password = $adminPassword } | ConvertTo-Json -Compress
    $login = Invoke-Nerv1860Http -Method 'POST' -Uri "$gatewayBaseUrl/api/console/v1/auth/login" -Body $loginBody
    $userAccessToken = [string] $login.Json.data.accessToken
    if ($login.StatusCode -ne 200 -or [string]::IsNullOrWhiteSpace($userAccessToken)) {
        throw "Admin login failed with HTTP $($login.StatusCode)."
    }
    $userHeaders = @{ Authorization = "Bearer $userAccessToken" }

    $listUri = "$businessGatewayBaseUrl/api/business-console/v1/engineering/manufacturing-boms?organizationId=$organizationId&environmentId=$environmentId&skuCode=$finishedGoodSku&status=Published&skip=0&take=100"
    $list = Invoke-Nerv1860Http -Method 'GET' -Uri $listUri -Headers $userHeaders
    Write-Nerv1860ResponseSource -Path (Join-Path $httpSourceDirectory 'mbom-list.json') -Response $list
    $listIdentity = @($list.Json.data.items | Where-Object {
        [string]::Equals([string] $_.bomCode, $bomCode, [StringComparison]::Ordinal) -and
        [string]::Equals([string] $_.revision, $bomRevision, [StringComparison]::Ordinal)
    })
    if ($list.StatusCode -ne 200 -or $listIdentity.Count -ne 1) {
        throw "MBOM list did not contain exactly one $bomCode/$bomRevision record; HTTP $($list.StatusCode)."
    }

    $detailUri = "$businessGatewayBaseUrl/api/business-console/v1/engineering/manufacturing-boms/$bomCode/$bomRevision?organizationId=$organizationId&environmentId=$environmentId"
    $detail = Invoke-Nerv1860Http -Method 'GET' -Uri $detailUri -Headers $userHeaders
    Write-Nerv1860ResponseSource -Path (Join-Path $httpSourceDirectory 'mbom-detail.json') -Response $detail
    $materialLine = @($detail.Json.data.materialLines | Where-Object { $_.isPhantom -ne $true }) | Select-Object -First 1
    $materialSku = [string] $materialLine.skuCode
    $materialUom = [string] $materialLine.unitOfMeasureCode
    if ($detail.StatusCode -ne 200 -or [string]::IsNullOrWhiteSpace($materialSku) -or [string]::IsNullOrWhiteSpace($materialUom)) {
        throw "MBOM detail did not expose a non-phantom material identity; HTTP $($detail.StatusCode)."
    }

    $availabilityUri = "$businessGatewayBaseUrl/api/business-console/v1/inventory/availability?organizationId=$organizationId&environmentId=$environmentId&skuCode=$materialSku&uomCode=$materialUom&siteCode=$siteCode"
    $availability = Invoke-Nerv1860Http -Method 'GET' -Uri $availabilityUri -Headers $userHeaders
    Write-Nerv1860ResponseSource -Path (Join-Path $httpSourceDirectory 'inventory-availability.json') -Response $availability
    $movementsUri = "$businessGatewayBaseUrl/api/business-console/v1/inventory/movements?organizationId=$organizationId&environmentId=$environmentId&skuCode=$materialSku&siteCode=$siteCode&movementType=inbound&page=1&pageSize=100"
    $movements = Invoke-Nerv1860Http -Method 'GET' -Uri $movementsUri -Headers $userHeaders
    Write-Nerv1860ResponseSource -Path (Join-Path $httpSourceDirectory 'inventory-movements.json') -Response $movements
    if ($availability.StatusCode -ne 200 -or $movements.StatusCode -ne 200) {
        throw "Inventory public chain failed: availability=$($availability.StatusCode), movements=$($movements.StatusCode)."
    }

    $directUri = "$productEngineeringBaseUrl/api/business/v1/engineering/manufacturing-boms/$bomCode/$bomRevision?organizationId=$organizationId&environmentId=$environmentId"
    $wrongToken = Invoke-Nerv1860Http -Method 'GET' -Uri $directUri -Headers @{ Authorization = 'Bearer deliberately-wrong-nerv-1860' }
    $correctToken = Invoke-Nerv1860Http -Method 'GET' -Uri $directUri -Headers @{ Authorization = "Bearer $internalToken" }
    Write-Nerv1860ResponseSource -Path (Join-Path $httpSourceDirectory 'wrong-token.json') -Response $wrongToken
    Write-Nerv1860ResponseSource -Path (Join-Path $httpSourceDirectory 'correct-token.json') -Response $correctToken
    if ($wrongToken.StatusCode -ne 401 -or $correctToken.StatusCode -ne 200) {
        throw "Internal bearer counterexample failed: wrong=$($wrongToken.StatusCode), correct=$($correctToken.StatusCode)."
    }

    $headSha = (Invoke-NativeCommandOutput -Command 'git' -Arguments @('rev-parse', 'HEAD') -WorkingDirectory $repoRoot -Name 'nerv-1860-head').Trim()
    $baseSha = (Invoke-NativeCommandOutput -Command 'git' -Arguments @('rev-parse', 'origin/main') -WorkingDirectory $repoRoot -Name 'nerv-1860-base').Trim()
    $runtimeEvidence = [ordered]@{
        schemaVersion = 2
        ticket = 'NERV-1860'
        producer = [ordered]@{
            path = 'scripts/verify-nerv-1860-internal-bearer.ps1'
            command = "pwsh scripts/verify-nerv-1860-internal-bearer.ps1 -SessionId $SessionId"
        }
        sessionId = $SessionId
        headSha = $headSha
        baseSha = $baseSha
        credential = [ordered]@{
            algorithm = 'SHA-256'
            prefixLength = 16
            allMatch = $true
            participants = $participants
        }
        businessIdentity = [ordered]@{
            organizationId = $organizationId
            environmentId = $environmentId
            siteCode = $siteCode
            finishedGoodSku = $finishedGoodSku
            bomCode = $bomCode
            bomRevision = $bomRevision
            materialSku = $materialSku
            materialUom = $materialUom
        }
        http = [ordered]@{
            login = [ordered]@{ status = $login.StatusCode; source = 'live response, body not retained because it contains a bearer token' }
            mbomList = [ordered]@{ status = $list.StatusCode; source = 'nerv-1860/http/mbom-list.json' }
            mbomDetail = [ordered]@{ status = $detail.StatusCode; source = 'nerv-1860/http/mbom-detail.json' }
            inventoryAvailability = [ordered]@{ status = $availability.StatusCode; source = 'nerv-1860/http/inventory-availability.json' }
            inventoryMovements = [ordered]@{ status = $movements.StatusCode; source = 'nerv-1860/http/inventory-movements.json' }
            wrongInternalToken = [ordered]@{ status = $wrongToken.StatusCode; source = 'nerv-1860/http/wrong-token.json' }
            correctSessionToken = [ordered]@{ status = $correctToken.StatusCode; source = 'nerv-1860/http/correct-token.json' }
        }
        sources = [ordered]@{
            managedManifest = $manifestPath
            managedCoordinatorStdout = $managed.StdoutPath
            managedCoordinatorStderr = $managed.StderrPath
        }
    }
}
catch {
    $acceptanceFailure = $_
}
finally {
    $userAccessToken = $null
    $internalToken = $null
    $adminPassword = $null

    if ($coordinatorPaused -and $null -ne $managed -and -not $managed.Process.HasExited) {
        try {
            Invoke-NativeCommandWithTimeout `
                -Command '/bin/kill' `
                -Arguments @('-CONT', "$($managed.ProcessId)") `
                -WorkingDirectory $repoRoot `
                -TimeoutSeconds 30 `
                -Name "nerv-1860-resume-$SessionId" | Out-Null
        }
        catch {
            if ($null -eq $acceptanceFailure) { $acceptanceFailure = $_ }
        }
    }

    if ($null -ne $managed) {
        if (-not $managed.Process.WaitForExit($RunTimeoutSeconds * 1000)) {
            if ($null -eq $acceptanceFailure) {
                $acceptanceFailure = [TimeoutException]::new("Managed FullStack did not finish within $RunTimeoutSeconds seconds after acceptance.")
            }
            $managed.Stop.Invoke("NERV-1860 managed FullStack timeout")
            try { Stop-Nerv1860OwnedSession -OwnedSessionId $SessionId }
            catch {
                if ($null -eq $acceptanceFailure) { $acceptanceFailure = $_ }
            }
        }
        else {
            $runExitCode = $managed.Process.ExitCode
            $managed.Stop.Invoke("NERV-1860 managed FullStack completed")
        }
    }
}

if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "Managed FullStack manifest is missing after run: $manifestPath"
}
$finalManifest = Read-NervFullStackManifest -SessionId $SessionId
if ($null -eq $artifactPath) {
    $artifactPath = [System.IO.Path]::GetFullPath([string] $finalManifest.artifactPath)
}

if ($null -ne $runtimeEvidence) {
    $iamLogPath = Join-Path $artifactPath 'aspire-logs/iam.ndjson'
    $businessGatewayLogPath = Join-Path $artifactPath 'aspire-logs/business-gateway.ndjson'
    $productEngineeringLogPath = Join-Path $artifactPath 'aspire-logs/business-product-engineering.ndjson'
    $iamInvalid = Get-Nerv1860LogCount -Path $iamLogPath -Predicate {
        param($record)
        ([string] $record.content).Contains('Invalid internal service bearer token', [StringComparison]::Ordinal)
    }
    $iamInternalAuthorization401 = Get-Nerv1860LogCount -Path $iamLogPath -Predicate {
        param($record)
        try {
            $entry = ([string] $record.content) | ConvertFrom-Json
            [string]::Equals([string] $entry.Properties.RequestPath, '/internal/iam/v1/authorization/check', [StringComparison]::Ordinal) -and [int] $entry.Properties.StatusCode -eq 401
        }
        catch { $false }
    }
    $businessGateway403 = Get-Nerv1860LogCount -Path $businessGatewayLogPath -Predicate {
        param($record)
        try { [int] (([string] $record.content | ConvertFrom-Json).Properties.StatusCode) -eq 403 }
        catch { $false }
    }
    $productEngineeringInvalid = Get-Nerv1860LogCount -Path $productEngineeringLogPath -Predicate {
        param($record)
        ([string] $record.content).Contains('Invalid internal service bearer token', [StringComparison]::Ordinal)
    }
    $productEngineering401 = Get-Nerv1860LogCount -Path $productEngineeringLogPath -Predicate {
        param($record)
        try { [int] (([string] $record.content | ConvertFrom-Json).Properties.StatusCode) -eq 401 }
        catch { $false }
    }
    $playwrightPath = Join-Path $artifactPath 'playwright-fullstack-proxy.json'
    $playwright = Get-Content -Raw -LiteralPath $playwrightPath | ConvertFrom-Json

    $runtimeEvidence['serviceLogs'] = [ordered]@{
        iamInvalidInternalBearer = $iamInvalid
        iamInternalAuthorization401 = $iamInternalAuthorization401
        businessGateway403 = $businessGateway403
        productEngineeringExpectedWrongTokenInvalidBearer = $productEngineeringInvalid
        productEngineeringExpectedWrongToken401 = $productEngineering401
        sources = [ordered]@{
            iam = 'aspire-logs/iam.ndjson'
            businessGateway = 'aspire-logs/business-gateway.ndjson'
            productEngineering = 'aspire-logs/business-product-engineering.ndjson'
        }
    }
    $runtimeEvidence['playwright'] = [ordered]@{
        expected = [int] $playwright.stats.expected
        unexpected = [int] $playwright.stats.unexpected
        skipped = [int] $playwright.stats.skipped
        source = 'playwright-fullstack-proxy.json'
    }
    $runtimeEvidence['managed'] = [ordered]@{
        runExit = $runExitCode
        acceptanceExit = if ($null -eq $acceptanceFailure) { 0 } else { 1 }
        state = [string] $finalManifest.state
        cleanupRemaining = @($finalManifest.cleanup.remaining).Count
        cleanupErrors = @($finalManifest.cleanup.errors).Count
        source = $manifestPath
    }

    $evidencePath = Join-Path $artifactPath 'nerv-1860/evidence.json'
    $runtimeEvidence | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $evidencePath -Encoding utf8
    Write-Diagnostic "NERV-1860 evidence written to $evidencePath"

    if ($iamInvalid -ne 0 -or $iamInternalAuthorization401 -ne 0 -or $businessGateway403 -ne 0) {
        throw "Unexpected authentication failures: IAM invalid=$iamInvalid, IAM internal 401=$iamInternalAuthorization401, BusinessGateway 403=$businessGateway403."
    }
    if ($productEngineeringInvalid -ne 1 -or $productEngineering401 -ne 1) {
        throw "Expected exactly one deliberate ProductEngineering wrong-token failure; invalid=$productEngineeringInvalid, HTTP401=$productEngineering401."
    }
    if ([int] $playwright.stats.expected -ne 1 -or [int] $playwright.stats.unexpected -ne 0 -or [int] $playwright.stats.skipped -ne 0) {
        throw "Playwright smoke result was not 1 passed / 0 failed / 0 skipped."
    }
}

if ($null -ne $acceptanceFailure) {
    throw $acceptanceFailure
}
if ($runExitCode -ne 0) {
    throw "Managed FullStack coordinator exited with $runExitCode."
}
if (-not [string]::Equals([string] $finalManifest.state, 'Stopped', [StringComparison]::OrdinalIgnoreCase) -or @($finalManifest.cleanup.remaining).Count -ne 0 -or @($finalManifest.cleanup.errors).Count -ne 0) {
    throw "Managed FullStack cleanup is incomplete: state=$($finalManifest.state), remaining=$(@($finalManifest.cleanup.remaining).Count), errors=$(@($finalManifest.cleanup.errors).Count)."
}

Write-Diagnostic "NERV-1860 managed FullStack acceptance passed: session=$SessionId head=$($runtimeEvidence.headSha) state=$($finalManifest.state)"
